// ConnectivityCheckStation.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Walk up to the server or the client, press E, and find out whether the link you just built
    /// actually works.
    ///
    /// WHY THIS IS A CHECK AND NOT A CUTSCENE
    ///
    /// The tempting version of this is a button that plays a "Connected!" dialog. It would take
    /// twenty lines and it would teach nothing, because it passes whether or not the player wired
    /// anything correctly. This one walks the physical path instead - this machine's cable, the end
    /// in the patch panel, the patch cord from that panel port to the switch - and when a link is
    /// missing it names WHICH link. That is the difference between a reward and an assessment, and
    /// it is the thing COC2 is actually examining: "network connectivity is checked in accordance
    /// with established procedures" means the trainee can find the break, not that a light went on.
    ///
    /// So a failure here reads like a real one:
    ///
    ///     Request timed out.
    ///     The cable reaches this machine, but its other end is not in the patch panel.
    ///
    /// ...and the player goes and fixes that specific thing.
    ///
    /// SETUP:
    ///   1. Add this to the server PC (and a second copy on the client PC). Give each one a trigger
    ///      collider the player can walk into - same as every other station.
    ///   2. Drag that machine's CableCarrier into Cable.
    ///   3. Set Equipment Group to the socket group on this machine - 'server' or 'client'.
    ///   4. Optionally drag the panel-to-switch PatchCordBoard into Requires Patch Phase.
    ///   5. Assign Success Dialog. That is the dialog you asked for.
    ///
    /// Everything else has a working default.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public class ConnectivityCheckStation : Interactable, ISaveable
    {
        [Header("Identity")]
        [Tooltip("Shown in messages, e.g. 'Server'. Just a label.")]
        public string stationName = "Server";
        [Tooltip("The address this machine pretends to have. Cosmetic - it only ever appears in " +
                 "the on-screen readout, but a plausible one makes the test read as a real ping.")]
        public string ipAddress = "192.168.1.10";
        [Tooltip("The address being pinged when there is no Peer. With a Peer set, the peer's own " +
                 "Ip Address is used instead, so the readout always names what is actually tested.")]
        public string targetAddress = "192.168.1.1";

        [Tooltip("The machine at the other end of the ping. Set the server's Peer to the client " +
                 "and the client's Peer to the server.\n\n" +
                 "With this set, the check verifies BOTH machines' paths before it replies - which " +
                 "is what a ping actually proves. Leave it empty and the machine only tests its own " +
                 "path out to the gateway.")]
        public ConnectivityCheckStation peer;

        [Header("What counts as connected")]
        [Tooltip("The cable the player ran to this machine. Its two ends are what get checked.")]
        public CableCarrier cable;
        [Tooltip("The socket group on THIS machine - 'server' or 'client'. One end of the cable " +
                 "must be sitting in a socket with this group id.")]
        public string equipmentGroup = "server";
        [Tooltip("The socket group at the other end - normally the patch panel front.")]
        public string panelGroup = "panel";

        [Tooltip("Optional. The panel-to-switch phase. Running a cable from this PC to a panel port " +
                 "that was never patched through to the switch is a dead end, and this is what " +
                 "makes the sim say so instead of passing.")]
        public PatchCordBoard requiresPatchPhase;

        [Tooltip("Optional. This machine's DeviceConfigStation. A PC with no IP address cannot " +
                 "reach anything however good the cabling is, and this is what makes the sim say " +
                 "that instead of passing on wiring alone.")]
        public DeviceConfigStation requiresConfig;

        [Tooltip("Optional. Task ids that must be complete first - 'punch_patch_panel' is the " +
                 "obvious one, since an unterminated horizontal run carries nothing.")]
        public string[] requiredTaskIds = new string[0];

        [Tooltip("Round 1 before round 2.\n\n" +
                 "The player is meant to address BOTH machines, then test both. With this on, a " +
                 "ping attempted before either machine has an address prints a real timeout, says " +
                 "which machine is missing one, and is not recorded as a failure - it is a " +
                 "sequencing mistake, not a wiring one, and marking it as a failed test would teach " +
                 "the wrong lesson. Needs Requires Config set on both stations.")]
        public bool requireBothConfigured = true;

        [Tooltip("Skip the cable, patch-phase and task checks and always report a link. Testing " +
                 "only - the Console shouts about it.\n\n" +
                 "It does NOT skip Require Both Configured: a machine with no IPv4 address still " +
                 "cannot be pinged, because that is the thing this task is teaching rather than a " +
                 "wiring detail to be bypassed.")]
        public bool alwaysPassForTesting = false;

        [Header("The test itself")]
        [Tooltip("Seconds the 'pinging' takes. A beat of waiting is what makes it feel like a test " +
                 "rather than a button.")]
        public float checkSeconds = 2f;
        [Tooltip("Optional readout on this machine's monitor - a TMP text. Gets the ping lines.")]
        public TMP_Text consoleText;
        [Tooltip("How many lines the readout keeps before the oldest scrolls off.")]
        public int consoleLines = 6;
        [Tooltip("The command-prompt window. Opened when the player presses E, closed by its X, by " +
                 "the Close Key, or by walking away. Leave it INACTIVE in the scene.")]
        public GameObject consolePanel;

        [Header("Typing the command")]
        [Tooltip("Where the player types 'ping 192.168.1.20'. Leave empty and pressing E runs the " +
                 "check straight away, the way it used to.")]
        public TMP_InputField commandInput;
        [Tooltip("Runs whatever is typed. Its On Click is wired for you at Start.")]
        public Button pingButton;
        [Tooltip("The command must actually be 'ping' followed by the right address.\n\n" +
                 "This is the difference between a button that says PING and a trainee who knows " +
                 "the command and the address they are testing. A wrong verb and a wrong address " +
                 "get different, real errors back.")]
        public bool requireCorrectCommand = true;

        [Header("Living inside a desktop shell")]
        [Tooltip("Untick when the command prompt is a window on a WindowsShell desktop rather than " +
                 "a station of its own.\n\n" +
                 "With this off, the trigger collider is switched off at Start so this component " +
                 "never competes to be the player's nearby interactable - the desktop's own station " +
                 "owns the E press, and the Start menu opens this console.")]
        public bool openOnInteract = true;

        [Tooltip("Tick when a WindowsShell owns the player lock. This station then only shows and " +
                 "hides its window, and leaves the cursor, the clock and the movement scripts alone.\n\n" +
                 "Two components both writing Cursor.lockState is how you end up with a mouse that " +
                 "never comes back.")]
        public bool managedByShell = false;

        [Header("While the console is open")]
        [Tooltip("Free the mouse cursor so the player can click into the command box.")]
        public bool releaseCursor = true;
        [Tooltip("Set Time.timeScale to 0. Stops a standard character controller dead.")]
        public bool freezeTime = false;
        [Tooltip("Switched off while the console is open, and back on when it closes.\n\n" +
                 "THIS is what stops the player walking away with the window still up. Drag the " +
                 "movement script and the mouse-look script from your first-person player in here. " +
                 "Disabling the scripts is gentler than freezing time - the rest of the scene keeps " +
                 "running, and a controller that uses unscaled time is not fooled by timeScale.")]
        public MonoBehaviour[] disableWhileOpen = new MonoBehaviour[0];
        [Tooltip("Hidden while the console is open - a crosshair, the mobile controls, the HUD.")]
        public GameObject[] hideWhileOpen = new GameObject[0];
        [Tooltip("Close the console if the player somehow ends up outside the station's trigger. " +
                 "A safety net, not the main mechanism.")]
        public bool closeWhenPlayerLeaves = true;
        [Tooltip("Also closes the console. Escape is what people press.")]
        public KeyCode closeKey = KeyCode.Escape;

        [Header("Link light (optional)")]
        [Tooltip("Switched on when the check passes - the green LED on the NIC.")]
        public GameObject linkLightOn;
        [Tooltip("Switched on while the link is down or the check has not been run.")]
        public GameObject linkLightOff;

        [Header("Dialogs")]
        [Tooltip("Played when the check PASSES. This is the one you asked for.")]
        public DialogSequence successDialog;
        [Tooltip("Played when the check FAILS. Leave empty and the reason still goes to the " +
                 "readout and the Console - but a dialog is where the player will actually read it.")]
        public DialogSequence failDialog;
        [Tooltip("Played the first time the player interacts, before the test runs. Optional " +
                 "briefing - 'let's see if this machine can reach the gateway'.")]
        public DialogSequence briefingDialog;
        [Tooltip("Show the success dialog only the first time it passes.")]
        public bool successDialogOnlyOnce = true;

        [Tooltip("Played ONCE, by whichever machine verifies LAST - the moment every station " +
                 "sharing this Task Id has passed its ping.\n\n" +
                 "Put the same DialogSequence on the server and the client; only the second one to " +
                 "pass will play it. This is the beat that closes the whole networking job, and no " +
                 "single station can deliver it, because neither one knows on its own whether the " +
                 "other has been tested yet. A ping proves one direction; both directions answering " +
                 "is what the assessment actually asks for.")]
        public DialogSequence allMachinesDialog;

        [Header("Task")]
        [Tooltip("Reported to TaskManager when this machine passes. Register it in COC2TaskSetup, " +
                 "or tick Auto Register below. Leave blank to report nothing.")]
        public string taskId = "verify_connectivity";
        [Tooltip("Register Task Id with TaskManager at Start if nothing else has.\n\n" +
                 "TaskManager.ReportProgress on an unregistered id silently does nothing, which is " +
                 "a genuinely nasty half-hour to lose. Ticking this means the station cannot be " +
                 "broken by forgetting a line in COC2TaskSetup.")]
        public bool autoRegisterTask = true;
        [Tooltip("How many machines have to pass for the task to complete. Two, if you have a " +
                 "server and a client - and then BOTH stations need the same Task Id and the same " +
                 "number here.")]
        public int taskSteps = 2;

        [Header("Events")]
        public UnityEvent OnCheckStarted;
        public UnityEvent OnPassed;
        public UnityEvent OnFailed;

        [Header("Diagnostics")]
        public bool logResult = true;

        /// <summary>True once this machine has passed. Other scripts - a waypoint step, a final
        /// assessment - can read this.</summary>
        public bool IsConnected { get; private set; }

        /// <summary>True while the ping is running, so a second E press cannot start it twice.</summary>
        public bool IsChecking { get; private set; }

        readonly List<string> lines = new List<string>();
        bool shownSuccess;
        bool shownBriefing;
        bool reported;
        bool submittedThisVisit;
        bool allDoneShown;
        float savedTimeScale = 1f;
        CursorLockMode savedLock;
        bool savedCursorVisible;

        /// <summary>True while the command-prompt window is up.</summary>
        public bool IsOpen { get; private set; }

        void Start()
        {
            if (autoRegisterTask && !string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                TaskManager.Instance.RegisterTask(taskId, Mathf.Max(1, taskSteps));

            // Wired in code rather than left to the Inspector, for the same reason as the config
            // station's Apply: a button whose On Click was never hooked looks exactly like one that
            // was, and clicking it does nothing with no clue where to look.
            if (pingButton != null) pingButton.onClick.AddListener(SubmitCommand);
            if (commandInput != null) commandInput.onSubmit.AddListener(OnInputSubmitted);

            if (consolePanel != null) consolePanel.SetActive(false);

            // A component that is not meant to answer E must not register as the nearby interactable
            // at all. PlayerInteractor keeps ONE reference, last writer wins, so simply ignoring the
            // press here would make the desktop's own station unreachable half the time.
            if (!openOnInteract)
            {
                // Only when the collider is OURS. Sharing a GameObject with another Interactable
                // means sharing its trigger, and switching it off here would silently take the
                // desktop's own station down with it - the exact failure this flag exists to avoid.
                Interactable[] siblings = GetComponents<Interactable>();
                if (siblings.Length > 1)
                    Debug.LogWarning("[ConnectivityCheckStation] " + stationName + " has Open On " +
                                     "Interact off, but it shares '" + gameObject.name + "' with " +
                                     (siblings.Length - 1) + " other Interactable(s) and therefore " +
                                     "shares their trigger. Move this component to its own child " +
                                     "object with its own collider, or the two will keep fighting " +
                                     "over the E press.", this);
                else
                {
                    Collider trigger = GetComponent<Collider>();
                    if (trigger != null) trigger.enabled = false;
                }
            }

            SetLight(false);
            ValidateSetup();
        }

        void Update()
        {
            if (!IsOpen) return;

            // Inside a shell the desktop owns both of these, and neither test is meaningful here.
            //
            // Escape belongs to the shell, which closes the top window first and only leaves the
            // machine from the bare desktop - handling it here as well would fire both on one frame.
            //
            // And the range check is actively WRONG in shell mode: Open On Interact being off means
            // this component's trigger is switched off at Start, so OnTriggerEnter never fires and
            // IsPlayerInRange is permanently false. Applying the walk-away net would close the
            // console on the frame after it opened - which reads as the window never appearing at all.
            if (managedByShell) return;

            if (Input.GetKeyDown(closeKey)) { CloseConsole(); return; }

            // The safety net that stops "the console followed me across the room". The player should
            // not be able to leave with it open at all - that is what disableWhileOpen is for - but
            // if something else moves them, or the trigger is smaller than it looks, the window goes
            // away rather than hanging over the world.
            if (closeWhenPlayerLeaves && !IsPlayerInRange && !IsChecking) CloseConsole();
        }

        public override void Interact()
        {
            if (!openOnInteract || IsOpen || IsChecking) return;

            if (briefingDialog != null && !shownBriefing && DialogManager.Instance != null &&
                !DialogManager.Instance.IsDialogOpen)
            {
                shownBriefing = true;
                DialogManager.Instance.ShowDialog(briefingDialog, OpenConsole);
                return;
            }

            OpenConsole();
        }

        // ---------------------------------------------------------------- open / close

        /// <summary>
        /// Puts the command prompt up and hands the player the keyboard.
        ///
        /// With no Command Input assigned this runs the ping immediately, which is how the station
        /// behaved before - so an older scene keeps working without being re-wired.
        /// </summary>
        public void OpenConsole()
        {
            if (IsOpen) return;
            IsOpen = true;
            submittedThisVisit = false;

            if (consolePanel != null) consolePanel.SetActive(true);

            // Inside a shell, the desktop's station already froze the player and freed the cursor.
            // Doing it again here would mean two components racing to restore the same values.
            if (!managedByShell)
            {
                foreach (GameObject go in hideWhileOpen) if (go != null) go.SetActive(false);
                foreach (MonoBehaviour m in disableWhileOpen) if (m != null) m.enabled = false;

                if (releaseCursor)
                {
                    savedLock = Cursor.lockState;
                    savedCursorVisible = Cursor.visible;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;

                    // TELL CursorKeeper, OR IT WILL TAKE THE CURSOR STRAIGHT BACK.
                    //
                    // CursorKeeper re-locks the cursor every frame that the learner is in
                    // first-person control and nothing has claimed it. Freeing it here without
                    // claiming it means the keeper undoes this line on the very next frame, and the
                    // command prompt cannot be typed into.
                    //
                    // Inside a shell this whole block is skipped - managedByShell - because the
                    // desktop station already claimed it. That is the nesting the counted Claim
                    // exists for, and skipping here keeps the count balanced either way.
                    CursorKeeper.Claim();
                }
                if (freezeTime) { savedTimeScale = Time.timeScale; Time.timeScale = 0f; }
            }

            if (commandInput == null) { StartCoroutine(RunCheck()); return; }

            // The header carries this machine's OWN address, the way ipconfig would. Two identical
            // command prompts on two desktops are otherwise indistinguishable on screen, and a
            // trainee cannot check that they are at the right PC - nor can you, when a wiring
            // mistake sends both windows to one station.
            lines.Clear();
            Write("Microsoft Windows [Version 10.0.19045.4046]");
            Write("(c) Microsoft Corporation. All rights reserved.");
            Write("");
            Write("Ethernet adapter Ethernet:   [" + stationName + "]");
            Write("   IPv4 Address. . . . . . . : " + ipAddress);
            Write("");
            Write("Type  ping " + PingTarget() + "  and press Enter.");
            PushConsole();

            Debug.Log("[ConnectivityCheckStation] " + stationName + ": console open. This machine " +
                      ipAddress + ", target " + PingTarget() + " " +
                      (peer != null && peer != this && !string.IsNullOrEmpty(peer.ipAddress)
                           ? "(peer '" + peer.stationName + "')"
                           : "(Target Address - no usable Peer is set)") + ".", this);

            commandInput.text = "";
            commandInput.interactable = true;
            commandInput.ActivateInputField();
        }

        /// <summary>Hides the console and gives the player back their legs. Wire the X button here.</summary>
        public void CloseConsole()
        {
            if (!IsOpen) return;
            IsOpen = false;

            // "Nothing happened" is two very different faults - the player never sent a command, or
            // they sent one and it was turned away on screen where the Unity log never sees it. Every
            // other line in this file reports the second case; this one reports the first, so the two
            // stop looking identical from the outside.
            if (!submittedThisVisit)
                Debug.LogWarning("[ConnectivityCheckStation] " + stationName + ": console closed with " +
                                 "NO command ever submitted. Neither Enter nor the Ping button reached " +
                                 "this station. If the player did type and press Enter, this machine's " +
                                 "Command Input or Ping Button is not the one in the window they were " +
                                 "looking at - run 'Report link' and compare the paths.", this);

            if (consolePanel != null) consolePanel.SetActive(false);
            if (managedByShell) return;

            foreach (GameObject go in hideWhileOpen) if (go != null) go.SetActive(true);
            foreach (MonoBehaviour m in disableWhileOpen) if (m != null) m.enabled = true;

            if (releaseCursor)
            {
                CursorKeeper.Release();      // paired with the Claim in Open()
                Cursor.lockState = savedLock;
                Cursor.visible = savedCursorVisible;
            }
            if (freezeTime) Time.timeScale = savedTimeScale;
        }

        // ---------------------------------------------------------------- the typed command

        void OnInputSubmitted(string _) { SubmitCommand(); }

        /// <summary>
        /// Reads what the player typed and either runs the ping or answers with the error a real
        /// command prompt would give. Wired to the Ping button and to Enter.
        /// </summary>
        public void SubmitCommand()
        {
            if (IsChecking || !IsOpen) return;
            submittedThisVisit = true;

            string typed = commandInput != null ? (commandInput.text ?? "").Trim() : "";
            string target = PingTarget();

            Write("C:\\> " + typed);
            if (commandInput != null) commandInput.text = "";

            if (!requireCorrectCommand || typed.Length == 0)
            {
                StartCoroutine(RunCheck());
                return;
            }

            string[] parts = typed.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";

            // Two different wrong answers, two different real errors. "ping" typed at a wrong
            // address is a different mistake from not knowing the command, and cmd distinguishes
            // them - so the trainee learns which one they made.
            if (verb != "ping")
            {
                Debug.Log("[ConnectivityCheckStation] " + stationName + ": rejected '" + typed +
                          "' - the command is not 'ping'.", this);
                Write("'" + (parts.Length > 0 ? parts[0] : typed) + "' is not recognized as an " +
                      "internal or external command, operable program or batch file.");
                Write("");
                PushConsole();
                if (commandInput != null) commandInput.ActivateInputField();
                return;
            }

            string arg = parts.Length > 1 ? parts[1] : "";
            if (arg.Length == 0)
            {
                Debug.Log("[ConnectivityCheckStation] " + stationName + ": rejected 'ping' with no " +
                          "address after it.", this);
                Write("Usage: ping [-t] [-a] [-n count] target_name");
                Write("");
                PushConsole();
                if (commandInput != null) commandInput.ActivateInputField();
                return;
            }

            if (!string.Equals(arg, target, System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[ConnectivityCheckStation] " + stationName + ": rejected 'ping " + arg +
                          "' - this machine's target is " + target + ".", this);
                Write("Ping request could not find host " + arg + ". Please check the name and " +
                      "try again.");
                Write("");
                PushConsole();
                if (commandInput != null) commandInput.ActivateInputField();
                return;
            }

            Debug.Log("[ConnectivityCheckStation] " + stationName + ": accepted 'ping " + target +
                      "', running the check.", this);
            StartCoroutine(RunCheck());
        }

        IEnumerator RunCheck()
        {
            IsChecking = true;
            OnCheckStarted?.Invoke();

            if (consolePanel != null) consolePanel.SetActive(true);
            if (commandInput != null) commandInput.interactable = false;   // no typing mid-ping

            string target = PingTarget();
            // The banner and the typed command are already on screen when the player got here by
            // typing. Only clear the scrollback for the old path, where E ran the ping directly.
            if (commandInput == null)
            {
                lines.Clear();
                Write("C:\\> ping " + target);
            }
            Write("");
            Write("Pinging " + target + " with 32 bytes of data:");
            PushConsole();

            // Unscaled, because a dialog may still be closing and dialogs stop the clock.
            float t = 0f;
            while (t < checkSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            // Round 1 before round 2. Testing a link before both machines have addresses is a
            // sequencing mistake, not a wiring one, and the two deserve different answers: this
            // branch prints a real timeout and stops WITHOUT calling Finish, so nothing is recorded
            // against the trainee and the fail dialog stays shut. Marking someone down for reaching
            // the right station in the wrong order teaches the wrong lesson.
            string notReady = NotReadyYet();
            if (notReady != null)
            {
                Write("Request timed out.");
                Write("Request timed out.");
                Write("");
                Write("Ping statistics for " + target + ":");
                Write("    Packets: Sent = 2, Received = 0, Lost = 2 (100% loss)");
                Write("");
                Write(notReady);
                PushConsole();

                IsChecking = false;
                if (commandInput != null)
                {
                    commandInput.interactable = true;
                    commandInput.ActivateInputField();
                }
                Debug.Log("[ConnectivityCheckStation] " + stationName + ": ping NOT RUN - " + notReady +
                          " Nothing was recorded either way.", this);
                yield break;
            }

            string failure = FindBreak();
            bool passed = failure == null;

            if (passed)
            {
                Write("Reply from " + target + ": bytes=32 time<1ms TTL=128");
                Write("Reply from " + target + ": bytes=32 time<1ms TTL=128");
                Write("");
                Write("Packets: Sent = 2, Received = 2, Lost = 0 (0% loss)");
            }
            else
            {
                Write("Request timed out.");
                Write("Request timed out.");
                Write("");
                Write(failure);
            }
            PushConsole();

            IsChecking = false;
            if (commandInput != null)
            {
                commandInput.interactable = true;
                commandInput.ActivateInputField();
            }
            Finish(passed, failure);
        }

        /// <summary>
        /// The address printed in the readout. With a peer set it is the peer's own address, so the
        /// line the player reads names the machine the check actually verified.
        /// </summary>
        string PingTarget()
        {
            if (peer != null && peer != this && !string.IsNullOrEmpty(peer.ipAddress))
                return peer.ipAddress;
            return targetAddress;
        }

        void Finish(bool passed, string failure)
        {
            SetLight(passed);

            if (logResult)
                Debug.LogWarning("[ConnectivityCheckStation] " + stationName + ": " +
                                 (passed ? "CONNECTED." : "NO LINK. " + failure), this);

            if (passed)
            {
                bool first = !IsConnected;
                IsConnected = true;

                if (first && !reported && !string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                {
                    reported = true;
                    TaskManager.Instance.ReportProgress(taskId);
                }

                OnPassed?.Invoke();

                // Two dialogs can land on this one ping - this machine's own result and the one that
                // closes the job. They are chained rather than fired together, because DialogManager
                // shows one at a time and would drop the second.
                bool skipMine = successDialogOnlyOnce && shownSuccess;
                bool jobJustClosed = EveryMachineVerified() && allMachinesDialog != null && !allDoneShown;

                if (jobJustClosed && !skipMine && successDialog != null &&
                    DialogManager.Instance != null && !DialogManager.Instance.IsDialogOpen)
                {
                    MarkJobClosed();
                    Debug.Log("[ConnectivityCheckStation] " + stationName + ": showing '" +
                              successDialog.name + "', then '" + allMachinesDialog.name + "'.", this);
                    DialogSequence after = allMachinesDialog;
                    DialogManager.Instance.ShowDialog(successDialog,
                        () => { if (DialogManager.Instance != null) DialogManager.Instance.ShowDialog(after); });
                }
                else
                {
                    Say(successDialog, skipMine, "Success");
                    if (jobJustClosed)
                    {
                        MarkJobClosed();
                        Say(allMachinesDialog, false, "All Machines");
                    }
                }

                shownSuccess = true;
            }
            else
            {
                IsConnected = false;
                OnFailed?.Invoke();
                Say(failDialog, false, "Fail");
            }
        }

        // ---------------------------------------------------------------- the actual check

        /// <summary>
        /// Walks the link from this machine outward and returns the FIRST thing that is broken, in
        /// player-readable words, or null when the whole path is intact.
        ///
        /// Order matters: it reports the break nearest the player first. Being told "the panel is
        /// not patched to the switch" while the cable at your feet is unplugged would send someone
        /// across the room to fix the wrong thing.
        /// </summary>
        string FindBreak()
        {
            if (alwaysPassForTesting) return null;

            // This machine first - the player is standing at its keyboard, and a fault under their
            // own hands should never be reported as a fault at the other end of the room.
            string mine = FindLocalBreak("this machine");
            if (mine != null) return mine;

            // Then the machine being pinged.
            //
            // A ping is a ROUND TRIP. Without this half, setting Target Address to the client's
            // address would print "Reply from 192.168.1.20" while the client's cable lay on the
            // floor - the readout would be lying, and a sim that lies about a test is worse than
            // one that does not offer the test.
            if (peer != null && peer != this)
            {
                string theirs = peer.FindLocalBreak("the " + peer.stationName);
                if (theirs != null)
                    return "This machine is fine, but the reply never came back. " + theirs;
            }

            return null;
        }

        /// <summary>
        /// The first break on THIS machine's own path, or null. Never looks at the peer, which is
        /// what stops two stations pointed at each other from recursing forever.
        ///
        /// Public because the peer asks this of us, and vice versa. <paramref name="who"/> names the
        /// machine in the message - "this machine" when we are reporting on ourselves, "the Client"
        /// when the server is reporting on us.
        /// </summary>
        /// <summary>
        /// Whether the procedure has reached the point where a ping means anything yet.
        ///
        /// A ping is a round trip, so BOTH machines need an address before either can be tested. The
        /// player is meant to set the server, set the client, and only then come back to test - and
        /// that ordering is worth teaching rather than assuming. Returning a message here keeps the
        /// early attempt out of the results entirely: it reads as "not yet", the way an unconfigured
        /// machine really behaves, instead of "you failed".
        ///
        /// Returns null once the procedure is ready, which is the normal case.
        /// </summary>
        public string NotReadyYet()
        {
            // Deliberately NOT skipped by Always Pass For Testing. That flag exists to let you walk
            // the procedure without running cables first; it is about the wiring checks. Addressing
            // both machines before testing them is the lesson this task teaches, not a wiring
            // detail, so it stays enforced - and leaving it enforced means you can actually test the
            // gate while Always Pass is on, instead of the gate quietly disappearing with it.
            if (!requireBothConfigured) return null;

            if (requiresConfig != null && !requiresConfig.IsConfigured)
                return "This machine has no IPv4 address yet. Set the address, subnet mask and " +
                       "gateway from the job order before testing the link.";

            if (peer != null && peer != this && peer.requiresConfig != null &&
                !peer.requiresConfig.IsConfigured)
                return "The " + peer.stationName.ToLower() + " has no IPv4 address yet. A ping is a " +
                       "round trip, so both machines must be configured before either one can be " +
                       "tested. Set up the " + peer.stationName.ToLower() + ", then come back.";

            return null;
        }

        public string FindLocalBreak(string who)
        {
            string Who = Cap(who);

            // 0. Does this machine have an address?
            if (requiresConfig != null && !requiresConfig.IsConfigured)
                return Who + " has no valid IP configuration. Open its network settings and set the " +
                       "address, subnet mask and gateway from the job order first.";

            // 1. Is there a cable at it at all?
            if (cable == null)
                return "No cable is assigned to " + who + " in the Inspector.";

            PatchCord here = EndInGroup(equipmentGroup);
            if (here == null)
                return "Nothing is plugged into " + who + ". Run the " + cable.cableName.ToLower() +
                       " and plug one end into the " + equipmentGroup + " port.";

            // 2. Does its other end reach the patch panel?
            PatchCord far = here == cable.firstEnd ? cable.secondEnd : cable.firstEnd;
            RJ45Socket farSocket = far != null && far.IsPlugged ? far.PluggedSocket : null;

            if (farSocket == null)
                return "The cable at " + who + " has its other end unplugged. Take it to the patch panel.";

            if (!string.IsNullOrEmpty(panelGroup) && farSocket.groupId != panelGroup)
                return "The cable at " + who + " has its other end in a " + farSocket.groupId +
                       " port, not the " + panelGroup + ". A workstation drop terminates at the " +
                       "patch panel front.";

            // 3. Is the horizontal run behind that panel port actually terminated?
            string missingTask = FirstUnfinishedTask();
            if (missingTask != null)
                return "The panel port for " + who + " is live but the run behind it is not finished ('" +
                       missingTask + "'). Terminate the patch panel first.";

            // 4. Is that panel port patched through to the switch?
            if (requiresPatchPhase != null &&
                !(requiresPatchPhase.HasFinished && requiresPatchPhase.LastResultPassed))
                return "Panel port " + farSocket.portNumber + " for " + who + " is not patched to " +
                       "the switch. A panel port on its own goes nowhere - run a patch cord from it " +
                       "to the switch (" + requiresPatchPhase.phaseName + ").";

            return null;
        }

        static string Cap(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        /// <summary>The cable end currently sitting in a socket of the given group, or null.</summary>
        PatchCord EndInGroup(string group)
        {
            if (Matches(cable.firstEnd, group)) return cable.firstEnd;
            if (Matches(cable.secondEnd, group)) return cable.secondEnd;
            return null;
        }

        static bool Matches(PatchCord end, string group)
        {
            if (end == null || !end.IsPlugged || end.PluggedSocket == null) return false;
            return string.IsNullOrEmpty(group) || end.PluggedSocket.groupId == group;
        }

        string FirstUnfinishedTask()
        {
            if (requiredTaskIds == null || requiredTaskIds.Length == 0) return null;
            if (TaskManager.Instance == null) return null;
            foreach (string id in requiredTaskIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!TaskManager.Instance.IsTaskComplete(id)) return id;
            }
            return null;
        }

        // ---------------------------------------------------------------- presentation

        void Write(string line)
        {
            lines.Add(line);
            while (lines.Count > Mathf.Max(1, consoleLines)) lines.RemoveAt(0);
        }

        void PushConsole()
        {
            if (consoleText == null) return;
            consoleText.text = string.Join("\n", lines.ToArray());
        }

        void SetLight(bool connected)
        {
            if (linkLightOn != null && linkLightOn.activeSelf != connected)
                linkLightOn.SetActive(connected);
            if (linkLightOff != null && linkLightOff.activeSelf == connected)
                linkLightOff.SetActive(!connected);
        }

        /// <summary>
        /// Shows a dialog, and says WHY when it cannot.
        ///
        /// This used to fail three different ways in silence - no asset assigned, no DialogManager in
        /// the scene, another dialog already open - and all three look identical from the player's
        /// chair: the check passes and nothing happens. Naming the reason turns "the dialog will not
        /// show" from a guessing game into one line of Console.
        ///
        /// The success case logs too, deliberately. If this says it showed the dialog and you still
        /// see nothing, the dialog is rendering BEHIND something - which is a canvas sorting problem,
        /// somewhere else entirely, and worth knowing immediately rather than after an hour here.
        /// </summary>
        void Say(DialogSequence sequence, bool alreadyShown, string which)
        {
            if (alreadyShown) return;

            if (sequence == null)
            {
                Debug.LogWarning("[ConnectivityCheckStation] " + stationName + " passed, but its " +
                                 which + " Dialog field is empty, so the player was told nothing. " +
                                 "Assign a DialogSequence to it.", this);
                return;
            }
            if (DialogManager.Instance == null)
            {
                Debug.LogWarning("[ConnectivityCheckStation] " + stationName + ": there is no " +
                                 "DialogManager in the scene, so '" + sequence.name + "' cannot be " +
                                 "shown.", this);
                return;
            }
            if (DialogManager.Instance.IsDialogOpen)
            {
                Debug.LogWarning("[ConnectivityCheckStation] " + stationName + ": another dialog was " +
                                 "already open, so '" + sequence.name + "' was dropped. Dismiss the " +
                                 "previous one before triggering this.", this);
                return;
            }

            Debug.Log("[ConnectivityCheckStation] " + stationName + ": showing '" + sequence.name +
                      "'. If nothing appears on screen, the dialog canvas is sorting BELOW the " +
                      "desktop - give it Override Sorting and a higher Sort Order.", this);
            DialogManager.Instance.ShowDialog(sequence);
        }

        // ---------------------------------------------------------------- public API

        /// <summary>
        /// True when every station sharing this Task Id has passed its ping.
        ///
        /// Task Id is the grouping rather than the hierarchy, so the server and client both reporting
        /// to 'verify_connectivity' are one job. Add a third machine later with the same id and the
        /// closing dialog simply waits for it.
        /// </summary>
        bool EveryMachineVerified()
        {
            ConnectivityCheckStation[] all =
                Object.FindObjectsByType<ConnectivityCheckStation>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None);
            foreach (ConnectivityCheckStation other in all)
            {
                if (other == null || other == this) continue;
                if (other.taskId != taskId) continue;
                if (!other.IsConnected) return false;
            }
            return true;
        }

        /// <summary>Flags the whole group, so a second ping at either machine cannot replay the beat.</summary>
        void MarkJobClosed()
        {
            ConnectivityCheckStation[] all =
                Object.FindObjectsByType<ConnectivityCheckStation>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None);
            foreach (ConnectivityCheckStation other in all)
                if (other != null && other.taskId == taskId) other.allDoneShown = true;
        }

        /// <summary>Runs the check from a UnityEvent or a UI button, without walking up and pressing E.</summary>
        public void RunCheckNow()
        {
            if (!IsChecking) StartCoroutine(RunCheck());
        }

        /// <summary>Forgets the result. Wire to a procedure restart if you let the player redo COC2.</summary>
        public void ResetStation()
        {
            IsConnected = false;
            reported = false;
            shownSuccess = false;
            shownBriefing = false;
            allDoneShown = false;
            lines.Clear();
            PushConsole();
            SetLight(false);
        }

        // ---------------------------------------------------------------- saving

        /// <summary>
        /// Unique per station, because two of these share a scene - the server and
        /// the client. SaveManager keys its ISaveables by this string and DROPS
        /// duplicates silently, so a fixed key would mean one machine's state
        /// quietly overwriting the other's.
        ///
        /// The object's name is the key, so RENAMING THIS GAMEOBJECT INVALIDATES
        /// ITS SAVED STATE - the entry stays in the file, nothing claims it, and the
        /// station comes back unverified. That is the right failure; a silent wrong
        /// restore would be worse.
        /// </summary>
        public string SaveKey { get { return "ConnectivityCheckStation:" + gameObject.name; } }

        [System.Serializable]
        class SaveState
        {
            public bool connected;
            public bool reported;
            public bool shownSuccess;
            public bool shownBriefing;
            public bool allDoneShown;
        }

        public string CaptureState()
        {
            return JsonUtility.ToJson(new SaveState
            {
                connected = IsConnected,
                reported = reported,
                shownSuccess = shownSuccess,
                shownBriefing = shownBriefing,
                allDoneShown = allDoneShown,
            });
        }

        /// <summary>
        /// WHY reported IS SAVED TOO, and not just IsConnected.
        ///
        /// TaskManager restores its own completed tasks, so on a resumed save the
        /// task is already done. If this station came back with reported = false it
        /// would report progress again on the next successful ping, pushing a
        /// two-step task past its finish line from one machine alone. Saving the
        /// flag keeps the two records agreeing.
        ///
        /// IsChecking is deliberately NOT saved: a ping that was mid-flight when the
        /// game closed did not happen, and restoring it would leave the station
        /// permanently believing a coroutine is still running.
        /// </summary>
        public void RestoreState(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            SaveState state = JsonUtility.FromJson<SaveState>(data);
            if (state == null) return;

            IsConnected = state.connected;
            reported = state.reported;
            shownSuccess = state.shownSuccess;
            shownBriefing = state.shownBriefing;
            allDoneShown = state.allDoneShown;

            // The link light is the only thing on the set that shows this state, so
            // it has to follow. Without it a restored save shows a verified station
            // with a dead light.
            SetLight(IsConnected);
        }

        // ---------------------------------------------------------------- self-reporting

        void ValidateSetup()
        {
            List<string> problems = new List<string>();

            if (alwaysPassForTesting)
                Debug.LogWarning("[ConnectivityCheckStation] " + stationName + " ALWAYS PASSES the " +
                                 "cable, patch-phase and task checks" +
                                 (requireBothConfigured
                                      ? " (both machines must still be configured first)"
                                      : "") +
                                 ". Untick 'Always Pass For Testing' before a graded build.", this);

            if (cable == null)
                problems.Add("Cable is empty, so there is nothing to check. Drag this machine's " +
                             "CableCarrier in.");
            else if (cable.firstEnd == null || cable.secondEnd == null)
                problems.Add("Cable '" + cable.cableName + "' does not have both ends assigned.");

            if (string.IsNullOrEmpty(equipmentGroup))
                problems.Add("Equipment Group is blank, so any socket counts as this machine's " +
                             "port - the check would pass on a cable plugged into the router.");
            else if (!GroupExists(equipmentGroup))
                problems.Add("No socket in the scene has Group Id '" + equipmentGroup + "'. Check " +
                             "the spelling on this machine's RJ45Socket - it is case sensitive.");

            if (!string.IsNullOrEmpty(panelGroup) && !GroupExists(panelGroup))
                problems.Add("No socket in the scene has Group Id '" + panelGroup + "'.");

            // Deliberately NOT an else-if chain. Every one of these produces the same visible
            // symptom - the prompt naming an address the player did not expect - so reporting only
            // the first one found sends you off to fix one field while another is still wrong.
            if (peer == this)
                problems.Add("Peer points at this same station, which is ignored. Point it at the " +
                             "OTHER machine, or leave it empty to test only this machine's path to " +
                             "the gateway.");

            if (peer == null)
                problems.Add("Peer is empty, so this machine pings Target Address (" + targetAddress +
                             ") instead of the other PC, and the two stations' Ip Address fields are " +
                             "never consulted. For a server/client pair, point Peer at the other " +
                             "machine's ConnectivityCheckStation.");

            if (peer != null && peer != this && string.IsNullOrEmpty(peer.ipAddress))
                problems.Add("Peer '" + peer.stationName + "' has no Ip Address, so the readout " +
                             "cannot name what it is pinging.");

            if (peer != null && peer != this &&
                string.Equals(peer.ipAddress, ipAddress, System.StringComparison.OrdinalIgnoreCase))
                problems.Add("This station and its peer '" + peer.stationName + "' both claim " +
                             ipAddress + ". Two machines cannot share an address on one subnet, and " +
                             "both command prompts will tell the player to ping the same number - " +
                             "which is what makes it look as though the Ip Address field is being " +
                             "ignored. Note this field is NOT the one the desktop builder fills in: " +
                             "the builder sets the EXPECTED values on the DeviceConfigStation, and " +
                             "this Ip Address on the check station is yours to set.");

            if (peer != null && peer != this && peer.peer != this)
                problems.Add("Peer is '" + peer.stationName + "', but that station's own Peer is " +
                             (peer.peer == null ? "empty" : "'" + peer.peer.stationName + "'") +
                             ". Ping is a round trip - point them at each other so both machines " +
                             "test the same link.");

            if (requiresConfig != null)
            {
                foreach (DeviceConfigStation.ConfigField f in requiresConfig.fields)
                {
                    if (f == null || string.IsNullOrEmpty(f.expectedValue)) continue;
                    if (f.expectedValue.IndexOf('.') < 0) continue;          // not an address
                    if (f.label != null && f.label.ToLower().Contains("ip") &&
                        !string.Equals(f.expectedValue, ipAddress, System.StringComparison.OrdinalIgnoreCase))
                        problems.Add("This station's Ip Address is " + ipAddress + ", but the player " +
                                     "is asked to type " + f.expectedValue + " into '" +
                                     requiresConfig.stationName + "'. The machine would end up " +
                                     "answering to an address it was never given - set both to the " +
                                     "same value.");
                }
            }

            if (consolePanel != null && consolePanel.activeSelf)
                problems.Add("Console Panel '" + consolePanel.name + "' is ACTIVE in the scene. It " +
                             "should start inactive - otherwise it is on screen from the first frame.");

            CheckConsoleOwnership(problems);

            // The gate is only as good as the two references it reads. Silently letting an early
            // ping through would be worse than not having the gate, because the run would look
            // ordered when it was not.
            if (requireBothConfigured)
            {
                if (requiresConfig == null)
                    problems.Add("Require Both Configured is on, but Requires Config is empty - so " +
                                 "nothing can tell whether THIS machine has an address, and a ping " +
                                 "attempted before round 1 will run anyway. Drag this machine's " +
                                 "DeviceConfigStation in.");
                if (peer != null && peer != this && peer.requiresConfig == null)
                    problems.Add("Require Both Configured is on, but peer '" + peer.stationName +
                                 "' has no Requires Config, so the far machine's address cannot be " +
                                 "checked and round 2 is not actually gated behind round 1.");
            }

            if (successDialog == null)
                problems.Add("Success Dialog is empty, so a passing check says nothing to the " +
                             "player. That is the whole point of this station.");

            if (!string.IsNullOrEmpty(taskId) && !autoRegisterTask && TaskManager.Instance != null)
                problems.Add("Task Id '" + taskId + "' is set but Auto Register is off. If nothing " +
                             "else registers it, reporting will silently do nothing.");

            if (GetComponent<Collider>() == null)
                problems.Add("No Collider, so the player can never walk up to this station.");

            if (problems.Count == 0)
            {
                Debug.LogWarning("[ConnectivityCheckStation] " + stationName + " ready. Checking: " +
                                 cable.cableName + " from '" + equipmentGroup + "' to '" + panelGroup +
                                 "'" + (requiresPatchPhase != null
                                            ? ", plus phase '" + requiresPatchPhase.phaseName + "'"
                                            : "") + ".", this);
                return;
            }

            string msg = "[ConnectivityCheckStation] " + stationName + " setup is incomplete:";
            foreach (string p in problems) msg += "\n  - " + p;
            Debug.LogWarning(msg, this);
        }

        /// <summary>
        /// Checks that this station's console belongs to this station's machine.
        ///
        /// WHY THIS IS WORTH ITS OWN CHECK
        ///
        /// Two desktops, two consoles, two stations. Four fields on each station decide which screen
        /// it prints on and which button runs it - Console Panel, Console Text, Command Input, Ping
        /// Button - and three of those are invisible in the Hierarchy once assigned. Point any one of
        /// them at the OTHER machine and everything still LOOKS right: a console opens, a ping runs, a
        /// result appears. It is simply the wrong machine's result. One station gets credited twice,
        /// the other never runs at all, the task sticks at 1 of 2, and only one success dialog ever
        /// fires. Nothing in that picture points back at the field that caused it, which is exactly
        /// why it is worth naming here instead of leaving it to be found by elimination.
        ///
        /// The rule is simple enough to state in one line: the three inner references must live inside
        /// Console Panel, and no two stations may share any of the four.
        /// </summary>
        void CheckConsoleOwnership(List<string> problems)
        {
            if (consolePanel != null)
            {
                MustBeInside(consoleText != null ? consoleText.transform : null, "Console Text", problems);
                MustBeInside(commandInput != null ? commandInput.transform : null, "Command Input", problems);
                MustBeInside(pingButton != null ? pingButton.transform : null, "Ping Button", problems);
            }

            ConnectivityCheckStation[] all =
                Object.FindObjectsByType<ConnectivityCheckStation>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None);
            foreach (ConnectivityCheckStation other in all)
            {
                if (other == null || other == this) continue;

                Shared(consolePanel, other.consolePanel, "Console Panel", other, problems);
                Shared(consoleText, other.consoleText, "Console Text", other, problems);
                Shared(commandInput, other.commandInput, "Command Input", other, problems);
                Shared(pingButton, other.pingButton, "Ping Button", other, problems);
            }
        }

        void MustBeInside(Transform t, string field, List<string> problems)
        {
            if (t == null) return;
            if (t.IsChildOf(consolePanel.transform)) return;

            problems.Add(field + " is '" + t.name + "', which is NOT inside this station's Console " +
                         "Panel '" + consolePanel.name + "'. It belongs to a different window - most " +
                         "likely the other machine's. This station will print into, or be run from, " +
                         "the wrong desktop.");
        }

        void Shared(Object mine, Object theirs, string field,
                    ConnectivityCheckStation other, List<string> problems)
        {
            if (mine == null || mine != theirs) return;

            problems.Add(field + " is the same object as on station '" + other.stationName + "' ('" +
                         mine.name + "'). Two machines cannot share one console: whichever station " +
                         "the window happens to talk to gets credited twice and the other never runs, " +
                         "so the verify task sticks partway and only one success dialog fires. Give " +
                         "each station the window under its own desktop.");
        }

        static bool GroupExists(string group)
        {
            for (int i = 0; i < RJ45Socket.All.Count; i++)
            {
                RJ45Socket s = RJ45Socket.All[i];
                if (s != null && s.groupId == group) return true;
            }
            return false;
        }

        /// <summary>Prints the link state one hop at a time. The fastest way to answer "why does it
        /// say no link when I plugged it in".</summary>
        [ContextMenu("Report link")]
        public void LogLink()
        {
            string msg = "[ConnectivityCheckStation] " + stationName + " link:";

            if (cable == null) { Debug.LogWarning(msg + "\n    No cable assigned.", this); return; }

            msg += "\n    cable            : " + cable.cableName +
                   (cable.IsComplete ? "   (both ends in)" : "   (not fully run)");
            msg += "\n    first end        : " + Describe(cable.firstEnd);
            msg += "\n    second end       : " + Describe(cable.secondEnd);
            msg += "\n    wants            : one end in '" + equipmentGroup + "', the other in '" + panelGroup + "'";
            if (requiresPatchPhase != null)
                msg += "\n    patch phase      : '" + requiresPatchPhase.phaseName + "' " +
                       (requiresPatchPhase.HasFinished
                            ? (requiresPatchPhase.LastResultPassed ? "PASSED" : "finished but FAILED")
                            : "not finished");
            // The three lines that answer "why is it pinging that address". Printed together,
            // because the answer is always a relationship between them rather than any one value.
            msg += "\n    this machine     : " + stationName + " (" + ipAddress + ")";

            if (peer == null)
                msg += "\n    peer             : none - falls back to Target Address";
            else if (peer == this)
                msg += "\n    peer             : POINTS AT ITSELF, which is ignored - falls back " +
                       "to Target Address";
            else
                msg += "\n    peer             : " + peer.stationName + " (" + peer.ipAddress + ") - " +
                       (peer.FindLocalBreak("the " + peer.stationName) == null ? "its path is clear"
                                                                              : "ITS PATH IS BROKEN");

            // Which desktop this station is actually wired to. Printed as full paths because the whole
            // failure mode here is a reference that looks right in the Inspector - 'CommandPrompt',
            // 'PingButton' - and is simply the other machine's copy of a window with the same name.
            msg += "\n    console panel    : " + Path(consolePanel != null ? consolePanel.transform : null);
            msg += "\n    console text     : " + Path(consoleText != null ? consoleText.transform : null);
            msg += "\n    command input    : " + Path(commandInput != null ? commandInput.transform : null);
            msg += "\n    ping button      : " + Path(pingButton != null ? pingButton.transform : null);

            msg += "\n    ping target      : " + PingTarget() + "   " +
                   (peer != null && peer != this && !string.IsNullOrEmpty(peer.ipAddress)
                        ? "(the peer's Ip Address)"
                        : "(this station's Target Address, because no usable peer is set)");

            // Always Pass is reported here, loudly, rather than being allowed to print CONNECTED over
            // a list of unplugged cables. A diagnostic that contradicts the evidence directly above
            // it is worse than no diagnostic - it teaches you to stop trusting the tool.
            if (alwaysPassForTesting)
            {
                msg += "\n    ---- verdict     : ALWAYS PASS FOR TESTING is ticked, so the checks " +
                       "above are ignored and this station passes regardless.";
                Debug.LogWarning(msg, this);
                return;
            }

            string notReady = NotReadyYet();
            if (notReady != null)
            {
                msg += "\n    ---- verdict     : NOT READY (round 1 unfinished) - " + notReady +
                       "\n                       A ping now would time out and record nothing.";
                Debug.LogWarning(msg, this);
                return;
            }

            string failure = FindBreak();
            msg += "\n    ---- verdict     : " + (failure == null ? "CONNECTED" : "NO LINK - " + failure);
            Debug.LogWarning(msg, this);
        }

        static string Path(Transform t)
        {
            if (t == null) return "(not assigned)";
            string path = t.name;
            Transform p = t.parent;
            while (p != null) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }

        static string Describe(PatchCord end)
        {
            if (end == null) return "(not assigned)";
            if (!end.IsPlugged || end.PluggedSocket == null) return "unplugged";
            return "in group '" + end.PluggedSocket.groupId + "' port " + end.PluggedSocket.portNumber;
        }
    }
}