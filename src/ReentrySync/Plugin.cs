using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Apollo;
using BepInEx;
using HarmonyLib;
using ReentrySync.Core;
using ReentrySync.Network;
using ReentrySync.Patches;
using UnityEngine;

namespace ReentrySync
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony? _harmony;
        private LocalWebServer? _server;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private float _telemetryTimer;
        private float _checklistTimer;

        private void Awake()
        {
            Logger.LogInfo($"Initializing {PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION}...");

            // 1. Harmony Patches
            try
            {
                _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
                _harmony.PatchAll();
                Logger.LogInfo("Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error applying Harmony patches: {ex.Message}");
            }

            // 2. Local Web & WebSocket Server
            try
            {
                _server = new LocalWebServer(httpPort: 8080, wsPort: 8081);
                _server.OnLog += msg => Logger.LogInfo(msg);
                _server.OnMessageReceived += HandleIncomingWebSocketMessage;
                _server.OnClientConnected += () =>
                {
                    _mainThreadQueue.Enqueue(() =>
                    {
                        BroadcastAllSwitches();
                        BroadcastChecklistState();
                        BroadcastDirectorAndAutopilot();
                    });
                };
                _server.Start();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error starting web server: {ex.Message}");
            }

            // 3. Cockpit Events -> WebSocket & Multiplayer Broadcast
            CockpitPatches.OnSwitchChanged += (id, pos) =>
            {
                _server?.Broadcast($"{{\"type\":\"switch_event\",\"id\":\"{JsonHelper.Escape(id)}\",\"pos\":\"{JsonHelper.Escape(pos)}\"}}");
                MultiplayerManager.BroadcastControlChange("CSM", "pin", id, pos);
            };

            CockpitPatches.OnButtonPressed += id =>
            {
                _server?.Broadcast($"{{\"type\":\"button_event\",\"id\":\"{JsonHelper.Escape(id)}\"}}");
                MultiplayerManager.BroadcastControlChange("CSM", "btn", id, "1");
            };

            CockpitPatches.OnKnobRotated += (id, sel) =>
            {
                _server?.Broadcast($"{{\"type\":\"knob_event\",\"id\":\"{JsonHelper.Escape(id)}\",\"selection\":{sel}}}");
                MultiplayerManager.BroadcastControlChange("CSM", "knob", id, sel.ToString());
            };

            CockpitPatches.OnFuseChanged += (id, pos) =>
            {
                _server?.Broadcast($"{{\"type\":\"fuse_event\",\"id\":\"{JsonHelper.Escape(id)}\",\"pos\":\"{JsonHelper.Escape(pos)}\"}}");
                MultiplayerManager.BroadcastControlChange("CSM", "fuse", id, pos);
            };

            // LM Cockpit Events -> Multiplayer Broadcast
            CockpitPatches.OnLMSwitchChanged += (id, pos) => MultiplayerManager.BroadcastControlChange("LM", "pin", id, pos);
            CockpitPatches.OnLMButtonPressed += id => MultiplayerManager.BroadcastControlChange("LM", "btn", id, "1");
            CockpitPatches.OnLMKnobRotated += (id, sel) => MultiplayerManager.BroadcastControlChange("LM", "knob", id, sel.ToString());
            CockpitPatches.OnLMFuseChanged += (id, pos) => MultiplayerManager.BroadcastControlChange("LM", "fuse", id, pos);

            MultiplayerManager.OnLog += msg => Logger.LogInfo(msg);
            MultiplayerManager.OnStateChanged += () =>
            {
                _mainThreadQueue.Enqueue(() => _server?.Broadcast(MultiplayerManager.BuildMultiplayerJson()));
            };

            CockpitPatches.OnChecklistUpdated += () =>
            {
                _mainThreadQueue.Enqueue(BroadcastChecklistState);
            };

            SmartChecklistEngine.OnCopilotActivity += status =>
            {
                Logger.LogInfo($"[Copilot] {status}");
                _server?.Broadcast($"{{\"type\":\"copilot_status\",\"active\":{(SmartChecklistEngine.IsAutoRunning ? "true" : "false")},\"holding\":{(SmartChecklistEngine.IsHolding ? "true" : "false")},\"text\":\"{JsonHelper.Escape(status)}\"}}");
            };

            Logger.LogInfo($"{PluginInfo.PLUGIN_NAME} is active! Open http://localhost:8080/ in your browser.");
        }

        private void HandleIncomingWebSocketMessage(string rawJson)
        {
            try
            {
                var dict = JsonHelper.ParseObject(rawJson);
                if (!dict.TryGetValue("action", out string? action) || string.IsNullOrEmpty(action))
                    return;

                switch (action)
                {
                    case "set_pin":
                    {
                        if (dict.TryGetValue("id", out string? id) && dict.TryGetValue("pos", out string? pos))
                        {
                            _mainThreadQueue.Enqueue(() =>
                            {
                                if (CockpitManager.TrySetPin(id, pos, out string err))
                                {
                                    MultiplayerManager.BroadcastControlChange("CSM", "pin", id, pos);
                                }
                                else if (!string.IsNullOrEmpty(err))
                                {
                                    Logger.LogWarning($"[SetPin] Error: {err}");
                                }
                            });
                        }
                        break;
                    }

                    case "dsky_key":
                    {
                        if (dict.TryGetValue("key", out string? key) && !string.IsNullOrEmpty(key))
                        {
                            _mainThreadQueue.Enqueue(() =>
                            {
                                DSKYBridge.PressKey(key, out string err);
                                if (!string.IsNullOrEmpty(err))
                                {
                                    Logger.LogWarning($"[DSKY] Error: {err}");
                                }
                            });
                        }
                        break;
                    }

                    case "get_switches":
                    {
                        _mainThreadQueue.Enqueue(BroadcastAllSwitches);
                        break;
                    }

                    case "get_checklist":
                    {
                        _mainThreadQueue.Enqueue(BroadcastChecklistState);
                        break;
                    }

                    case "load_checklist":
                    {
                        if (dict.TryGetValue("path", out string? path) && !string.IsNullOrEmpty(path))
                        {
                            _mainThreadQueue.Enqueue(() =>
                            {
                                bool success = SmartChecklistEngine.LoadChecklistByPath(path, out string err);
                                Logger.LogInfo(success ? $"[Checklist] Loaded: {path}" : $"[Checklist] Load error: {err}");
                                BroadcastChecklistState();
                            });
                        }
                        break;
                    }

                    case "checklist_next":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            SmartChecklistEngine.ExecuteNextStep(out string summary);
                            Logger.LogInfo($"[Checklist] {summary}");
                            BroadcastChecklistState();
                        });
                        break;
                    }

                    case "checklist_auto_start":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            SmartChecklistEngine.StartAutoRunner(this);
                            Logger.LogInfo("[Checklist] Auto-Copilot started.");
                            BroadcastChecklistState();
                        });
                        break;
                    }

                    case "checklist_auto_stop":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            SmartChecklistEngine.StopAutoRunner(this);
                            Logger.LogInfo("[Checklist] Auto-Copilot stopped.");
                            BroadcastChecklistState();
                        });
                        break;
                    }

                    case "checklist_clear":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            SmartChecklistEngine.StopAutoRunner(this);
                            SmartChecklistEngine.ClearChecklist();
                            Logger.LogInfo("[Checklist] Cleared.");
                            BroadcastChecklistState();
                        });
                        break;
                    }

                    case "checklist_speed":
                    {
                        if (dict.TryGetValue("speed", out string? speedStr) &&
                            float.TryParse(speedStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float spd))
                        {
                            _mainThreadQueue.Enqueue(() =>
                            {
                                SmartChecklistEngine.CopilotPacingDelay = Mathf.Clamp(spd, 0.2f, 3.0f);
                                Logger.LogInfo($"[Checklist] Copilot pacing speed set to {SmartChecklistEngine.CopilotPacingDelay:0.00}s");
                                BroadcastChecklistState();
                            });
                        }
                        break;
                    }

                    case "checklist_timestamps":
                    {
                        bool en = !dict.TryGetValue("enabled", out var ev) || ev.Equals("true", StringComparison.OrdinalIgnoreCase);
                        _mainThreadQueue.Enqueue(() =>
                        {
                            SmartChecklistEngine.RespectTimestamps = en;
                            Logger.LogInfo($"[Checklist] Respect Timestamps: {(en ? "ON" : "OFF")}");
                            BroadcastChecklistState();
                        });
                        break;
                    }

                    case "checklist_skip_hold":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            SmartChecklistEngine.SkipCurrentHold();
                            BroadcastChecklistState();
                        });
                        break;
                    }

                    // --- MET-Aware Mission Director ---
                    case "director_toggle":
                    {
                        bool en = dict.TryGetValue("enabled", out string? ev) && ev.Equals("true", StringComparison.OrdinalIgnoreCase);
                        _mainThreadQueue.Enqueue(() =>
                        {
                            MissionDirector.AutoDirectorEnabled = en;
                            Logger.LogInfo($"[Director] Autonomous Mission Director: {(en ? "ENABLED" : "DISABLED")}");
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    case "director_run_recommended":
                    {
                        bool autoExec = !dict.TryGetValue("auto", out string? av) || av.Equals("true", StringComparison.OrdinalIgnoreCase);
                        _mainThreadQueue.Enqueue(() =>
                        {
                            MissionDirector.RunRecommendedChecklist(this, autoExec, out string msg);
                            Logger.LogInfo($"[Director] {msg}");
                            BroadcastChecklistState();
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    case "director_reset":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            MissionDirector.ResetCompletedHistory();
                            Logger.LogInfo("[Director] Timeline history reset.");
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    // --- Full-Flight Autopilot & GNC ---
                    case "autopilot_mode":
                    {
                        if (dict.TryGetValue("mode", out string? modeStr) && Enum.TryParse(modeStr, true, out AutopilotMode apMode))
                        {
                            bool imm = dict.TryGetValue("immediate", out string? imv) && imv.Equals("true", StringComparison.OrdinalIgnoreCase);
                            float tgtOrb = 60f;
                            if (dict.TryGetValue("targetOrbitNm", out string? orbStr))
                                float.TryParse(orbStr, NumberStyles.Float, CultureInfo.InvariantCulture, out tgtOrb);

                            _mainThreadQueue.Enqueue(() =>
                            {
                                if (tgtOrb > 5f) FlightAutopilot.TargetAscentOrbitNm = tgtOrb;
                                FlightAutopilot.SetMode(apMode, imm);
                                Logger.LogInfo($"[Autopilot] Mode -> {apMode} ({FlightAutopilot.PhaseDetail})");
                                BroadcastDirectorAndAutopilot();
                            });
                        }
                        break;
                    }

                    case "autopilot_align":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            FlightAutopilot.AlignPlatformsNow(out string msg);
                            Logger.LogInfo($"[Autopilot] {msg}");
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    // --- Burn Planner & Lunar Landing Site ---
                    case "plan_burn":
                    {
                        string bMode = dict.TryGetValue("mode", out string? bm) ? bm : "custom_orbit";
                        double tAp = 60, tPe = 60;
                        float ph = 0, dv = 0;
                        if (dict.TryGetValue("targetAp", out string? apS)) double.TryParse(apS, NumberStyles.Float, CultureInfo.InvariantCulture, out tAp);
                        if (dict.TryGetValue("targetPe", out string? peS)) double.TryParse(peS, NumberStyles.Float, CultureInfo.InvariantCulture, out tPe);
                        if (dict.TryGetValue("phase", out string? phS)) float.TryParse(phS, NumberStyles.Float, CultureInfo.InvariantCulture, out ph);
                        if (dict.TryGetValue("dv", out string? dvS)) float.TryParse(dvS, NumberStyles.Float, CultureInfo.InvariantCulture, out dv);
                        string att = dict.TryGetValue("attitude", out string? atS) ? atS : "Prograde";
                        string eng = dict.TryGetValue("engine", out string? enS) ? enS : "";

                        _mainThreadQueue.Enqueue(() =>
                        {
                            var plan = BurnPlannerBridge.PlanOrbitBurn(bMode, tAp, tPe, ph, dv, att, eng);
                            MultiplayerManager.BroadcastBurnPlanSync(bMode, tAp, tPe, ph, dv, att, eng);
                            Logger.LogInfo($"[BurnPlanner] {plan.StatusMessage}");
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    case "upload_burn_agc":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            BurnPlannerBridge.UploadPlanToAGC(out string msg);
                            Logger.LogInfo($"[BurnPlanner] {msg}");
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    case "select_landing_site":
                    {
                        string siteName = dict.TryGetValue("name", out string? sn) ? sn : "Custom Site";
                        float lat = 0, lon = 0, trackDeg = 0;
                        if (dict.TryGetValue("lat", out string? laS)) float.TryParse(laS, NumberStyles.Float, CultureInfo.InvariantCulture, out lat);
                        if (dict.TryGetValue("lon", out string? loS)) float.TryParse(loS, NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
                        if (dict.TryGetValue("trackDeg", out string? trS)) float.TryParse(trS, NumberStyles.Float, CultureInfo.InvariantCulture, out trackDeg);
                        bool useTrack = dict.TryGetValue("useTrack", out string? utS) && utS.Equals("true", StringComparison.OrdinalIgnoreCase);

                        _mainThreadQueue.Enqueue(() =>
                        {
                            if (BurnPlannerBridge.SelectLandingSite(siteName, lat, lon, trackDeg, useTrack, out string msg))
                            {
                                MultiplayerManager.BroadcastLandingSiteSync(siteName, lat, lon, trackDeg, useTrack);
                            }
                            Logger.LogInfo($"[LandingSite] {msg}");
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    // --- Crew Co-op Multiplayer Commands ---
                    case "mp_host":
                    {
                        int port = dict.TryGetValue("port", out var ps) && int.TryParse(ps, out var pv) ? pv : 7777;
                        string cs = dict.TryGetValue("callsign", out var c) ? c : "Commander";
                        string rl = dict.TryGetValue("role", out var r) ? r : "CDR";
                        _mainThreadQueue.Enqueue(() =>
                        {
                            MultiplayerManager.StartDirectHost(port, cs, rl);
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    case "mp_join":
                    {
                        string host = dict.TryGetValue("host", out var hs) ? hs.Trim() : "127.0.0.1";
                        int port = dict.TryGetValue("port", out var ps) && int.TryParse(ps, out var pv) ? pv : 7777;
                        string cs = dict.TryGetValue("callsign", out var c) ? c : "Pilot";
                        string rl = dict.TryGetValue("role", out var r) ? r : "CMP";
                        string rm = dict.TryGetValue("room", out var rms) ? rms.Trim() : "";
                        bool asHost = dict.TryGetValue("asHost", out var ah) && ah.Equals("true", StringComparison.OrdinalIgnoreCase);
                        _mainThreadQueue.Enqueue(() =>
                        {
                            MultiplayerManager.Connect(host, port, cs, rl, rm, asHost);
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    case "mp_disconnect":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            MultiplayerManager.Disconnect();
                            BroadcastDirectorAndAutopilot();
                        });
                        break;
                    }

                    case "mp_chat":
                    {
                        if (dict.TryGetValue("text", out var txt) && !string.IsNullOrWhiteSpace(txt))
                        {
                            _mainThreadQueue.Enqueue(() =>
                            {
                                MultiplayerManager.SendChatMessage(txt);
                            });
                        }
                        break;
                    }

                    case "mp_take_authority":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            MultiplayerManager.TakeFlightControl();
                        });
                        break;
                    }

                    case "mp_force_sync":
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            MultiplayerManager.RequestOrPushFullSync();
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error parsing incoming command: {ex.Message}");
            }
        }

        private void FixedUpdate()
        {
            FlightAutopilot.FixedTick();
            MultiplayerManager.FixedTick();
        }

        private void Update()
        {
            // 1. Drain incoming main thread commands
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error executing main thread action: {ex.Message}");
                }
            }

            // 1b. Tick Multiplayer (incoming TCP packets + 10 Hz physics sync)
            MultiplayerManager.TickMainThread();

            // 2. Periodic Checklist, Mission Director, Autopilot, Planner & MP broadcast (every 0.5s)
            _checklistTimer += Time.deltaTime;
            if (_checklistTimer >= 0.5f)
            {
                _checklistTimer = 0f;
                MissionDirector.Tick(this);
                BroadcastChecklistState();
                BroadcastDirectorAndAutopilot();
            }

            // 3. Periodic Telemetry broadcast (5 Hz / every 0.2s)
            _telemetryTimer += Time.deltaTime;
            if (_telemetryTimer >= 0.2f)
            {
                _telemetryTimer = 0f;
                BroadcastTelemetry();
            }
        }

        private void BroadcastDirectorAndAutopilot()
        {
            if (_server == null || !_server.IsRunning) return;
            try
            {
                _server.Broadcast(MissionDirector.BuildDirectorJson());
                _server.Broadcast(FlightAutopilot.BuildAutopilotJson());
                _server.Broadcast(BurnPlannerBridge.BuildPlannerJson());
                _server.Broadcast(MultiplayerManager.BuildMultiplayerJson());
            }
            catch { }
        }

        private void BroadcastAllSwitches()
        {
            var pins = CockpitManager.GetAllPinStates();
            var sb = new StringBuilder(4096);
            sb.Append("{\"type\":\"all_switches\",\"data\":{");
            int i = 0;
            foreach (var kvp in pins)
            {
                if (i++ > 0) sb.Append(",");
                sb.Append($"\"{kvp.Key}\":\"{kvp.Value}\"");
            }
            sb.Append("}}");
            _server?.Broadcast(sb.ToString());
        }

        private void BroadcastTelemetry()
        {
            if (_server == null || !_server.IsRunning) return;

            try
            {
                if (CockpitManager.IsApolloActive)
                {
                    var curr = State.Current;
                    bool inLM = State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule && curr.LunarModuleState != null;
                    var orb = inLM ? curr.LunarModuleState!.Orbit : curr.Orbit;

                    double ap = 0, pe = 0, vel = 0, inc = 0, alt = 0;
                    float busA = 28f, busB = 28f;

                    if (orb != null)
                    {
                        // OrbitState ApogeeAltitude / PerigeeAltitude are in km -> convert to NM!
                        ap = orb.ApogeeAltitude / 1.852;
                        pe = orb.PerigeeAltitude / 1.852;
                        inc = orb.Inclination;
                    }

                    if (inLM && curr.LunarModuleState?.Velocity != null)
                    {
                        vel = curr.LunarModuleState.Velocity.VelocityFeetPrSecond;
                        alt = curr.LunarModuleState.AltitudeMeters * 3.28084;
                    }
                    else
                    {
                        if (curr.Velocity != null) vel = curr.Velocity.VelocityFeetPrSecond;
                        alt = curr.AltitudeMeters * 3.28084;
                    }

                    var sb = new StringBuilder(640);
                    sb.Append("{\"type\":\"telemetry\",\"data\":{");
                    sb.Append($"\"craft\":\"{(inLM ? "Apollo LM" : "Apollo CSM")}\",");
                    sb.Append($"\"met\":\"{JsonHelper.Escape(MissionDirector.GetMetString())}\",");
                    sb.Append($"\"phase\":\"{JsonHelper.Escape(MissionDirector.CurrentPhase)}\",");
                    sb.Append($"\"soi\":\"{JsonHelper.Escape(MissionDirector.CurrentSOI)}\",");
                    sb.Append($"\"ap\":{ap.ToString("F1", CultureInfo.InvariantCulture)},");
                    sb.Append($"\"pe\":{pe.ToString("F1", CultureInfo.InvariantCulture)},");
                    sb.Append($"\"vel\":{vel.ToString("F0", CultureInfo.InvariantCulture)},");
                    sb.Append($"\"inc\":{inc.ToString("F2", CultureInfo.InvariantCulture)},");
                    sb.Append($"\"alt\":{alt.ToString("F0", CultureInfo.InvariantCulture)},");
                    sb.Append($"\"busa\":{busA.ToString("F1", CultureInfo.InvariantCulture)},");
                    sb.Append($"\"busb\":{busB.ToString("F1", CultureInfo.InvariantCulture)},");
                    sb.Append("\"dsky\":{");
                    sb.Append($"\"prog\":\"{JsonHelper.Escape(DSKYBridge.GetProgram())}\",");
                    sb.Append($"\"verb\":\"{JsonHelper.Escape(DSKYBridge.GetVerb())}\",");
                    sb.Append($"\"noun\":\"{JsonHelper.Escape(DSKYBridge.GetNoun())}\",");
                    sb.Append($"\"r1\":\"{JsonHelper.Escape(DSKYBridge.GetRegister1())}\",");
                    sb.Append($"\"r2\":\"{JsonHelper.Escape(DSKYBridge.GetRegister2())}\",");
                    sb.Append($"\"r3\":\"{JsonHelper.Escape(DSKYBridge.GetRegister3())}\",");
                    sb.Append($"\"compact\":{(DSKYBridge.GetCompAct() ? "true" : "false")}");
                    sb.Append("}");
                    sb.Append("}}");

                    _server.Broadcast(sb.ToString());
                }
            }
            catch { }
        }

        private void BroadcastChecklistState()
        {
            if (_server == null || !_server.IsRunning) return;
            try
            {
                string json = SmartChecklistEngine.BuildChecklistJson();
                _server.Broadcast(json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error building checklist JSON: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            MultiplayerManager.Disconnect();
            _server?.Stop();
            _harmony?.UnpatchSelf();
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.reentry.sync";
        public const string PLUGIN_NAME = "Reentry Sync & Copilot";
        public const string PLUGIN_VERSION = "1.0.1";
    }
}
