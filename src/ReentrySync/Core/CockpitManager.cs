using System;
using System.Collections.Generic;
using Apollo;
using ApolloCockpitTools;
using HarmonyLib;
using UnityEngine;
using LMTools = Apollo.LunarModule.CockpitTools;
using Apollo.LunarModule;

namespace ReentrySync.Core
{
    public static class CockpitManager
    {
        // CSM Delegates
        private static readonly Action<ApolloVirtualCockpitState, PinID, PinPosition>? _updatePin =
            AccessTools.MethodDelegate<Action<ApolloVirtualCockpitState, PinID, PinPosition>>(
                AccessTools.Method(typeof(ApolloVirtualCockpitState), "UpdatePin"));

        private static readonly Action<ApolloVirtualCockpitState, FuseID, FusePosition>? _updateFuse =
            AccessTools.MethodDelegate<Action<ApolloVirtualCockpitState, FuseID, FusePosition>>(
                AccessTools.Method(typeof(ApolloVirtualCockpitState), "UpdateFuse"));

        private static readonly Action<ApolloVirtualCockpitState, KnobID, int>? _updateKnob =
            AccessTools.MethodDelegate<Action<ApolloVirtualCockpitState, KnobID, int>>(
                AccessTools.Method(typeof(ApolloVirtualCockpitState), "UpdateKnobSelection"));

        // LM Delegates
        private static readonly Action<LunarModuleCockpitState, LMTools.PinID, LMTools.PinPosition>? _updateLMPin =
            AccessTools.MethodDelegate<Action<LunarModuleCockpitState, LMTools.PinID, LMTools.PinPosition>>(
                AccessTools.Method(typeof(LunarModuleCockpitState), "UpdatePin"));

        private static readonly Action<LunarModuleCockpitState, LMTools.FuseID, LMTools.FusePosition>? _updateLMFuse =
            AccessTools.MethodDelegate<Action<LunarModuleCockpitState, LMTools.FuseID, LMTools.FusePosition>>(
                AccessTools.Method(typeof(LunarModuleCockpitState), "UpdateFuse"));

        private static readonly Action<LunarModuleCockpitState, LMTools.KnobID, int>? _updateLMKnob =
            AccessTools.MethodDelegate<Action<LunarModuleCockpitState, LMTools.KnobID, int>>(
                AccessTools.Method(typeof(LunarModuleCockpitState), "UpdateKnobSelection"));

        public static bool IsApolloActive => State.Current != null && State.Current.CockpitState != null;
        public static bool IsLMActive => State.Current != null && State.Current.LunarModuleState != null && State.Current.LunarModuleState.CockpitState != null;
        public static bool IsInLM => State.ActiveSpacecraft == AvailableSpacecrafts.LunarModule;

        // -------------------------------------------------------------
        // CSM State Readers
        // -------------------------------------------------------------
        public static PinPosition GetPinPosition(PinID id)
        {
            if (!IsApolloActive) return PinPosition.NULL;
            try
            {
                return State.Current.CockpitState.GetPinPosition(id);
            }
            catch
            {
                return PinPosition.NULL;
            }
        }

        public static FusePosition GetFusePosition(FuseID id)
        {
            if (!IsApolloActive) return FusePosition.NULL;
            try
            {
                return State.Current.CockpitState.GetFusePosition(id);
            }
            catch
            {
                return FusePosition.NULL;
            }
        }

        public static int GetKnobSelection(KnobID id)
        {
            if (!IsApolloActive) return -1;
            try
            {
                var comp = State.Current.CockpitState.GetKnobSwitchComponent(id);
                return comp != null ? comp.selection : -1;
            }
            catch
            {
                return -1;
            }
        }

        // -------------------------------------------------------------
        // CSM Actuators
        // -------------------------------------------------------------
        public static bool TrySetPin(PinID pinId, PinPosition pos, out string error)
        {
            error = string.Empty;
            if (!IsApolloActive)
            {
                error = "Apollo cockpit is not active.";
                return false;
            }

            try
            {
                var comp = State.Current.CockpitState.GetPinComponent(pinId);
                if (comp != null)
                {
                    if (comp.cover != null && !comp.cover.Open)
                    {
                        comp.cover.Open = true;
                    }
                    comp.position = pos;
                    comp.RefreshPin();
                }

                _updatePin?.Invoke(State.Current.CockpitState, pinId, pos);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySetPin(string pinName, string positionName, out string error)
        {
            error = string.Empty;
            if (!Enum.TryParse(pinName, true, out PinID pinId))
            {
                error = $"Unknown PinID: {pinName}";
                return false;
            }

            if (!Enum.TryParse(positionName, true, out PinPosition pos))
            {
                if (int.TryParse(positionName, out int numPos) && Enum.IsDefined(typeof(PinPosition), numPos))
                {
                    pos = (PinPosition)numPos;
                }
                else
                {
                    error = $"Unknown PinPosition: {positionName}. Valid: Up, Down, Middle, Left, Right, NULL";
                    return false;
                }
            }

            return TrySetPin(pinId, pos, out error);
        }

        public static bool TrySetFuse(FuseID fuseId, FusePosition pos, out string error)
        {
            error = string.Empty;
            if (!IsApolloActive)
            {
                error = "Apollo cockpit is not active.";
                return false;
            }

            try
            {
                var comp = State.Current.CockpitState.GetFuseComponent(fuseId);
                if (comp != null)
                {
                    comp.position = pos;
                    comp.Refresh();
                }

                _updateFuse?.Invoke(State.Current.CockpitState, fuseId, pos);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySetFuse(string fuseName, string positionName, out string error)
        {
            error = string.Empty;
            if (!Enum.TryParse(fuseName, true, out FuseID fuseId))
            {
                error = $"Unknown FuseID: {fuseName}";
                return false;
            }

            if (!Enum.TryParse(positionName, true, out FusePosition pos))
            {
                error = $"Unknown FusePosition: {positionName}";
                return false;
            }

            return TrySetFuse(fuseId, pos, out error);
        }

        public static bool TrySetKnob(KnobID knobId, int selection, out string error)
        {
            error = string.Empty;
            if (!IsApolloActive)
            {
                error = "Apollo cockpit is not active.";
                return false;
            }

            try
            {
                var comp = State.Current.CockpitState.GetKnobSwitchComponent(knobId);
                if (comp != null)
                {
                    comp.selection = selection;
                    comp.RefreshKnob();
                }

                _updateKnob?.Invoke(State.Current.CockpitState, knobId, selection);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySetKnob(string knobName, int selection, out string error)
        {
            error = string.Empty;
            if (!Enum.TryParse(knobName, true, out KnobID knobId))
            {
                error = $"Unknown KnobID: {knobName}";
                return false;
            }

            return TrySetKnob(knobId, selection, out error);
        }

        public static bool TryPressButton(TriggerButtonID btnId, out string error)
        {
            error = string.Empty;
            if (!IsApolloActive)
            {
                error = "Apollo cockpit is not active.";
                return false;
            }

            try
            {
                ApolloCockpitPushButton? targetComp = State.Current.CockpitState.GetTriggerButtonComponent(btnId);
                if (targetComp == null)
                {
                    var comps = State.Current.CockpitState.GetTriggerButtonComponents(btnId);
                    if (comps != null && comps.Count > 0)
                    {
                        foreach (var c in comps)
                        {
                            if (c != null)
                            {
                                targetComp = c;
                                break;
                            }
                        }
                    }
                }

                if (targetComp != null)
                {
                    if (targetComp.cover != null && !targetComp.cover.Open)
                    {
                        targetComp.cover.Open = true;
                    }
                    // PushButton(-1) triggers UpdateTriggerButton(Id, -1) and sets WasJustClickedTriggerHandler = true
                    targetComp.PushButton(-1);
                    return true;
                }

                // Fallback if no physical component found in scene
                var cockpit = ApolloProLogicHandler.Instance?.apolloRocket?.Cockpit;
                if (cockpit != null)
                {
                    cockpit.UpdateTriggerButton(btnId, -1);
                    return true;
                }

                error = $"TriggerButton {btnId} component not found in scene.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryPressButton(string buttonName, out string error)
        {
            error = string.Empty;
            if (!Enum.TryParse(buttonName, true, out TriggerButtonID btnId))
            {
                error = $"Unknown TriggerButtonID: {buttonName}";
                return false;
            }

            return TryPressButton(btnId, out error);
        }

        // -------------------------------------------------------------
        // LM State Readers & Actuators
        // -------------------------------------------------------------
        public static LMTools.PinPosition GetLMPinPosition(LMTools.PinID id)
        {
            if (!IsLMActive) return LMTools.PinPosition.NULL;
            try
            {
                return State.Current.LunarModuleState.CockpitState.GetPinPosition(id);
            }
            catch
            {
                return LMTools.PinPosition.NULL;
            }
        }

        public static LMTools.FusePosition GetLMFusePosition(LMTools.FuseID id)
        {
            if (!IsLMActive) return LMTools.FusePosition.NULL;
            try
            {
                return State.Current.LunarModuleState.CockpitState.GetFusePosition(id);
            }
            catch
            {
                return LMTools.FusePosition.NULL;
            }
        }

        public static int GetLMKnobSelection(LMTools.KnobID id)
        {
            if (!IsLMActive) return -1;
            try
            {
                var comp = State.Current.LunarModuleState.CockpitState.GetRotKnobComponent(id);
                return comp != null ? comp.selection : -1;
            }
            catch
            {
                return -1;
            }
        }

        public static bool TrySetLMPin(LMTools.PinID pinId, LMTools.PinPosition pos, out string error)
        {
            error = string.Empty;
            if (!IsLMActive)
            {
                error = "Lunar Module cockpit is not active.";
                return false;
            }

            try
            {
                var comp = State.Current.LunarModuleState.CockpitState.GetPinComponent(pinId);
                if (comp != null)
                {
                    comp.position = pos;
                    comp.RefreshPin();
                }

                _updateLMPin?.Invoke(State.Current.LunarModuleState.CockpitState, pinId, pos);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySetLMFuse(LMTools.FuseID fuseId, LMTools.FusePosition pos, out string error)
        {
            error = string.Empty;
            if (!IsLMActive)
            {
                error = "Lunar Module cockpit is not active.";
                return false;
            }

            try
            {
                var comp = State.Current.LunarModuleState.CockpitState.GetFuseComponent(fuseId);
                if (comp != null)
                {
                    comp.position = pos;
                    comp.Refresh();
                }

                _updateLMFuse?.Invoke(State.Current.LunarModuleState.CockpitState, fuseId, pos);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySetLMKnob(LMTools.KnobID knobId, int selection, out string error)
        {
            error = string.Empty;
            if (!IsLMActive)
            {
                error = "Lunar Module cockpit is not active.";
                return false;
            }

            try
            {
                var comp = State.Current.LunarModuleState.CockpitState.GetRotKnobComponent(knobId);
                if (comp != null)
                {
                    comp.selection = selection;
                    comp.RefreshKnob();
                }

                _updateLMKnob?.Invoke(State.Current.LunarModuleState.CockpitState, knobId, selection);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryPressLMButton(LMTools.TriggerButtonID btnId, out string error)
        {
            error = string.Empty;
            if (!IsLMActive)
            {
                error = "Lunar Module cockpit is not active.";
                return false;
            }

            try
            {
                var comp = State.Current.LunarModuleState.CockpitState.GetTriggerButtonComponent(btnId);
                if (comp != null)
                {
                    if (comp.cover != null && !comp.cover.Open)
                    {
                        comp.cover.Open = true;
                    }
                    comp.PushButton(-1);
                    return true;
                }

                State.Current.LunarModuleState.CockpitState.UpdateTriggerButton(btnId, -1);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySetLMPin(string pinName, string posName, out string error)
        {
            error = "";
            if (!Enum.TryParse(pinName, true, out LMTools.PinID pinId)) { error = $"Unknown LM PinID: {pinName}"; return false; }
            if (!Enum.TryParse(posName, true, out LMTools.PinPosition pos)) { error = $"Unknown LM PinPosition: {posName}"; return false; }
            return TrySetLMPin(pinId, pos, out error);
        }

        public static bool TrySetLMFuse(string fuseName, string posName, out string error)
        {
            error = "";
            if (!Enum.TryParse(fuseName, true, out LMTools.FuseID fuseId)) { error = $"Unknown LM FuseID: {fuseName}"; return false; }
            if (!Enum.TryParse(posName, true, out LMTools.FusePosition pos)) { error = $"Unknown LM FusePosition: {posName}"; return false; }
            return TrySetLMFuse(fuseId, pos, out error);
        }

        public static bool TrySetLMKnob(string knobName, int selection, out string error)
        {
            error = "";
            if (!Enum.TryParse(knobName, true, out LMTools.KnobID knobId)) { error = $"Unknown LM KnobID: {knobName}"; return false; }
            return TrySetLMKnob(knobId, selection, out error);
        }

        public static bool TryPressLMButton(string btnName, out string error)
        {
            error = "";
            if (!Enum.TryParse(btnName, true, out LMTools.TriggerButtonID btnId)) { error = $"Unknown LM TriggerButtonID: {btnName}"; return false; }
            return TryPressLMButton(btnId, out error);
        }

        // -------------------------------------------------------------
        // Full Cockpit Snapshot Readers (CSM & LM)
        // -------------------------------------------------------------
        public static Dictionary<string, string> GetAllPinStates()
        {
            var dict = new Dictionary<string, string>();
            if (!IsApolloActive) return dict;

            foreach (PinID id in Enum.GetValues(typeof(PinID)))
            {
                if (id == PinID.NULL) continue;
                try
                {
                    var pos = State.Current.CockpitState.GetPinPosition(id);
                    dict[id.ToString()] = pos.ToString();
                }
                catch { }
            }

            return dict;
        }

        public static Dictionary<string, string> GetAllFuseStates()
        {
            var dict = new Dictionary<string, string>();
            if (!IsApolloActive) return dict;

            foreach (FuseID id in Enum.GetValues(typeof(FuseID)))
            {
                if (id == FuseID.NULL) continue;
                try
                {
                    var pos = State.Current.CockpitState.GetFusePosition(id);
                    dict[id.ToString()] = pos.ToString();
                }
                catch { }
            }
            return dict;
        }

        public static Dictionary<string, int> GetAllKnobStates()
        {
            var dict = new Dictionary<string, int>();
            if (!IsApolloActive) return dict;

            foreach (KnobID id in Enum.GetValues(typeof(KnobID)))
            {
                if (id == KnobID.NULL) continue;
                try
                {
                    int sel = GetKnobSelection(id);
                    if (sel >= 0) dict[id.ToString()] = sel;
                }
                catch { }
            }
            return dict;
        }

        public static Dictionary<string, string> GetAllLMPinStates()
        {
            var dict = new Dictionary<string, string>();
            if (!IsLMActive) return dict;

            foreach (LMTools.PinID id in Enum.GetValues(typeof(LMTools.PinID)))
            {
                if (id == LMTools.PinID.NULL) continue;
                try
                {
                    dict[id.ToString()] = GetLMPinPosition(id).ToString();
                }
                catch { }
            }
            return dict;
        }

        public static Dictionary<string, string> GetAllLMFuseStates()
        {
            var dict = new Dictionary<string, string>();
            if (!IsLMActive) return dict;

            foreach (LMTools.FuseID id in Enum.GetValues(typeof(LMTools.FuseID)))
            {
                if (id == LMTools.FuseID.NULL) continue;
                try
                {
                    dict[id.ToString()] = GetLMFusePosition(id).ToString();
                }
                catch { }
            }
            return dict;
        }

        public static Dictionary<string, int> GetAllLMKnobStates()
        {
            var dict = new Dictionary<string, int>();
            if (!IsLMActive) return dict;

            foreach (LMTools.KnobID id in Enum.GetValues(typeof(LMTools.KnobID)))
            {
                if (id == LMTools.KnobID.NULL) continue;
                try
                {
                    int sel = GetLMKnobSelection(id);
                    if (sel >= 0) dict[id.ToString()] = sel;
                }
                catch { }
            }
            return dict;
        }
    }
}
