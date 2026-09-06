// DeviceConfigStation.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// A machine the player walks up to, opens, and types settings into: the server's IP address,
    /// the client's IP address, the router's SSID and security. Press Apply and it is marked against
    /// the job order.
    ///
    /// ONE SCRIPT FOR THREE STATIONS, ON PURPOSE
    ///
    /// The server, the client and the router all do the same thing - present some named fields, take
    /// what the player typed, compare it to what the job order asked for, and say which lines are
    /// wrong. Only the field LIST differs. Writing three near-identical scripts would mean three
    /// places to fix every bug and three Inspectors to learn; this way you learn one component and
    /// fill in a different list on each machine.
    ///
    /// WHAT IT MARKS
    ///
    /// Each field carries the answer the job order calls for. Comparison is trimmed and
    /// case-insensitive, so "wpa2" and "WPA2 " both pass - the trainee is being examined on knowing
    /// the setting, not on their typing. Where several answers are legitimate (any host address from
    /// .10 to .20, say) put them all in Also Accept and any one of them passes.
    ///
    /// SETUP:
    ///   1. Add this to the server PC, beside ConnectivityCheckStation. Trigger collider as usual.
    ///   2. Build a small UI panel with one TMP Input Field per setting and an Apply button.
    ///      Leave the panel INACTIVE in the scene - this switches it on.
    ///   3. Drag the panel into Panel, the button into Apply Button.
    ///   4. Fill Fields: one entry per setting, each with its Input Field and its Expected Value.
    ///   5. Assign Success Dialog.
    ///
    /// Then do the same on the client and the router with their own field lists.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public class DeviceConfigStation : Interactable
    {
        [System.Serializable]
        public class ConfigField
        {
            [Tooltip("What this setting is called, e.g. 'IP Address'. Used in the result message.")]
            public string label = "IP Address";

            [Tooltip("The box the player types into.")]
            public TMP_InputField input;

            [Tooltip("What the job order asks for. Compared trimmed and case-insensitively.")]
            public string expectedValue = "192.168.1.10";

            [Tooltip("Other answers that are also correct. Leave empty when there is exactly one " +
                     "right answer.")]
            public string[] alsoAccept = new string[0];

            [Tooltip("Shown beside this field when the player gets it wrong. Say WHY, not just " +
                     "'wrong' - 'the host part must be unique on this subnet' teaches something.")]
            [TextArea(1, 3)]
            public string wrongHint = "";

            [Tooltip("Fill this box in for the player when the station opens. Use it for settings " +
                     "that are given rather than worked out, like a gateway everyone shares.")]
            public bool prefill = false;
        }

        [Header("Identity")]
        [Tooltip("Shown in messages, e.g. 'Server' or 'Router'.")]
        public string stationName = "Server";

        [Header("The settings being marked")]
        public List<ConfigField> fields = new List<ConfigField>();

        [Header("UI")]
        [Tooltip("The panel that appears when the player interacts. Leave it INACTIVE in the scene.")]
        public GameObject panel;
        [Tooltip("The Apply button. Its On Click is wired for you at Start - you do not have to " +
                 "hook anything in the Inspector.")]
        public Button applyButton;
        [Tooltip("Optional Close/Cancel button.")]
        public Button closeButton;
        [Tooltip("Where the result is written - 'IP Address is wrong: ...'.")]
        public TMP_Text resultText;
        [Tooltip("Optional. The job order the player is working from, shown while the panel is open.")]
        public TMP_Text jobOrderText;
        [TextArea(3, 8)]
        [Tooltip("What to write into Job Order Text. This is the spec the trainee is marked against, " +
                 "so it should say exactly what the fields expect.")]
        public string jobOrder = "";

        [Header("While the panel is open")]
        [Tooltip("Free the mouse cursor so the player can click the fields. Untick if your own " +
                 "pause handling already does this.")]
        public bool releaseCursor = true;
        [Tooltip("Set Time.timeScale to 0 while the panel is open.")]
        public bool freezeTime = false;
        [Tooltip("Hidden while the panel is open - a crosshair, the mobile controls, a HUD.")]
        public GameObject[] hideWhileOpen = new GameObject[0];

        [Header("Rules")]
        [Tooltip("Let the player keep trying after a wrong answer. Untick only if a wrong " +
                 "configuration should end the attempt.")]
        public bool allowRetry = true;
        [Tooltip("Blank fields count as wrong rather than being skipped.")]
        public bool requireEveryField = true;
        [Tooltip("Fill every field with its expected value and pass on the first Apply. Testing " +
                 "only - the Console shouts about it.")]
        public bool autoFillForTesting = false;

        [Header("Dialogs")]
        public DialogSequence briefingDialog;
        public DialogSequence successDialog;
        public DialogSequence failDialog;
        public bool successDialogOnlyOnce = true;

        [Header("Task")]
        [Tooltip("Reported to TaskManager once this machine is configured correctly.")]
        public string taskId = "configure_network";
        [Tooltip("Register Task Id at Start if nothing else has. Reporting an unregistered id " +
                 "silently does nothing, which is a nasty afternoon to lose.")]
        public bool autoRegisterTask = true;
        [Tooltip("How many machines share this Task Id. Server + client = 2. Every station sharing " +
                 "the id must carry the same number here.")]
        public int taskSteps = 2;

        [Header("Events")]
        public UnityEvent OnOpened;
        public UnityEvent OnClosed;
        public UnityEvent OnPassed;
        public UnityEvent OnFailed;

        [Header("Diagnostics")]
        public bool logResult = true;

        /// <summary>True once this machine has been configured correctly. ConnectivityCheckStation
        /// reads this, and so can a waypoint step.</summary>
        public bool IsConfigured { get; private set; }

        /// <summary>True while the panel is up.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>How many times Apply has been pressed with something wrong. Worth recording in
        /// an assessment - a trainee who gets it right first time is not the same as one who
        /// brute-forced it.</summary>
        public int WrongAttempts { get; private set; }

        bool shownSuccess;
        bool shownBriefing;
        bool reported;
        float savedTimeScale = 1f;
        CursorLockMode savedLock;
        bool savedCursorVisible;

        void Start()
        {
            if (autoRegisterTask && !string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                TaskManager.Instance.RegisterTask(taskId, Mathf.Max(1, taskSteps));

            // Wired in code rather than left to the Inspector. A button whose On Click was never
            // hooked looks identical to one that was, and the failure - clicking Apply and nothing
            // happening at all - gives no clue where to look.
            if (applyButton != null) applyButton.onClick.AddListener(Apply);
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            if (panel != null) panel.SetActive(false);
            ValidateSetup();
        }

        public override void Interact()
        {
            if (IsOpen) return;

            if (briefingDialog != null && !shownBriefing && DialogManager.Instance != null &&
                !DialogManager.Instance.IsDialogOpen)
            {
                shownBriefing = true;
                DialogManager.Instance.ShowDialog(briefingDialog, Open);
                return;
            }
            Open();
        }

        // ---------------------------------------------------------------- open / close

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            if (panel != null) panel.SetActive(true);
            foreach (GameObject go in hideWhileOpen) if (go != null) go.SetActive(false);

            if (releaseCursor)
            {
                savedLock = Cursor.lockState;
                savedCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (freezeTime) { savedTimeScale = Time.timeScale; Time.timeScale = 0f; }

            SetText(jobOrderText, jobOrder);
            SetText(resultText, "");

            foreach (ConfigField f in fields)
            {
                if (f == null || f.input == null) continue;
                if (autoFillForTesting || f.prefill) f.input.text = f.expectedValue;
            }

            OnOpened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            if (panel != null) panel.SetActive(false);
            foreach (GameObject go in hideWhileOpen) if (go != null) go.SetActive(true);

            if (releaseCursor) { Cursor.lockState = savedLock; Cursor.visible = savedCursorVisible; }
            if (freezeTime) Time.timeScale = savedTimeScale;

            OnClosed?.Invoke();
        }

        // ---------------------------------------------------------------- marking

        /// <summary>Marks what the player typed. Wired to the Apply button at Start; also public so
        /// a UnityEvent or an on-screen keyboard's Done key can call it.</summary>
        public void Apply()
        {
            List<string> wrong = new List<string>();

            for (int i = 0; i < fields.Count; i++)
            {
                ConfigField f = fields[i];
                if (f == null || f.input == null) continue;

                string typed = (f.input.text ?? "").Trim();

                if (typed.Length == 0)
                {
                    if (requireEveryField) wrong.Add(f.label + " is empty.");
                    continue;
                }
                if (!Accepts(f, typed))
                    wrong.Add(f.label + " is wrong." +
                              (string.IsNullOrEmpty(f.wrongHint) ? "" : " " + f.wrongHint));
            }

            bool passed = wrong.Count == 0;

            if (passed)
            {
                SetText(resultText, stationName + " configured. Settings applied.");
                Finish(true);
                return;
            }

            WrongAttempts++;

            // Every wrong line, not just the first. A configuration screen is marked as a whole -
            // fixing one line and being told about the next one only then is how you turn a
            // two-minute task into ten minutes of guessing.
            string msg = "Settings not applied:";
            foreach (string w in wrong) msg += "\n  - " + w;
            SetText(resultText, msg);

            Finish(false);

            if (!allowRetry) Close();
        }

        static bool Accepts(ConfigField f, string typed)
        {
            if (Same(typed, f.expectedValue)) return true;
            if (f.alsoAccept != null)
                foreach (string alt in f.alsoAccept)
                    if (Same(typed, alt)) return true;
            return false;
        }

        static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        void Finish(bool passed)
        {
            if (logResult)
                Debug.LogWarning("[DeviceConfigStation] " + stationName + ": " +
                                 (passed ? "configured correctly." : "rejected, attempt " +
                                  WrongAttempts + "."), this);

            if (passed)
            {
                bool first = !IsConfigured;
                IsConfigured = true;

                if (first && !reported && !string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                {
                    reported = true;
                    TaskManager.Instance.ReportProgress(taskId);
                }

                OnPassed?.Invoke();
                Say(successDialog, successDialogOnlyOnce && shownSuccess);
                shownSuccess = true;
                Close();
            }
            else
            {
                OnFailed?.Invoke();
                Say(failDialog, false);
            }
        }

        void Say(DialogSequence sequence, bool alreadyShown)
        {
            if (sequence == null || alreadyShown) return;
            if (DialogManager.Instance == null || DialogManager.Instance.IsDialogOpen) return;
            DialogManager.Instance.ShowDialog(sequence);
        }

        static void SetText(TMP_Text t, string s) { if (t != null) t.text = s; }

        // ---------------------------------------------------------------- public API

        /// <summary>Clears the result. Wire to a procedure restart.</summary>
        public void ResetStation()
        {
            IsConfigured = false;
            reported = false;
            shownSuccess = false;
            shownBriefing = false;
            WrongAttempts = 0;
            foreach (ConfigField f in fields)
                if (f != null && f.input != null && !f.prefill) f.input.text = "";
            SetText(resultText, "");
        }

        // ---------------------------------------------------------------- self-reporting

        void ValidateSetup()
        {
            List<string> problems = new List<string>();

            if (autoFillForTesting)
                Debug.LogWarning("[DeviceConfigStation] " + stationName + " AUTO-FILLS every field. " +
                                 "Untick 'Auto Fill For Testing' before a graded build.", this);

            if (fields.Count == 0)
                problems.Add("Fields is empty - there is nothing to configure or mark.");

            if (panel == null)
                problems.Add("Panel is empty, so interacting opens nothing.");
            else if (panel.activeSelf)
                problems.Add("Panel '" + panel.name + "' is ACTIVE in the scene. It should start " +
                             "inactive - otherwise it is on screen from the first frame.");

            if (applyButton == null)
                problems.Add("Apply Button is empty. Nothing can submit the form. (You can call " +
                             "Apply() from a UnityEvent instead, if that is deliberate.)");

            for (int i = 0; i < fields.Count; i++)
            {
                ConfigField f = fields[i];
                string where = "Field " + i + " ('" + (f != null ? f.label : "null") + "')";
                if (f == null) { problems.Add("Field " + i + " is empty."); continue; }
                if (f.input == null) problems.Add(where + " has no Input Field assigned.");
                if (string.IsNullOrEmpty(f.expectedValue))
                    problems.Add(where + " has no Expected Value, so nothing typed can ever be right.");
            }

            if (successDialog == null)
                problems.Add("Success Dialog is empty, so a correct configuration says nothing.");

            if (GetComponent<Collider>() == null)
                problems.Add("No Collider, so the player can never walk up to this station.");

            if (problems.Count == 0)
            {
                Debug.LogWarning("[DeviceConfigStation] " + stationName + " ready, marking " +
                                 fields.Count + " setting(s).", this);
                return;
            }

            string msg = "[DeviceConfigStation] " + stationName + " setup is incomplete:";
            foreach (string p in problems) msg += "\n  - " + p;
            Debug.LogWarning(msg, this);
        }

        /// <summary>Prints every field, what is typed in it, and what it wants.</summary>
        [ContextMenu("Report settings")]
        public void LogSettings()
        {
            string msg = "[DeviceConfigStation] " + stationName + ", " + fields.Count + " setting(s):";
            foreach (ConfigField f in fields)
            {
                if (f == null) { msg += "\n    (empty)"; continue; }
                string typed = f.input != null ? "'" + (f.input.text ?? "") + "'" : "(no input field)";
                msg += "\n    " + f.label + "   typed: " + typed + "   wants: '" + f.expectedValue + "'" +
                       (f.alsoAccept != null && f.alsoAccept.Length > 0
                            ? " (or " + f.alsoAccept.Length + " alternative(s))" : "") +
                       "   " + (f.input != null && Accepts(f, (f.input.text ?? "").Trim()) ? "OK" : "no");
            }
            msg += "\n    ---- configured: " + IsConfigured + ", wrong attempts: " + WrongAttempts;
            Debug.LogWarning(msg, this);
        }
    }
}
