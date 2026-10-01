// COC4TicketIntroPanel.cs
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using CircuitCrafter.Networking.Unity;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The intro panel for a COC 4 Story Mode ticket. Every time the learner arrives at a repair site
    /// from the ticket board, this panel opens first - one for every ticket, on every visit - and the
    /// job starts when they press Start Job. It replaces the arrival dialog (TicketArrivalDialog) and
    /// the Guide's plan message in Story Mode.
    ///
    /// WHAT IS ON IT
    ///   - The ticket's title and status (In Progress, or Diagnosed - repair pending on a return visit).
    ///   - The CUSTOMER'S REPORT - the ticket's Description, in the customer's own words. The cause is
    ///     never shown: finding it is the job. (TicketData.symptomText is left out for the same reason.)
    ///   - HOW TO COMPLETE THIS JOB - the five troubleshooting steps (CONFIRM, EXAMINE, DIAGNOSE,
    ///     REPAIR, TEST) with the real keys or buttons, the step to start on highlighted, and steps
    ///     already done ticked off. On a return visit it also names the cause found last time.
    ///     A site with no power-on test (a network ticket) gets the network version of the steps.
    ///   - The controls for this device, then Back to Tickets and Start Job (Enter also starts).
    ///
    /// WHILE IT IS OPEN the player cannot walk or look around and the cursor is free, so the buttons
    /// can be clicked. Start Job hands everything back, points the waypoint arrow at the first place
    /// to go, and lets COC4StepGuide carry on with the step messages.
    ///
    /// NOTHING TO WIRE. It adds itself to every repair site reached from a Story Mode ticket and builds
    /// its own screen. To change the wording or colours, add this component to the Managers object of a
    /// site scene and edit it there - that copy is used instead. To use your own designed panel, fill in
    /// the fields under "Your own panel". To go back to the old arrival dialog in a scene, add this
    /// component there and untick it.
    ///
    /// STORY MODE ONLY. In the COC 4 assessment (or any assessment) it never appears - the assessment
    /// keeps its own job order dialog, and TicketArrivalDialog is left switched on there.
    ///
    /// TEXT TOKENS for the step lines (each also works capitalised, e.g. {Interact}):
    ///   {interact} "press E" / "tap Use"    {leave} "press E again" / "tap Step Back"
    ///   {diagnose} "press F (or click Diagnose)" / "tap Diagnose"    {click} "click" / "tap"
    ///   {grab} "press G" / "tap Grab"       {penalty} seconds added per wrong diagnosis
    ///   {cause} the cause found on an earlier visit    {ticket} the ticket title
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(340)]   // after COC4StepGuide (330), so the guide knows the step first
    public class COC4TicketIntroPanel : MonoBehaviour
    {
        // ==================================================================
        // Adds itself to repair sites
        // ==================================================================

        /// <summary>Set false before a scene loads to stop the panel adding itself.</summary>
        public static bool AutoInstall = true;

        /// <summary>True while the panel is on screen. COC4StepGuide waits for this to be false.</summary>
        public static bool IsShowing { get; private set; }

        /// <summary>True when a panel in this scene has taken over the arrival from the dialogs.</summary>
        public static bool HandlesArrival { get; private set; }

        public static COC4TicketIntroPanel Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsShowing = false;
            HandlesArrival = false;
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AddIfTicketSite(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AddIfTicketSite(scene);
        }

        static void AddIfTicketSite(Scene scene)
        {
            if (!AutoInstall || !scene.IsValid() || !scene.isLoaded) return;
            if (!IsStoryTicket()) return;

            // Already in the scene (even unticked): that copy is the one in charge.
            if (FindAnyObjectByType<COC4TicketIntroPanel>(FindObjectsInactive.Include) != null) return;

            if (!IsRepairSite()) return;

            GameObject go = new GameObject("COC4TicketIntroPanel (added automatically)");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<COC4TicketIntroPanel>();
        }

        /// <summary>A Story Mode ticket is active - not the assessment.</summary>
        static bool IsStoryTicket()
        {
            return RepairTicketMode.IsActive && !COC4AssessmentRun.IsActive && !AssessmentMode.IsActive;
        }

        /// <summary>A scene the ticket sent the learner to - not the ticket board itself.</summary>
        static bool IsRepairSite()
        {
            if (FindAnyObjectByType<TicketHubView>(FindObjectsInactive.Include) != null) return false;

            return FindAnyObjectByType<TicketFaultCoordinator>(FindObjectsInactive.Include) != null ||
                   FindAnyObjectByType<RepairStation>(FindObjectsInactive.Include) != null ||
                   FindAnyObjectByType<PCPowerStation>(FindObjectsInactive.Include) != null ||
                   FindAnyObjectByType<TicketDiagnosisPanel>(FindObjectsInactive.Include) != null;
        }

        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("When it shows")]
        [Tooltip("Also show the panel when the learner comes back to a ticket they already diagnosed. " +
                 "Off: those visits get the old arrival dialog instead.")]
        public bool showOnReturnVisits = true;

        [Header("Text")]
        public string headerLine = "SERVICE TICKET  |  COC 4 STORY MODE";

        public string statusInProgress = "Status: In Progress";
        public string statusDiagnosed = "Status: Diagnosed - repair pending";

        public string reportHeading = "CUSTOMER'S REPORT";

        [Tooltip("Shown when the ticket has no Description.")]
        [TextArea(2, 4)]
        public string noReportText = "The customer did not leave a description. Switch the computer on and see the problem for yourself.";

        public string stepsHeading = "HOW TO COMPLETE THIS JOB";

        [Tooltip("The five steps at a site with a power-on test. Write each as \"NAME - what to do\"; the " +
                 "name is shown in bold. Tokens: {interact}, {leave}, {diagnose}, {click}, {penalty}.")]
        [TextArea(2, 4)]
        public string[] hardwareSteps =
        {
            "CONFIRM - Walk to the monitor, {interact} and {click} POWER. Watch the whole start-up: lights and fans, the BIOS screen, any beeps, and whether Windows loads.",
            "EXAMINE - {Leave} to step back, go to the system unit and {interact} to open it. Look before you touch: is every part seated, every cable plugged in, anything dusty?",
            "DIAGNOSE - {Diagnose} and choose the cause that explains everything you saw. Each wrong answer adds {penalty} seconds.",
            "REPAIR - Go to the system unit, {interact} to open it and fix the cause you named - nothing else.",
            "TEST - Step back from the case, go to the monitor, {interact} and {click} POWER again. The job is finished only when the computer starts up correctly."
        };

        [Tooltip("The five steps at a site with no power-on test (a network ticket). Same format and tokens.")]
        [TextArea(2, 4)]
        public string[] networkSteps =
        {
            "CONFIRM - Go to the computer and check what it can and cannot reach.",
            "EXAMINE - Look at its network settings: the IP address, subnet mask and default gateway.",
            "DIAGNOSE - {Diagnose} and choose the cause that explains what you found. Each wrong answer adds {penalty} seconds.",
            "REPAIR - Correct the setting that is wrong, then {click} OK.",
            "TEST - Ping the server from the Command Prompt to prove the connection works."
        };

        [Tooltip("On a return visit, above the steps. Tokens: {cause}.")]
        [TextArea(2, 4)]
        public string returnNote = "Cause found on your last visit: {cause}. Steps 1 to 3 are done - carry on from step 4.";

        [Tooltip("Next to the step to start on.")]
        public string currentStepTag = "START HERE";

        [Tooltip("Next to a step that is already done.")]
        public string doneTag = "done";

        [Tooltip("The controls line on a computer. Tokens: {interactKey}, {diagnoseControl}.")]
        [TextArea(1, 3)]
        public string controlsPC = "<b>WASD</b> walk    <b>Mouse</b> look    <b>{interactKey}</b> use / step back    {diagnoseControl}";

        [Tooltip("Added to the controls line on a computer when the site has tools to pick up. Tokens: {grabKey}.")]
        public string controlsGrabPC = "    <b>{grabKey}</b> pick up a tool";

        [Tooltip("The controls line on a phone or tablet. Tokens: {useButton}, {backButton}.")]
        [TextArea(1, 3)]
        public string controlsTouch = "<b>Joystick</b> walk    <b>Drag</b> look    <b>{useButton}</b> / <b>{backButton}</b> at a station    <b>Diagnose</b> button";

        [Tooltip("Added to the controls line on touch when the site has tools to pick up. Tokens: {grabButton}.")]
        public string controlsGrabTouch = "    <b>{grabButton}</b> pick up a tool";

        public string startLabel = "Start Job";
        public string backLabel = "Back to Tickets";

        [Tooltip("Shown beside the buttons on a computer.")]
        public string enterHint = "Press Enter to start";

        [Header("Buttons")]
        [Tooltip("Shows Back to Tickets, which returns to the ticket board without starting. The ticket stays as it was.")]
        public bool showBackButton = true;

        [Tooltip("The ticket board scene for Back to Tickets. Blank: AutoReturnToHub's Hub Scene Name, or \"COC4-Hub\".")]
        public string hubSceneName = "";

        [Tooltip("Enter (or keypad Enter) presses Start Job.")]
        public bool enterStarts = true;

        [Header("Look")]
        [Tooltip("Blank: the font of the dialog box, so the panel matches the rest of the game.")]
        public TMP_FontAsset font;

        [Tooltip("Optional 9-sliced sprite for the card and buttons (rounded corners). Blank: flat colour.")]
        public Sprite panelSprite;

        public Color accentColor = new Color32(0x46, 0xC8, 0xF5, 0xFF);
        public Color highlightColor = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        public Color doneColor = new Color32(0x5A, 0xE0, 0xA8, 0xFF);
        public Color cardColor = new Color32(0x0F, 0x1A, 0x2A, 0xFF);

        [Tooltip("Text and buttons are this much bigger on a phone or tablet.")]
        [Range(1f, 1.6f)] public float touchScale = 1.25f;

        [Tooltip("Canvas sort order. Raised further if another screen canvas is higher.")]
        public int sortingOrder = 500;

        [Header("After Start Job")]
        [Tooltip("Points the waypoint arrow at the first place to go (the monitor, or the system unit on a return visit).")]
        public bool pointArrow = true;

        [Tooltip("Called when Start Job is pressed, after the player is handed back control.")]
        public UnityEvent onStarted;

        [Header("Your own panel (optional)")]
        [Tooltip("Leave empty and the panel builds itself. Assign your own panel object to use your design; " +
                 "the text fields below are filled in, and any left empty are skipped.")]
        public GameObject panelRoot;
        public TMP_Text titleText;
        public TMP_Text statusText;
        public TMP_Text reportText;
        public TMP_Text noteText;
        public TMP_Text stepsText;
        public TMP_Text controlsText;
        public TMP_Text hintText;
        public Image iconImage;
        public Button startButton;
        public Button backButton;

        [Header("Debug")]
        public bool logPanel = true;

        // ==================================================================
        // Runtime
        // ==================================================================

        bool willShow;
        bool holding;
        bool refreshedAfterStart;
        float readyAt = float.MaxValue;
        bool builtHere;
        Canvas canvas;
        RectTransform card;
        float scale = 1f;

        PCPowerStation power;
        RepairStation monitor;
        RepairStation unit;
        ToolBelt toolBelt;
        TicketDiagnosisPanel diagnosisPanel;
        NetworkedWorkstation networkStation;
        Transform arrivalTarget;

        // ==================================================================
        // Unity
        // ==================================================================

        void Awake()
        {
            // Unity runs Awake on an unticked component too. Unticked means "keep the old arrival
            // dialog in this scene", so it must not take anything over.
            if (!enabled) return;

            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[COC4TicketIntroPanel] A second COC4TicketIntroPanel on '" + name +
                                 "' - the one on '" + Instance.name + "' is already in charge. Removing this copy.", this);
                Destroy(this);
                return;
            }
            Instance = this;

            if (!IsStoryTicket() || !IsRepairSite()) return;
            if (RepairTicketMode.IsDiagnosed && !showOnReturnVisits) return;

            willShow = true;
            HandlesArrival = true;

            // The arrival dialog would open on top of the panel. It is switched off before its Start
            // runs (every Awake comes before any Start), so it never plays. In the assessment this
            // code is never reached and the dialog carries the job order as before.
            foreach (TicketArrivalDialog d in FindObjectsByType<TicketArrivalDialog>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (d == null || !d.enabled) continue;
                Transform target = COC4Controls.Field<Transform>(d, "waypointTarget");
                if (target != null && arrivalTarget == null) arrivalTarget = target;
                d.enabled = false;
            }
        }

        void Start()
        {
            if (!willShow)
            {
                enabled = false;
                return;
            }

            FindSite();
            Show();
        }

        void OnDisable()
        {
            // Never leave the player frozen behind a panel that has gone away.
            if (IsShowing && Instance == this)
            {
                IsShowing = false;
                if (panelRoot != null) panelRoot.SetActive(false);
            }
            Release();
        }

        void OnDestroy()
        {
            Release();
            if (Instance != this) return;
            Instance = null;
            IsShowing = false;
            HandlesArrival = false;
        }

        void Update()
        {
            if (!IsShowing) return;

            // Once more on the first frame: every Start in the scene has run by now, so the guide
            // knows exactly which step the learner is on.
            if (!refreshedAfterStart)
            {
                refreshedAfterStart = true;
                FillTexts();
            }

            // Escape would open the pause menu underneath this panel, where it cannot be seen or
            // clicked. The panel answers the press instead (PauseMenuController steps aside for a
            // claimed Escape), so the learner starts the job or goes back first.
            if (Input.GetKeyDown(KeyCode.Escape)) EscapeKeyClaim.Claim();

            if (enterStarts && Time.unscaledTime >= readyAt &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                StartJob();
        }

        void LateUpdate()
        {
            // Several scripts write Cursor.lockState, some every frame. Keep it free while the panel is up.
            if (!IsShowing || COC4Controls.IsTouch) return;
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible) CursorControl.Free();
        }

        // ==================================================================
        // Showing and closing
        // ==================================================================

        void Show()
        {
            if (panelRoot == null) BuildPanel();
            WireButtons();
            FillTexts();

            panelRoot.SetActive(true);
            BringToFront();
            EnsureEventSystem();

            IsShowing = true;
            Hold();
            CursorControl.Free();
            ListWithCursorArbiter();

            // A short grace period, so an Enter still held from the board does not skip the panel.
            readyAt = Time.unscaledTime + 0.3f;

            if (logPanel)
                Debug.Log("[COC4TicketIntroPanel] Intro panel shown for '" + RepairTicketMode.ActiveTicketId + "'" +
                          (RepairTicketMode.IsDiagnosed ? " (return visit, already diagnosed)." : "."), this);
        }

        /// <summary>The Start Job button. Also callable from anywhere.</summary>
        public void StartJob()
        {
            if (!IsShowing) return;
            Close();

            // Hand the mouse back to looking around, unless something else still needs it free.
            if (!PlayerInputLock.IsLocked) CursorControl.Capture();

            if (pointArrow) PointArrow();

            if (onStarted != null) onStarted.Invoke();

            if (logPanel) Debug.Log("[COC4TicketIntroPanel] Start Job - '" + RepairTicketMode.ActiveTicketId + "'.", this);
        }

        /// <summary>The Back to Tickets button: back to the board without starting. The ticket keeps its status.</summary>
        public void BackToTickets()
        {
            if (!IsShowing) return;
            Close();

            string scene = HubScene();
            if (logPanel) Debug.Log("[COC4TicketIntroPanel] Back to Tickets - loading '" + scene + "'.", this);
            SceneManager.LoadScene(scene);
        }

        void Close()
        {
            IsShowing = false;
            if (panelRoot != null) panelRoot.SetActive(false);
            Release();
        }

        string HubScene()
        {
            if (!string.IsNullOrWhiteSpace(hubSceneName)) return hubSceneName.Trim();
            AutoReturnToHub auto = FindAnyObjectByType<AutoReturnToHub>(FindObjectsInactive.Include);
            string fromAuto = auto != null ? COC4Controls.Field<string>(auto, "hubSceneName") : null;
            return string.IsNullOrWhiteSpace(fromAuto) ? "COC4-Hub" : fromAuto.Trim();
        }

        /// <summary>No walking or mouse-look while the panel is up, and CursorKeeper leaves the cursor alone.</summary>
        void Hold()
        {
            if (holding) return;
            holding = true;
            PlayerInputLock.Acquire();
            CursorKeeper.Claim();
        }

        void Release()
        {
            if (!holding) return;
            holding = false;
            PlayerInputLock.Release();
            CursorKeeper.Release();
        }

        /// <summary>A site built from a COC 3 scene has a CursorArbiter; the panel goes on its Free While Active list.</summary>
        void ListWithCursorArbiter()
        {
            CursorArbiter arbiter = CursorArbiter.Instance;
            if (arbiter == null || panelRoot == null) return;

            GameObject[] list = arbiter.freeWhileActive;
            if (list != null)
                foreach (GameObject go in list) if (go == panelRoot) return;

            int count = list != null ? list.Length : 0;
            var grown = new GameObject[count + 1];
            if (count > 0) System.Array.Copy(list, grown, count);
            grown[count] = panelRoot;
            arbiter.freeWhileActive = grown;
        }

        void PointArrow()
        {
            if (WaypointIndicator.Instance == null) return;

            // The guide knows the exact step (it may be step 2 on a second visit, say).
            COC4StepGuide guide = COC4StepGuide.Instance;
            if (guide != null && guide.isActiveAndEnabled && guide.PointArrowAtCurrentStep()) return;

            Transform target;
            if (RepairTicketMode.IsDiagnosed)
                target = networkStation != null ? networkStation.transform : StationPoint(unit);
            else if (arrivalTarget != null)
                target = arrivalTarget;
            else if (monitor != null)
                target = StationPoint(monitor);
            else
                target = networkStation != null ? networkStation.transform : null;

            if (target != null) WaypointIndicator.Instance.SetTarget(target);
        }

        static Transform StationPoint(RepairStation s)
        {
            if (s == null) return null;
            Transform p = COC4Controls.Field<Transform>(s, "stationPoint");
            return p != null ? p : s.transform;
        }

        // ==================================================================
        // The site
        // ==================================================================

        void FindSite()
        {
            power = FindAnyObjectByType<PCPowerStation>(FindObjectsInactive.Include);
            diagnosisPanel = FindAnyObjectByType<TicketDiagnosisPanel>(FindObjectsInactive.Include);
            toolBelt = ToolBelt.Instance != null ? ToolBelt.Instance : FindAnyObjectByType<ToolBelt>(FindObjectsInactive.Include);

            if (power != null)
            {
                monitor = COC4Controls.Field<RepairStation>(power, "monitorStation");
                unit = COC4Controls.Field<RepairStation>(power, "systemUnitStation");
            }
            if (unit == null && toolBelt != null) unit = COC4Controls.Field<RepairStation>(toolBelt, "depositStation");
            if (unit == null || monitor == null)
            {
                foreach (RepairStation s in FindObjectsByType<RepairStation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (s == null || s == unit || s == monitor) continue;
                    if (monitor == null) monitor = s;
                    else if (unit == null) unit = s;
                }
            }

            foreach (NetworkFault f in FindObjectsByType<NetworkFault>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (f == null || !f.IsActiveForThisVisit) continue;
                networkStation = COC4Controls.Field<NetworkedWorkstation>(f, "station");
                if (networkStation != null) break;
            }
        }

        /// <summary>1 to 5; 6 when every step is done.</summary>
        int CurrentStep()
        {
            // A network site has no power-on test to show that step 1 is done, so it always starts at
            // the beginning (or at the repair, once diagnosed).
            if (power == null) return RepairTicketMode.IsDiagnosed ? 4 : 1;

            COC4StepGuide guide = COC4StepGuide.Instance;
            if (guide != null && guide.isActiveAndEnabled)
            {
                int n = guide.CurrentStepNumber;
                if (n > 0) return n;
            }
            return RepairTicketMode.IsDiagnosed ? 4 : 1;
        }

        int PenaltySeconds()
        {
            return diagnosisPanel != null ? COC4Controls.Field(diagnosisPanel, "wrongAnswerPenaltySeconds", 60) : 60;
        }

        static string CauseOf(TicketData t)
        {
            if (t == null || t.diagnosisOptions == null) return "the cause you named";
            foreach (TicketData.DiagnosisOption o in t.diagnosisOptions)
                if (o != null && o.isCorrect && !string.IsNullOrWhiteSpace(o.label))
                {
                    string s = o.label.Trim();
                    // Keep acronyms ("RAM ...", "PSU ...") as they are.
                    if (s.Length > 1 && char.IsUpper(s[1])) return s;
                    return char.ToLowerInvariant(s[0]) + s.Substring(1);
                }
            return "the cause you named";
        }

        // ==================================================================
        // Text
        // ==================================================================

        void FillTexts()
        {
            TicketData ticket = RepairTicketMode.ActiveTicket;
            bool diagnosed = RepairTicketMode.IsDiagnosed;
            bool touch = COC4Controls.IsTouch;

            Dictionary<string, string> common = COC4Controls.Tokens(unit != null ? unit : monitor, toolBelt);
            var vars = new Dictionary<string, string>
            {
                { "ticket", ticket != null && !string.IsNullOrWhiteSpace(ticket.title) ? ticket.title.Trim() : "Service ticket" },
                { "cause", CauseOf(ticket) },
                { "penalty", PenaltySeconds().ToString() }
            };

            if (titleText != null) titleText.text = vars["ticket"];

            if (iconImage != null)
            {
                Sprite icon = ticket != null ? ticket.icon : null;
                iconImage.sprite = icon;
                iconImage.gameObject.SetActive(icon != null);
            }

            if (statusText != null)
            {
                statusText.text = diagnosed ? statusDiagnosed : statusInProgress;
                statusText.color = diagnosed ? highlightColor : accentColor;
            }

            if (reportText != null)
            {
                string report = ticket != null ? ticket.description : null;
                reportText.text = string.IsNullOrWhiteSpace(report) ? noReportText : "\"" + report.Trim() + "\"";
            }

            if (noteText != null)
            {
                noteText.text = diagnosed ? COC4Controls.Fill(returnNote, vars, common) : "";
                noteText.gameObject.SetActive(diagnosed && !string.IsNullOrWhiteSpace(returnNote));
            }

            if (stepsText != null)
                stepsText.text = BuildSteps(power != null ? hardwareSteps : networkSteps, CurrentStep(), vars, common);

            if (controlsText != null) controlsText.text = BuildControls(touch, common);

            if (hintText != null)
            {
                hintText.text = touch || !enterStarts ? "" : enterHint;
            }

            if (backButton != null) backButton.gameObject.SetActive(showBackButton);
            SetButtonLabel(startButton, startLabel);
            SetButtonLabel(backButton, backLabel);

            FitToScreen();
        }

        string BuildSteps(string[] steps, int current, Dictionary<string, string> vars, Dictionary<string, string> common)
        {
            if (steps == null) return "";

            string highlight = "#" + ColorUtility.ToHtmlStringRGB(highlightColor);
            string done = "#" + ColorUtility.ToHtmlStringRGB(doneColor);
            var sb = new StringBuilder();

            for (int i = 0; i < steps.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(steps[i])) continue;
                int n = i + 1;
                string name, body;
                SplitStep(COC4Controls.Fill(steps[i], vars, common), out name, out body);

                if (sb.Length > 0) sb.Append('\n');

                // A hanging indent: the number sits on its own, the text lines up after it.
                if (n < current)
                    sb.Append("<color=" + done + ">" + n + ".<indent=1.4em><b>" + name + "</b>  -  " + doneTag + "</indent></color>");
                else if (n == current)
                    sb.Append("<color=" + highlight + ">" + n + ".<indent=1.4em><b>" + name + "</b>   <size=80%>" + currentStepTag +
                              "</size></color>\n<color=#FFFFFF>" + body + "</color></indent>");
                else
                    sb.Append("<color=#DDE6F2>" + n + ".<indent=1.4em><b>" + name + "</b>  <color=#9AAABF>" + body + "</color></indent></color>");
            }
            return sb.ToString();
        }

        /// <summary>"CONFIRM - Walk to ..." into "CONFIRM" and "Walk to ...".</summary>
        static void SplitStep(string line, out string name, out string body)
        {
            line = (line ?? "").Trim();
            int dash = line.IndexOf(" - ");
            if (dash > 0 && dash <= 24)
            {
                name = line.Substring(0, dash).Trim();
                body = line.Substring(dash + 3).Trim();
            }
            else
            {
                name = "";
                body = line;
            }
        }

        string BuildControls(bool touch, Dictionary<string, string> common)
        {
            KeyCode diagnoseKey = RepairWorkGate.DiagnosisKey;
            var vars = new Dictionary<string, string>
            {
                { "diagnoseControl", diagnoseKey != KeyCode.None
                    ? "<b>" + COC4Controls.KeyName(diagnoseKey) + "</b> diagnose"
                    : "<b>Diagnose</b> button" }
            };

            string line = touch ? controlsTouch : controlsPC;
            if (toolBelt != null) line += touch ? controlsGrabTouch : controlsGrabPC;
            return COC4Controls.Fill(line, vars, common);
        }

        static void SetButtonLabel(Button b, string label)
        {
            if (b == null || string.IsNullOrEmpty(label)) return;
            TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = label;
        }

        void WireButtons()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(StartJob);
                startButton.onClick.AddListener(StartJob);
            }
            if (backButton != null)
            {
                backButton.onClick.RemoveListener(BackToTickets);
                backButton.onClick.AddListener(BackToTickets);
            }
        }

        // ==================================================================
        // The screen it builds for itself
        // ==================================================================

        static readonly Color TextMain = new Color32(0xF2, 0xF6, 0xFB, 0xFF);
        static readonly Color TextSoft = new Color32(0x8F, 0xA3, 0xBF, 0xFF);
        static readonly Color BoxColor = new Color32(0x0A, 0x13, 0x20, 0xFF);
        static readonly Color LineColor = new Color32(0x22, 0x32, 0x4A, 0xFF);

        void BuildPanel()
        {
            builtHere = true;
            bool touch = COC4Controls.IsTouch;
            scale = touch ? touchScale : 1f;
            if (font == null && DialogManager.Instance != null)
            {
                TMP_Text dialogText = COC4Controls.Field<TMP_Text>(DialogManager.Instance, "dialogBodyText");
                if (dialogText != null) font = dialogText.font;
            }

            // Canvas - its own, so it sorts above everything and scales with the screen.
            GameObject canvasGO = NewUI("COC4 Ticket Intro Canvas", transform);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;   // always 1080 tall; wide phones just get more room at the sides
            canvasGO.AddComponent<GraphicRaycaster>();

            // Backdrop - dims the site and stops clicks reaching anything behind it.
            panelRoot = NewUI("Intro Panel", canvasGO.transform);
            Stretch(panelRoot);
            Image backdrop = panelRoot.AddComponent<Image>();
            backdrop.color = new Color(0.02f, 0.04f, 0.08f, 0.86f);
            backdrop.raycastTarget = true;

            // Card
            GameObject cardGO = NewUI("Card", panelRoot.transform);
            card = (RectTransform)cardGO.transform;
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            float canvasWidth = 1080f * Screen.width / Mathf.Max(1f, Screen.height);
            float width = Mathf.Clamp(touch ? 1500f : 1120f, 600f, Mathf.Max(600f, canvasWidth - 80f));
            card.sizeDelta = new Vector2(width, 0f);
            Image cardImage = cardGO.AddComponent<Image>();
            cardImage.color = cardColor;
            if (panelSprite != null) { cardImage.sprite = panelSprite; cardImage.type = Image.Type.Sliced; }

            VerticalLayoutGroup column = cardGO.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(56, 56, 46, 40);
            column.spacing = 14f * scale;
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            ContentSizeFitter fitter = cardGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Accent bar along the top edge.
            GameObject bar = NewUI("Accent Bar", cardGO.transform);
            bar.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(0f, 8f);
            barRect.anchoredPosition = Vector2.zero;
            bar.AddComponent<Image>().color = accentColor;

            TMP_Text header = Label(cardGO.transform, "Header", 19f, accentColor, FontStyles.Bold, TextAlignmentOptions.Left);
            header.text = headerLine;
            header.characterSpacing = 6f;

            // Title row: the ticket's icon (if it has one) and title.
            GameObject titleRow = NewUI("Title Row", cardGO.transform);
            HorizontalLayoutGroup titleLayout = titleRow.AddComponent<HorizontalLayoutGroup>();
            titleLayout.spacing = 20f;
            titleLayout.childAlignment = TextAnchor.MiddleLeft;
            titleLayout.childControlWidth = true;
            titleLayout.childControlHeight = true;
            titleLayout.childForceExpandWidth = false;
            titleLayout.childForceExpandHeight = false;

            GameObject iconGO = NewUI("Icon", titleRow.transform);
            iconImage = iconGO.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            LayoutElement iconSize = iconGO.AddComponent<LayoutElement>();
            iconSize.minWidth = iconSize.preferredWidth = 84f * scale;
            iconSize.minHeight = iconSize.preferredHeight = 84f * scale;

            titleText = Label(titleRow.transform, "Title", 42f, TextMain, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            statusText = Label(cardGO.transform, "Status", 23f, accentColor, FontStyles.Bold, TextAlignmentOptions.Left);

            Divider(cardGO.transform);

            TMP_Text reportHead = Label(cardGO.transform, "Report Heading", 19f, TextSoft, FontStyles.Bold, TextAlignmentOptions.Left);
            reportHead.text = reportHeading;
            reportHead.characterSpacing = 4f;

            GameObject reportBox = NewUI("Report Box", cardGO.transform);
            Image boxImage = reportBox.AddComponent<Image>();
            boxImage.color = BoxColor;
            if (panelSprite != null) { boxImage.sprite = panelSprite; boxImage.type = Image.Type.Sliced; }
            VerticalLayoutGroup boxLayout = reportBox.AddComponent<VerticalLayoutGroup>();
            boxLayout.padding = new RectOffset(28, 28, 18, 18);
            boxLayout.childControlWidth = true;
            boxLayout.childControlHeight = true;
            boxLayout.childForceExpandWidth = true;
            boxLayout.childForceExpandHeight = false;
            reportText = Label(reportBox.transform, "Report", 26f, TextMain, FontStyles.Italic, TextAlignmentOptions.TopLeft);

            noteText = Label(cardGO.transform, "Return Note", 23f, doneColor, FontStyles.Bold, TextAlignmentOptions.Left);

            TMP_Text stepsHead = Label(cardGO.transform, "Steps Heading", 19f, TextSoft, FontStyles.Bold, TextAlignmentOptions.Left);
            stepsHead.text = stepsHeading;
            stepsHead.characterSpacing = 4f;

            stepsText = Label(cardGO.transform, "Steps", 24f, TextMain, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            stepsText.lineSpacing = 4f;
            stepsText.paragraphSpacing = 12f;

            Divider(cardGO.transform);

            controlsText = Label(cardGO.transform, "Controls", 19f, TextSoft, FontStyles.Normal, TextAlignmentOptions.Left);

            // Buttons row: hint on the left, Back and Start on the right.
            GameObject buttons = NewUI("Buttons", cardGO.transform);
            HorizontalLayoutGroup buttonLayout = buttons.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = 20f;
            buttonLayout.childAlignment = TextAnchor.MiddleRight;
            buttonLayout.childControlWidth = true;
            buttonLayout.childControlHeight = true;
            buttonLayout.childForceExpandWidth = false;
            buttonLayout.childForceExpandHeight = false;

            hintText = Label(buttons.transform, "Hint", 19f, TextSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            hintText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            float buttonHeight = touch ? 104f : 72f;
            float buttonWidth = touch ? 380f : 300f;
            backButton = MakeButton(buttons.transform, "Back Button", backLabel, LineColor, TextMain, buttonWidth, buttonHeight);
            startButton = MakeButton(buttons.transform, "Start Button", startLabel, highlightColor, new Color32(0x10, 0x15, 0x1F, 0xFF),
                                     buttonWidth, buttonHeight);
        }

        /// <summary>Shrinks the card to fit when a long report or a phone's big text makes it taller than the screen.</summary>
        void FitToScreen()
        {
            if (!builtHere || card == null) return;
            card.localScale = Vector3.one;
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            float height = card.rect.height;
            const float room = 1080f - 60f;
            float s = height > room ? room / height : 1f;
            card.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>Its own canvas is sorted above every other screen canvas that is showing.</summary>
        void BringToFront()
        {
            if (panelRoot == null) return;

            Canvas own = canvas != null ? canvas : panelRoot.GetComponentInParent<Canvas>();
            if (own == null) return;

            int highest = int.MinValue;
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c == null || c == own || !c.isActiveAndEnabled) continue;
                if (c.renderMode == RenderMode.WorldSpace) continue;
                if (!c.isRootCanvas && !c.overrideSorting) continue;
                if (c.transform.IsChildOf(panelRoot.transform)) continue;
                if (c.sortingOrder > highest) highest = c.sortingOrder;
            }

            if (!own.isRootCanvas) own.overrideSorting = true;
            if (highest != int.MinValue && own.sortingOrder <= highest)
                own.sortingOrder = Mathf.Min(highest + 1, short.MaxValue);
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>(FindObjectsInactive.Exclude) != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Debug.Log("[COC4TicketIntroPanel] This scene had no EventSystem, so the panel's buttons could not be clicked. One was added.");
        }

        static GameObject NewUI(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;   // UI
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stretch(GameObject go)
        {
            RectTransform r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        TMP_Text Label(Transform parent, string name, float size, Color color, FontStyles style, TextAlignmentOptions align)
        {
            GameObject go = NewUI(name, parent);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size * scale;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            t.text = "";
            return t;
        }

        void Divider(Transform parent)
        {
            GameObject line = NewUI("Divider", parent);
            Image img = line.AddComponent<Image>();
            img.color = LineColor;
            img.raycastTarget = false;
            LayoutElement le = line.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 2f;
        }

        Button MakeButton(Transform parent, string name, string label, Color background, Color textColor, float width, float height)
        {
            GameObject go = NewUI(name, parent);
            Image img = go.AddComponent<Image>();
            img.color = background;
            if (panelSprite != null) { img.sprite = panelSprite; img.type = Image.Type.Sliced; }
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            le.minHeight = le.preferredHeight = height;

            TMP_Text t = Label(go.transform, "Label", 26f, textColor, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(t.gameObject);
            t.text = label;
            return b;
        }
    }
}
