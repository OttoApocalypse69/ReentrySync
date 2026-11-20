using System;
using UnityEngine;
using UnityEngine.UI;

namespace ReentrySync.Core
{
    public static class DSKYBridge
    {
        public static DSKYHandler? ActiveHandler { get; internal set; }

        public static bool IsAvailable => ActiveHandler != null;

        public static string GetProgram()
        {
            if (ActiveHandler == null) return "00";
            return SafeGetText(ActiveHandler.ProgramD1) + SafeGetText(ActiveHandler.ProgramD2);
        }

        public static string GetVerb()
        {
            if (ActiveHandler == null) return "00";
            return SafeGetText(ActiveHandler.VerbD1) + SafeGetText(ActiveHandler.VerbD2);
        }

        public static string GetNoun()
        {
            if (ActiveHandler == null) return "00";
            return SafeGetText(ActiveHandler.NounD1) + SafeGetText(ActiveHandler.NounD2);
        }

        public static string GetRegister1()
        {
            if (ActiveHandler == null) return "+00000";
            return SafeGetText(ActiveHandler.Register1Sign) +
                   SafeGetText(ActiveHandler.Register1D1) +
                   SafeGetText(ActiveHandler.Register1D2) +
                   SafeGetText(ActiveHandler.Register1D3) +
                   SafeGetText(ActiveHandler.Register1D4) +
                   SafeGetText(ActiveHandler.Register1D5);
        }

        public static string GetRegister2()
        {
            if (ActiveHandler == null) return "+00000";
            return SafeGetText(ActiveHandler.Register2Sign) +
                   SafeGetText(ActiveHandler.Register2D1) +
                   SafeGetText(ActiveHandler.Register2D2) +
                   SafeGetText(ActiveHandler.Register2D3) +
                   SafeGetText(ActiveHandler.Register2D4) +
                   SafeGetText(ActiveHandler.Register2D5);
        }

        public static string GetRegister3()
        {
            if (ActiveHandler == null) return "+00000";
            return SafeGetText(ActiveHandler.Register3Sign) +
                   SafeGetText(ActiveHandler.Register3D1) +
                   SafeGetText(ActiveHandler.Register3D2) +
                   SafeGetText(ActiveHandler.Register3D3) +
                   SafeGetText(ActiveHandler.Register3D4) +
                   SafeGetText(ActiveHandler.Register3D5);
        }

        public static bool GetCompAct()
        {
            if (ActiveHandler == null || ActiveHandler.CompActLight == null) return false;
            return ActiveHandler.CompActLight.enabled && ActiveHandler.CompActLight.gameObject.activeInHierarchy;
        }

        public static bool PressKey(string key, out string error)
        {
            error = string.Empty;
            string buttonName = key.ToUpperInvariant() switch
            {
                "VERB" => "AGCVerb",
                "NOUN" => "AGCNoun",
                "ENTR" or "ENTER" => "AGCEntr",
                "CLR" or "CLEAR" => "AGCClear",
                "PRO" or "PROCEED" => "AGCPro",
                "KEYREL" => "AGCKeyRel",
                "RSET" or "RESET" => "AGCRset",
                "+" or "PLUS" => "AGCPluss",
                "-" or "MINUS" => "AGCMinus",
                "0" => "AGC0",
                "1" => "AGC1",
                "2" => "AGC2",
                "3" => "AGC3",
                "4" => "AGC4",
                "5" => "AGC5",
                "6" => "AGC6",
                "7" => "AGC7",
                "8" => "AGC8",
                "9" => "AGC9",
                _ => key
            };

            return CockpitManager.TryPressButton(buttonName, out error);
        }

        private static string SafeGetText(Text? text)
        {
            if (text == null || string.IsNullOrEmpty(text.text)) return " ";
            return text.text;
        }
    }
}
