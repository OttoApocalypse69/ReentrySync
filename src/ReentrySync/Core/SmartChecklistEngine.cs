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
        public static string LastCopilotActivity { get; private set; } = "Auto-Copilot standing by.";
        public static float CopilotPacingDelay { get; set; } = 0.7f;

        private static Coroutine? _autoCoroutine;

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
            SetCopilotStatus("⚡ Auto-Copilot engaged. Scanning checklist...");

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
                    SetCopilotStatus("✓ Checklist complete! Standing by.");
                    IsAutoRunning = false;
                    CockpitPatches.NotifyChecklistUpdated();
                    yield break;
                }

                var inst = ActiveHighlightHandler.instructions[0];
                var cmd = inst.cmd;
                string displayText = GetInstructionDisplayText(inst);

                // 2. Handle Manual / Verification / Commentary Steps
                if (inst.ManualProceed || cmd == null)
                {
                    string text = inst.ManualProceed_Instruction ?? cmd?.Message ?? "Manual step";

                    // A. Header or section title (e.g. >> BOOST PREPARATIONS or -25:00 IMU ALIGN)
                    if (text.StartsWith(">>") || text.StartsWith("[") || text.Contains("CHECKLIST") || text.StartsWith("-"))
                    {
                        SetCopilotStatus($"Section: {text}");
                        yield return new WaitForSeconds(Mathf.Min(0.4f, CopilotPacingDelay));
                        SafeRemoveTopInstruction(inst);
                        continue;
                    }

                    // B. AGC Program Observer (e.g. "Wait for computer to complete P01 and automatically run P02" or "Verify P02")
                    string? targetProg = TryExtractTargetProgram(text);
                    if (!string.IsNullOrEmpty(targetProg))
                    {
                        SetCopilotStatus($"Waiting for AGC Program P{targetProg} (Current: P{DSKYBridge.GetProgram()})...");
                        float waitElapsed = 0f;
                        while (IsAutoRunning && DSKYBridge.GetProgram() != targetProg && waitElapsed < 60f)
                        {
                            yield return new WaitForSeconds(0.5f);
                            waitElapsed += 0.5f;
                            SetCopilotStatus($"Waiting for AGC Program P{targetProg} (Current: P{DSKYBridge.GetProgram()})...");
                        }
                        if (!IsAutoRunning) break;
                    }

                    // C. General verification / reading step
                    SetCopilotStatus($"Verified: {text}");
                    yield return new WaitForSeconds(Mathf.Max(0.4f, CopilotPacingDelay));
                    SafeRemoveTopInstruction(inst);
                    continue;
                }

                // 3. Control Step (Switch, Fuse, Knob, Button)
                bool isButton = cmd.ButtonId != TriggerButtonID.NULL || cmd.LM_ButtonId != LMTools.TriggerButtonID.NULL;
                bool isDskyBtn = cmd.ButtonId != TriggerButtonID.NULL && IsDskyButton(cmd.ButtonId);

                // A. Check if switch/breaker/knob is already in required position
                if (!isButton && IsControlAlreadySatisfied(cmd))
                {
                    SetCopilotStatus($"✓ {displayText} [ALREADY SET]");
                    yield return new WaitForSeconds(0.15f);
                    if (!inst.missionRelated)
                    {
                        SafeRemoveTopInstruction(inst);
                    }
                    else
                    {
                        // Give mission manager a moment to detect already-set control
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

            SetCopilotStatus("Auto-Copilot disengaged.");
            CockpitPatches.NotifyChecklistUpdated();
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

        private static string? TryExtractTargetProgram(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            // Matches e.g. "P02", "P01", "P11", "P30", "P52" — use LAST match (e.g. "complete P01 and run P02" -> "02")
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
