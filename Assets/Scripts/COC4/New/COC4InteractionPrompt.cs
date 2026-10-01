// COC4InteractionPrompt.cs
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The interaction prompt for a COC 4 repair site - the same kind of prompt as COC 1 and COC 2's
    /// PlayerInteractor: a line of text on a panel, e.g. "Press E to use the monitor", shown on PC and
    /// hidden on phones and tablets.
    ///
    /// ATTACH THIS TO: the Managers object of each COC 4 repair-site scene. Then wire, exactly as on
    /// PlayerInteractor:
    ///   Prompt Text  - the TMP text that shows the line.
    ///   Prompt UI    - the panel behind it, switched off whenever there is nothing to say.
    ///   Prompt On Touch Devices - leave OFF (PC only).
    ///
    /// WHAT IT SAYS (the stations' own words - change a station's name or key and the prompt follows):
    ///   "Press E to use the monitor" / "Press E to use the system unit"   walking up to a station
    ///   "Press F to diagnose"                                            a case locked until diagnosed
    ///   "Press E to step back"                                           at a station
    ///   "Press G to pick up the cleaning brush"                          a tool within reach
    ///
    /// HOW. RepairStation and ToolBelt already write that line into their own optional Prompt Text. Each
    /// of them is given a hidden text of its own to write into - one per station, because a station that
    /// is out of range blanks its label every frame, so two stations sharing your label would blank each
    /// other - and this shows the one that matters on the Prompt Text you wired. A Prompt Text you had
    /// already assigned on a station is taken over and blanked. RepairStation and ToolBelt are not modified.
    ///
    /// SAME PANEL AS COC 1? These site scenes are copies of COC 1, so COC 1's PlayerInteractor may already
    /// drive a prompt panel here. Wire that same panel if you like: when PlayerInteractor is running and
    /// uses the same Prompt Text or Prompt UI, the line is handed to it (PlayerInteractor.RequestPrompt -
    /// its own way for stations to use its prompt) instead of both scripts writing to one panel.
    ///
    /// HIDDEN while a dialog is open (like PlayerInteractor), the ticket intro panel is up, the diagnosis
    /// panel is open, the game is paused, or the player is held still outside a station.
    ///
    /// COC 4 ONLY: it does nothing unless the scene was reached from a ticket, so a scene shared with the
    /// COC 1 campaign is left alone there.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(360)]   // after the stations, which write their line in Update
    public class COC4InteractionPrompt : MonoBehaviour
    {
        [Header("UI")]
        [Tooltip("Simple prompt text, e.g. 'Press E to use the monitor'.")]
        [SerializeField] private TMP_Text promptText;

        [Tooltip("The panel holding the prompt. Switched off whenever there is nothing to say.\n\n" +
                 "Clearing the text alone leaves the panel behind it on screen - an empty box following " +
                 "the player around the room. Assign the panel and it disappears with the words.")]
        [SerializeField] private GameObject promptUI;

        [Tooltip("Show the text prompt on touch devices too.\n\n" +
                 "Off by default. On a phone the on-screen Use / Step Back / Grab buttons are already " +
                 "labelled, and a line of text naming a key the device does not have is worse than no " +
                 "prompt at all.")]
        [SerializeField] private bool promptOnTouchDevices = false;

        [Header("Options")]
        [Tooltip("Show 'Press E to step back' while the learner is at the monitor or inside the system unit.")]
        [SerializeField] private bool showStepBackPrompt = true;

        [Tooltip("Take over any Prompt Text already assigned on a RepairStation or the ToolBelt, so the line " +
                 "only ever appears here - and never on a phone. Leave on.")]
        [SerializeField] private bool takeOverStationPrompts = true;

        [Tooltip("At a case locked until diagnosed, show just \"Press F to diagnose\" instead of the station's " +
                 "full line (\"Press F to diagnose - the case stays shut until you do\").")]
        [SerializeField] private bool shortDiagnosePrompt = true;

        [Tooltip("Writes what it is doing to the Console.")]
        [SerializeField] private bool logPrompt = false;

        // ==================================================================

        class Source
        {
            public MonoBehaviour owner;   // a RepairStation or the ToolBelt
            public TMP_Text proxy;        // the hidden text it writes its line into
        }

        readonly List<Source> stationSources = new List<Source>();
        readonly List<Source> toolSources = new List<Source>();
        readonly Dictionary<RepairStation, Source> byStation = new Dictionary<RepairStation, Source>();

        GameObject proxyHolder;
        MobileControlsVisibility touchControls;
        TicketDiagnosisPanel[] diagnosisPanels = new TicketDiagnosisPanel[0];
        global::PlayerInteractor sharedCOC1;

        /// <summary>The line on screen right now, or "". For tests and debugging.</summary>
        public string ShownText { get; private set; } = "";

        // ==================================================================

        void Start()
        {
            if (!RepairTicketMode.IsActive)
            {
                // Normal campaign play in a scene shared with COC 1: not a repair site right now.
                enabled = false;
                return;
            }

            if (promptText == null && promptUI == null)
            {
                Debug.LogWarning("[COC4InteractionPrompt] Prompt Text and Prompt UI are both empty, so there is " +
                                 "nowhere to show the prompt. Wire them in the Inspector.", this);
                enabled = false;
                return;
            }

            HookSources();

            touchControls = FindAnyObjectByType<MobileControlsVisibility>(FindObjectsInactive.Include);
            diagnosisPanels = FindObjectsByType<TicketDiagnosisPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sharedCOC1 = FindCOC1InteractorOnSamePanel();

            Show("");

            if (logPrompt)
                Debug.Log("[COC4InteractionPrompt] Watching " + stationSources.Count + " station(s) and " +
                          toolSources.Count + " tool belt(s)" +
                          (sharedCOC1 != null ? "; sharing COC 1's prompt panel through PlayerInteractor." : "."), this);
        }

        void OnDisable()
        {
            Show("");
        }

        void LateUpdate()
        {
            string line = Tidy(ChooseLine());
            if (line.Length > 0 && HiddenNow()) line = "";

            // COC 1's PlayerInteractor owns this panel while it is running: hand it the line.
            if (sharedCOC1 != null && sharedCOC1.isActiveAndEnabled)
            {
                if (line.Length > 0) global::PlayerInteractor.RequestPrompt(line);
                ShownText = line;
                return;
            }

            Show(line);
        }

        // ==================================================================

        /// <summary>Writes the line, the way PlayerInteractor does: text and panel always agree.</summary>
        void Show(string line)
        {
            bool show = line.Length > 0;

            if (promptText != null) promptText.text = show ? line : "";
            if (promptUI != null && promptUI.activeSelf != show) promptUI.SetActive(show);

            if (logPrompt && line != ShownText)
                Debug.Log("[COC4InteractionPrompt] " + (show ? "\"" + line + "\"" : "(hidden)"), this);

            ShownText = line;
        }

        /// <summary>At a station: that station's line. Otherwise a tool in reach, then a station in reach.</summary>
        string ChooseLine()
        {
            RepairStation current = RepairStation.Current;
            if (current != null)
            {
                if (!showStepBackPrompt) return "";
                Source src;
                return byStation.TryGetValue(current, out src) ? Line(src) : "";
            }

            foreach (Source src in toolSources)
            {
                string t = Line(src);
                if (t.Length > 0) return t;
            }

            foreach (Source src in stationSources)
            {
                string t = Line(src);
                if (t.Length > 0) return t;
            }
            return "";
        }

        // "Press F to diagnose - the case stays shut until you do"  ->  "Press F to diagnose"
        static readonly Regex DiagnoseLine = new Regex(@"^\s*(Press\s+\S+\s+to\s+diagnose)\b.*$",
                                                       RegexOptions.IgnoreCase | RegexOptions.Singleline);

        string Tidy(string line)
        {
            if (!shortDiagnosePrompt || line.Length == 0) return line;
            Match m = DiagnoseLine.Match(line);
            return m.Success ? m.Groups[1].Value : line;
        }

        static string Line(Source src)
        {
            if (src == null || src.owner == null || !src.owner.isActiveAndEnabled || src.proxy == null) return "";
            string t = src.proxy.text;
            return string.IsNullOrWhiteSpace(t) ? "" : t.Trim();
        }

        bool HiddenNow()
        {
            if (!promptOnTouchDevices && TouchSession()) return true;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return true;
            if (COC4TicketIntroPanel.IsShowing) return true;
            if (PauseMenuController.AnyPaused) return true;
            foreach (TicketDiagnosisPanel p in diagnosisPanels)
                if (p != null && p.IsOpen) return true;

            // Held still outside any station: a "walk up and press E" line asks for something they cannot do.
            if (PlayerInputLock.IsLocked && RepairStation.Current == null) return true;
            return false;
        }

        /// <summary>
        /// The same test as COC 1's PlayerInteractor: MobileControlsVisibility decides (its Show In Editor
        /// tick included), and only without one does the platform decide.
        /// </summary>
        bool TouchSession()
        {
            if (touchControls != null) return touchControls.Wanted;
            return Application.isMobilePlatform;
        }

        // ==================================================================
        // Wiring the stations' own words
        // ==================================================================

        void HookSources()
        {
            proxyHolder = new GameObject("Prompt sources (hidden)", typeof(RectTransform));
            proxyHolder.transform.SetParent(transform, false);
            proxyHolder.SetActive(false);   // never drawn - only written to

            foreach (RepairStation s in FindObjectsByType<RepairStation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Source src = Hook(s);
                if (src == null) continue;
                stationSources.Add(src);
                byStation[s] = src;
            }

            foreach (ToolBelt b in FindObjectsByType<ToolBelt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Source src = Hook(b);
                if (src != null) toolSources.Add(src);
            }
        }

        /// <summary>Gives a station (or the tool belt) its own hidden text to write its prompt into.</summary>
        Source Hook(MonoBehaviour owner)
        {
            if (owner == null) return null;

            System.Reflection.FieldInfo field = null;
            for (System.Type t = owner.GetType(); t != null && field == null; t = t.BaseType)
                field = t.GetField("promptText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                                 System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != typeof(TMP_Text))
            {
                Debug.LogWarning("[COC4InteractionPrompt] '" + owner.name + "' has no Prompt Text field, so it cannot " +
                                 "be prompted for.", owner);
                return null;
            }

            TMP_Text existing = field.GetValue(owner) as TMP_Text;
            if (existing == promptText && existing != null)
                existing = null;   // wired to this prompt's own text - take it over quietly
            else if (existing != null && !takeOverStationPrompts)
                return new Source { owner = owner, proxy = existing };

            GameObject go = new GameObject("Prompt of " + owner.name, typeof(RectTransform));
            go.transform.SetParent(proxyHolder.transform, false);
            TMP_Text proxy = go.AddComponent<TextMeshProUGUI>();
            proxy.text = "";
            field.SetValue(owner, proxy);

            if (existing != null)
            {
                existing.text = "";
                Debug.Log("[COC4InteractionPrompt] '" + owner.name + "' had its own Prompt Text ('" + existing.name +
                          "'). Its line now shows on this prompt instead, and that text has been blanked - you can " +
                          "clear the station's Prompt Text field.", existing);
            }
            return new Source { owner = owner, proxy = proxy };
        }

        /// <summary>COC 1's PlayerInteractor, if it drives the same Prompt Text or Prompt UI as this.</summary>
        global::PlayerInteractor FindCOC1InteractorOnSamePanel()
        {
            foreach (global::PlayerInteractor pi in FindObjectsByType<global::PlayerInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (pi == null) continue;
                TMP_Text theirText = COC4Controls.Field<TMP_Text>(pi, "promptText");
                GameObject theirUI = COC4Controls.Field<GameObject>(pi, "promptUI");
                bool same = (promptText != null && theirText == promptText) || (promptUI != null && theirUI == promptUI);
                if (same) return pi;
            }
            return null;
        }
    }
}