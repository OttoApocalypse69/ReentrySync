using System;
using Apollo;
using Apollo.LunarModule;
using ApolloCockpitTools;
using HarmonyLib;
using ReentrySync.Core;
using ReentrySync.Network;
using WilhelmsenStudios.Reentry.Checklists;

namespace ReentrySync.Patches
{
    public static class CockpitPatches
    {
        public static event Action<string, string>? OnSwitchChanged;
        public static event Action<string>? OnButtonPressed;
        public static event Action<string, int>? OnKnobRotated;
        public static event Action<string, string>? OnFuseChanged;

        public static event Action<string, string>? OnLMSwitchChanged;
        public static event Action<string>? OnLMButtonPressed;
        public static event Action<string, int>? OnLMKnobRotated;
        public static event Action<string, string>? OnLMFuseChanged;

        public static event Action? OnChecklistUpdated;

        public static void NotifyChecklistUpdated()
        {
            try { OnChecklistUpdated?.Invoke(); } catch { }
        }

        // ---------------- CSM Patches ----------------
        [HarmonyPatch(typeof(ApolloCockpitPinSwitch), nameof(ApolloCockpitPinSwitch.PushPin))]
        public static class ApolloCockpitPinSwitch_PushPin_Patch
        {
            public static void Postfix(ApolloCockpitPinSwitch __instance)
            {
                if (MultiplayerManager.IsApplyingRemote) return;
                try
                {
                    string id = __instance.id.ToString();
                    string pos = __instance.position.ToString();
                    OnSwitchChanged?.Invoke(id, pos);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(ApolloCockpitPushButton), nameof(ApolloCockpitPushButton.PushButton))]
        public static class ApolloCockpitPushButton_PushButton_Patch
        {
            public static void Postfix(ApolloCockpitPushButton __instance)
            {
                if (MultiplayerManager.IsApplyingRemote) return;
                try
                {
                    string id = __instance.Id.ToString();
                    OnButtonPressed?.Invoke(id);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(ApolloCockpitRotKnob), nameof(ApolloCockpitRotKnob.RotateKnob))]
        public static class ApolloCockpitRotKnob_RotateKnob_Patch
        {
            public static void Postfix(ApolloCockpitRotKnob __instance)
            {
                if (MultiplayerManager.IsApplyingRemote) return;
                try
                {
                    string id = __instance.id.ToString();
                    OnKnobRotated?.Invoke(id, __instance.selection);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(ApolloCockpitFuse), nameof(ApolloCockpitFuse.PushFuse))]
        public static class ApolloCockpitFuse_PushFuse_Patch
        {
            public static void Postfix(ApolloCockpitFuse __instance)
            {
                if (MultiplayerManager.IsApplyingRemote) return;
                try
                {
                    string id = __instance.id.ToString();
                    string pos = __instance.position.ToString();
                    OnFuseChanged?.Invoke(id, pos);
                }
                catch { }
            }
        }

        // ---------------- LM Patches ----------------
        [HarmonyPatch(typeof(LunarModuleCockpitPinSwitch), nameof(LunarModuleCockpitPinSwitch.PushPin))]
        public static class LunarModuleCockpitPinSwitch_PushPin_Patch
        {
            public static void Postfix(LunarModuleCockpitPinSwitch __instance)
            {
                if (MultiplayerManager.IsApplyingRemote) return;
                try
                {
                    OnLMSwitchChanged?.Invoke(__instance.id.ToString(), __instance.position.ToString());
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(LunarModuleCockpitPushButton), nameof(LunarModuleCockpitPushButton.PushButton))]
        public static class LunarModuleCockpitPushButton_PushButton_Patch
        {
            public static void Postfix(LunarModuleCockpitPushButton __instance)
            {
                if (MultiplayerManager.IsApplyingRemote) return;
                try
                {
                    OnLMButtonPressed?.Invoke(__instance.Id.ToString());
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(LunarModuleCockpitRotKnob), nameof(LunarModuleCockpitRotKnob.RotateKnob))]
        public static class LunarModuleCockpitRotKnob_RotateKnob_Patch
        {
            public static void Postfix(LunarModuleCockpitRotKnob __instance)
            {
                if (MultiplayerManager.IsApplyingRemote) return;
                try
                {
                    OnLMKnobRotated?.Invoke(__instance.id.ToString(), __instance.selection);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(LunarModuleCockpitFuse), nameof(LunarModuleCockpitFuse.PushFuse))]
        public static class LunarModuleCockpitFuse_PushFuse_Patch
        {
            public static void Postfix(LunarModuleCockpitFuse __instance)
            {
                if (MultiplayerManager.IsApplyingRemote) return;
                try
                {
                    OnLMFuseChanged?.Invoke(__instance.id.ToString(), __instance.position.ToString());
                }
                catch { }
            }
        }

        // ---------------- DSKY & Checklist Patches ----------------
        [HarmonyPatch(typeof(DSKYHandler), "Start")]
        public static class DSKYHandler_Start_Patch
        {
            public static void Postfix(DSKYHandler __instance)
            {
                DSKYBridge.ActiveHandler = __instance;
            }
        }

        [HarmonyPatch(typeof(OpenAChecklistHandler), "Start")]
        public static class OpenAChecklistHandler_Start_Patch
        {
            public static void Postfix(OpenAChecklistHandler __instance)
            {
                SmartChecklistEngine.ActiveOpenHandler = __instance;
                OnChecklistUpdated?.Invoke();
            }
        }

        [HarmonyPatch(typeof(OpenAChecklistHandler), "OpenChecklist")]
        public static class OpenAChecklistHandler_OpenChecklist_Patch
        {
            public static void Postfix(OpenAChecklistHandler __instance)
            {
                SmartChecklistEngine.ActiveOpenHandler = __instance;
                OnChecklistUpdated?.Invoke();
            }
        }

        [HarmonyPatch(typeof(HighlightHandler), "Start")]
        public static class HighlightHandler_Start_Patch
        {
            public static void Postfix(HighlightHandler __instance)
            {
                SmartChecklistEngine.ActiveHighlightHandler = __instance;
                OnChecklistUpdated?.Invoke();
            }
        }

        [HarmonyPatch(typeof(HighlightHandler), "Proceed")]
        public static class HighlightHandler_Proceed_Patch
        {
            public static void Postfix(HighlightHandler __instance)
            {
                OnChecklistUpdated?.Invoke();
            }
        }

        [HarmonyPatch(typeof(ChecklistMenuHandler), "Start")]
        public static class ChecklistMenuHandler_Start_Patch
        {
            public static void Postfix(ChecklistMenuHandler __instance)
            {
                SmartChecklistEngine.ActiveMenuHandler = __instance;
                OnChecklistUpdated?.Invoke();
            }
        }
    }
}
