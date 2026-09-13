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
    public class DeviceConfigStation : Interactable, ISaveable
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
        [Tooltip("Switched off while the panel is open, and back on when it closes.\n\n" +
                 "THIS is what stops the player walking away with the window still up. Drag the " +
                 "movement script and the mouse-look script from your first-person player in here. " +
                 "Disabling the scripts is gentler than freezing time - the rest of the scene keeps " +
                 "running, and a controller written against unscaled time ignores timeScale anyway.")]
        public MonoBehaviour[] disableWhileOpen = new MonoBehaviour[0];

        [Header("Rules")]
        [Tooltip("Let the player keep trying after a wrong answer. Untick only if a wrong " +
                 "configuration should end the attempt.")]
        public bool allowRetry = true;
        [Tooltip("Blank fields count as wrong rather than being skipped.")]
        public bool requireEveryField = true;
        [Tooltip("Pressing E again while the machine is open steps away from it.\n\n" +
                 "E is what the player used to sit down, so it is the key their hand is already on. " +
                 "Without this the only ways out are Escape or the taskbar's Leave PC, and a player " +
                 "who opened the desktop by pressing E will press E to leave it before they think " +
                 "to look for either.")]
        public bool closeOnInteract = true;

        [Tooltip("Close the whole panel when the settings are accepted.\n\n" +
                 "Untick when this panel is one window on a WindowsShell desktop: clicking OK in " +
                 "TCP/IPv4 Properties should dismiss that dialog and leave the player standing at " +
                 "the desktop, not eject them from the machine. Wire the OK button's On Click to " +
                 "the shell's CloseAll() as well and the behaviour matches Windows.")]
        public bool closePanelOnPass = true;
        [Tooltip("Fill every field with its expected value and pass on the first Apply. Testing " +
                 "only - the Console shouts about it.")]
        public bool autoFillForTesting = false;

        [Header("Dialogs")]
        public DialogSequence briefingDialog;
        public DialogSequence successDialog;
        public DialogSequence failDialog;

        [Tooltip("Played ONCE, by whichever machine is configured last - the moment every station " +
                 "sharing this Task Id is done.\n\n" +
                 "Put the same DialogSequence on the server and the client. Only one of them will " +
                 "ever play it: the one that finishes second. This is the beat that closes round 1 " +
                 "and sends the player off to test the link, which the per-machine Success Dialog " +
                 "cannot say because neither station knows on its own whether it was the last.")]
        public DialogSequence allMachinesDialog;
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

        /// <summary>
        /// The machine the player is sitting at right now, or null.
        ///
        /// There are two of these in the scene and neither is a singleton, so anything that needs
        /// "the one that is open" - a shared Back button, an assessment watching where the learner
        /// is - would otherwise have to search every frame. Open() and Close() already know, so
        /// they say so here instead.
        /// </summary>
        public static DeviceConfigStation OpenStation { get; private set; }

        /// <summary>How many times Apply has been pressed with something wrong. Worth recording in
        /// an assessment - a trainee who gets it right first time is not the same as one who
        /// brute-forced it.</summary>
        public int WrongAttempts { get; private set; }

        bool shownSuccess;
        bool shownBriefing;
        bool reported;
        float savedTimeScale = 1f;
        Transform player;
        bool allDoneShown;
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

            // Cached once. The tag is how every other station in this project finds the player, so
            // a missing tag would already have broken more than this.
            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) player = tagged.transform;

            ValidateSetup();
        }

        public override void Interact()
        {
            // Before anything else: is the player actually standing at THIS machine?
            //
            // PlayerInteractor keeps one 'current' reference and the last station to claim the
            // player wins. A station that switches itself off while its panel is open stops
            // receiving OnTriggerExit, so its in-range flag sticks True after the player has walked
            // away - and from then on E at the far machine keeps opening the near one. Every window
            // is identical, so the player sees a desktop appear and has no way to tell it is the
            // wrong PC; the only visible symptom is the command prompt naming an address they did
            // not expect. Distance is the ground truth here, so ask it.
            DeviceConfigStation nearer = NearerStation();
            if (nearer != null)
            {
                if (IsOpen) Close();

                // The handoff has to carry E's full meaning, not half of it. E is a toggle: it sits
                // the player down at a machine, and it stands them back up again. Forwarding only
                // the "sit down" half means that once the right machine is open, every further E
                // press arrives here, gets handed on, and is quietly dropped because that station is
                // already open - so the player is locked inside a desktop with no way out.
                if (nearer.IsOpen) { if (nearer.closeOnInteract) nearer.Close(); }
                else nearer.Enter();
                return;
            }

            // E toggles. PlayerInteractor already suppresses the key while a dialog is open, so a
            // success dialog gets read and dismissed on its own before this can fire.
            if (IsOpen)
            {
                if (closeOnInteract) Close();
                return;
            }

            Enter();
        }

        /// <summary>
        /// True when every station sharing this Task Id has been configured correctly.
        ///
        /// Task Id is the grouping, not the scene hierarchy: two machines that report to
        /// 'configure_network' are one round, and a router station reporting to something else is
        /// not part of it. That keeps the round-closing dialog correct if a third machine is added
        /// later, without anything new to wire.
        /// </summary>
        bool EveryMachineConfigured()
        {
            DeviceConfigStation[] all =
                Object.FindObjectsByType<DeviceConfigStation>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None);
            foreach (DeviceConfigStation other in all)
            {
                if (other == null || other == this) continue;
                if (other.taskId != taskId) continue;
                if (!other.IsConfigured) return false;
            }
            return true;
        }

        /// <summary>Flags the whole group, so re-applying at either machine cannot replay the beat.</summary>
        void MarkRoundClosed()
        {
            DeviceConfigStation[] all =
                Object.FindObjectsByType<DeviceConfigStation>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None);
            foreach (DeviceConfigStation other in all)
                if (other != null && other.taskId == taskId) other.allDoneShown = true;
        }

        /// <summary>Briefing first if there is one, then the desktop. The normal way in.</summary>
        void Enter()
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

        /// <summary>
        /// The station the player is really standing at, when it is not this one.
        ///
        /// Returns null in the normal case - this station is the closest, so E belongs to it. The
        /// margin stops two stations from bouncing the player between them when they are genuinely
        /// equidistant, which would be worse than either answer.
        /// </summary>
        DeviceConfigStation NearerStation()
        {
            if (player == null) return null;

            float mine = DistanceToPlayer(this, player.position);

            // Inside our own trigger, this E press is ours and no comparison is needed. Without this
            // the handoff could bounce a player who is genuinely standing at this machine over to
            // another one whose pivot happens to sit closer - and then bounce them back, so E would
            // never close anything.
            if (mine <= 0.01f) return null;

            DeviceConfigStation best = null;
            float bestDistance = mine;

            DeviceConfigStation[] all =
                Object.FindObjectsByType<DeviceConfigStation>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None);
            foreach (DeviceConfigStation other in all)
            {
                if (other == null || other == this) continue;
                float d = DistanceToPlayer(other, player.position);
                if (d < bestDistance - 0.25f) { bestDistance = d; best = other; }
            }

            if (best != null)
                Debug.LogWarning("[DeviceConfigStation] E reached " + stationName + ", but the player " +
                                 "is " + bestDistance.ToString("0.0") + "m from " + best.stationName +
                                 " and " + mine.ToString("0.0") + "m from here. " + stationName +
                                 "'s in-range flag is stuck on - a station switched off while its " +
                                 "panel is open never receives OnTriggerExit, so it keeps claiming " +
                                 "the player after they walk away, and every E press lands on the " +
                                 "wrong machine. Opening " + best.stationName + " instead.", this);

            return best;
        }

        /// <summary>
        /// How far the player is from a station, measured to its TRIGGER rather than its pivot.
        ///
        /// A PC's transform origin is wherever the artist left it - often the model's base, sometimes
        /// the far corner of a desk. The trigger is the thing that actually defines "at this machine",
        /// so it is the thing to measure. Zero means the player is standing inside it.
        /// </summary>
        static float DistanceToPlayer(DeviceConfigStation station, Vector3 playerPosition)
        {
            Collider trigger = station.GetComponent<Collider>();
            if (trigger == null) return Vector3.Distance(playerPosition, station.transform.position);
            return Vector3.Distance(playerPosition, trigger.bounds.ClosestPoint(playerPosition));
        }

        // ---------------------------------------------------------------- open / close

        public void Open()
        {
            if (IsOpen) return;

            // Only one machine can be sat at. Two of these panels open together means two desktops
            // stacked on one canvas, and uGUI hands every click to whichever draws last - so the
            // player clicks Start on the machine they are looking at and the OTHER machine's Start
            // menu is what actually opens. Nothing on screen shows that; the windows are identical.
            CloseOthers();

            IsOpen = true;
            OpenStation = this;

            if (panel != null) panel.SetActive(true);
            Debug.Log("[DeviceConfigStation] " + stationName + " opened, showing '" +
                      (panel != null ? panel.name : "(no panel)") + "'.", this);
            foreach (GameObject go in hideWhileOpen) if (go != null) go.SetActive(false);
            foreach (MonoBehaviour m in disableWhileOpen) if (m != null) m.enabled = false;

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

            // Only clear it if this is still the machine on record. CloseOthers() shuts the previous
            // station AFTER the new one has claimed the slot, so clearing unconditionally here would
            // blank out the machine the player just sat down at.
            if (OpenStation == this) OpenStation = null;

            if (panel != null) panel.SetActive(false);
            foreach (GameObject go in hideWhileOpen) if (go != null) go.SetActive(true);
            foreach (MonoBehaviour m in disableWhileOpen) if (m != null) m.enabled = true;

            if (releaseCursor) { Cursor.lockState = savedLock; Cursor.visible = savedCursorVisible; }
            if (freezeTime) Time.timeScale = savedTimeScale;

            OnClosed?.Invoke();
        }

        /// <summary>
        /// Shuts any other machine the player left open.
        ///
        /// This is a guard rather than a fix, and it says so when it fires. The usual cause is that
        /// the station could not be closed the normal way - most often because it was listed in its
        /// own Disable While Open, which switches the component off the moment the panel appears, and
        /// a disabled component receives neither the E key nor OnTriggerExit. Silently tidying up
        /// after that would hide a scene fault that will keep producing wrong-machine results, so the
        /// warning names both stations and stays in the log.
        /// </summary>
        void CloseOthers()
        {
            DeviceConfigStation[] all =
                Object.FindObjectsByType<DeviceConfigStation>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None);
            foreach (DeviceConfigStation other in all)
            {
                if (other == null || other == this || !other.IsOpen) continue;

                Debug.LogWarning("[DeviceConfigStation] " + stationName + " opened while " +
                                 other.stationName + " was STILL OPEN. Closing " + other.stationName +
                                 " first. Two desktops on screen at once stack on one canvas and " +
                                 "every click goes to whichever draws last, so the player operates " +
                                 "one machine while looking at the other - which is what makes both " +
                                 "consoles ping the same address and only one machine ever get " +
                                 "credited. Find out why " + other.stationName + " never closed.", this);
                other.Close();
            }
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

                // Two dialogs can land on the same Apply - this machine's own "configured" line and
                // the round-closing one. They are chained rather than fired together, because
                // DialogManager shows one at a time and would drop the second.
                bool skipMine = successDialogOnlyOnce && shownSuccess;
                bool roundJustClosed = EveryMachineConfigured() && allMachinesDialog != null &&
                                       !allDoneShown;

                if (roundJustClosed && !skipMine && successDialog != null &&
                    DialogManager.Instance != null && !DialogManager.Instance.IsDialogOpen)
                {
                    MarkRoundClosed();
                    Debug.Log("[DeviceConfigStation] " + stationName + ": showing '" +
                              successDialog.name + "', then '" + allMachinesDialog.name + "'.", this);
                    DialogSequence after = allMachinesDialog;
                    DialogManager.Instance.ShowDialog(successDialog,
                        () => { if (DialogManager.Instance != null) DialogManager.Instance.ShowDialog(after); });
                }
                else
                {
                    Say(successDialog, skipMine, "Success");
                    if (roundJustClosed)
                    {
                        MarkRoundClosed();
                        Say(allMachinesDialog, false, "All Machines");
                    }
                }

                shownSuccess = true;
                if (closePanelOnPass) Close();
            }
            else
            {
                OnFailed?.Invoke();
                Say(failDialog, false, "Fail");
            }
        }

        /// <summary>
        /// Shows a dialog, and says WHY when it cannot. See the note on the same method in
        /// ConnectivityCheckStation - three silent failure modes that all look identical from the
        /// player's chair, so each one names itself.
        /// </summary>
        void Say(DialogSequence sequence, bool alreadyShown, string which)
        {
            if (alreadyShown) return;

            if (sequence == null)
            {
                Debug.LogWarning("[DeviceConfigStation] " + stationName + ": the " + which +
                                 " Dialog field is empty, so the player was told nothing. Assign a " +
                                 "DialogSequence to it.", this);
                return;
            }
            if (DialogManager.Instance == null)
            {
                Debug.LogWarning("[DeviceConfigStation] " + stationName + ": there is no " +
                                 "DialogManager in the scene, so '" + sequence.name + "' cannot be " +
                                 "shown.", this);
                return;
            }
            if (DialogManager.Instance.IsDialogOpen)
            {
                Debug.LogWarning("[DeviceConfigStation] " + stationName + ": another dialog was " +
                                 "already open, so '" + sequence.name + "' was dropped.", this);
                return;
            }

            Debug.Log("[DeviceConfigStation] " + stationName + ": showing '" + sequence.name +
                      "'. If nothing appears, the dialog canvas is sorting BELOW the desktop.", this);
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
            allDoneShown = false;
            shownBriefing = false;
            WrongAttempts = 0;
            foreach (ConfigField f in fields)
                if (f != null && f.input != null && !f.prefill) f.input.text = "";
            SetText(resultText, "");
        }

        // ---------------------------------------------------------------- saving

        /// <summary>
        /// Unique per station, because two of these share a scene - the server and
        /// the client. SaveManager keys its ISaveables by this string and DROPS
        /// duplicates silently, so a fixed key would mean one machine's state
        /// quietly overwriting the other's.
        ///
        /// The object's name is the key, so RENAMING THIS GAMEOBJECT INVALIDATES
        /// ITS SAVED STATE - the entry is still in the file, nothing claims it, and
        /// the station comes back unconfigured. That is the right failure: a silent
        /// wrong restore would be worse.
        /// </summary>
        public string SaveKey { get { return "DeviceConfigStation:" + gameObject.name; } }

        [System.Serializable]
        class SaveState
        {
            public bool configured;
            public bool reported;
            public bool shownSuccess;
            public bool shownBriefing;
            public bool allDoneShown;
            public int wrongAttempts;
        }

        public string CaptureState()
        {
            return JsonUtility.ToJson(new SaveState
            {
                configured = IsConfigured,
                reported = reported,
                shownSuccess = shownSuccess,
                shownBriefing = shownBriefing,
                allDoneShown = allDoneShown,
                wrongAttempts = WrongAttempts,
            });
        }

        /// <summary>
        /// WHY reported IS SAVED TOO, and not just IsConfigured.
        ///
        /// TaskManager restores its own completed tasks, so on a resumed save the
        /// task is already done. If this station came back with reported = false it
        /// would report progress a second time the next time the learner pressed
        /// Apply, pushing a two-step task past its own finish line and completing it
        /// from one machine. Saving the flag keeps the two records agreeing.
        ///
        /// The dialog flags travel for the same reason: a learner who has already
        /// been told the machine is configured should not be told again just because
        /// they closed the game in between.
        /// </summary>
        public void RestoreState(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            SaveState state = JsonUtility.FromJson<SaveState>(data);
            if (state == null) return;

            IsConfigured = state.configured;
            reported = state.reported;
            shownSuccess = state.shownSuccess;
            shownBriefing = state.shownBriefing;
            allDoneShown = state.allDoneShown;
            WrongAttempts = state.wrongAttempts;

            if (IsConfigured) SetText(resultText, stationName + " configured. Settings applied.");
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

            // The one entry in this list that turns a working station into a permanently open one.
            // Worth catching by name, because the symptom appears nowhere near the cause: the panel
            // simply stops responding to E, stays up for the rest of the session, and its desktop
            // quietly steals the clicks meant for the other machine.
            foreach (MonoBehaviour m in disableWhileOpen)
            {
                if (m != this) continue;
                problems.Add("Disable While Open contains THIS station. Opening the panel switches " +
                             "this component off, and a disabled component receives neither the E key " +
                             "nor OnTriggerExit - so the panel can never be closed again and stays on " +
                             "screen for the rest of the session, stacked over the other machine's " +
                             "desktop. Remove it from the list; the station already stops responding " +
                             "to E on its own while it is open.");
            }

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

            if (player == null)
                problems.Add("No GameObject tagged 'Player' was found, so this station cannot tell " +
                             "whether the player is standing at it or at the other machine. E may " +
                             "open the wrong PC and nothing will say so.");

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