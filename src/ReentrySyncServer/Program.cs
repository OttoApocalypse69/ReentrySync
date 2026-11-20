using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 7777;
var rooms = new ConcurrentDictionary<string, RoomState>(StringComparer.OrdinalIgnoreCase);

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();

Console.WriteLine("============================================================");
Console.WriteLine("  REENTRY SYNC — DEDICATED MULTIPLAYER RELAY SERVER");
Console.WriteLine($"  Listening on TCP 0.0.0.0:{port}");
Console.WriteLine("  Crews can Host/Join using any Room Code (e.g. APOLLO11)");
Console.WriteLine("============================================================");

while (true)
{
    var tcp = await listener.AcceptTcpClientAsync();
    tcp.NoDelay = true;
    _ = Task.Run(() => HandleClientAsync(tcp));
}

async Task HandleClientAsync(TcpClient tcp)
{
    string endpoint = tcp.Client.RemoteEndPoint?.ToString() ?? "unknown";
    var conn = new ClientConn
    {
        Client = tcp,
        Writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = true }
    };

    RoomState? joinedRoom = null;

    try
    {
        using var reader = new StreamReader(tcp.GetStream(), Encoding.UTF8);
        while (tcp.Connected)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? type = TryGetJsonString(line, "t");

            if (type == "hello")
            {
                string roomCode = (TryGetJsonString(line, "room") ?? "DEFAULT").Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(roomCode)) roomCode = "DEFAULT";

                conn.PeerId = TryGetJsonString(line, "peer") ?? Guid.NewGuid().ToString("N")[..8];
                conn.Callsign = TryGetJsonString(line, "callsign") ?? "Crew";
                conn.Role = TryGetJsonString(line, "role") ?? "CMP";
                conn.Craft = TryGetJsonString(line, "craft") ?? "CSM";
                conn.IsHost = string.Equals(TryGetJsonString(line, "isHost"), "true", StringComparison.OrdinalIgnoreCase);

                joinedRoom = rooms.GetOrAdd(roomCode, code => new RoomState(code));
                joinedRoom.AddClient(conn);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] + [{conn.Role}] {conn.Callsign} ({endpoint}) joined room '{roomCode}' (Crew: {joinedRoom.Count})");

                // Broadcast updated roster to everyone in the room, then forward hello so Host sends snapshot!
                joinedRoom.BroadcastRoster();
                joinedRoom.Broadcast(line, exclude: conn);
                continue;
            }

            if (joinedRoom == null) continue;

            if (type == "authority")
            {
                string? newAuth = TryGetJsonString(line, "peer");
                if (!string.IsNullOrEmpty(newAuth))
                {
                    joinedRoom.AuthorityPeerId = newAuth;
                    joinedRoom.Broadcast(line, exclude: conn);
                    joinedRoom.BroadcastRoster();
                    continue;
                }
            }

            // Relay all cockpit, physics, snapshot, burn, landing site, and chat packets to room crewmates!
            joinedRoom.Broadcast(line, exclude: conn);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ! Connection {endpoint} ended: {ex.Message}");
    }
    finally
    {
        if (joinedRoom != null)
        {
            joinedRoom.RemoveClient(conn);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] - [{conn.Role}] {conn.Callsign} left room '{joinedRoom.Code}' (Crew: {joinedRoom.Count})");
            if (joinedRoom.Count == 0)
            {
                rooms.TryRemove(joinedRoom.Code, out _);
            }
            else
            {
                joinedRoom.Broadcast($"{{\"t\":\"chat\",\"peer\":\"relay\",\"from\":\"MCC\",\"role\":\"RELAY\",\"text\":\"[{conn.Role}] {conn.Callsign} disconnected.\"}}", null);
                joinedRoom.BroadcastRoster();
            }
        }
        try { tcp.Close(); } catch { }
    }
}

static string? TryGetJsonString(string json, string prop)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty(prop, out var el))
        {
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        }
    }
    catch { }
    return null;
}

class ClientConn
{
    public TcpClient Client { get; set; } = null!;
    public StreamWriter Writer { get; set; } = null!;
    public object SendLock { get; } = new();
    public string PeerId { get; set; } = "";
    public string Callsign { get; set; } = "Crew";
    public string Role { get; set; } = "CMP";
    public string Craft { get; set; } = "CSM";
    public bool IsHost { get; set; }

    public void Send(string line)
    {
        lock (SendLock)
        {
            try { Writer.WriteLine(line); } catch { }
        }
    }
}

class RoomState
{
    public string Code { get; }
    public string AuthorityPeerId { get; set; } = "";
    private readonly List<ClientConn> _clients = new();
    private readonly object _lock = new();

    public RoomState(string code) => Code = code;
    public int Count { get { lock (_lock) return _clients.Count; } }

    public void AddClient(ClientConn c)
    {
        lock (_lock)
        {
            _clients.RemoveAll(x => x.PeerId == c.PeerId);
            if (_clients.Count == 0 || c.IsHost)
            {
                c.IsHost = true;
                if (string.IsNullOrEmpty(AuthorityPeerId)) AuthorityPeerId = c.PeerId;
            }
            _clients.Add(c);
        }
    }

    public void RemoveClient(ClientConn c)
    {
        lock (_lock)
        {
            _clients.Remove(c);
            if (_clients.Count > 0 && (c.IsHost || AuthorityPeerId == c.PeerId))
            {
                _clients[0].IsHost = true;
                AuthorityPeerId = _clients[0].PeerId;
            }
        }
    }

    public void Broadcast(string line, ClientConn? exclude)
    {
        List<ClientConn> snap;
        lock (_lock) snap = new List<ClientConn>(_clients);
        foreach (var c in snap)
        {
            if (exclude != null && ReferenceEquals(c, exclude)) continue;
            c.Send(line);
        }
    }

    public void BroadcastRoster()
    {
        var sb = new StringBuilder();
        List<ClientConn> snap;
        string auth;
        lock (_lock)
        {
            snap = new List<ClientConn>(_clients);
            auth = AuthorityPeerId;
            for (int i = 0; i < snap.Count; i++)
            {
                var m = snap[i];
                if (i > 0) sb.Append(';');
                sb.Append($"{m.PeerId}|{m.Callsign.Replace("|", "").Replace(";", "")}|{m.Role}|{m.Craft}|{(m.IsHost ? "1" : "0")}");
            }
        }
        string pkt = $"{{\"t\":\"roster\",\"auth\":\"{auth}\",\"list\":\"{sb}\"}}";
        foreach (var c in snap) c.Send(pkt);
    }
}
