using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Apollo;
using Apollo.Mission;
using ApolloCockpitTools;
using HarmonyLib;
using ReentrySync.Patches;
using UnityEngine;
using WilhelmsenStudios.Reentry.Checklists;
using WilhelmsenStudios.Reentry.GEConsoleCommands;
using LMTools = Apollo.LunarModule.CockpitTools;

namespace ReentrySync.Core
{
    public static class SmartChecklistEngine
    {
        public static HighlightHandler? ActiveHighlightHandler { get; set; }
        public static OpenAChecklistHandler? ActiveOpenHandler { get; set; }
        public static ChecklistMenuHandler? ActiveMenuHandler { get; set; }

        public static bool IsAutoRunning { get; private set; }
        public static bool IsHolding { get; private set; }
        public static bool RespectTimestamps { get; set; } = true;
        public static string LastCopilotActivity { get; private set; } = "Auto-Copilot standing by.";
        public static float CopilotPacingDelay { get; set; } = 0.7f;

        private static Coroutine? _autoCoroutine;
        private static volatile bool _skipCurrentHold = false;

        public static void SkipCurrentHold()
        {
            if (IsHolding)
            {
                _skipCurrentHold = true;
                SetCopilotStatus("Hold overridden by crew — continuing...");
            }
        }

        public static event Action<string>? OnCopilotActivity;

        private static readonly Action<OpenAChecklistHandler>? _runChecklist =
            AccessTools.MethodDelegate<Action<OpenAChecklistHandler>>(
                AccessTools.Method(typeof(OpenAChecklistHandler), "runChecklist"));

        private static readonly AccessTools.FieldRef<ChecklistMenuHandler, List<ApolloChecklistGenerator.Checklist>>? _allLoadedChecklistsRef =
            AccessTools.FieldRefAccess<ChecklistMenuHandler, List<ApolloChecklistGenerator.Checklist>>("allLoadedChecklists");

        private static readonly Func<HighlightHandler, string, int, string>? _parseInstructionInt =
            AccessTools.MethodDelegate<Func<HighlightHandler, string, int, string>>(
                AccessTools.Method(typeof(HighlightHandler), "ParseInstructionToText", new[] { typeof(string), typeof(int) }));

        private static readonly Func<HighlightHandler, string, string, string>? _parseInstructionStr =
            AccessTools.MethodDelegate<Func<HighlightHandler, string, string, string>>(
                AccessTools.Method(typeof(HighlightHandler), "ParseInstructionToText", new[] { typeof(string), typeof(string) }));

        public static void EnsureInstances()
        {
            if (ActiveHighlightHandler == null)
                ActiveHighlightHandler = UnityEngine.Object.FindAnyObjectByType<HighlightHandler>();

            if (ActiveOpenHandler == null)
                ActiveOpenHandler = UnityEngine.Object.FindAnyObjectByType<OpenAChecklistHandler>();

            if (ActiveMenuHandler == null)
                ActiveMenuHandler = UnityEngine.Object.FindAnyObjectByType<ChecklistMenuHandler>();
        }

        public static bool HasActiveChecklist()
        {
            EnsureInstances();

            if (ActiveHighlightHandler != null && ActiveHighlightHandler.instructions != null && ActiveHighlightHandler.instructions.Count > 0)
                return true;

            var loaded = ActiveOpenHandler?.GetLoadedChecklist();
            return loaded != null && loaded.Steps != null && loaded.Steps.Count > 0;
        }

        public static string GetCurrentChecklistName()
        {
            EnsureInstances();

            if (ActiveHighlightHandler != null && !string.IsNullOrEmpty(ActiveHighlightHandler.GetCurrentlyRunningChecklistName()))
            {
                return ActiveHighlightHandler.GetCurrentlyRunningChecklistName();
            }

            var loaded = ActiveOpenHandler?.GetLoadedChecklist();
            if (loaded != null && !string.IsNullOrEmpty(loaded.Name))
            {
                return loaded.Name;
            }

            return "No Active Checklist";
        }

        // -------------------------------------------------------------
        // Control Satisfaction & State Detection
        // -------------------------------------------------------------
        public static bool IsControlAlreadySatisfied(MissionCommand cmd)
        {
            if (cmd == null) return false;

            // CSM Controls
            if (cmd.PinID != PinID.NULL)
            {
                return CockpitManager.GetPinPosition(cmd.PinID) == cmd.PinPosition;
            }
            if (cmd.FuseID != FuseID.NULL)
            {
                return CockpitManager.GetFusePosition(cmd.FuseID) == cmd.FusePosition;
            }
            if (cmd.KnobID != KnobID.NULL)
            {
                return CockpitManager.GetKnobSelection(cmd.KnobID) == (int)cmd.Value1;
            }

            // LM Controls
            if (cmd.LM_PinID != LMTools.PinID.NULL)
            {
                return CockpitManager.GetLMPinPosition(cmd.LM_PinID) == cmd.LM_PinPosition;
            }
            if (cmd.LM_FuseID != LMTools.FuseID.NULL)
            {
                return CockpitManager.GetLMFusePosition(cmd.LM_FuseID) == cmd.LM_FusePosition;
            }
            if (cmd.LM_KnobID != LMTools.KnobID.NULL)
            {
                return CockpitManager.GetLMKnobSelection(cmd.LM_KnobID) == (int)cmd.Value1;
            }

            // Push buttons are momentary pulses, so they are not statically "already satisfied"
            return false;
        }

        public static string GetControlDescription(MissionCommand cmd)
        {
            if (cmd == null) return "Command";

            if (cmd.PinID != PinID.NULL) return $"Switch {cmd.PinID} -> {cmd.PinPosition}";
            if (cmd.FuseID != FuseID.NULL) return $"Breaker {cmd.FuseID} -> {cmd.FusePosition}";
            if (cmd.KnobID != KnobID.NULL) return $"Knob {cmd.KnobID} -> {(int)cmd.Value1}";
            if (cmd.ButtonId != TriggerButtonID.NULL) return $"Button {cmd.ButtonId}";

            if (cmd.LM_PinID != LMTools.PinID.NULL) return $"LM Switch {cmd.LM_PinID} -> {cmd.LM_PinPosition}";
            if (cmd.LM_FuseID != LMTools.FuseID.NULL) return $"LM Breaker {cmd.LM_FuseID} -> {cmd.LM_FusePosition}";
            if (cmd.LM_KnobID != LMTools.KnobID.NULL) return $"LM Knob {cmd.LM_KnobID} -> {(int)cmd.Value1}";
            if (cmd.LM_ButtonId != LMTools.TriggerButtonID.NULL) return $"LM Button {cmd.LM_ButtonId}";

            return cmd.Message ?? "Action";
        }

        public static bool IsDskyButton(TriggerButtonID id)
        {
            return (int)id >= (int)TriggerButtonID.AGCVerb && (int)id <= (int)TriggerButtonID.AGCRset;
        }

        public static bool ExecuteControlStep(MissionCommand cmd, out string desc)
        {
            desc = string.Empty;
            if (cmd == null) return false;

            // CSM Actuations
            if (cmd.PinID != PinID.NULL)
            {
                bool ok = CockpitManager.TrySetPin(cmd.PinID, cmd.PinPosition, out string err);
                desc = $"Flipped switch {cmd.PinID} to {cmd.PinPosition}" + (!string.IsNullOrEmpty(err) ? $" ({err})" : "");
                return ok;
            }
            if (cmd.ButtonId != TriggerButtonID.NULL)
            {
                bool ok = CockpitManager.TryPressButton(cmd.ButtonId, out string err);
                desc = $"Pressed {cmd.ButtonId}" + (!string.IsNullOrEmpty(err) ? $" ({err})" : "");
                return ok;
            }
            if (cmd.KnobID != KnobID.NULL)
            {
                bool ok = CockpitManager.TrySetKnob(cmd.KnobID, (int)cmd.Value1, out string err);
                desc = $"Set knob {cmd.KnobID} to {(int)cmd.Value1}" + (!string.IsNullOrEmpty(err) ? $" ({err})" : "");
                return ok;
            }
            if (cmd.FuseID != FuseID.NULL)
            {
                bool ok = CockpitManager.TrySetFuse(cmd.FuseID, cmd.FusePosition, out string err);
                desc = $"Set breaker {cmd.FuseID} to {cmd.FusePosition}" + (!string.IsNullOrEmpty(err) ? $" ({err})" : "");
                return ok;
            }

            // LM Actuations
            if (cmd.LM_PinID != LMTools.PinID.NULL)
            {
                bool ok = CockpitManager.TrySetLMPin(cmd.LM_PinID, cmd.LM_PinPosition, out string err);
                desc = $"Flipped LM switch {cmd.LM_PinID} to {cmd.LM_PinPosition}" + (!string.IsNullOrEmpty(err) ? $" ({err})" : "");
                return ok;
            }
            if (cmd.LM_ButtonId != LMTools.TriggerButtonID.NULL)
            {
                bool ok = CockpitManager.TryPressLMButton(cmd.LM_ButtonId, out string err);
                desc = $"Pressed LM button {cmd.LM_ButtonId}" + (!string.IsNullOrEmpty(err) ? $" ({err})" : "");
                return ok;
            }
            if (cmd.LM_KnobID != LMTools.KnobID.NULL)
            {
                bool ok = CockpitManager.TrySetLMKnob(cmd.LM_KnobID, (int)cmd.Value1, out string err);
                desc = $"Set LM knob {cmd.LM_KnobID} to {(int)cmd.Value1}" + (!string.IsNullOrEmpty(err) ? $" ({err})" : "");
                return ok;
            }
            if (cmd.LM_FuseID != LMTools.FuseID.NULL)
            {
                bool ok = CockpitManager.TrySetLMFuse(cmd.LM_FuseID, cmd.LM_FusePosition, out string err);
                desc = $"Set LM breaker {cmd.LM_FuseID} to {cmd.LM_FusePosition}" + (!string.IsNullOrEmpty(err) ? $" ({err})" : "");
                return ok;
            }

            desc = cmd.Message ?? "Action completed";
            return true;
        }

        // -------------------------------------------------------------
        // Single Step Manual Execution (User clicked "NEXT STEP")
        // -------------------------------------------------------------
        public static bool ExecuteNextStep(out string summary)
        {
            summary = string.Empty;
            EnsureInstances();

            // If Auto-Copilot is currently holding on a timestamp or telemetry condition, NEXT STEP overrides the hold!
            if (IsAutoRunning && IsHolding)
            {
                SkipCurrentHold();
                summary = "Skipped current timestamp/condition hold.";
                return true;
            }

            // Case 1: Running in HighlightHandler
            if (ActiveHighlightHandler != null && ActiveHighlightHandler.instructions != null && ActiveHighlightHandler.instructions.Count > 0)
            {
                var inst = ActiveHighlightHandler.instructions[0];
                var cmd = inst.cmd;

                if (inst.ManualProceed || cmd == null)
                {
                    summary = $"Acknowledged: {inst.ManualProceed_Instruction ?? cmd?.Message ?? "Manual step"}";
                    SafeRemoveTopInstruction(inst);
                    return true;
                }

                // Control step
                ExecuteControlStep(cmd, out summary);
                SafeRemoveTopInstruction(inst);
                return true;
            }

            // Case 2: OpenAChecklistHandler has a loaded checklist waiting to start
            var loaded = ActiveOpenHandler?.GetLoadedChecklist();
            if (loaded != null && loaded.Steps != null && loaded.Steps.Count > 0)
            {
                try
                {
                    if (ActiveOpenHandler != null)
                    {
                        _runChecklist?.Invoke(ActiveOpenHandler);
                        summary = $"Started checklist: {loaded.Name}";
                        CockpitPatches.NotifyChecklistUpdated();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    summary = $"Error starting checklist: {ex.Message}";
                    return false;
                }
            }

            summary = "No active checklist found.";
            return false;
        }

        // -------------------------------------------------------------
        // Intelligent Auto-Copilot Loop
        // -------------------------------------------------------------
        public static void StartAutoRunner(MonoBehaviour host, float stepDelaySeconds = -1f)
        {
            if (stepDelaySeconds > 0f)
                CopilotPacingDelay = Mathf.Clamp(stepDelaySeconds, 0.2f, 3.0f);

            if (IsAutoRunning) return;
            IsAutoRunning = true;
            _autoCoroutine = host.StartCoroutine(AutoRunnerLoop());
        }

        public static void StopAutoRunner(MonoBehaviour host)
        {
            IsAutoRunning = false;
            IsHolding = false;
            _skipCurrentHold = false;
            if (_autoCoroutine != null)
            {
                host.StopCoroutine(_autoCoroutine);
                _autoCoroutine = null;
            }
            SetCopilotStatus("Auto-Copilot disengaged.");
            CockpitPatches.NotifyChecklistUpdated();
        }

        private static IEnumerator AutoRunnerLoop()
        {
            SetCopilotStatus("Auto-Copilot engaged. Scanning checklist...");
            IsHolding = false;
            _skipCurrentHold = false;

            while (IsAutoRunning)
            {
                EnsureInstances();

                // 1. If instructions queue is empty, see if we need to start a loaded checklist
                if (ActiveHighlightHandler == null || ActiveHighlightHandler.instructions == null || ActiveHighlightHandler.instructions.Count == 0)
                {
                    var loaded = ActiveOpenHandler?.GetLoadedChecklist();
                    if (ActiveOpenHandler != null && loaded != null && loaded.Steps != null && loaded.Steps.Count > 0)
                    {
                        SetCopilotStatus($"Starting checklist: {loaded.Name}");
                        _runChecklist?.Invoke(ActiveOpenHandler);
                        yield return new WaitForSeconds(0.4f);
                        continue;
                    }

                    // Done!
                    IsHolding = false;
                    SetCopilotStatus("[OK] Checklist complete! Standing by.");
                    IsAutoRunning = false;
                    CockpitPatches.NotifyChecklistUpdated();
                    yield break;
                }

                var inst = ActiveHighlightHandler.instructions[0];
                var cmd = inst.cmd;
                string displayText = GetInstructionDisplayText(inst);

                // 2. Handle Manual / Timestamp / Wait / Verification Steps
                if (inst.ManualProceed || cmd == null)
                {
                    string text = (inst.ManualProceed_Instruction ?? cmd?.Message ?? "Manual step").Trim();
                    _skipCurrentHold = false;

                    // A. Pure checklist title banner inserted by HighlightHandler (starts with ">>")
                    if (text.StartsWith(">>"))
                    {
                        SetCopilotStatus($"Checklist: {text.TrimStart('>').Trim()}");
                        yield return new WaitForSeconds(Mathf.Min(0.35f, CopilotPacingDelay));
                        SafeRemoveTopInstruction(inst);
                        continue;
                    }

                    // B. Check Timestamp, Duration, Telemetry & AGC Program Holds (when RespectTimestamps is ON)
                    if (RespectTimestamps && !text.StartsWith("(skip", StringComparison.OrdinalIgnoreCase))
                    {
                        // B1. Launch Countdown Timestamp: "T -15:00:", "Continue at T-15:00", "-15:00" while on pad
                        if (TryParseLaunchCountdownSeconds(text, out double targetTtlSec))
                        {
                            while (IsAutoRunning && RespectTimestamps && !_skipCurrentHold &&
                                   State.Current != null && !State.Current.IsReleasedFromLaunchpad)
                            {
                                double curTtl = ApolloGameTimeHandler.TimeToLaunch.TotalSeconds;
                                if (curTtl <= targetTtlSec + 0.5) break;

                                IsHolding = true;
                                double rem = curTtl - targetTtlSec;
                                SetCopilotStatus($"HOLDING for T-{FormatMmSs(targetTtlSec)} (Now T-{FormatMmSs(curTtl)} | Wait {FormatMmSs(rem)}) — click NEXT STEP to override");
                                yield return new WaitForSeconds(0.25f);
                            }
                            IsHolding = false;
                            if (!IsAutoRunning) break;
                        }
                        // B2. Post-Liftoff / Post-Ignition MET Timestamp: "+00:42", "+02:16", "T+03:15"
                        else if (TryParsePostEventSeconds(text, out double targetMetSec))
                        {
                            string chkName = GetCurrentChecklistName();
                            bool useIgnitionClock = chkName.IndexOf("PDI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    chkName.IndexOf("TLI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    (ApolloGameTimeHandler.TimeSinceLaunch.TotalSeconds > 1800 &&
                                                     ApolloGameTimeHandler.TimeFromIgnition.TotalSeconds >= 0 &&
                                                     ApolloGameTimeHandler.TimeFromIgnition.TotalSeconds < 3600);

                            while (IsAutoRunning && RespectTimestamps && !_skipCurrentHold &&
                                   State.Current != null && State.Current.IsReleasedFromLaunchpad)
                            {
                                double curSec = useIgnitionClock
                                    ? ApolloGameTimeHandler.TimeFromIgnition.TotalSeconds
                                    : ApolloGameTimeHandler.TimeSinceLaunch.TotalSeconds;
                                if (curSec >= targetMetSec - 0.5) break;

                                IsHolding = true;
                                double rem = targetMetSec - curSec;
                                string lbl = useIgnitionClock ? "TFI" : "MET";
                                SetCopilotStatus($"HOLDING for {lbl} +{FormatMmSs(targetMetSec)} (Now +{FormatMmSs(curSec)} | Wait {FormatMmSs(rem)}) — click NEXT STEP to override");
                                yield return new WaitForSeconds(0.25f);
                            }
                            IsHolding = false;
                            if (!IsAutoRunning) break;
                        }
                        // B3. In-Flight Countdown-to-Event: "-10:00", "-05:00", "-00:30", "-01:15h", "(-03:00)"
                        else if (State.Current != null && State.Current.IsReleasedFromLaunchpad &&
                                 TryParseInFlightCountdownSeconds(text, out double targetTfiSec))
                        {
                            while (IsAutoRunning && RespectTimestamps && !_skipCurrentHold)
                            {
                                double curCountdown = GetActiveInFlightCountdownSeconds();
                                // Only hold if an active countdown is running and still further out than target
                                if (curCountdown <= 0.1 || curCountdown <= targetTfiSec + 0.5) break;

                                IsHolding = true;
                                double rem = curCountdown - targetTfiSec;
                                SetCopilotStatus($"HOLDING for -{FormatMmSs(targetTfiSec)} (Event in {FormatMmSs(curCountdown)} | Wait {FormatMmSs(rem)}) — click NEXT STEP to override");
                                yield return new WaitForSeconds(0.25f);
                            }
                            IsHolding = false;
                            if (!IsAutoRunning) break;
                        }

                        // B4. Explicit Duration Waits: "Wait 10 seconds...", "(WAIT 2 SECS)", "Bypass for 20 sec"
                        if (TryParseDurationWaitSeconds(text, out double waitDurSec))
                        {
                            double elapsedGameSec = 0;
                            while (IsAutoRunning && RespectTimestamps && !_skipCurrentHold && elapsedGameSec < waitDurSec)
                            {
                                IsHolding = true;
                                double rem = Math.Max(0, waitDurSec - elapsedGameSec);
                                SetCopilotStatus($"WAITING {rem:0.0}s / {waitDurSec:0}s: {text}");
                                float dt = ApolloGameTimeHandler.deltaTime > 0f ? ApolloGameTimeHandler.deltaTime : Time.deltaTime;
                                yield return null;
                                elapsedGameSec += Math.Max(dt, Time.deltaTime);
                            }
                            IsHolding = false;
                            if (!IsAutoRunning) break;
                        }

                        // B5. Physical Flight-State / Sensor Holds (0.05G, S-II IECO, S-IVB Cutoff, Touchdown)
                        if (NeedsSensorHold(text, out string sensorGoal))
                        {
                            while (IsAutoRunning && RespectTimestamps && !_skipCurrentHold && !IsSensorGoalMet(sensorGoal, out string progress))
                            {
                                IsHolding = true;
                                SetCopilotStatus($"HOLDING ({progress}): {text} — click NEXT STEP to override");
                                yield return new WaitForSeconds(0.3f);
                            }
                            IsHolding = false;
                            if (!IsAutoRunning) break;
                        }

                        // B6. AGC Program Observer (e.g. "Verify P02", "Verify P11", "Wait for computer to complete P01 and automatically run P02")
                        string? targetProg = TryExtractTargetProgramForWait(text);
                        if (!string.IsNullOrEmpty(targetProg))
                        {
                            while (IsAutoRunning && RespectTimestamps && !_skipCurrentHold && DSKYBridge.GetProgram() != targetProg)
                            {
                                IsHolding = true;
                                SetCopilotStatus($"HOLDING for AGC Program P{targetProg} (Current: P{DSKYBridge.GetProgram()}) — click NEXT STEP to override");
                                yield return new WaitForSeconds(0.4f);
                            }
                            IsHolding = false;
                            if (!IsAutoRunning) break;
                        }
                    }

                    // C. Actionable Text Steps (e.g., GDC ALIGN button hold!)
                    if (!text.StartsWith("(skip", StringComparison.OrdinalIgnoreCase) &&
                        text.IndexOf("GDC ALIGN", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetCopilotStatus($"Aligning GDC to IMU: {text}");
                        CockpitManager.TryPressButton(TriggerButtonID.GDCAlign, out _);
                        try
                        {
                            var rocket = ApolloProLogicHandler.Instance?.apolloRocket;
                            if (rocket != null) rocket.AlignGDCPlatformToBody(rocket.transform);
                        }
                        catch { }
                        yield return new WaitForSeconds(Mathf.Max(0.5f, CopilotPacingDelay));
                        SafeRemoveTopInstruction(inst);
                        continue;
                    }

                    // D. Acknowledge & proceed
                    SetCopilotStatus($"[OK] {text}");
                    yield return new WaitForSeconds(Mathf.Max(0.35f, CopilotPacingDelay));
                    SafeRemoveTopInstruction(inst);
                    continue;
                }

                // 3. Control Step (Switch, Fuse, Knob, Button)
                bool isButton = cmd.ButtonId != TriggerButtonID.NULL || cmd.LM_ButtonId != LMTools.TriggerButtonID.NULL;
                bool isDskyBtn = cmd.ButtonId != TriggerButtonID.NULL && IsDskyButton(cmd.ButtonId);

                // A. Check if switch/breaker/knob is already in required position
                if (!isButton && IsControlAlreadySatisfied(cmd))
                {
                    SetCopilotStatus($"[OK] {displayText} [ALREADY SET]");
                    yield return new WaitForSeconds(0.15f);
                    if (!inst.missionRelated)
                    {
                        SafeRemoveTopInstruction(inst);
                    }
                    else
                    {
                        yield return new WaitForSeconds(0.3f);
                    }
                    continue;
                }

                // B. Actuate the control
                ExecuteControlStep(cmd, out string desc);
                SetCopilotStatus($"Actuated: {displayText}");

                // Pacing: AGC buttons require crisp keystrokes (~0.28s) so the AGC doesn't drop keys
                float stepPace = isDskyBtn ? 0.28f : CopilotPacingDelay;
                yield return new WaitForSeconds(0.15f);

                // C. Check if game auto-proceeded or if we need to remove the completed instruction
                if (ActiveHighlightHandler.instructions.Count > 0 && ActiveHighlightHandler.instructions[0] == inst)
                {
                    if (!inst.missionRelated && (isButton || IsControlAlreadySatisfied(cmd)))
                    {
                        SafeRemoveTopInstruction(inst);
                    }
                }

                // Pilot cadence delay before next physical action
                yield return new WaitForSeconds(Mathf.Max(0.1f, stepPace - 0.15f));
            }

            IsHolding = false;
            SetCopilotStatus("Auto-Copilot disengaged.");
            CockpitPatches.NotifyChecklistUpdated();
        }

        // -------------------------------------------------------------
        // Timestamp, Duration & Telemetry Condition Parsers
        // -------------------------------------------------------------
        private static string FormatMmSs(double totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            var ts = TimeSpan.FromSeconds(totalSeconds);
            return ts.TotalHours >= 1.0
                ? $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes:00}:{ts.Seconds:00}";
        }

        private static bool TryParseLaunchCountdownSeconds(string text, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(text)) return false;

            // Matches: "T -15:00:", "T-15:00", "continue at T-15:00", "T -04:10: L/V ENGINE", or "-15:00" when on launchpad
            var m = Regex.Match(text, @"\bT\s*-\s*(\d{1,2}):(\d{2})(?::(\d{2}))?", RegexOptions.IgnoreCase);
            if (!m.Success && State.Current != null && !State.Current.IsReleasedFromLaunchpad)
            {
                m = Regex.Match(text, @"^\s*-\s*(\d{1,2}):(\d{2})(?::(\d{2}))?");
            }

            if (m.Success)
            {
                int a = int.Parse(m.Groups[1].Value);
                int b = int.Parse(m.Groups[2].Value);
                if (m.Groups[3].Success)
                {
                    int c = int.Parse(m.Groups[3].Value);
                    seconds = a * 3600 + b * 60 + c;
                }
                else
                {
                    seconds = a * 60 + b;
                }
                return true;
            }
            return false;
        }

        private static bool TryParsePostEventSeconds(string text, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(text)) return false;

            // Matches: "+00:42 Mode 1B", "+2:39 Outboard Cutoff", "T+03:15", "+00:26: Throttle Up"
            var m = Regex.Match(text, @"(?:^|\bT\s*|\bat\s+)\+\s*(\d{1,3}):(\d{2})(?::(\d{2}))?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int a = int.Parse(m.Groups[1].Value);
                int b = int.Parse(m.Groups[2].Value);
                if (m.Groups[3].Success)
                {
                    int c = int.Parse(m.Groups[3].Value);
                    seconds = a * 3600 + b * 60 + c;
                }
                else
                {
                    seconds = a * 60 + b;
                }
                return true;
            }
            return false;
        }

        private static bool TryParseInFlightCountdownSeconds(string text, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(text)) return false;

            // Matches: "-10:00", "-05:00", "-00:05 (PRO on V99)", "(-03:00)", "-01:15h", "-00:45m"
            var m = Regex.Match(text, @"(?:^|\(|\s)-(\d{1,2}):(\d{2})(h|m)?\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int a = int.Parse(m.Groups[1].Value);
                int b = int.Parse(m.Groups[2].Value);
                string unit = m.Groups[3].Success ? m.Groups[3].Value.ToLowerInvariant() : "";
                seconds = unit == "h" ? (a * 3600 + b * 60) : (a * 60 + b);
                return true;
            }
            return false;
        }

        private static double GetActiveInFlightCountdownSeconds()
        {
            try
            {
                if (FlightAutopilot.Mode == AutopilotMode.AUTO_BURN && FlightAutopilot.BurnTigRemainingSec > 0f)
                    return FlightAutopilot.BurnTigRemainingSec;

                if (ApolloGameTimeHandler.TimeFromIgnition.TotalSeconds < -0.1)
                    return -ApolloGameTimeHandler.TimeFromIgnition.TotalSeconds;

                if (ApolloGameTimeHandler.TimeToRetro.TotalSeconds > 0.1)
                    return ApolloGameTimeHandler.TimeToRetro.TotalSeconds;
            }
            catch { }
            return 0;
        }

        private static bool TryParseDurationWaitSeconds(string text, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(text)) return false;

            // Matches: "Wait 5 seconds", "Wait 10 seconds in EMS TEST 2", "(WAIT 2 SECS)", "Bypass for 20 sec"
            var m = Regex.Match(text, @"\b(?:wait|bypass\s+for|hold\s+for)\s+(\d{1,3})\s*(?:sec|secs|seconds|s)\b", RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, out double s) && s > 0 && s <= 600)
            {
                seconds = s;
                return true;
            }
            return false;
        }

        private static bool NeedsSensorHold(string text, out string goal)
        {
            goal = "";
            if (string.IsNullOrEmpty(text)) return false;

            if (text.IndexOf("Wait for 0.05G", StringComparison.OrdinalIgnoreCase) >= 0) { goal = "005G"; return true; }
            if (text.IndexOf("Wait for S-II IECO", StringComparison.OrdinalIgnoreCase) >= 0) { goal = "SII_IECO"; return true; }
            if (text.IndexOf("Wait for S-IVB Cutoff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("SIVB ECO", StringComparison.OrdinalIgnoreCase) >= 0) { goal = "SIVB_ECO"; return true; }
            if (string.Equals(text.Trim(), "TOUCHDOWN", StringComparison.OrdinalIgnoreCase)) { goal = "TOUCHDOWN"; return true; }
            return false;
        }

        private static bool IsSensorGoalMet(string goal, out string progress)
        {
            progress = goal;
            var cur = State.Current;
            var rocket = ApolloProLogicHandler.Instance?.apolloRocket;
            if (cur == null) return true;

            switch (goal)
            {
                case "005G":
                    progress = $"Waiting for 0.05G (Now {cur.GForce:0.00}G)";
                    return cur.Dot05GDetected || cur.GForce >= 0.05;

                case "SII_IECO":
                    progress = $"Waiting for S-II IECO (Stage: {rocket?.CurrentStage})";
                    return rocket != null && rocket.CurrentStage >= ProApolloRocket.ApolloStages.StageS_IVB;

                case "SIVB_ECO":
                    double peKm = cur.Orbit != null ? cur.Orbit.PerigeeAltitude : 0;
                    progress = $"Waiting for S-IVB Orbit Insertion (Pe {peKm / 1.852:0.0} NM)";
                    return (rocket != null && rocket.CurrentStage >= ProApolloRocket.ApolloStages.StageS_SM) ||
                           (peKm > 130.0 && cur.GForce < 0.15);

                case "TOUCHDOWN":
                    var lm = cur.LunarModuleState;
                    double altFt = lm != null ? lm.AltitudeMeters * 3.28084 : 0;
                    progress = $"Waiting for Lunar Touchdown (Alt {altFt:0} ft)";
                    return lm == null || (altFt < 15.0 && Math.Abs(lm.AltitudeChangeFeetSec) < 4.0);
            }
            return true;
        }

        private static void SafeRemoveTopInstruction(HighlightInstruction expected)
        {
            try
            {
                if (ActiveHighlightHandler != null &&
                    ActiveHighlightHandler.instructions != null &&
                    ActiveHighlightHandler.instructions.Count > 0 &&
                    ActiveHighlightHandler.instructions[0] == expected)
                {
                    ActiveHighlightHandler.instructions.RemoveAt(0);
                    CockpitPatches.NotifyChecklistUpdated();
                }
            }
            catch { }
        }

        public static void ClearChecklist()
        {
            EnsureInstances();
            try
            {
                if (ActiveHighlightHandler != null && ActiveHighlightHandler.instructions != null)
                {
                    ActiveHighlightHandler.instructions.Clear();
                }
                IsHolding = false;
                SetCopilotStatus("Checklist cleared.");
                CockpitPatches.NotifyChecklistUpdated();
            }
            catch { }
        }

        private static void SetCopilotStatus(string status)
        {
            LastCopilotActivity = status;
            try { OnCopilotActivity?.Invoke(status); } catch { }
        }

        private static string? TryExtractTargetProgramForWait(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            // Only hold for AGC program if text explicitly asks to Verify / Wait / Complete a program
            if (text.IndexOf("verify", StringComparison.OrdinalIgnoreCase) < 0 &&
                text.IndexOf("wait", StringComparison.OrdinalIgnoreCase) < 0 &&
                text.IndexOf("complete", StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            var matches = Regex.Matches(text, @"\bP([0-9]{2})\b", RegexOptions.IgnoreCase);
            if (matches.Count > 0)
            {
                var last = matches[matches.Count - 1];
                if (last.Groups.Count > 1)
                {
                    return last.Groups[1].Value;
                }
            }
            return null;
        }

        public static string GetInstructionDisplayText(HighlightInstruction inst)
        {
            if (inst == null) return "";
            if (inst.ManualProceed || inst.cmd == null)
            {
                return inst.ManualProceed_Instruction ?? inst.cmd?.Message ?? "Manual Verification";
            }

            var cmd = inst.cmd;
            if (!string.IsNullOrEmpty(cmd.Message))
            {
                return cmd.Message;
            }

            // Use the game's built-in ChecklistDictionary label resolver!
            if (ActiveHighlightHandler != null)
            {
                try
                {
                    string? parsed = null;
                    if (cmd.PinID != PinID.NULL && _parseInstructionInt != null)
                        parsed = _parseInstructionInt(ActiveHighlightHandler, "switch_" + cmd.PinID, (int)cmd.PinPosition);
                    else if (cmd.FuseID != FuseID.NULL && _parseInstructionInt != null)
                        parsed = _parseInstructionInt(ActiveHighlightHandler, "fuse_" + cmd.FuseID, (int)cmd.FusePosition);
                    else if (cmd.KnobID != KnobID.NULL && _parseInstructionInt != null)
                        parsed = _parseInstructionInt(ActiveHighlightHandler, "knob_" + cmd.KnobID, (int)cmd.Value1);
                    else if (cmd.ButtonId != TriggerButtonID.NULL && _parseInstructionStr != null)
                        parsed = _parseInstructionStr(ActiveHighlightHandler, "button_" + cmd.ButtonId, "PUSH");
                    else if (cmd.LM_PinID != LMTools.PinID.NULL && _parseInstructionInt != null)
                        parsed = _parseInstructionInt(ActiveHighlightHandler, "lm_switch_" + cmd.LM_PinID, (int)cmd.LM_PinPosition);
                    else if (cmd.LM_FuseID != LMTools.FuseID.NULL && _parseInstructionInt != null)
                        parsed = _parseInstructionInt(ActiveHighlightHandler, "lm_fuse_" + cmd.LM_FuseID, (int)cmd.LM_FusePosition);
                    else if (cmd.LM_KnobID != LMTools.KnobID.NULL && _parseInstructionInt != null)
                        parsed = _parseInstructionInt(ActiveHighlightHandler, "lm_knob_" + cmd.LM_KnobID, (int)cmd.Value1);
                    else if (cmd.LM_ButtonId != LMTools.TriggerButtonID.NULL && _parseInstructionStr != null)
                        parsed = _parseInstructionStr(ActiveHighlightHandler, "lm_button_" + cmd.LM_ButtonId, "PUSH");

                    if (!string.IsNullOrEmpty(parsed))
                    {
                        // Strip Unity rich-text tags like <color=...> if any
                        return Regex.Replace(parsed, "<.*?>", string.Empty).Trim();
                    }
                }
                catch { }
            }

            return GetControlDescription(cmd);
        }

        // -------------------------------------------------------------
        // JSON Telemetry & State Serialization
        // -------------------------------------------------------------
        public static string BuildChecklistJson()
        {
            EnsureInstances();
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"checklist\",\"data\":{");

            string chkName = GetCurrentChecklistName();
            sb.Append($"\"name\":\"{JsonHelper.Escape(chkName)}\",");
            sb.Append($"\"copilotActive\":{(IsAutoRunning ? "true" : "false")},");
            sb.Append($"\"copilotHolding\":{(IsHolding ? "true" : "false")},");
            sb.Append($"\"respectTimestamps\":{(RespectTimestamps ? "true" : "false")},");
            sb.Append($"\"copilotSpeed\":{CopilotPacingDelay.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"copilotActivity\":\"{JsonHelper.Escape(LastCopilotActivity)}\",");

            // Instructions
            if (ActiveHighlightHandler != null && ActiveHighlightHandler.instructions != null && ActiveHighlightHandler.instructions.Count > 0)
            {
                sb.Append("\"active\":true,");
                sb.Append($"\"count\":{ActiveHighlightHandler.instructions.Count},");
                sb.Append("\"steps\":[");

                int maxSteps = Math.Min(ActiveHighlightHandler.instructions.Count, 120);
                for (int i = 0; i < maxSteps; i++)
                {
                    if (i > 0) sb.Append(",");
                    var inst = ActiveHighlightHandler.instructions[i];
                    var cmd = inst.cmd;
                    string text = GetInstructionDisplayText(inst);
                    string target = cmd != null ? GetControlDescription(cmd) : (inst.ManualProceed ? "VERIFY" : "");
                    bool alreadyOk = cmd != null && IsControlAlreadySatisfied(cmd);

                    sb.Append("{");
                    sb.Append($"\"text\":\"{JsonHelper.Escape(text)}\",");
                    sb.Append($"\"target\":\"{JsonHelper.Escape(target)}\",");
                    sb.Append($"\"ok\":{(alreadyOk ? "true" : "false")},");
                    sb.Append($"\"isCurrent\":{(i == 0 ? "true" : "false")}");
                    sb.Append("}");
                }
                sb.Append("]");
            }
            else
            {
                var loaded = ActiveOpenHandler?.GetLoadedChecklist();
                if (loaded != null && loaded.Steps != null)
                {
                    sb.Append("\"active\":false,");
                    sb.Append($"\"count\":{loaded.Steps.Count},");
                    sb.Append("\"steps\":[");

                    for (int i = 0; i < loaded.Steps.Count; i++)
                    {
                        if (i > 0) sb.Append(",");
                        var step = loaded.Steps[i];
                        string text = !string.IsNullOrEmpty(step.Text) ? step.Text : step.Description ?? "Step " + (i + 1);
                        string typeStr = step.Type.ToString();

                        sb.Append("{");
                        sb.Append($"\"text\":\"{JsonHelper.Escape(text)}\",");
                        sb.Append($"\"type\":\"{JsonHelper.Escape(typeStr)}\",");
                        sb.Append($"\"setID\":{step.SetID},");
                        sb.Append($"\"toPos\":{step.ToPosID},");
                        sb.Append("\"isCurrent\":false");
                        sb.Append("}");
                    }
                    sb.Append("]");
                }
                else
                {
                    sb.Append("\"active\":false,\"count\":0,\"steps\":[]");
                }
            }

            // Available Checklists for selection
            sb.Append(",\"available\":[");
            if (ActiveMenuHandler != null && _allLoadedChecklistsRef != null)
            {
                var list = _allLoadedChecklistsRef(ActiveMenuHandler);
                if (list != null)
                {
                    int count = 0;
                    foreach (var c in list)
                    {
                        if (c == null) continue;
                        if (count > 0) sb.Append(",");
                        string path = c.getPath() ?? "";
                        sb.Append("{");
                        sb.Append($"\"name\":\"{JsonHelper.Escape(c.Name)}\",");
                        sb.Append($"\"group\":\"{JsonHelper.Escape(c.Group)}\",");
                        sb.Append($"\"path\":\"{JsonHelper.Escape(path)}\"");
                        sb.Append("}");
                        count++;
                    }
                }
            }
            sb.Append("]");

            sb.Append("}}");
            return sb.ToString();
        }

        public static bool LoadChecklistByPath(string path, out string error)
        {
            error = string.Empty;
            EnsureInstances();

            if (ActiveOpenHandler == null)
            {
                error = "OpenAChecklistHandler not found in scene.";
                return false;
            }

            try
            {
                ActiveOpenHandler.OpenChecklist(path);
                CockpitPatches.NotifyChecklistUpdated();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
