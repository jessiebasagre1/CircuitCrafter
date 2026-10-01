// COC4Controls.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The words for the COC 4 controls, worked out for the device the learner is on. Used by
    /// COC4StepGuide (the Guide messages) and COC4TicketIntroPanel (the ticket intro), so both always
    /// say the same thing: "press E" on a PC, "tap Use" on a phone or tablet.
    ///
    /// NOT A COMPONENT - nothing to attach. The keys and button labels are read from the scene:
    ///   interact key   - RepairStation's Interact Key (E by default)
    ///   diagnose key   - RepairWorkGate's Open Diagnosis Key (F by default)
    ///   grab key       - ToolBelt's Grab Key (G by default)
    ///   button labels  - the text on RepairStation's Enter / Exit buttons and ToolBelt's Grab button
    /// Change a key or a button label in the Inspector and every line follows.
    ///
    /// TOKENS (each also works capitalised, e.g. {Interact} gives "Press E"):
    ///   {interact} "press E" / "tap Use"          {leave}  "press E again" / "tap Step Back"
    ///   {diagnose} "press F (or click Diagnose)" / "tap Diagnose"
    ///   {grab}     "press G" / "tap Grab"         {click}  "click" / "tap"
    ///   {interactKey} "E"   {diagnoseKey} "F"   {grabKey} "G"   (the bare key names)
    /// </summary>
    public static class COC4Controls
    {
        /// <summary>True on a phone or tablet, or while the on-screen joystick is showing.</summary>
        public static bool IsTouch
        {
            get { return Application.isMobilePlatform || CursorControl.TouchControlsVisible; }
        }

        public static KeyCode InteractKey(RepairStation station)
        {
            KeyCode k = station != null ? Field(station, "interactKey", KeyCode.E) : KeyCode.E;
            return k == KeyCode.None ? KeyCode.E : k;
        }

        public static KeyCode GrabKey(ToolBelt belt)
        {
            KeyCode k = belt != null ? Field(belt, "grabKey", KeyCode.G) : KeyCode.G;
            return k == KeyCode.None ? KeyCode.G : k;
        }

        /// <summary>The control words for this device. <paramref name="station"/> and <paramref name="belt"/> may be null.</summary>
        public static Dictionary<string, string> Tokens(RepairStation station, ToolBelt belt)
        {
            bool touch = IsTouch;

            KeyCode interact = InteractKey(station);
            KeyCode grab = GrabKey(belt);
            KeyCode diagnose = RepairWorkGate.DiagnosisKey;

            string useLabel = ButtonLabel(station != null ? Field<Button>(station, "enterButton") : null, "Use");
            string backLabel = ButtonLabel(station != null ? Field<Button>(station, "exitButton") : null, "Step Back");
            string grabLabel = ButtonLabel(belt != null ? Field<Button>(belt, "grabButton") : null, "Grab");

            string diagnoseText = touch
                ? "tap Diagnose"
                : diagnose == KeyCode.None ? "open Diagnose" : "press " + KeyName(diagnose) + " (or click Diagnose)";

            return new Dictionary<string, string>
            {
                { "interact", touch ? "tap " + useLabel : "press " + KeyName(interact) },
                { "leave", touch ? "tap " + backLabel : "press " + KeyName(interact) + " again" },
                { "diagnose", diagnoseText },
                { "grab", touch ? "tap " + grabLabel : "press " + KeyName(grab) },
                { "click", touch ? "tap" : "click" },
                { "interactKey", KeyName(interact) },
                { "diagnoseKey", diagnose == KeyCode.None ? "" : KeyName(diagnose) },
                { "grabKey", KeyName(grab) },
                { "useButton", useLabel },
                { "backButton", backLabel },
                { "grabButton", grabLabel }
            };
        }

        /// <summary>
        /// Replaces {tokens} in <paramref name="line"/>: first <paramref name="vars"/>, then
        /// <paramref name="common"/>. Twice, so a token may hold another ({toUnit} holds {interact}).
        /// </summary>
        public static string Fill(string line, Dictionary<string, string> vars, Dictionary<string, string> common)
        {
            if (string.IsNullOrEmpty(line)) return line;

            string result = line;
            for (int pass = 0; pass < 2; pass++)
            {
                if (vars != null)
                    foreach (var pair in vars) result = Replace(result, pair.Key, pair.Value);
                if (common != null)
                    foreach (var pair in common) result = Replace(result, pair.Key, pair.Value);
            }
            return result;
        }

        static string Replace(string text, string key, string value)
        {
            if (text.IndexOf('{') < 0) return text;
            value = value ?? "";
            text = text.Replace("{" + key + "}", value);
            if (key.Length > 0)
            {
                string capKey = char.ToUpperInvariant(key[0]) + key.Substring(1);
                string capValue = value.Length > 0 ? char.ToUpperInvariant(value[0]) + value.Substring(1) : value;
                if (capKey != key) text = text.Replace("{" + capKey + "}", capValue);
            }
            return text;
        }

        public static string KeyName(KeyCode k)
        {
            string s = k.ToString();
            if (s.StartsWith("Alpha") && s.Length == 6) return s.Substring(5);
            return s;
        }

        /// <summary>The text on a button (TextMeshPro or legacy Text), or the fallback.</summary>
        public static string ButtonLabel(Button b, string fallback)
        {
            if (b == null) return fallback;
            TMP_Text tmp = b.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) return tmp.text.Trim();
            Text legacy = b.GetComponentInChildren<Text>(true);
            if (legacy != null && !string.IsNullOrWhiteSpace(legacy.text)) return legacy.text.Trim();
            return fallback;
        }

        // ------------------------------------------------------------------
        // Reading private fields - the COC 4 scripts are left untouched
        // ------------------------------------------------------------------

        static readonly Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();

        public static T Field<T>(object target, string field, T fallback = default(T))
        {
            if (target == null) return fallback;
            Type type = target.GetType();
            string cacheKey = type.FullName + "." + field;

            FieldInfo info;
            if (!fieldCache.TryGetValue(cacheKey, out info))
            {
                for (Type t = type; t != null && info == null; t = t.BaseType)
                    info = t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                             BindingFlags.DeclaredOnly);
                fieldCache[cacheKey] = info;
            }

            if (info == null) return fallback;
            object value = info.GetValue(target);
            if (value is T) return (T)value;
            return fallback;
        }
    }
}
