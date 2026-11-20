using System;
using System.Collections.Generic;
using System.Text;
using Apollo;
using HarmonyLib;
using UnityEngine;
using WilhelmsenStudios.Reentry.Checklists;
using WilhelmsenStudios.Reentry.GEConsoleCommands;

namespace ReentrySync.Core
{
    public class TimelineEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string ChecklistKey { get; set; } = ""; // Matches ProjectName or Name substring
        public string PhaseTag { get; set; } = "";
        public double TriggerMetSeconds { get; set; } = -1; // -1 means phase-triggered rather than strictly MET-triggered
        public double MaxMetSeconds { get; set; } = double.MaxValue;
        public string TimeDisplay { get; set; } = "";
        public string Reason { get; set; } = "";
        public bool IsLunarModule { get; set; } = false;
        public bool Completed { get; set; } = false;
    }

    public static class MissionDirector
    {
        public static bool AutoDirectorEnabled { get; set; } = false;
        public static string CurrentPhase { get; private set; } = "STANDBY";
        public static string CurrentSOI { get; private set; } = "EARTH";
        public static string RecommendedChecklistName { get; private set; } = "None";
        public static string RecommendedChecklistPath { get; private set; } = "";
        public static string RecommendedReason { get; private set; } = "Monitoring spacecraft state...";
        public static string LastDirectorAction { get; private set; } = "Mission Director monitoring MET & systems.";

        private static readonly HashSet<string> _completedIds = new HashSet<string>();
        private static readonly List<TimelineEntry> _timeline = new List<TimelineEntry>();
        private static float _lastAutoTriggerTime = -30f;

        private static readonly AccessTools.FieldRef<ChecklistMenuHandler, List<ApolloChecklistGenerator.Checklist>>? _allLoadedChecklistsRef =
            AccessTools.FieldRefAccess<ChecklistMenuHandler, List<ApolloChecklistGenerator.Checklist>>("allLoadedChecklists");

        static MissionDirector()
        {
            BuildDefaultApolloTimeline();
        }

        private static void BuildDefaultApolloTimeline()
        {
            _timeline.Clear();

            // --- Phase-driven Flight Procedures ---
            _timeline.Add(new TimelineEntry
            {
                Id = "phase_prelaunch",
                Title = "Launch Preparations",
                ChecklistKey = "PreFlightBoostPreparations",
                PhaseTag = "PRE-LAUNCH",
                TimeDisplay = "T-25:00",
                Reason = "On launchpad prior to liftoff — configure IMU, RCS, SCS & launch systems"
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_boost",
                Title = "Ascent / Boost",
                ChecklistKey = "AscentBoost",
                PhaseTag = "BOOST",
                TimeDisplay = "000:00:00",
                Reason = "Powered ascent active — monitor stages and EDS"
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_insertion",
                Title = "Earth Orbit Insertion (Item 1)",
                ChecklistKey = "Insertion_Item1",
                PhaseTag = "EARTH ORBIT",
                TimeDisplay = "000:12:00",
                Reason = "In Earth parking orbit — post-insertion spacecraft configuration"
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_tli_prep",
                Title = "TLI Preparation",
                ChecklistKey = "TLIPreparation",
                PhaseTag = "TLI PREP",
                TimeDisplay = "001:45:00",
                Reason = "S-IVB attached in Earth orbit — prepare for Trans-Lunar Injection"
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_tnd_prep",
                Title = "CSM/S-IVB Separation Prep",
                ChecklistKey = "TND_CSMSeparationPrep",
                PhaseTag = "TRANSLUNAR",
                TimeDisplay = "003:05:00",
                Reason = "Post-TLI trajectory — prepare for CSM transposition & LM extraction"
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_tnd_dock",
                Title = "Transposition & Docking",
                ChecklistKey = "TND_Docking",
                PhaseTag = "TRANSLUNAR",
                TimeDisplay = "003:15:00",
                Reason = "Dock CSM probe to Lunar Module drogue"
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_lunar_orbit_pdi_day",
                Title = "LM Activation (PDI Day)",
                ChecklistKey = "LMAct_PDIDay",
                PhaseTag = "LUNAR ORBIT",
                TimeDisplay = "LOI+1",
                Reason = "In Lunar Orbit — activate and check out Lunar Module systems",
                IsLunarModule = true
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_lm_prep_undock",
                Title = "Prep for Undocking",
                ChecklistKey = "LMAct_PDIDay_PrepForUndock",
                PhaseTag = "LUNAR ORBIT",
                TimeDisplay = "PDI-2h",
                Reason = "Prepare CSM/LM tunnel and systems for undocking",
                IsLunarModule = true
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_lm_pre_pdi",
                Title = "Pre-PDI Preparations",
                ChecklistKey = "LMTimeline_PrePDI",
                PhaseTag = "LM DESCENT PREP",
                TimeDisplay = "PDI-30m",
                Reason = "LM undocked in lunar orbit — configure DPS, PGNS & Landing Radar",
                IsLunarModule = true
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_lm_pdi",
                Title = "Powered Descent Initiation (PDI)",
                ChecklistKey = "LMTimeline_PDI",
                PhaseTag = "POWERED DESCENT",
                TimeDisplay = "PDI",
                Reason = "Executing Lunar Powered Descent to landing site",
                IsLunarModule = true
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_lm_surface",
                Title = "Lunar Surface Post-Landing",
                ChecklistKey = "LMLunarSurface_FirstRevAct",
                PhaseTag = "LUNAR SURFACE",
                TimeDisplay = "LANDED",
                Reason = "On the Lunar Surface — secure descent stage and verify stay/no-stay",
                IsLunarModule = true
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_lm_launch_prep",
                Title = "Lunar Liftoff Preparations",
                ChecklistKey = "LMLunarSurface_LaunchPrep",
                PhaseTag = "LUNAR SURFACE",
                TimeDisplay = "ASC-30m",
                Reason = "Configure LM Ascent Stage, PGNS P12 & APS for liftoff",
                IsLunarModule = true
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_entry_prep",
                Title = "Lunar Entry Vehicle Prep",
                ChecklistKey = "LunarEntry_VehPrep",
                PhaseTag = "REENTRY",
                TimeDisplay = "EI-30m",
                Reason = "Approaching Earth Entry Interface — configure CM RCS, batteries & EMS"
            });

            _timeline.Add(new TimelineEntry
            {
                Id = "phase_earth_landing",
                Title = "Earth Landing / Parachutes",
                ChecklistKey = "LunarEntry_EarthLanding",
                PhaseTag = "LANDING",
                TimeDisplay = "EI+5m",
                Reason = "Atmospheric descent — arm ELS and monitor drogue/main chutes"
            });

            // --- MET-Triggered Apollo Generic Schedule Housekeeping ---
            AddMetTask("met_bat_b_1", "Charge Battery B", "Sys_1_EPS_BatChrg", 4, 33, "Scheduled EPS Battery B recharge");
            AddMetTask("met_h2o_1", "Waste Water Tank Drain #1", "Sys_1_ECS_WasteWaterTkDrain", 20, 0, "Scheduled 20h MET waste water dump");
            AddMetTask("met_purge_1", "Fuel Cell Cryo Purge (O2)", "Sys_1_EPS_FCPwrPlntPrg", 20, 15, "Scheduled 20h MET O2 cryo purge");
            AddMetTask("met_bat_a_1", "Charge Battery A", "Sys_1_EPS_BatChrg", 23, 5, "Scheduled EPS Battery A recharge");
            AddMetTask("met_bat_a_2", "Charge Battery A", "Sys_1_EPS_BatChrg", 30, 50, "Scheduled EPS Battery A recharge");
            AddMetTask("met_h2o_2", "Waste Water Tank Drain #2", "Sys_1_ECS_WasteWaterTkDrain", 40, 0, "Scheduled 40h MET waste water dump");
            AddMetTask("met_purge_2", "Fuel Cell Cryo Purge (O2/H2)", "Sys_1_EPS_FCPwrPlntPrg", 40, 15, "Scheduled 40h MET O2/H2 purge");
            AddMetTask("met_bat_b_2", "Charge Battery B", "Sys_1_EPS_BatChrg", 48, 36, "Scheduled EPS Battery B recharge");
            AddMetTask("met_h2o_3", "Waste Water Tank Drain #3", "Sys_1_ECS_WasteWaterTkDrain", 60, 0, "Scheduled 60h MET waste water dump");
            AddMetTask("met_purge_3", "Fuel Cell Cryo Purge (O2)", "Sys_1_EPS_FCPwrPlntPrg", 60, 15, "Scheduled 60h MET O2 purge");
            AddMetTask("met_h2o_4", "Waste Water Tank Drain #4", "Sys_1_ECS_WasteWaterTkDrain", 80, 0, "Scheduled 80h MET waste water dump");
            AddMetTask("met_h2o_5", "Waste Water Tank Drain #5", "Sys_1_ECS_WasteWaterTkDrain", 100, 0, "Scheduled 100h MET waste water dump");
            AddMetTask("met_bat_b_3", "Charge Battery B", "Sys_1_EPS_BatChrg", 118, 55, "Scheduled EPS Battery B recharge");
            AddMetTask("met_h2o_6", "Waste Water Tank Drain #6", "Sys_1_ECS_WasteWaterTkDrain", 120, 0, "Scheduled 120h MET waste water dump");
            AddMetTask("met_bat_a_3", "Charge Battery A", "Sys_1_EPS_BatChrg", 142, 0, "Scheduled EPS Battery A recharge");
        }

        private static void AddMetTask(string id, string title, string checklistKey, int hours, int mins, string reason)
        {
            double sec = hours * 3600.0 + mins * 60.0;
            _timeline.Add(new TimelineEntry
            {
                Id = id,
                Title = title,
                ChecklistKey = checklistKey,
                PhaseTag = "SCHEDULED",
                TriggerMetSeconds = sec,
                MaxMetSeconds = sec + 3600.0, // valid window: 1 hour after scheduled time
                TimeDisplay = $"{hours:000}:{mins:00}:00",
                Reason = reason
            });
        }

        public static string GetMetString()
        {
            try
            {
                if (State.Current != null && !State.Current.IsReleasedFromLaunchpad && ApolloGameTimeHandler.TimeToLaunch.TotalSeconds > 0)
                {
                    var ttl = ApolloGameTimeHandler.TimeToLaunch;
                    return $"T-{(int)ttl.TotalHours:00}:{ttl.Minutes:00}:{ttl.Seconds:00}";
                }
                var met = ApolloGameTimeHandler.TimeSinceLaunch;
                return $"{(int)met.TotalHours:000}:{met.Minutes:00}:{met.Seconds:00}";
            }
            catch
            {
                return "000:00:00";
            }
        }

        public static double GetMetSeconds()
        {
            try
            {
                return ApolloGameTimeHandler.TimeSinceLaunch.TotalSeconds;
            }
            catch
            {
                return 0;
            }
        }

        public static void Tick(MonoBehaviour host)
        {
            if (State.Current == null)
            {
                CurrentPhase = "NO FLIGHT ACTIVE";
                return;
            }

            UpdateFlightPhase();
            TimelineEntry? due = FindNextDueEntry();

            if (due != null)
            {
                string? path = ResolveChecklistPath(due.ChecklistKey);
                RecommendedChecklistName = due.Title;
                RecommendedChecklistPath = path ?? "";
                RecommendedReason = $"{due.Reason} [{due.TimeDisplay}]";

                // Autonomous Director: automatically load and execute due checklist!
                if (AutoDirectorEnabled &&
                    !string.IsNullOrEmpty(path) &&
                    !SmartChecklistEngine.IsAutoRunning &&
                    !SmartChecklistEngine.HasActiveChecklist() &&
                    Time.unscaledTime - _lastAutoTriggerTime > 8.0f)
                {
                    _lastAutoTriggerTime = Time.unscaledTime;
                    _completedIds.Add(due.Id);
                    due.Completed = true;

                    if (SmartChecklistEngine.LoadChecklistByPath(path, out string err))
                    {
                        LastDirectorAction = $"Auto-loaded '{due.Title}' for phase {CurrentPhase} (MET {GetMetString()})";
                        SmartChecklistEngine.StartAutoRunner(host);
                    }
                    else
                    {
                        LastDirectorAction = $"Failed to auto-load '{due.Title}': {err}";
                    }
                }
            }
            else
            {
                RecommendedChecklistName = "All Scheduled Procedures Complete";
                RecommendedChecklistPath = "";
                RecommendedReason = $"Cruising in {CurrentPhase} — monitoring MET ({GetMetString()}) & spacecraft telemetry.";
            }
        }

        private static void UpdateFlightPhase()
        {
            var cur = State.Current;
            var rocket = ApolloProLogicHandler.Instance?.apolloRocket;
            var lm = cur.LunarModuleState;
            double metSec = GetMetSeconds();

            bool nearMoon = cur.SOIDistToMoon > 0 && cur.SOIDistToMoon < cur.SOIDistToEarth;
            CurrentSOI = nearMoon ? "MOON" : "EARTH";

            double csmAltFt = cur.AltitudeMeters * 3.28084;

            // 1. Pre-launch
            if (!cur.IsReleasedFromLaunchpad && !cur.HasBeenInSpace && csmAltFt < 500)
            {
                CurrentPhase = "PRE-LAUNCH";
                return;
            }

            // 2. Splashdown
            if (cur.IsSplashdown)
            {
                CurrentPhase = "SPLASHDOWN";
                return;
            }

            // 3. Reentry / Earth Landing
            if (!nearMoon && (cur.Dot05GDetected || cur.HasTransitionedToLanding || (cur.HasBeenInSpace && csmAltFt < 400000)))
            {
                CurrentPhase = csmAltFt < 100000 ? "LANDING" : "REENTRY";
                return;
            }

            // 4. Boost / Ascent
            if (rocket != null && (rocket.CurrentStage == ProApolloRocket.ApolloStages.Stage_SI_C ||
                                   rocket.CurrentStage == ProApolloRocket.ApolloStages.Stage_S_II ||
                                   (rocket.CurrentStage == ProApolloRocket.ApolloStages.StageS_IVB && csmAltFt < 450000 && metSec < 800)))
            {
                CurrentPhase = "BOOST";
                return;
            }

            // 5. Lunar SOI phases
            if (nearMoon)
            {
                if (State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule && lm != null)
                {
                    double lmAltFt = lm.AltitudeMeters * 3.28084;
                    if (lmAltFt < 80 && Math.Abs(lm.AltitudeChangeFeetSec) < 5)
                    {
                        CurrentPhase = "LUNAR SURFACE";
                        return;
                    }
                    if (lm.DPS != null && lm.DPS.IsThrusting() && lmAltFt < 65000)
                    {
                        CurrentPhase = "POWERED DESCENT";
                        return;
                    }
                    if (lm.APS != null && lm.APS.IsThrusting())
                    {
                        CurrentPhase = "LUNAR ASCENT";
                        return;
                    }
                    if (lmAltFt < 100000)
                    {
                        CurrentPhase = "LM DESCENT PREP";
                        return;
                    }
                }

                CurrentPhase = "LUNAR ORBIT";
                return;
            }

            // 6. Earth Orbit / TLI / Translunar Coast
            if (cur.Orbit != null && cur.Orbit.ApogeeAltitude > 5000) // High apogee = TLI / Translunar coast
            {
                CurrentPhase = "TRANSLUNAR";
                return;
            }

            if (rocket != null && rocket.CurrentStage == ProApolloRocket.ApolloStages.StageS_IVB && metSec > 4500)
            {
                CurrentPhase = "TLI PREP";
                return;
            }

            CurrentPhase = "EARTH ORBIT";
        }

        private static TimelineEntry? FindNextDueEntry()
        {
            double metSec = GetMetSeconds();
            bool inLM = State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule;

            // Priority 1: Check MET-scheduled housekeeping tasks due right now!
            foreach (var item in _timeline)
            {
                if (_completedIds.Contains(item.Id)) continue;
                if (item.TriggerMetSeconds >= 0 && metSec >= item.TriggerMetSeconds && metSec <= item.MaxMetSeconds)
                {
                    return item;
                }
            }

            // Priority 2: Check current Flight-Phase procedure
            foreach (var item in _timeline)
            {
                if (_completedIds.Contains(item.Id)) continue;
                if (item.TriggerMetSeconds >= 0) continue; // skip MET-only items

                if (item.PhaseTag == CurrentPhase)
                {
                    if (item.IsLunarModule && !inLM && CurrentPhase != "LUNAR ORBIT")
                        continue;
                    return item;
                }
            }

            return null;
        }

        public static bool RunRecommendedChecklist(MonoBehaviour host, bool autoExecute, out string message)
        {
            message = "";
            TimelineEntry? due = FindNextDueEntry();
            if (due == null)
            {
                message = "No recommended checklist currently due for this phase.";
                return false;
            }

            string? path = ResolveChecklistPath(due.ChecklistKey);
            if (string.IsNullOrEmpty(path))
            {
                message = $"Checklist '{due.ChecklistKey}' not found in loaded checklists.";
                return false;
            }

            _completedIds.Add(due.Id);
            due.Completed = true;

            if (!SmartChecklistEngine.LoadChecklistByPath(path, out string err))
            {
                message = $"Failed to load checklist: {err}";
                return false;
            }

            if (autoExecute)
            {
                SmartChecklistEngine.StartAutoRunner(host);
                message = $"Mission Director started '{due.Title}' with Auto-Copilot.";
            }
            else
            {
                message = $"Mission Director loaded '{due.Title}' into cockpit.";
            }

            LastDirectorAction = message;
            return true;
        }

        public static void ResetCompletedHistory()
        {
            _completedIds.Clear();
            foreach (var t in _timeline) t.Completed = false;
            LastDirectorAction = "Timeline completion history reset.";
        }

        public static string? ResolveChecklistPath(string key)
        {
            SmartChecklistEngine.EnsureInstances();
            if (SmartChecklistEngine.ActiveMenuHandler == null || _allLoadedChecklistsRef == null)
                return null;

            var list = _allLoadedChecklistsRef(SmartChecklistEngine.ActiveMenuHandler);
            if (list == null) return null;

            // Exact match on ProjectName or path folder first
            foreach (var c in list)
            {
                if (c == null) continue;
                string path = c.getPath() ?? "";
                if (string.Equals(c.ProjectName, key, StringComparison.OrdinalIgnoreCase) ||
                    path.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return path;
                }
            }

            // Substring match on display name
            foreach (var c in list)
            {
                if (c == null) continue;
                if (!string.IsNullOrEmpty(c.Name) && c.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return c.getPath();
                }
            }

            return null;
        }

        public static string BuildDirectorJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"director\",\"data\":{");
            sb.Append($"\"autoEnabled\":{(AutoDirectorEnabled ? "true" : "false")},");
            sb.Append($"\"phase\":\"{JsonHelper.Escape(CurrentPhase)}\",");
            sb.Append($"\"soi\":\"{JsonHelper.Escape(CurrentSOI)}\",");
            sb.Append($"\"met\":\"{JsonHelper.Escape(GetMetString())}\",");
            sb.Append($"\"recommendedName\":\"{JsonHelper.Escape(RecommendedChecklistName)}\",");
            sb.Append($"\"recommendedPath\":\"{JsonHelper.Escape(RecommendedChecklistPath)}\",");
            sb.Append($"\"recommendedReason\":\"{JsonHelper.Escape(RecommendedReason)}\",");
            sb.Append($"\"lastAction\":\"{JsonHelper.Escape(LastDirectorAction)}\",");

            sb.Append("\"timeline\":[");
            double metSec = GetMetSeconds();
            int emitted = 0;
            foreach (var item in _timeline)
            {
                // Show relevant phase items + next upcoming MET tasks
                bool isCurrentPhase = item.PhaseTag == CurrentPhase;
                bool isUpcomingMet = item.TriggerMetSeconds >= 0 && item.TriggerMetSeconds >= metSec - 1800 && item.TriggerMetSeconds <= metSec + 86400;
                if (!isCurrentPhase && !isUpcomingMet && item.TriggerMetSeconds >= 0) continue;

                if (emitted++ > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"id\":\"{JsonHelper.Escape(item.Id)}\",");
                sb.Append($"\"title\":\"{JsonHelper.Escape(item.Title)}\",");
                sb.Append($"\"phase\":\"{JsonHelper.Escape(item.PhaseTag)}\",");
                sb.Append($"\"time\":\"{JsonHelper.Escape(item.TimeDisplay)}\",");
                sb.Append($"\"reason\":\"{JsonHelper.Escape(item.Reason)}\",");
                sb.Append($"\"completed\":{(item.Completed ? "true" : "false")},");
                sb.Append($"\"isDue\":{(isCurrentPhase && !item.Completed ? "true" : "false")}");
                sb.Append("}");
            }
            sb.Append("]}}");
            return sb.ToString();
        }
    }
}
