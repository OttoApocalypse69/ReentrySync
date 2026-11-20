using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Apollo;
using Apollo.LunarModule;
using HarmonyLib;
using ReentrySync.Core;
using UnityEngine;

namespace ReentrySync.Network
{
    public enum MultiplayerMode
    {
        OFFLINE,
        HOST,
        CLIENT,
        RELAY_HOST,
        RELAY_CLIENT
    }

    public class CrewMemberInfo
    {
        public string PeerId { get; set; } = "";
        public string Callsign { get; set; } = "Astronaut";
        public string Role { get; set; } = "CDR"; // CDR, CMP, LMP, FLIGHT
        public string Craft { get; set; } = "CSM";
        public bool IsHost { get; set; }
    }

    public class IntercomEntry
    {
        public string Time { get; set; } = "";
        public string From { get; set; } = "";
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsSystem { get; set; }
    }

    internal class RemotePeerConnection
    {
        public string PeerId { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public TcpClient Client { get; set; } = null!;
        public StreamWriter Writer { get; set; } = null!;
        public object SendLock { get; } = new object();
        public CrewMemberInfo Info { get; set; } = new CrewMemberInfo();
    }

    public static class MultiplayerManager
    {
        public static volatile bool IsApplyingRemote = false;

        public static MultiplayerMode Mode { get; private set; } = MultiplayerMode.OFFLINE;
        public static bool IsConnected => Mode != MultiplayerMode.OFFLINE;
        public static bool IsAuthoritativeHost => Mode == MultiplayerMode.HOST || Mode == MultiplayerMode.RELAY_HOST;

        public static string LocalPeerId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public static string LocalCallsign { get; private set; } = "CDR-1";
        public static string LocalRole { get; private set; } = "CDR";
        public static string RoomCode { get; private set; } = "";
        public static string RemoteEndpoint { get; private set; } = "";
        public static string StatusText { get; private set; } = "Offline — Single Player";
        public static string FlightAuthorityPeerId { get; private set; } = "";
        public static bool HasFlightAuthority => !IsConnected || FlightAuthorityPeerId == LocalPeerId || (string.IsNullOrEmpty(FlightAuthorityPeerId) && IsAuthoritativeHost);

        public static event Action<string>? OnLog;
        public static event Action? OnStateChanged;

        private static TcpListener? _listener;
        private static TcpClient? _client;
        private static StreamWriter? _clientWriter;
        private static readonly object _clientSendLock = new object();
        private static CancellationTokenSource? _cts;

        private static readonly List<RemotePeerConnection> _hostPeers = new List<RemotePeerConnection>();
        private static readonly object _peersLock = new object();

        private static readonly Dictionary<string, CrewMemberInfo> _roster = new Dictionary<string, CrewMemberInfo>();
        private static readonly List<IntercomEntry> _intercom = new List<IntercomEntry>();
        private static readonly ConcurrentQueue<(string? sourcePeerId, string json)> _incomingPackets = new ConcurrentQueue<(string?, string)>();

        private static float _physicsSendTimer = 0f;
        private static float _rosterSyncTimer = 0f;

        // Cached physics interpolation targets for smooth non-authority sync
        private static bool _hasRemoteCsmRot = false;
        private static Quaternion _targetCsmRot = Quaternion.identity;
        private static Vector3 _targetCsmAngVel = Vector3.zero;
        private static bool _hasRemoteLmRot = false;
        private static Quaternion _targetLmRot = Quaternion.identity;

        // Reflection helpers for rigidbodies & NBodies
        private static readonly AccessTools.FieldRef<ProApolloRocket, Rigidbody>? _csmRbRef =
            AccessTools.FieldRefAccess<ProApolloRocket, Rigidbody>("rb");
        private static readonly AccessTools.FieldRef<ProApolloRocket, NBody>? _csmBodyRef =
            AccessTools.FieldRefAccess<ProApolloRocket, NBody>("physicsBody");
        private static readonly AccessTools.FieldRef<ApolloProLogicHandler, LunarModuleController>? _logicLmRef =
            AccessTools.FieldRefAccess<ApolloProLogicHandler, LunarModuleController>("lunarModule");
        private static readonly AccessTools.FieldRef<ApolloProLogicHandler, LunarModuleLandingController>? _logicLmLandRef =
            AccessTools.FieldRefAccess<ApolloProLogicHandler, LunarModuleLandingController>("lunarModuleLandingController");
        private static readonly AccessTools.FieldRef<LunarModuleController, Rigidbody>? _lmRbRef =
            AccessTools.FieldRefAccess<LunarModuleController, Rigidbody>("rb");
        private static readonly AccessTools.FieldRef<LunarModuleLandingController, Rigidbody>? _lmLandRbRef =
            AccessTools.FieldRefAccess<LunarModuleLandingController, Rigidbody>("rb");

        // -------------------------------------------------------------
        // Public Connection Lifecycle
        // -------------------------------------------------------------
        public static void StartDirectHost(int port, string callsign, string role)
        {
            Disconnect();
            LocalCallsign = string.IsNullOrWhiteSpace(callsign) ? "Commander" : callsign.Trim();
            LocalRole = string.IsNullOrWhiteSpace(role) ? "CDR" : role.Trim().ToUpperInvariant();
            RoomCode = "DIRECT-P2P";
            RemoteEndpoint = $"0.0.0.0:{port}";
            FlightAuthorityPeerId = LocalPeerId;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                Mode = MultiplayerMode.HOST;

                lock (_roster)
                {
                    _roster.Clear();
                    _roster[LocalPeerId] = new CrewMemberInfo
                    {
                        PeerId = LocalPeerId,
                        Callsign = LocalCallsign,
                        Role = LocalRole,
                        Craft = GetLocalCraftTag(),
                        IsHost = true
                    };
                }

                StatusText = $"Hosting Direct P2P on port {port} (Share your LAN / Tailscale / Public IP)";
                AddIntercom("SYSTEM", "MCC", $"Mission hosted on port {port} as [{LocalRole}] {LocalCallsign}.", true);
                OnLog?.Invoke($"[MP] {StatusText}");

                Task.Run(() => AcceptLoopAsync(_cts.Token));
                OnStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to host on port {port}: {ex.Message}";
                OnLog?.Invoke($"[MP] {StatusText}");
                Disconnect();
            }
        }

        public static void Connect(string hostIp, int port, string callsign, string role, string roomCode = "", bool asRelayHost = false)
        {
            Disconnect();
            LocalCallsign = string.IsNullOrWhiteSpace(callsign) ? "Pilot" : callsign.Trim();
            LocalRole = string.IsNullOrWhiteSpace(role) ? "CMP" : role.Trim().ToUpperInvariant();
            RoomCode = roomCode?.Trim().ToUpperInvariant() ?? "";
            RemoteEndpoint = $"{hostIp}:{port}";

            bool usingRelay = !string.IsNullOrEmpty(RoomCode);
            Mode = usingRelay
                ? (asRelayHost ? MultiplayerMode.RELAY_HOST : MultiplayerMode.RELAY_CLIENT)
                : MultiplayerMode.CLIENT;

            if (asRelayHost) FlightAuthorityPeerId = LocalPeerId;

            StatusText = usingRelay
                ? $"Connecting to Relay {hostIp}:{port} (Room {RoomCode})..."
                : $"Connecting to Host {hostIp}:{port}...";
            OnStateChanged?.Invoke();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    var tcp = new TcpClient();
                    tcp.NoDelay = true;
                    await tcp.ConnectAsync(hostIp, port);

                    _client = tcp;
                    var stream = tcp.GetStream();
                    _clientWriter = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    var reader = new StreamReader(stream, Encoding.UTF8);

                    lock (_roster)
                    {
                        _roster.Clear();
                        _roster[LocalPeerId] = new CrewMemberInfo
                        {
                            PeerId = LocalPeerId,
                            Callsign = LocalCallsign,
                            Role = LocalRole,
                            Craft = GetLocalCraftTag(),
                            IsHost = asRelayHost
                        };
                    }

                    StatusText = usingRelay
                        ? $"Connected via Relay {hostIp}:{port} | Room: {RoomCode}"
                        : $"Connected to Crew Host {hostIp}:{port}";

                    AddIntercom("SYSTEM", "MCC", StatusText, true);
                    OnLog?.Invoke($"[MP] {StatusText}");

                    // Send Hello handshake
                    string hello = BuildHelloPacket(asRelayHost);
                    SendToServer(hello);

                    // If client, request full cockpit & AGC snapshot immediately!
                    if (!asRelayHost)
                    {
                        SendToServer($"{{\"t\":\"req_snapshot\",\"peer\":\"{LocalPeerId}\"}}");
                    }

                    OnStateChanged?.Invoke();

                    while (!token.IsCancellationRequested && tcp.Connected)
                    {
                        string? line = await reader.ReadLineAsync();
                        if (line == null) break;
                        if (line.Length > 0)
                        {
                            _incomingPackets.Enqueue((null, line));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        StatusText = $"Connection error: {ex.Message}";
                        AddIntercom("SYSTEM", "MCC", StatusText, true);
                        OnLog?.Invoke($"[MP] {StatusText}");
                    }
                }
                finally
                {
                    if (!token.IsCancellationRequested)
                    {
                        Disconnect();
                    }
                }
            });
        }

        public static void Disconnect()
        {
            var prevMode = Mode;
            Mode = MultiplayerMode.OFFLINE;
            FlightAuthorityPeerId = "";
            _hasRemoteCsmRot = false;
            _hasRemoteLmRot = false;

            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _client?.Close(); } catch { }

            lock (_peersLock)
            {
                foreach (var p in _hostPeers)
                {
                    try { p.Client.Close(); } catch { }
                }
                _hostPeers.Clear();
            }

            lock (_roster) _roster.Clear();

            _listener = null;
            _client = null;
            _clientWriter = null;

            if (prevMode != MultiplayerMode.OFFLINE)
            {
                StatusText = "Disconnected — Single Player";
                AddIntercom("SYSTEM", "MCC", "Disconnected from multiplayer session.", true);
                OnLog?.Invoke("[MP] Disconnected.");
                OnStateChanged?.Invoke();
            }
        }

        // -------------------------------------------------------------
        // Direct P2P Host Accept & Peer Loops
        // -------------------------------------------------------------
        private static async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var tcp = await _listener.AcceptTcpClientAsync();
                    tcp.NoDelay = true;
                    var peer = new RemotePeerConnection
                    {
                        Client = tcp,
                        Writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = true }
                    };

                    lock (_peersLock) _hostPeers.Add(peer);
                    _ = Task.Run(() => PeerReadLoopAsync(peer, token));
                }
                catch when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[MP] Accept error: {ex.Message}");
                }
            }
        }

        private static async Task PeerReadLoopAsync(RemotePeerConnection peer, CancellationToken token)
        {
            try
            {
                var reader = new StreamReader(peer.Client.GetStream(), Encoding.UTF8);
                while (!token.IsCancellationRequested && peer.Client.Connected)
                {
                    string? line = await reader.ReadLineAsync();
                    if (line == null) break;
                    if (line.Length > 0)
                    {
                        _incomingPackets.Enqueue((peer.PeerId, line));
                    }
                }
            }
            catch { }
            finally
            {
                lock (_peersLock) _hostPeers.Remove(peer);
                string leftName = peer.Info.Callsign;
                string leftRole = peer.Info.Role;
                lock (_roster) _roster.Remove(peer.PeerId);

                if (FlightAuthorityPeerId == peer.PeerId)
                {
                    FlightAuthorityPeerId = LocalPeerId;
                }

                AddIntercom("SYSTEM", "MCC", $"[{leftRole}] {leftName} left the crew.", true);
                BroadcastRoster();
                OnStateChanged?.Invoke();
            }
        }

        // -------------------------------------------------------------
        // Outgoing Broadcast Helpers
        // -------------------------------------------------------------
        private static void SendToServer(string jsonLine)
        {
            if (_clientWriter == null) return;
            lock (_clientSendLock)
            {
                try { _clientWriter.WriteLine(jsonLine); } catch { }
            }
        }

        private static void SendToPeer(RemotePeerConnection peer, string jsonLine)
        {
            lock (peer.SendLock)
            {
                try { peer.Writer.WriteLine(jsonLine); } catch { }
            }
        }

        private static void BroadcastNetwork(string jsonLine, string? excludePeerId = null)
        {
            if (!IsConnected) return;

            if (Mode == MultiplayerMode.HOST)
            {
                lock (_peersLock)
                {
                    foreach (var p in _hostPeers)
                    {
                        if (excludePeerId != null && p.PeerId == excludePeerId) continue;
                        SendToPeer(p, jsonLine);
                    }
                }
            }
            else
            {
                SendToServer(jsonLine);
            }
        }

        // -------------------------------------------------------------
        // Public Crew Actions (Called from CockpitPatches / Plugin)
        // -------------------------------------------------------------
        public static void BroadcastControlChange(string craft, string kind, string id, string val)
        {
            if (!IsConnected || IsApplyingRemote) return;

            string pkt = $"{{\"t\":\"ctrl\",\"peer\":\"{LocalPeerId}\",\"from\":\"{JsonHelper.Escape(LocalCallsign)}\",\"role\":\"{JsonHelper.Escape(LocalRole)}\",\"craft\":\"{craft}\",\"kind\":\"{kind}\",\"id\":\"{JsonHelper.Escape(id)}\",\"val\":\"{JsonHelper.Escape(val)}\"}}";
            BroadcastNetwork(pkt);
        }

        public static void SendChatMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            AddIntercom(LocalCallsign, LocalRole, text, false);

            if (IsConnected)
            {
                string pkt = $"{{\"t\":\"chat\",\"peer\":\"{LocalPeerId}\",\"from\":\"{JsonHelper.Escape(LocalCallsign)}\",\"role\":\"{JsonHelper.Escape(LocalRole)}\",\"text\":\"{JsonHelper.Escape(text)}\"}}";
                BroadcastNetwork(pkt);
            }
            OnStateChanged?.Invoke();
        }

        public static void TakeFlightControl()
        {
            if (!IsConnected) return;
            FlightAuthorityPeerId = LocalPeerId;
            AddIntercom("SYSTEM", "FLIGHT", $"[{LocalRole}] {LocalCallsign} assumed Flight Control Authority.", true);
            string pkt = $"{{\"t\":\"authority\",\"peer\":\"{LocalPeerId}\",\"from\":\"{JsonHelper.Escape(LocalCallsign)}\",\"role\":\"{JsonHelper.Escape(LocalRole)}\"}}";
            BroadcastNetwork(pkt);
            if (Mode == MultiplayerMode.HOST) BroadcastRoster();
            OnStateChanged?.Invoke();
        }

        public static void RequestOrPushFullSync()
        {
            if (!IsConnected) return;
            if (IsAuthoritativeHost)
            {
                SendFullSnapshot(null);
                AddIntercom("SYSTEM", "SYNC", "Pushed full Cockpit, AGC/LGC Memory & Orbit snapshot to crew.", true);
            }
            else
            {
                BroadcastNetwork($"{{\"t\":\"req_snapshot\",\"peer\":\"{LocalPeerId}\"}}");
                AddIntercom("SYSTEM", "SYNC", "Requested full Cockpit & AGC/LGC snapshot from Host...", true);
            }
            OnStateChanged?.Invoke();
        }

        public static void BroadcastLandingSiteSync(string name, float lat, float lon, float trackDeg, bool useTrack)
        {
            if (!IsConnected || IsApplyingRemote) return;
            var inv = CultureInfo.InvariantCulture;
            string pkt = $"{{\"t\":\"sync_site\",\"peer\":\"{LocalPeerId}\",\"name\":\"{JsonHelper.Escape(name)}\",\"lat\":\"{lat.ToString(inv)}\",\"lon\":\"{lon.ToString(inv)}\",\"trk\":\"{trackDeg.ToString(inv)}\",\"ut\":\"{(useTrack ? "1" : "0")}\"}}";
            BroadcastNetwork(pkt);
        }

        public static void BroadcastBurnPlanSync(string mode, double ap, double pe, float phase, float dv, string att, string eng)
        {
            if (!IsConnected || IsApplyingRemote) return;
            var inv = CultureInfo.InvariantCulture;
            string pkt = $"{{\"t\":\"sync_burn\",\"peer\":\"{LocalPeerId}\",\"mode\":\"{JsonHelper.Escape(mode)}\",\"ap\":\"{ap.ToString(inv)}\",\"pe\":\"{pe.ToString(inv)}\",\"ph\":\"{phase.ToString(inv)}\",\"dv\":\"{dv.ToString(inv)}\",\"att\":\"{JsonHelper.Escape(att)}\",\"eng\":\"{JsonHelper.Escape(eng)}\"}}";
            BroadcastNetwork(pkt);
        }

        // -------------------------------------------------------------
        // Main Thread Tick & Physics Sync Loop
        // -------------------------------------------------------------
        public static void TickMainThread()
        {
            // 1. Process all incoming packets safely on Unity's main thread
            int processed = 0;
            while (processed++ < 64 && _incomingPackets.TryDequeue(out var item))
            {
                try
                {
                    ProcessPacket(item.sourcePeerId, item.json);
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[MP] Packet error: {ex.Message}");
                }
            }

            if (!IsConnected) return;

            // 2. Authoritative Physics & Orbit Broadcast at 10 Hz
            _physicsSendTimer += Time.deltaTime;
            if (_physicsSendTimer >= 0.10f)
            {
                _physicsSendTimer = 0f;
                if (HasFlightAuthority)
                {
                    BroadcastPhysicsSnapshot();
                }
            }

            // 3. Periodic Roster / Craft update (every 2s)
            _rosterSyncTimer += Time.deltaTime;
            if (_rosterSyncTimer >= 2.0f)
            {
                _rosterSyncTimer = 0f;
                lock (_roster)
                {
                    if (_roster.TryGetValue(LocalPeerId, out var me))
                        me.Craft = GetLocalCraftTag();
                }
                if (Mode == MultiplayerMode.HOST) BroadcastRoster();
            }
        }

        public static void FixedTick()
        {
            if (!IsConnected || HasFlightAuthority) return;

            // Smooth attitude interpolation on non-authority crewmates so cockpit view is silky smooth
            var logic = ApolloProLogicHandler.Instance;
            if (logic == null) return;

            if (_hasRemoteCsmRot && logic.apolloRocket != null && _csmRbRef != null)
            {
                var rb = _csmRbRef(logic.apolloRocket);
                if (rb != null)
                {
                    rb.MoveRotation(Quaternion.Slerp(rb.rotation, _targetCsmRot, 0.35f));
                    rb.angularVelocity = _targetCsmAngVel;
                }
            }

            if (_hasRemoteLmRot && _logicLmRef != null)
            {
                var lm = _logicLmRef(logic);
                if (lm != null && _lmRbRef != null)
                {
                    var lmRb = _lmRbRef(lm);
                    if (lmRb != null)
                    {
                        lmRb.MoveRotation(Quaternion.Slerp(lmRb.rotation, _targetLmRot, 0.35f));
                    }
                }
            }
        }

        // -------------------------------------------------------------
        // Packet Processing
        // -------------------------------------------------------------
        private static void ProcessPacket(string? sourcePeerId, string rawJson)
        {
            var d = JsonHelper.ParseObject(rawJson);
            if (!d.TryGetValue("t", out string? type) || string.IsNullOrEmpty(type)) return;

            // Ignore echoes of our own packets (e.g. from relay)
            if (d.TryGetValue("peer", out string? senderPeer) && senderPeer == LocalPeerId && type != "roster")
                return;

            // If we are Direct P2P Host, relay crew events to all other clients!
            if (Mode == MultiplayerMode.HOST && sourcePeerId != null &&
                (type == "ctrl" || type == "chat" || type == "phys" || type == "authority" || type == "sync_site" || type == "sync_burn"))
            {
                BroadcastNetwork(rawJson, excludePeerId: sourcePeerId);
            }

            switch (type)
            {
                case "hello":
                {
                    string pid = d.TryGetValue("peer", out var p) ? p : (sourcePeerId ?? Guid.NewGuid().ToString("N").Substring(0, 8));
                    string cs = d.TryGetValue("callsign", out var c) ? c : "Crew";
                    string rl = d.TryGetValue("role", out var r) ? r : "CMP";
                    string cr = d.TryGetValue("craft", out var cf) ? cf : "CSM";
                    bool isH = d.TryGetValue("isHost", out var ih) && ih == "true";

                    var info = new CrewMemberInfo { PeerId = pid, Callsign = cs, Role = rl, Craft = cr, IsHost = isH };
                    lock (_roster) _roster[pid] = info;

                    if (Mode == MultiplayerMode.HOST && sourcePeerId != null)
                    {
                        lock (_peersLock)
                        {
                            var conn = _hostPeers.Find(x => x.PeerId == sourcePeerId);
                            if (conn != null)
                            {
                                conn.PeerId = pid;
                                conn.Info = info;
                            }
                        }
                        AddIntercom("SYSTEM", "MCC", $"[{rl}] {cs} joined the spacecraft!", true);
                        BroadcastRoster();
                        SendFullSnapshot(pid);
                    }
                    else if (IsAuthoritativeHost)
                    {
                        // Relay host greets newcomer & sends full snapshot!
                        AddIntercom("SYSTEM", "MCC", $"[{rl}] {cs} joined Room {RoomCode}!", true);
                        BroadcastRoster();
                        SendFullSnapshot(null);
                    }
                    OnStateChanged?.Invoke();
                    break;
                }

                case "roster":
                {
                    if (d.TryGetValue("auth", out var authId) && !string.IsNullOrEmpty(authId))
                        FlightAuthorityPeerId = authId;

                    if (d.TryGetValue("list", out var listStr))
                    {
                        lock (_roster)
                        {
                            _roster.Clear();
                            foreach (var entry in listStr.Split(';'))
                            {
                                if (string.IsNullOrEmpty(entry)) continue;
                                var parts = entry.Split('|');
                                if (parts.Length >= 4)
                                {
                                    _roster[parts[0]] = new CrewMemberInfo
                                    {
                                        PeerId = parts[0],
                                        Callsign = parts[1],
                                        Role = parts[2],
                                        Craft = parts[3],
                                        IsHost = parts.Length > 4 && parts[4] == "1"
                                    };
                                }
                            }
                        }
                    }
                    OnStateChanged?.Invoke();
                    break;
                }

                case "chat":
                {
                    string from = d.TryGetValue("from", out var f) ? f : "Crew";
                    string role = d.TryGetValue("role", out var r) ? r : "CMP";
                    string text = d.TryGetValue("text", out var tx) ? tx : "";
                    AddIntercom(from, role, text, false);
                    OnStateChanged?.Invoke();
                    break;
                }

                case "authority":
                {
                    string pid = d.TryGetValue("peer", out var p) ? p : "";
                    string from = d.TryGetValue("from", out var f) ? f : "Crew";
                    string role = d.TryGetValue("role", out var r) ? r : "";
                    if (!string.IsNullOrEmpty(pid))
                    {
                        FlightAuthorityPeerId = pid;
                        AddIntercom("SYSTEM", "FLIGHT", $"[{role}] {from} took Flight Control Authority.", true);
                        if (Mode == MultiplayerMode.HOST) BroadcastRoster();
                        OnStateChanged?.Invoke();
                    }
                    break;
                }

                case "req_snapshot":
                {
                    if (IsAuthoritativeHost)
                    {
                        SendFullSnapshot(Mode == MultiplayerMode.HOST ? sourcePeerId : null);
                    }
                    break;
                }

                case "ctrl":
                {
                    ApplyRemoteControl(d);
                    break;
                }

                case "snapshot_part":
                {
                    ApplySnapshotPart(d);
                    break;
                }

                case "phys":
                {
                    if (!HasFlightAuthority)
                    {
                        ApplyRemotePhysics(d);
                    }
                    break;
                }

                case "sync_site":
                {
                    IsApplyingRemote = true;
                    try
                    {
                        var inv = CultureInfo.InvariantCulture;
                        string nm = d.TryGetValue("name", out var n) ? n : "Synced Site";
                        float lat = d.TryGetValue("lat", out var la) && float.TryParse(la, NumberStyles.Float, inv, out var fl) ? fl : 0f;
                        float lon = d.TryGetValue("lon", out var lo) && float.TryParse(lo, NumberStyles.Float, inv, out var fo) ? fo : 0f;
                        float trk = d.TryGetValue("trk", out var tr) && float.TryParse(tr, NumberStyles.Float, inv, out var ft) ? ft : 0f;
                        bool ut = d.TryGetValue("ut", out var u) && u == "1";
                        BurnPlannerBridge.SelectLandingSite(nm, lat, lon, trk, ut, out _);
                        AddIntercom("SYSTEM", "GNC", $"Landing site synced: {nm} ({lat:0.2}°, {lon:0.2}°)", true);
                    }
                    finally { IsApplyingRemote = false; }
                    break;
                }

                case "sync_burn":
                {
                    IsApplyingRemote = true;
                    try
                    {
                        var inv = CultureInfo.InvariantCulture;
                        string bm = d.TryGetValue("mode", out var m) ? m : "custom_orbit";
                        double ap = d.TryGetValue("ap", out var aS) && double.TryParse(aS, NumberStyles.Float, inv, out var da) ? da : 60;
                        double pe = d.TryGetValue("pe", out var pS) && double.TryParse(pS, NumberStyles.Float, inv, out var dp) ? dp : 60;
                        float ph = d.TryGetValue("ph", out var phS) && float.TryParse(phS, NumberStyles.Float, inv, out var fp) ? fp : 0;
                        float dv = d.TryGetValue("dv", out var dvS) && float.TryParse(dvS, NumberStyles.Float, inv, out var fd) ? fd : 0;
                        string att = d.TryGetValue("att", out var at) ? at : "Prograde";
                        string eng = d.TryGetValue("eng", out var en) ? en : "";
                        BurnPlannerBridge.PlanOrbitBurn(bm, ap, pe, ph, dv, att, eng);
                        AddIntercom("SYSTEM", "GNC", $"Burn plan synced: {BurnPlannerBridge.CurrentPlan.Name}", true);
                    }
                    finally { IsApplyingRemote = false; }
                    break;
                }
            }
        }

        private static void ApplyRemoteControl(Dictionary<string, string> d)
        {
            string craft = d.TryGetValue("craft", out var c) ? c : "CSM";
            string kind = d.TryGetValue("kind", out var k) ? k : "";
            string id = d.TryGetValue("id", out var i) ? i : "";
            string val = d.TryGetValue("val", out var v) ? v : "";
            string from = d.TryGetValue("from", out var f) ? f : "Crew";
            string role = d.TryGetValue("role", out var r) ? r : "";

            IsApplyingRemote = true;
            try
            {
                if (craft == "LM")
                {
                    if (kind == "pin") CockpitManager.TrySetLMPin(id, val, out _);
                    else if (kind == "fuse") CockpitManager.TrySetLMFuse(id, val, out _);
                    else if (kind == "knob" && int.TryParse(val, out int sel)) CockpitManager.TrySetLMKnob(id, sel, out _);
                    else if (kind == "btn") CockpitManager.TryPressLMButton(id, out _);
                }
                else
                {
                    if (kind == "pin") CockpitManager.TrySetPin(id, val, out _);
                    else if (kind == "fuse") CockpitManager.TrySetFuse(id, val, out _);
                    else if (kind == "knob" && int.TryParse(val, out int sel)) CockpitManager.TrySetKnob(id, sel, out _);
                    else if (kind == "btn") CockpitManager.TryPressButton(id, out _);
                }

                string desc = kind == "btn" ? $"pressed {id}" : $"set {id} → {val}";
                AddIntercom(from, role, $"[{craft}] {desc}", true);
            }
            finally
            {
                IsApplyingRemote = false;
            }
        }

        // -------------------------------------------------------------
        // Full Cockpit + AGC/LGC Memory Snapshot Sync
        // -------------------------------------------------------------
        private static void SendFullSnapshot(string? targetPeerId)
        {
            void Dispatch(string pkt)
            {
                if (Mode == MultiplayerMode.HOST && targetPeerId != null)
                {
                    lock (_peersLock)
                    {
                        var peer = _hostPeers.Find(p => p.PeerId == targetPeerId);
                        if (peer != null) SendToPeer(peer, pkt);
                    }
                }
                else
                {
                    BroadcastNetwork(pkt);
                }
            }

            // 1. CSM Pins, Fuses, Knobs
            Dispatch(BuildSnapshotPart("csm_pins", EncodeStringMap(CockpitManager.GetAllPinStates())));
            Dispatch(BuildSnapshotPart("csm_fuses", EncodeStringMap(CockpitManager.GetAllFuseStates())));
            Dispatch(BuildSnapshotPart("csm_knobs", EncodeIntMap(CockpitManager.GetAllKnobStates())));

            // 2. LM Pins, Fuses, Knobs
            if (CockpitManager.IsLMActive)
            {
                Dispatch(BuildSnapshotPart("lm_pins", EncodeStringMap(CockpitManager.GetAllLMPinStates())));
                Dispatch(BuildSnapshotPart("lm_fuses", EncodeStringMap(CockpitManager.GetAllLMFuseStates())));
                Dispatch(BuildSnapshotPart("lm_knobs", EncodeIntMap(CockpitManager.GetAllLMKnobStates())));
            }

            // 3. Full AGC (CMC) Computer Memory Bank!
            try
            {
                var agcMem = State.Current?.Internal?.AGC?.Memory?.GetEntireMemory();
                if (agcMem != null && agcMem.Count > 0)
                    Dispatch(BuildSnapshotPart("agc_mem", EncodeFloatMap(agcMem)));
            }
            catch { }

            // 4. Full LGC Computer Memory Bank!
            try
            {
                var lgcMem = State.Current?.LunarModuleState?.Internal?.LGC?.Memory?.GetEntireMemory();
                if (lgcMem != null && lgcMem.Count > 0)
                    Dispatch(BuildSnapshotPart("lgc_mem", EncodeFloatMap(lgcMem)));
            }
            catch { }

            // 5. Physics snapshot immediately after
            BroadcastPhysicsSnapshot();
        }

        private static string BuildSnapshotPart(string sec, string data)
        {
            return $"{{\"t\":\"snapshot_part\",\"peer\":\"{LocalPeerId}\",\"sec\":\"{sec}\",\"data\":\"{JsonHelper.Escape(data)}\"}}";
        }

        private static void ApplySnapshotPart(Dictionary<string, string> d)
        {
            if (!d.TryGetValue("sec", out var sec) || !d.TryGetValue("data", out var data)) return;

            IsApplyingRemote = true;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var pairs = data.Split(';');
                switch (sec)
                {
                    case "csm_pins":
                        foreach (var kv in pairs)
                        {
                            int eq = kv.IndexOf('=');
                            if (eq > 0) CockpitManager.TrySetPin(kv.Substring(0, eq), kv.Substring(eq + 1), out _);
                        }
                        AddIntercom("SYSTEM", "SYNC", "Synced all CSM switches from Host.", true);
                        break;

                    case "csm_fuses":
                        foreach (var kv in pairs)
                        {
                            int eq = kv.IndexOf('=');
                            if (eq > 0) CockpitManager.TrySetFuse(kv.Substring(0, eq), kv.Substring(eq + 1), out _);
                        }
                        break;

                    case "csm_knobs":
                        foreach (var kv in pairs)
                        {
                            int eq = kv.IndexOf('=');
                            if (eq > 0 && int.TryParse(kv.Substring(eq + 1), out int sel))
                                CockpitManager.TrySetKnob(kv.Substring(0, eq), sel, out _);
                        }
                        break;

                    case "lm_pins":
                        foreach (var kv in pairs)
                        {
                            int eq = kv.IndexOf('=');
                            if (eq > 0) CockpitManager.TrySetLMPin(kv.Substring(0, eq), kv.Substring(eq + 1), out _);
                        }
                        AddIntercom("SYSTEM", "SYNC", "Synced all LM switches from Host.", true);
                        break;

                    case "lm_fuses":
                        foreach (var kv in pairs)
                        {
                            int eq = kv.IndexOf('=');
                            if (eq > 0) CockpitManager.TrySetLMFuse(kv.Substring(0, eq), kv.Substring(eq + 1), out _);
                        }
                        break;

                    case "lm_knobs":
                        foreach (var kv in pairs)
                        {
                            int eq = kv.IndexOf('=');
                            if (eq > 0 && int.TryParse(kv.Substring(eq + 1), out int sel))
                                CockpitManager.TrySetLMKnob(kv.Substring(0, eq), sel, out _);
                        }
                        break;

                    case "agc_mem":
                    {
                        var map = new Dictionary<int, float>();
                        foreach (var kv in pairs)
                        {
                            int eq = kv.IndexOf('=');
                            if (eq > 0 &&
                                int.TryParse(kv.Substring(0, eq), out int key) &&
                                float.TryParse(kv.Substring(eq + 1), NumberStyles.Float, inv, out float val))
                            {
                                map[key] = val;
                            }
                        }
                        if (map.Count > 0 && State.Current?.Internal?.AGC?.Memory != null)
                        {
                            State.Current.Internal.AGC.Memory.SetEntireMemory(map);
                            AddIntercom("SYSTEM", "SYNC", $"Synced AGC Computer Memory ({map.Count} registers).", true);
                        }
                        break;
                    }

                    case "lgc_mem":
                    {
                        var map = new Dictionary<int, float>();
                        foreach (var kv in pairs)
                        {
                            int eq = kv.IndexOf('=');
                            if (eq > 0 &&
                                int.TryParse(kv.Substring(0, eq), out int key) &&
                                float.TryParse(kv.Substring(eq + 1), NumberStyles.Float, inv, out float val))
                            {
                                map[key] = val;
                            }
                        }
                        if (map.Count > 0 && State.Current?.LunarModuleState?.Internal?.LGC?.Memory != null)
                        {
                            State.Current.LunarModuleState.Internal.LGC.Memory.SetEntireMemory(map);
                            AddIntercom("SYSTEM", "SYNC", $"Synced LGC Computer Memory ({map.Count} registers).", true);
                        }
                        break;
                    }
                }
            }
            finally
            {
                IsApplyingRemote = false;
            }
        }

        // -------------------------------------------------------------
        // Authoritative Orbital, Attitude & Engine Physics Sync (10 Hz)
        // -------------------------------------------------------------
        private static void BroadcastPhysicsSnapshot()
        {
            var logic = ApolloProLogicHandler.Instance;
            if (logic == null || State.Current == null) return;

            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(512);
            sb.Append($"{{\"t\":\"phys\",\"peer\":\"{LocalPeerId}\",");
            sb.Append($"\"met\":\"{ApolloGameTimeHandler.TimeSinceLaunch.TotalSeconds.ToString("0.###", inv)}\",");

            var ge = GravityEngine.Instance();

            // CSM NBody + Attitude
            if (logic.apolloRocket != null)
            {
                var csmBody = _csmBodyRef != null ? _csmBodyRef(logic.apolloRocket) : null;
                if (ge != null && csmBody != null)
                {
                    var p = ge.GetPositionDoubleV3(csmBody);
                    var v = ge.GetVelocityDoubleV3(csmBody);
                    sb.Append($"\"cp\":\"{p.x.ToString("R", inv)},{p.y.ToString("R", inv)},{p.z.ToString("R", inv)}\",");
                    sb.Append($"\"cv\":\"{v.x.ToString("R", inv)},{v.y.ToString("R", inv)},{v.z.ToString("R", inv)}\",");
                }

                var rb = _csmRbRef != null ? _csmRbRef(logic.apolloRocket) : null;
                if (rb != null)
                {
                    var q = rb.rotation;
                    var av = rb.angularVelocity;
                    sb.Append($"\"cr\":\"{q.x.ToString("0.####", inv)},{q.y.ToString("0.####", inv)},{q.z.ToString("0.####", inv)},{q.w.ToString("0.####", inv)}\",");
                    sb.Append($"\"ca\":\"{av.x.ToString("0.###", inv)},{av.y.ToString("0.###", inv)},{av.z.ToString("0.###", inv)}\",");
                }
            }

            // LM NBody + Attitude + DPS/APS
            var lm = _logicLmRef != null ? _logicLmRef(logic) : null;
            if (lm != null && State.Current.LunarModuleState != null)
            {
                var lmBody = lm.GetComponent<NBody>();
                if (ge != null && lmBody != null)
                {
                    var p = ge.GetPositionDoubleV3(lmBody);
                    var v = ge.GetVelocityDoubleV3(lmBody);
                    sb.Append($"\"lp\":\"{p.x.ToString("R", inv)},{p.y.ToString("R", inv)},{p.z.ToString("R", inv)}\",");
                    sb.Append($"\"lv\":\"{v.x.ToString("R", inv)},{v.y.ToString("R", inv)},{v.z.ToString("R", inv)}\",");
                }

                var lmRb = _lmRbRef != null ? _lmRbRef(lm) : null;
                if (lmRb != null)
                {
                    var q = lmRb.rotation;
                    sb.Append($"\"lr\":\"{q.x.ToString("0.####", inv)},{q.y.ToString("0.####", inv)},{q.z.ToString("0.####", inv)},{q.w.ToString("0.####", inv)}\",");
                }

                var dps = State.Current.LunarModuleState.DPS;
                var aps = State.Current.LunarModuleState.APS;
                sb.Append($"\"dps\":\"{(dps != null && dps.IsThrusting() ? dps.GetAutomaticThrust().ToString("0.##", inv) : "-1")}\",");
                sb.Append($"\"aps\":\"{(aps != null && aps.IsThrusting() ? "1" : "0")}\",");
            }

            sb.Append("\"ok\":\"1\"}");
            BroadcastNetwork(sb.ToString());
        }

        private static void ApplyRemotePhysics(Dictionary<string, string> d)
        {
            var logic = ApolloProLogicHandler.Instance;
            if (logic == null || State.Current == null) return;

            var inv = CultureInfo.InvariantCulture;
            var ge = GravityEngine.Instance();

            // Sync MET clock if drifted > 0.5s
            if (d.TryGetValue("met", out var metS) && double.TryParse(metS, NumberStyles.Float, inv, out double remoteMet))
            {
                if (Math.Abs(ApolloGameTimeHandler.TimeSinceLaunch.TotalSeconds - remoteMet) > 0.5)
                {
                    ApolloGameTimeHandler.TimeSinceLaunch = TimeSpan.FromSeconds(remoteMet);
                }
            }

            // CSM Orbit + Attitude
            if (logic.apolloRocket != null)
            {
                if (ge != null && d.TryGetValue("cp", out var cpS) && d.TryGetValue("cv", out var cvS) &&
                    TryParseV3d(cpS, out Vector3d cp) && TryParseV3d(cvS, out Vector3d cv))
                {
                    var csmBody = _csmBodyRef != null ? _csmBodyRef(logic.apolloRocket) : null;
                    if (csmBody != null)
                    {
                        var curP = ge.GetPositionDoubleV3(csmBody);
                        if ((curP - cp).magnitude > 0.0002)
                        {
                            ge.UpdatePositionAndVelocity(csmBody, cp, cv);
                        }
                    }
                }

                if (d.TryGetValue("cr", out var crS) && TryParseQuat(crS, out Quaternion cr))
                {
                    _targetCsmRot = cr;
                    _hasRemoteCsmRot = true;
                }
                if (d.TryGetValue("ca", out var caS) && TryParseV3(caS, out Vector3 ca))
                {
                    _targetCsmAngVel = ca;
                }
            }

            // LM Orbit + Attitude + DPS/APS
            var lm = _logicLmRef != null ? _logicLmRef(logic) : null;
            if (lm != null && State.Current.LunarModuleState != null)
            {
                if (ge != null && d.TryGetValue("lp", out var lpS) && d.TryGetValue("lv", out var lvS) &&
                    TryParseV3d(lpS, out Vector3d lp) && TryParseV3d(lvS, out Vector3d lv))
                {
                    var lmBody = lm.GetComponent<NBody>();
                    if (lmBody != null)
                    {
                        var curP = ge.GetPositionDoubleV3(lmBody);
                        if ((curP - lp).magnitude > 0.0002)
                        {
                            ge.UpdatePositionAndVelocity(lmBody, lp, lv);
                        }
                    }
                }

                if (d.TryGetValue("lr", out var lrS) && TryParseQuat(lrS, out Quaternion lr))
                {
                    _targetLmRot = lr;
                    _hasRemoteLmRot = true;
                }

                if (d.TryGetValue("dps", out var dpsS) && float.TryParse(dpsS, NumberStyles.Float, inv, out float dpsThr) && State.Current.LunarModuleState.DPS != null)
                {
                    var dps = State.Current.LunarModuleState.DPS;
                    if (dpsThr >= 0f)
                    {
                        if (!dps.IsArmed()) dps.ArmEngine();
                        dps.SetAutomaticThrust(dpsThr);
                        if (!dps.IsThrusting()) dps.Ignite();
                    }
                    else if (dps.IsThrusting())
                    {
                        dps.SetAutomaticThrust(0f);
                        dps.Cutoff();
                    }
                }
            }
        }

        // -------------------------------------------------------------
        // Helpers & JSON Serialization for Web Dashboard
        // -------------------------------------------------------------
        private static string BuildHelloPacket(bool asHost)
        {
            return $"{{\"t\":\"hello\",\"peer\":\"{LocalPeerId}\",\"callsign\":\"{JsonHelper.Escape(LocalCallsign)}\",\"role\":\"{JsonHelper.Escape(LocalRole)}\",\"craft\":\"{GetLocalCraftTag()}\",\"room\":\"{JsonHelper.Escape(RoomCode)}\",\"isHost\":\"{(asHost ? "true" : "false")}\"}}";
        }

        private static void BroadcastRoster()
        {
            var sb = new StringBuilder();
            lock (_roster)
            {
                int i = 0;
                foreach (var kv in _roster)
                {
                    var m = kv.Value;
                    if (i++ > 0) sb.Append(';');
                    sb.Append($"{m.PeerId}|{m.Callsign.Replace("|", "").Replace(";", "")}|{m.Role}|{m.Craft}|{(m.IsHost ? "1" : "0")}");
                }
            }
            string pkt = $"{{\"t\":\"roster\",\"auth\":\"{FlightAuthorityPeerId}\",\"list\":\"{JsonHelper.Escape(sb.ToString())}\"}}";
            BroadcastNetwork(pkt);
        }

        private static string GetLocalCraftTag()
        {
            return State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule ? "LM" : "CSM";
        }

        private static void AddIntercom(string from, string role, string text, bool isSystem)
        {
            lock (_intercom)
            {
                _intercom.Add(new IntercomEntry
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    From = from,
                    Role = role,
                    Text = text,
                    IsSystem = isSystem
                });
                if (_intercom.Count > 50) _intercom.RemoveAt(0);
            }
        }

        private static string EncodeStringMap(Dictionary<string, string> map)
        {
            var sb = new StringBuilder(map.Count * 18);
            int i = 0;
            foreach (var kv in map)
            {
                if (i++ > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        private static string EncodeIntMap(Dictionary<string, int> map)
        {
            var sb = new StringBuilder(map.Count * 16);
            int i = 0;
            foreach (var kv in map)
            {
                if (i++ > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        private static string EncodeFloatMap(Dictionary<int, float> map)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(map.Count * 14);
            int i = 0;
            foreach (var kv in map)
            {
                if (i++ > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("G7", inv));
            }
            return sb.ToString();
        }

        private static bool TryParseV3d(string s, out Vector3d v)
        {
            v = Vector3d.zero;
            var p = s.Split(',');
            var inv = CultureInfo.InvariantCulture;
            if (p.Length == 3 &&
                double.TryParse(p[0], NumberStyles.Float, inv, out double x) &&
                double.TryParse(p[1], NumberStyles.Float, inv, out double y) &&
                double.TryParse(p[2], NumberStyles.Float, inv, out double z))
            {
                v = new Vector3d(x, y, z);
                return true;
            }
            return false;
        }

        private static bool TryParseV3(string s, out Vector3 v)
        {
            v = Vector3.zero;
            var p = s.Split(',');
            var inv = CultureInfo.InvariantCulture;
            if (p.Length == 3 &&
                float.TryParse(p[0], NumberStyles.Float, inv, out float x) &&
                float.TryParse(p[1], NumberStyles.Float, inv, out float y) &&
                float.TryParse(p[2], NumberStyles.Float, inv, out float z))
            {
                v = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        private static bool TryParseQuat(string s, out Quaternion q)
        {
            q = Quaternion.identity;
            var p = s.Split(',');
            var inv = CultureInfo.InvariantCulture;
            if (p.Length == 4 &&
                float.TryParse(p[0], NumberStyles.Float, inv, out float x) &&
                float.TryParse(p[1], NumberStyles.Float, inv, out float y) &&
                float.TryParse(p[2], NumberStyles.Float, inv, out float z) &&
                float.TryParse(p[3], NumberStyles.Float, inv, out float w))
            {
                q = new Quaternion(x, y, z, w);
                return true;
            }
            return false;
        }

        public static string BuildMultiplayerJson()
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"type\":\"multiplayer\",\"data\":{");
            sb.Append($"\"mode\":\"{Mode}\",");
            sb.Append($"\"connected\":{(IsConnected ? "true" : "false")},");
            sb.Append($"\"peerId\":\"{LocalPeerId}\",");
            sb.Append($"\"callsign\":\"{JsonHelper.Escape(LocalCallsign)}\",");
            sb.Append($"\"role\":\"{JsonHelper.Escape(LocalRole)}\",");
            sb.Append($"\"room\":\"{JsonHelper.Escape(RoomCode)}\",");
            sb.Append($"\"endpoint\":\"{JsonHelper.Escape(RemoteEndpoint)}\",");
            sb.Append($"\"status\":\"{JsonHelper.Escape(StatusText)}\",");
            sb.Append($"\"authorityId\":\"{JsonHelper.Escape(FlightAuthorityPeerId)}\",");
            sb.Append($"\"hasAuthority\":{(HasFlightAuthority ? "true" : "false")},");

            sb.Append("\"crew\":[");
            lock (_roster)
            {
                int i = 0;
                foreach (var kv in _roster)
                {
                    var m = kv.Value;
                    if (i++ > 0) sb.Append(",");
                    sb.Append("{");
                    sb.Append($"\"id\":\"{m.PeerId}\",");
                    sb.Append($"\"callsign\":\"{JsonHelper.Escape(m.Callsign)}\",");
                    sb.Append($"\"role\":\"{JsonHelper.Escape(m.Role)}\",");
                    sb.Append($"\"craft\":\"{JsonHelper.Escape(m.Craft)}\",");
                    sb.Append($"\"isHost\":{(m.IsHost ? "true" : "false")},");
                    sb.Append($"\"hasAuth\":{(m.PeerId == FlightAuthorityPeerId ? "true" : "false")}");
                    sb.Append("}");
                }
            }
            sb.Append("],");

            sb.Append("\"intercom\":[");
            lock (_intercom)
            {
                for (int i = 0; i < _intercom.Count; i++)
                {
                    var e = _intercom[i];
                    if (i > 0) sb.Append(",");
                    sb.Append("{");
                    sb.Append($"\"time\":\"{e.Time}\",");
                    sb.Append($"\"from\":\"{JsonHelper.Escape(e.From)}\",");
                    sb.Append($"\"role\":\"{JsonHelper.Escape(e.Role)}\",");
                    sb.Append($"\"text\":\"{JsonHelper.Escape(e.Text)}\",");
                    sb.Append($"\"sys\":{(e.IsSystem ? "true" : "false")}");
                    sb.Append("}");
                }
            }
            sb.Append("]}}");
            return sb.ToString();
        }
    }
}
