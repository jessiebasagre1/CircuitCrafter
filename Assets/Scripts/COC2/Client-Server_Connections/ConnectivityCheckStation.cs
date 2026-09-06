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
    public class ConnectivityCheckStation : Interactable
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

        [Tooltip("Turn every check off and always pass. Testing only - the Console shouts about it.")]
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
            SetLight(false);
            ValidateSetup();
        }

        void Update()
        {
            if (!IsOpen) return;

            if (Input.GetKeyDown(closeKey)) { CloseConsole(); return; }

            // The safety net that stops "the console followed me across the room". The player should
            // not be able to leave with it open at all - that is what disableWhileOpen is for - but
            // if something else moves them, or the trigger is smaller than it looks, the window goes
            // away rather than hanging over the world.
            if (closeWhenPlayerLeaves && !IsPlayerInRange && !IsChecking) CloseConsole();
        }

        public override void Interact()
        {
            if (IsOpen || IsChecking) return;

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

            if (consolePanel != null) consolePanel.SetActive(true);
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

            if (commandInput == null) { StartCoroutine(RunCheck()); return; }

            lines.Clear();
            Write("Microsoft Windows [Version 10.0.19045.4046]");
            Write("(c) Microsoft Corporation. All rights reserved.");
            Write("");
            Write("Type  ping " + PingTarget() + "  and press Enter.");
            PushConsole();

            commandInput.text = "";
            commandInput.interactable = true;
            commandInput.ActivateInputField();
        }

        /// <summary>Hides the console and gives the player back their legs. Wire the X button here.</summary>
        public void CloseConsole()
        {
            if (!IsOpen) return;
            IsOpen = false;

            if (consolePanel != null) consolePanel.SetActive(false);
            foreach (GameObject go in hideWhileOpen) if (go != null) go.SetActive(true);
            foreach (MonoBehaviour m in disableWhileOpen) if (m != null) m.enabled = true;

            if (releaseCursor) { Cursor.lockState = savedLock; Cursor.visible = savedCursorVisible; }
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
                Write("Usage: ping [-t] [-a] [-n count] target_name");
                Write("");
                PushConsole();
                if (commandInput != null) commandInput.ActivateInputField();
                return;
            }

            if (!string.Equals(arg, target, System.StringComparison.OrdinalIgnoreCase))
            {
                Write("Ping request could not find host " + arg + ". Please check the name and " +
                      "try again.");
                Write("");
                PushConsole();
                if (commandInput != null) commandInput.ActivateInputField();
                return;
            }

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
                Say(successDialog, successDialogOnlyOnce && shownSuccess);
                shownSuccess = true;
            }
            else
            {
                IsConnected = false;
                OnFailed?.Invoke();
                Say(failDialog, false);
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

        void Say(DialogSequence sequence, bool alreadyShown)
        {
            if (sequence == null || alreadyShown) return;
            if (DialogManager.Instance == null || DialogManager.Instance.IsDialogOpen) return;
            DialogManager.Instance.ShowDialog(sequence);
        }

        // ---------------------------------------------------------------- public API

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
            lines.Clear();
            PushConsole();
            SetLight(false);
        }

        // ---------------------------------------------------------------- self-reporting

        void ValidateSetup()
        {
            List<string> problems = new List<string>();

            if (alwaysPassForTesting)
                Debug.LogWarning("[ConnectivityCheckStation] " + stationName + " ALWAYS PASSES. " +
                                 "Untick 'Always Pass For Testing' before a graded build.", this);

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

            if (peer == this)
                problems.Add("Peer points at this same station. Point it at the OTHER machine, or " +
                             "leave it empty to test only this machine's path to the gateway.");
            else if (peer != null && string.IsNullOrEmpty(peer.ipAddress))
                problems.Add("Peer '" + peer.stationName + "' has no Ip Address, so the readout " +
                             "cannot name what it is pinging.");
            else if (peer != null && peer.peer != this)
                problems.Add("Peer is '" + peer.stationName + "', but that station's own Peer is " +
                             (peer.peer == null ? "empty" : "'" + peer.peer.stationName + "'") +
                             ". Ping is a round trip - point them at each other so both machines " +
                             "test the same link.");

            if (consolePanel != null && consolePanel.activeSelf)
                problems.Add("Console Panel '" + consolePanel.name + "' is ACTIVE in the scene. It " +
                             "should start inactive - otherwise it is on screen from the first frame.");

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
            if (peer != null && peer != this)
                msg += "\n    peer             : " + peer.stationName + " (" + peer.ipAddress + ") - " +
                       (peer.FindLocalBreak("the " + peer.stationName) == null ? "its path is clear"
                                                                              : "ITS PATH IS BROKEN");

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

            string failure = FindBreak();
            msg += "\n    ---- verdict     : " + (failure == null ? "CONNECTED" : "NO LINK - " + failure);
            Debug.LogWarning(msg, this);
        }

        static string Describe(PatchCord end)
        {
            if (end == null) return "(not assigned)";
            if (!end.IsPlugged || end.PluggedSocket == null) return "unplugged";
            return "in group '" + end.PluggedSocket.groupId + "' port " + end.PluggedSocket.portNumber;
        }
    }
}