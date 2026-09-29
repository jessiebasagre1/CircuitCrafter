// COC2StepGuide.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The instructor for COC 2. It tells the learner what the next step is, how to do it, and why
    /// it is done that way, from the first tool to the final ping.
    ///
    /// WHAT IT ADDS
    ///   - An overview of the whole job when COC 2 starts (a fresh start, not a Continue).
    ///   - An instruction for each step as the learner reaches it: get the crimping tool, go to the
    ///     crimping station, run each cable, configure the network, ping, final inspection.
    ///   - Window-by-window help on the SERVER and CLIENT desktops: Start, Control Panel, Network
    ///     and Sharing Center, Change adapter settings, Ethernet, TCP/IPv4 - with the job order's
    ///     values and what each value means. Then help with the Command Prompt and ping.
    ///   - Result messages for configuring and pinging (the settings that were wrong, the break
    ///     that stopped the ping), for each cable run, and for the final inspection.
    ///   - A short warning when the learner walks up to a station too early (no crimping tool, no
    ///     cable for the tester, patch panel still locked).
    ///
    /// IT NEVER REPLACES A DIALOG YOU ASSIGNED. Every station dialog field that is filled in keeps
    /// working exactly as before. This only speaks where a field is EMPTY. The stage briefings in
    /// COC2StageDialogs, the two-cable messages in COC2BothCablesGate and the "pick up the cable"
    /// message in CrimpedCablePickup are left to those components.
    ///
    /// NOTHING TO WIRE. It adds itself to any scene that contains the COC 2 stations, and finds
    /// every station by itself. To reword a line, add this component to the Managers object yourself
    /// and edit the text there - the scene's copy is used instead of an automatic one. To switch it
    /// off, add it to Managers and untick the component.
    ///
    /// STORY MODE ONLY for the teaching. In the COC 2 assessment it adds only short, neutral result
    /// messages ("Server configured.", "Request timed out.") where the station has none, and only
    /// while COC2AssessmentDialogs > Allow Results is ticked. It does nothing in a COC 4 repair visit.
    ///
    /// TEXT TOKENS you can use in any line: {interact}, {pickup}, {leave} (and capitalised
    /// {Interact}, {Pickup}, {Leave}) become the right key or on-screen button for the device -
    /// "press E" on a PC, "tap Interact" on a phone. {arrow} becomes " Follow the arrow." when the
    /// scene has a waypoint arrow. Other tokens are listed in each field's tooltip.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(320)]
    public class COC2StepGuide : MonoBehaviour
    {
        // ==================================================================
        // Adds itself to COC 2 scenes
        // ==================================================================

        /// <summary>Set false before a scene loads to stop the guide adding itself.</summary>
        public static bool AutoInstall = true;

        public static COC2StepGuide Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            // The first scene has already loaded by the time this runs.
            AddIfCOC2Scene(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AddIfCOC2Scene(scene);
        }

        static void AddIfCOC2Scene(Scene scene)
        {
            if (!AutoInstall || !scene.IsValid() || !scene.isLoaded) return;

            // Already in the scene (even switched off): that copy is the one in charge.
            if (FindAnyObjectByType<COC2StepGuide>(FindObjectsInactive.Include) != null) return;

            bool isCOC2 =
                FindAnyObjectByType<CrimpingStationController>(FindObjectsInactive.Include) != null ||
                FindAnyObjectByType<DeviceConfigStation>(FindObjectsInactive.Include) != null ||
                FindAnyObjectByType<PunchDownStation>(FindObjectsInactive.Include) != null ||
                FindAnyObjectByType<COC2TaskSetup>(FindObjectsInactive.Include) != null;
            if (!isCOC2) return;

            GameObject go = new GameObject("COC2StepGuide (added automatically)");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<COC2StepGuide>();
        }

        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("Speaker")]
        [Tooltip("Name shown at the top of every message from this component. \"Guide\" matches the " +
                 "COC 2 dialog assets already in the project.")]
        public string speakerName = "Guide";

        [Header("What to show")]
        [Tooltip("The overview of the whole job when COC 2 starts. Not shown on Continue.")]
        public bool showOverview = true;

        [Tooltip("An instruction as each step is reached: the tool, the crimping station, each cable " +
                 "run, configuring, pinging, the final inspection.")]
        public bool announceSteps = true;

        [Tooltip("Help on each window of the Server and Client desktops, and in the Command Prompt.")]
        public bool desktopTips = true;

        [Tooltip("Result messages for configuring, pinging, cable runs and the final inspection - " +
                 "only where the station's own dialog field is empty.")]
        public bool coverEmptyStationDialogs = true;

        [Tooltip("A short warning when the learner walks up to a station too early - only where " +
                 "that station's own warning dialog is empty.")]
        public bool approachWarnings = true;

        [Tooltip("COC 2 ASSESSMENT: short, neutral result messages where the station has none. Also " +
                 "needs COC2AssessmentDialogs > Allow Results.")]
        public bool assessmentResults = true;

        [Tooltip("Leave on. Keeps a message on top of the Server/Client desktop while it is open. " +
                 "The desktop is usually built on the same canvas as the dialog box, AFTER it, so " +
                 "it draws over the dialog and the learner sees nothing.")]
        public bool keepDialogAboveDesktop = true;

        [Header("Timing")]
        [Tooltip("Seconds after the scene starts before the first message, so the scene and any " +
                 "intro of its own can settle.")]
        [Min(0f)] public float startDelay = 1.5f;

        [Tooltip("How long the learner must be out of every station, with no dialog or quiz, before " +
                 "a 'next step' instruction appears.")]
        [Min(0f)] public float settleSeconds = 0.6f;

        [Header("Task ids (taken from COC2TaskSetup when the scene has one)")]
        public string crimpTaskId = "crimp_cable";
        public string testTaskId = "test_cable";
        public string punchTaskId = "punch_patch_panel";
        public string patchTaskId = "patch_to_switch";

        [Header("Diagnostics")]
        [Tooltip("Print each message this shows, and what it is covering, in the Console.")]
        public bool logGuide = true;

        // ------------------------------------------------------------------ text: start

        [Header("Text - overview")]
        [Tooltip("{steps} = the numbered job list, built from what is in the scene. {controls} = the " +
                 "controls line for this device.")]
        [TextArea(2, 8)]
        public string[] overviewLines =
        {
            "Welcome to COC 2: Set Up Computer Networks. In this job you will build a small wired " +
            "network, from bare cable to a working link between two computers.",

            "Here is the whole job, in order:\n{steps}",

            "{controls}",

            "Work in that order - a network is built from the cable up. I will explain each step as " +
            "you reach it.{arrow}"
        };

        [TextArea(2, 4)]
        public string controlsKeyboard =
            "Controls: walk with W A S D and look with the mouse. Press {interactKey} to use a station, " +
            "{pickupKey} to pick something up, and Space or a click to continue a message like this one.";

        [TextArea(2, 4)]
        public string controlsTouch =
            "Controls: walk with the joystick and drag the screen to look around. Tap Interact to use " +
            "a station, Pick Up to pick something up, and tap a message like this one to continue.";

        [Header("Text - step 1: the crimping tool")]
        [TextArea(2, 6)]
        public string[] toolLines =
        {
            "Step 1: get your tool. You need a crimping tool - one tool that cuts UTP cable, strips " +
            "its outer jacket and crimps the RJ45 connector on.",

            "It is on the workbench.{arrow} Walk up to it and {pickup}."
        };

        [Header("Text - making the cables")]
        [TextArea(2, 6)]
        public string[] crimpStationLines =
        {
            "Now go to the crimping station and {interact} to start your first cable.{arrow}",

            "First you choose which cable to build. A STRAIGHT-THROUGH cable has the same colour " +
            "order on both ends (T568B to T568B). It connects DIFFERENT kinds of device: a PC to a " +
            "switch, or a switch to a router.",

            "A CROSSOVER cable has T568A on one end and T568B on the other, which swaps the send and " +
            "receive pairs. It connects the SAME kind of device: PC to PC, or switch to switch."
        };

        [Tooltip("Added when COC2BothCablesGate requires both cable types.")]
        [TextArea(2, 4)]
        public string crimpBothLine =
            "You must build both cables and pass the tester with each, in either order.";

        [TextArea(2, 4)]
        public string crimpFourStepsLine =
            "Every cable takes the same four steps: strip the jacket, arrange the eight wires in " +
            "colour order, crimp the connector, and test it.";

        [Header("Text - patch panel")]
        [Tooltip("Only used when the scene has no COC2BothCablesGate telling the learner this.")]
        [TextArea(2, 6)]
        public string[] punchLines =
        {
            "Your cable work is done. Next, go to the patch panel and {interact} to terminate the " +
            "cable run with the punch-down tool.{arrow}"
        };

        [Tooltip("Shown if the learner leaves the patch panel after the punching but before the " +
                 "patch cords are in.")]
        [TextArea(2, 6)]
        public string[] patchReturnLines =
        {
            "The punch-down is done, but the patch cords are not in yet. Go back to the patch panel " +
            "and {interact} to patch each panel port to the switch.{arrow}"
        };

        [Header("Text - running the cables to the computers")]
        [Tooltip("{cable} = the cable's name, {type} = straight-through or crossover, {device} = where " +
                 "the second end goes (server, client), {first} = where the first end goes (patch " +
                 "panel), {plug} = the plug key or button.")]
        [TextArea(2, 6)]
        public string[] cableReadyLines =
        {
            "Next: connect the {device} to the network. The {cable} - a {type} cable - is ready on " +
            "the bench.{arrow}",

            "{Pickup} to pick it up. Carry the plug to the {first} and, when it is close to a free " +
            "port, {plug}. Then walk the other end to the {device} and {plug} again."
        };

        [Tooltip("Added to the FIRST cable's message only.")]
        [TextArea(2, 6)]
        public string[] cableReadyTeachLines =
        {
            "Why the patch panel? In a real building a computer never plugs straight into the " +
            "switch. It plugs into the patch panel, and that panel port is already patched through " +
            "to the switch. This cable finishes the path: computer, patch panel, switch.",

            "A computer-to-panel cable is always straight-through, because a computer and a switch " +
            "are different kinds of device."
        };

        [TextArea(2, 4)]
        public string[] cablePickedUpLines =
        {
            "You are carrying the {cable}. Take the plug to the {first} and {plug} when it is close to " +
            "a free port."
        };

        [TextArea(2, 4)]
        public string[] cableFirstEndLines =
        {
            "One end is in the {first}. Now carry the other end to the {device} and {plug}. If you " +
            "need your hands free, {drop} to put the cable down."
        };

        [TextArea(2, 4)]
        public string[] cableCompleteLines =
        {
            "Done - the {cable} now links the {device} to the patch panel."
        };

        [Header("Text - configuring the network")]
        [Tooltip("{joborder} = every machine's values from its DeviceConfigStation. {first} = the " +
                 "machine to start with.")]
        [TextArea(2, 8)]
        public string[] configureLines =
        {
            "The cabling is finished: every computer now has a physical path to the switch. But a " +
            "cable alone does not make a network. Each computer also needs an IP address before " +
            "they can talk to each other.",

            "Your job order:\n{joborder}",

            "Go to the {first} and {interact} to sit down at it.{arrow}"
        };

        [Header("Text - desktop help, first machine (full route)")]
        [Tooltip("{machine} = this computer, e.g. Server.")]
        [TextArea(2, 6)]
        public string[] tipDesktopLines =
        {
            "You are at the {machine}'s Windows desktop. To give it a fixed (static) IP address, go " +
            "through Control Panel.",

            "Click Start at the bottom-left of the screen. (Escape goes back one window. When you " +
            "are finished, {leave}.)"
        };

        [TextArea(2, 4)]
        public string[] tipStartConfigLines =
        {
            "Click Control Panel. It holds the settings for the whole computer, networking included."
        };

        [TextArea(2, 4)]
        public string[] tipControlPanelLines =
        {
            "Click Network and Sharing Center - this is where Windows keeps everything about network " +
            "connections."
        };

        [TextArea(2, 4)]
        public string[] tipNetworkCenterLines =
        {
            "Click Change adapter settings. An adapter is a network card, and the IP address is a " +
            "setting of the card."
        };

        [TextArea(2, 4)]
        public string[] tipAdaptersLines =
        {
            "Ethernet is the wired network card - the one your cable is plugged into. Click Ethernet " +
            "to open its Properties. (Wi-Fi is not used in this job.)"
        };

        [TextArea(2, 4)]
        public string[] tipEthernetLines =
        {
            "This list shows what the card can use. Click Internet Protocol Version 4 (TCP/IPv4) to " +
            "open its Properties. IPv4 addresses are four numbers separated by dots, like 192.168.1.10."
        };

        [Tooltip("{values} = this machine's settings from the job order, one per line.")]
        [TextArea(2, 6)]
        public string[] tipTcpIpLines =
        {
            "Select \"Use the following IP address\", then type the {machine}'s values from the job " +
            "order:\n{values}\nThen click OK."
        };

        [Tooltip("Explained once, on the first machine. {ip}, {mask}, {net} and {gateway} are filled " +
                 "in from the job order.")]
        [TextArea(2, 4)]
        public string teachIpLine =
            "IP address ({ip}): this computer's own address. No two devices on a network may share " +
            "one - like house numbers on one street.";

        [TextArea(2, 4)]
        public string teachMaskLine =
            "Subnet mask ({mask}): it marks which part of the address is the network. Here the first " +
            "three numbers ({net}) are the network and the last number is the computer, so every " +
            "machine on this network must start with {net}.";

        [TextArea(2, 4)]
        public string teachGatewayLine =
            "Default gateway ({gateway}): the router's address. A computer sends anything meant for " +
            "another network there, so it is the same on every computer in this network.";

        [TextArea(2, 4)]
        public string teachDnsLine =
            "DNS server ({dns}): the server that turns website names into IP addresses.";

        [Header("Text - desktop help, later machines")]
        [TextArea(2, 6)]
        public string[] tipDesktopRecapLines =
        {
            "This is the {machine}'s desktop. Same route as before: Start > Control Panel > Network " +
            "and Sharing Center > Change adapter settings > Ethernet > Internet Protocol Version 4 " +
            "(TCP/IPv4). This time use the {machine}'s own values."
        };

        [Tooltip("{other} = a machine still to configure.")]
        [TextArea(2, 4)]
        public string[] tipDesktopDoneLines =
        {
            "The {machine} is already configured. Next, set up the {other}: {leave}, then walk to the " +
            "{other} and {interact}."
        };

        [Header("Text - desktop help, testing with ping")]
        [TextArea(2, 4)]
        public string[] tipDesktopPingLines =
        {
            "To test the link, click Start and open Command Prompt."
        };

        [TextArea(2, 4)]
        public string[] tipStartPingLines =
        {
            "Click Command Prompt."
        };

        [Tooltip("{target} = the address to ping, {targetName} = the machine that has it.")]
        [TextArea(2, 4)]
        public string[] tipCmdReadyLines =
        {
            "Type  ping {target}  and press Enter (or click Ping)."
        };

        [Tooltip("Added the first time only.")]
        [TextArea(2, 6)]
        public string[] tipCmdTeachLines =
        {
            "Ping sends small test messages to the {targetName} and waits for it to answer. " +
            "\"Reply from {target}\" means the whole path works. \"Request timed out\" means something " +
            "on the way is broken - the line under it tells you what."
        };

        [TextArea(2, 4)]
        public string[] tipCmdNoAddressLines =
        {
            "This computer has no IP address yet, so a ping cannot work. Close the Command Prompt (X) " +
            "and set the address first: Start > Control Panel > Network and Sharing Center."
        };

        [TextArea(2, 4)]
        public string[] tipCmdPeerNotReadyLines =
        {
            "The {machine} is ready, but the {other} has no IP address yet. A ping is a round trip, so " +
            "configure the {other} first, then come back."
        };

        [Header("Text - configuration results")]
        [Tooltip("{values} = the settings, on one line.")]
        [TextArea(2, 4)]
        public string[] configPassLines =
        {
            "The {machine} is configured: {values}."
        };

        [TextArea(2, 4)]
        public string[] configNextLines =
        {
            "Next, set up the {other}. {Leave}, then walk to the {other} and {interact}.{arrow}"
        };

        [Tooltip("{problems} = each wrong or empty setting, one per line.")]
        [TextArea(2, 6)]
        public string[] configFailLines =
        {
            "The settings were not applied. Check these against the job order:\n{problems}",
            "Fix them and click OK again."
        };

        [Header("Text - testing the link")]
        [Tooltip("{where} = where to start: at the desktop the learner is sitting at, or walking back.")]
        [TextArea(2, 6)]
        public string[] verifyLines =
        {
            "Both computers have their IP addresses. Now prove the network works, with the ping " +
            "command.",

            "Ping sends a small message to another computer and waits for it to answer. An answer " +
            "proves the whole path: both cables, the patch panel, the switch and both IP settings.",

            "{where}"
        };

        [Tooltip("{here}, {target}, {other}, {hereIp}.")]
        [TextArea(2, 4)]
        public string verifyHereLine =
            "You are at the {here} now, so start here: click Start, open Command Prompt and type  " +
            "ping {target}. Then go to the {other} and ping {hereIp} back.";

        [Tooltip("{first}, {target}, {other}.")]
        [TextArea(2, 4)]
        public string verifyWalkLine =
            "Go back to the {first} and {interact}. Click Start, open Command Prompt and type  ping " +
            "{target}. Then do the same from the {other}.{arrow}";

        [TextArea(2, 4)]
        public string[] pingPassLines =
        {
            "Reply from {target}! The {machine} reached the {targetName} and got an answer back."
        };

        [Tooltip("{other} = the machine still to test from, {otherTarget} = what it should ping.")]
        [TextArea(2, 4)]
        public string[] pingOtherWayLines =
        {
            "Now test the other direction: go to the {other}, open its Command Prompt and type  ping " +
            "{otherTarget}."
        };

        [TextArea(2, 4)]
        public string[] allVerifiedLines =
        {
            "Both computers answer each other. Your network is up and working - from bare cable to a " +
            "verified link."
        };

        [Tooltip("{reason} = what is broken, worked out the same way the ping itself works it out.")]
        [TextArea(2, 6)]
        public string[] pingFailLines =
        {
            "Request timed out - no reply came back.",
            "{reason}",
            "Fix that, then run the ping again."
        };

        [Header("Text - final inspection")]
        [TextArea(2, 6)]
        public string[] inspectLines =
        {
            "Last step: the final inspection. Walk to the inspection point and {interact} to read the " +
            "report.{arrow}",

            "An inspector checks every part of the job against the job order. Each line says PASS or " +
            "FAIL, and a FAIL line tells you what to put right."
        };

        [TextArea(2, 4)]
        public string[] inspectPassedLines =
        {
            "The inspection passed. Every checkpoint meets the job order, so the installation is " +
            "signed off."
        };

        [TextArea(2, 4)]
        public string[] inspectFailedLines =
        {
            "The inspection found work that does not meet the job order yet. Read the FAIL lines on " +
            "the report - each one says what to put right - then inspect again."
        };

        [Header("Text - walking up too early")]
        [TextArea(2, 4)]
        public string[] warnNoToolLines =
        {
            "You need the crimping tool before you can make a cable. It is on the workbench - walk up " +
            "to it and {pickup}."
        };

        [TextArea(2, 4)]
        public string[] warnNoCableLines =
        {
            "The cable tester needs a finished cable. Make one at the crimping station first, pick it " +
            "up, then bring it here."
        };

        [TextArea(2, 4)]
        public string[] warnPanelLockedLines =
        {
            "The patch panel is not ready for you yet. Finish your cables and pass the tester with " +
            "each one first."
        };

        [Header("Text - COC 2 assessment (neutral results only)")]
        public string assessmentConfigPass = "{machine} configured.";
        public string assessmentConfigFail = "The {machine}'s settings were not accepted.";
        public string assessmentPingPass = "Reply received from {target}.";
        public string assessmentPingFail = "Request timed out.";
        public string assessmentInspectPass = "Final inspection passed.";
        public string assessmentInspectFail = "Final inspection not passed.";

        // ==================================================================
        // Runtime state
        // ==================================================================

        enum Stage { None, GetTool, MakeCables, Punch, Patch, RunCable, Configure, Verify, Inspect, Done }

        enum TaskState { Unknown, NotDone, Done }

        class Message
        {
            public string key;
            public bool once = true;              // remembered after showing; a repeat key is ignored
            public Func<List<string>> build;      // lines, worked out when it is shown
            public bool walking;                  // waits until the learner is out of every station
            public bool result;                   // a reply to what the learner just did - shown at once
            public Func<bool> valid;              // dropped once this is false
            public Func<bool> ready;              // waits while this is false
            public bool ignoreSuppression;        // assessment messages
            public float queuedAt;
        }

        readonly List<Message> pending = new List<Message>();
        readonly HashSet<string> shownKeys = new HashSet<string>();
        readonly List<KeyValuePair<UnityEvent, UnityAction>> listeners =
            new List<KeyValuePair<UnityEvent, UnityAction>>();

        bool storyMode;
        bool coc2Assessment;
        bool started;
        bool freshStart = true;
        float enabledAt;
        float nextStagePoll;
        float walkQuietSince = -1f;
        float viewQuietSince = -1f;
        Stage lastStage = Stage.None;
        CableCarrier lastCarrier;
        bool visitedCableMaking;
        bool firstCableTaught;
        bool tcpipTaught;
        bool cmdTaught;
        DeviceConfigStation fullTipsMachine;

        // Scene objects, found once.
        DeviceConfigStation[] configs = new DeviceConfigStation[0];
        ConnectivityCheckStation[] checks = new ConnectivityCheckStation[0];
        CableCarrier[] carriers = new CableCarrier[0];
        WindowsShell[] shells = new WindowsShell[0];
        PatchCordBoard[] boards = new PatchCordBoard[0];
        CrimpingStationInteractable[] crimpStations = new CrimpingStationInteractable[0];
        CableTypeSelectionUI[] cableChoices = new CableTypeSelectionUI[0];
        PunchDownStation[] punchStations = new PunchDownStation[0];
        COC2FinalInspection inspection;

        readonly Dictionary<ConnectivityCheckStation, bool> consoleWasOpen =
            new Dictionary<ConnectivityCheckStation, bool>();
        readonly Dictionary<Interactable, bool> wasInRange = new Dictionary<Interactable, bool>();

        // Dialog box lifted above the desktop.
        static FieldInfo dialogPanelField;
        GameObject liftedPanel;
        Canvas liftedCanvas;
        bool liftSavedOverride;
        int liftSavedOrder;
        bool warnedHiddenDialog;

        // ==================================================================
        // Unity
        // ==================================================================

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[COC2StepGuide] A second COC2StepGuide on '" + name + "' - the one on '" +
                                 Instance.name + "' is already in charge. Removing this copy.", this);
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            foreach (var pair in listeners)
                if (pair.Key != null) pair.Key.RemoveListener(pair.Value);
            listeners.Clear();

            LowerDialog();

            if (Instance == this) Instance = null;
        }

        void OnDisable()
        {
            LowerDialog();
        }

        void Start()
        {
            storyMode = !AssessmentMode.IsActive && !RepairTicketMode.IsActive;
            coc2Assessment = AssessmentMode.IsAssessing("COC2");

            if (!storyMode && !coc2Assessment)
            {
                if (logGuide)
                    Debug.Log("[COC2StepGuide] Not a COC 2 Story Mode run or a COC 2 assessment - " +
                              "standing by.", this);
                enabled = false;
                return;
            }

            ReadTaskIds();
            FindStations();
            Subscribe();

            enabledAt = Time.unscaledTime;
            freshStart = SaveManager.Instance == null || !SaveManager.Instance.IsContinuingSave;

            if (DialogManager.Instance == null)
                Debug.LogWarning("[COC2StepGuide] No DialogManager in this scene, so nothing can be shown.", this);

            ReportCoverage();
        }

        void Update()
        {
            if (!started)
            {
                if (Time.unscaledTime - enabledAt < startDelay) return;
                if (Restoring()) return;
                started = true;

                if (storyMode && showOverview && freshStart)
                    Enqueue(new Message { key = "overview", walking = true, build = BuildOverview });
            }

            TrackQuietTime();

            if (storyMode)
            {
                WatchCableMaking();

                if (announceSteps && Time.unscaledTime >= nextStagePoll)
                {
                    nextStagePoll = Time.unscaledTime + 0.25f;
                    PollStage();
                }

                if (desktopTips) WatchConsoles();
                if (approachWarnings) WatchApproaches();
            }

            ShowNextMessage();
        }

        void LateUpdate()
        {
            KeepDialogAboveDesktop();
        }

        // ==================================================================
        // Setup
        // ==================================================================

        void ReadTaskIds()
        {
            COC2TaskSetup setup = FindAnyObjectByType<COC2TaskSetup>(FindObjectsInactive.Include);
            if (setup == null) return;
            if (!string.IsNullOrEmpty(setup.crimpTaskId)) crimpTaskId = setup.crimpTaskId;
            if (!string.IsNullOrEmpty(setup.testTaskId)) testTaskId = setup.testTaskId;
            if (!string.IsNullOrEmpty(setup.patchPanelTaskId)) punchTaskId = setup.patchPanelTaskId;
            if (!string.IsNullOrEmpty(setup.patchCordTaskId)) patchTaskId = setup.patchCordTaskId;
        }

        void FindStations()
        {
            configs = Sorted(Find<DeviceConfigStation>(), s => s.stationName);
            checks = Sorted(Find<ConnectivityCheckStation>(), s => s.stationName);
            shells = Find<WindowsShell>();
            crimpStations = Find<CrimpingStationInteractable>();
            cableChoices = Find<CableTypeSelectionUI>();
            punchStations = Find<PunchDownStation>();
            inspection = FindAnyObjectByType<COC2FinalInspection>(FindObjectsInactive.Include);

            // The plugging phases the patch panel runs, in its own order; every board otherwise.
            var phaseList = new List<PatchCordBoard>();
            foreach (PunchDownStation p in punchStations)
                if (p != null && p.patchPhases != null)
                    foreach (PatchCordBoard b in p.patchPhases)
                        if (b != null && !phaseList.Contains(b)) phaseList.Add(b);
            if (phaseList.Count == 0) phaseList.AddRange(Find<PatchCordBoard>());
            boards = phaseList.ToArray();

            // Cables in the order they can be run: a cable that requires another comes after it.
            var carrierList = new List<CableCarrier>(Find<CableCarrier>());
            carrierList.Sort((a, b) =>
            {
                int d = ChainDepth(a).CompareTo(ChainDepth(b));
                return d != 0 ? d : ServerFirst(a.cableName, b.cableName);
            });
            carriers = carrierList.ToArray();
        }

        void Subscribe()
        {
            foreach (DeviceConfigStation s in configs)
            {
                if (s == null) continue;
                DeviceConfigStation station = s;
                Listen(station.OnPassed, () => OnConfigPassed(station));
                Listen(station.OnFailed, () => OnConfigFailed(station));
            }

            foreach (ConnectivityCheckStation c in checks)
            {
                if (c == null) continue;
                ConnectivityCheckStation check = c;
                Listen(check.OnPassed, () => OnPingPassed(check));
                Listen(check.OnFailed, () => OnPingFailed(check));
            }

            if (inspection != null)
            {
                Listen(inspection.OnInspectionPassed, () => OnInspected(true));
                Listen(inspection.OnInspectionFailed, () => OnInspected(false));
            }

            if (!storyMode) return;   // the rest is teaching

            foreach (WindowsShell sh in shells)
            {
                if (sh == null) continue;
                WindowsShell shell = sh;
                Listen(shell.OnScreenChanged, () => OnShellScreen(shell));
            }

            foreach (CableCarrier cc in carriers)
            {
                if (cc == null) continue;
                CableCarrier carrier = cc;
                Listen(carrier.OnPickedUp, () => OnCablePickedUp(carrier));
                Listen(carrier.OnEndPlugged, () => OnCableEndPlugged(carrier));
                Listen(carrier.OnComplete, () => OnCableComplete(carrier));
            }
        }

        void Listen(UnityEvent evt, UnityAction action)
        {
            if (evt == null) return;
            evt.AddListener(action);
            listeners.Add(new KeyValuePair<UnityEvent, UnityAction>(evt, action));
        }

        /// <summary>Says once, at the start, which empty dialog fields this is covering.</summary>
        void ReportCoverage()
        {
            if (!logGuide) return;

            var covered = new List<string>();
            foreach (DeviceConfigStation s in configs)
            {
                if (s == null) continue;
                var f = new List<string>();
                if (s.successDialog == null) f.Add("success");
                if (s.failDialog == null) f.Add("fail");
                if (s.allMachinesDialog == null) f.Add("all machines");
                if (f.Count > 0) covered.Add(s.stationName + " config (" + string.Join(", ", f) + ")");
            }
            foreach (ConnectivityCheckStation c in checks)
            {
                if (c == null) continue;
                var f = new List<string>();
                if (c.successDialog == null) f.Add("success");
                if (c.failDialog == null) f.Add("fail");
                if (c.allMachinesDialog == null) f.Add("all machines");
                if (f.Count > 0) covered.Add(c.stationName + " ping (" + string.Join(", ", f) + ")");
            }
            if (storyMode)
                foreach (CableCarrier cc in carriers)
                {
                    if (cc == null) continue;
                    if (cc.pickedUpDialog == null || cc.firstEndPluggedDialog == null || cc.completeDialog == null)
                        covered.Add(cc.cableName);
                }

            Debug.Log("[COC2StepGuide] " + (storyMode ? "Story Mode guide running" : "COC 2 assessment - neutral results only") +
                      ". Desktops: " + shells.Length + ", config stations: " + configs.Length +
                      ", ping stations: " + checks.Length + ", cables to run: " + carriers.Length +
                      (covered.Count > 0 ? ". Speaking where these dialog fields are empty: " + string.Join("; ", covered) +
                                            ". (Those stations may still log that the field is empty - that is expected.)"
                                         : ". Every station dialog field is filled in - only instructions are added."),
                      this);
        }

        // ==================================================================
        // The step the learner is on
        // ==================================================================

        void PollStage()
        {
            CableCarrier carrier;
            Stage stage = CurrentStage(out carrier);
            if (stage == lastStage && carrier == lastCarrier) return;

            lastStage = stage;
            lastCarrier = carrier;
            if (logGuide) Debug.Log("[COC2StepGuide] Current step: " + stage +
                                    (carrier != null ? " (" + carrier.cableName + ")" : "") + ".", this);
            Announce(stage, carrier);
        }

        Stage CurrentStage(out CableCarrier carrier)
        {
            carrier = null;

            if (!ToolDone()) return Stage.GetTool;
            if (!CablesDone()) return Stage.MakeCables;
            if (!PunchDone()) return Stage.Punch;
            if (!PatchDone()) return Stage.Patch;

            foreach (CableCarrier c in carriers)
                if (c != null && !c.IsComplete) { carrier = c; return Stage.RunCable; }

            if (configs.Length > 0 && !AllConfigured()) return Stage.Configure;
            if (checks.Length > 0 && !AllVerified()) return Stage.Verify;
            if (inspection != null && !inspection.LastResultPassed && State(inspection.taskId) != TaskState.Done)
                return Stage.Inspect;

            return Stage.Done;
        }

        bool ToolDone()
        {
            CrimpingToolInventory inv = CrimpingToolInventory.Instance;
            if (inv == null || inv.HasTool || visitedCableMaking) return true;
            COC2BothCablesGate gate = COC2BothCablesGate.Instance;
            if (gate != null && gate.PassedCount > 0) return true;
            return State(crimpTaskId) == TaskState.Done || State(testTaskId) == TaskState.Done;
        }

        bool CablesDone()
        {
            if (CrimpingStationController.Instance == null) return true;
            COC2BothCablesGate gate = COC2BothCablesGate.Instance;
            if (gate != null && gate.IsComplete) return true;
            TaskState t = State(testTaskId);
            if (t == TaskState.Unknown) return gate == null;   // cannot be judged - do not get stuck on it
            return t == TaskState.Done;
        }

        bool PunchDone()
        {
            if (punchStations.Length == 0) return true;
            return State(punchTaskId) != TaskState.NotDone;
        }

        bool PatchDone()
        {
            if (boards.Length == 0) return true;

            // Either one is enough. The task is the official record, but a phase whose Task Id was
            // left blank never reports it - and waiting on it would stop every instruction after
            // this step for good.
            if (State(patchTaskId) == TaskState.Done) return true;
            foreach (PatchCordBoard b in boards)
                if (b != null && !(b.HasFinished && b.LastResultPassed)) return false;
            return true;
        }

        bool AllConfigured()
        {
            foreach (DeviceConfigStation s in configs) if (s != null && !s.IsConfigured) return false;
            return true;
        }

        bool NoneConfigured()
        {
            foreach (DeviceConfigStation s in configs) if (s != null && s.IsConfigured) return false;
            return true;
        }

        bool AllVerified()
        {
            foreach (ConnectivityCheckStation c in checks) if (c != null && !c.IsConnected) return false;
            return true;
        }

        bool NoneVerified()
        {
            foreach (ConnectivityCheckStation c in checks) if (c != null && c.IsConnected) return false;
            return true;
        }

        TaskState State(string id)
        {
            if (string.IsNullOrEmpty(id) || TaskManager.Instance == null) return TaskState.Unknown;
            int current, required;
            if (!TaskManager.Instance.TryGetProgress(id, out current, out required)) return TaskState.Unknown;
            return TaskManager.Instance.IsTaskComplete(id) ? TaskState.Done : TaskState.NotDone;
        }

        /// <summary>Queues the instruction for a step the learner has just reached.</summary>
        void Announce(Stage stage, CableCarrier carrier)
        {
            switch (stage)
            {
                case Stage.GetTool:
                    Enqueue(new Message
                    {
                        key = "step:tool", walking = true,
                        valid = () => lastStage == Stage.GetTool,
                        build = () => Fill(toolLines, null)
                    });
                    break;

                case Stage.MakeCables:
                    Enqueue(new Message
                    {
                        key = "step:crimp", walking = true,
                        valid = () => lastStage == Stage.MakeCables && !visitedCableMaking && NoCableBuiltYet(),
                        build = BuildCrimpStation
                    });
                    break;

                case Stage.Punch:
                    // COC2BothCablesGate already ends the cable work with "Next, go to the patch panel".
                    if (GateAnnouncesPanel()) break;
                    Enqueue(new Message
                    {
                        key = "step:punch", walking = true,
                        valid = () => lastStage == Stage.Punch,
                        build = () => Fill(punchLines, null)
                    });
                    break;

                case Stage.Patch:
                    Enqueue(new Message
                    {
                        key = "step:patch", walking = true,
                        valid = () => lastStage == Stage.Patch,
                        build = () => Fill(patchReturnLines, null)
                    });
                    break;

                case Stage.RunCable:
                    if (carrier == null || carrier.HasBeenPickedUp || carrier.availableDialog != null) break;
                    CableCarrier c = carrier;
                    Enqueue(new Message
                    {
                        key = "step:cable:" + c.GetInstanceID(), walking = true,
                        ready = () => c.IsAvailable,
                        valid = () => lastStage == Stage.RunCable && lastCarrier == c && !c.HasBeenPickedUp,
                        build = () => BuildCableReady(c)
                    });
                    break;

                case Stage.Configure:
                    Enqueue(new Message
                    {
                        key = "step:configure", walking = true,
                        valid = () => lastStage == Stage.Configure && NoneConfigured(),
                        build = BuildConfigure
                    });
                    break;

                case Stage.Verify:
                    // A dialog of your own on the config stations closes that round - it speaks instead.
                    foreach (DeviceConfigStation s in configs)
                        if (s != null && s.allMachinesDialog != null) return;
                    Enqueue(new Message
                    {
                        key = "step:verify", walking = false,
                        valid = () => lastStage == Stage.Verify && NoneVerified(),
                        build = BuildVerify
                    });
                    break;

                case Stage.Inspect:
                    Enqueue(new Message
                    {
                        key = "step:inspect", walking = true,
                        valid = () => lastStage == Stage.Inspect,
                        build = () => Fill(inspectLines, null)
                    });
                    break;
            }
        }

        bool NoCableBuiltYet()
        {
            COC2BothCablesGate gate = COC2BothCablesGate.Instance;
            if (gate != null && gate.PassedCount > 0) return false;
            CrimpingStationController station = CrimpingStationController.Instance;
            return station == null || !station.IsCrimped;
        }

        bool GateAnnouncesPanel()
        {
            COC2BothCablesGate gate = COC2BothCablesGate.Instance;
            return gate != null && gate.nextStepLines != null && gate.nextStepLines.Length > 0;
        }

        /// <summary>Notes the first time the learner is inside any part of the crimping station.</summary>
        void WatchCableMaking()
        {
            if (visitedCableMaking) return;
            if (InCableMaking()) visitedCableMaking = true;
        }

        bool InCableMaking()
        {
            if (StrippingViewController.Instance != null && StrippingViewController.Instance.InStrippingView) return true;
            if (CrimpStageViewController.Instance != null && CrimpStageViewController.Instance.InCrimpView) return true;
            if (CrimpingViewController.Instance != null && CrimpingViewController.Instance.InCrimpingView) return true;
            if (TestingViewController.Instance != null && TestingViewController.Instance.InTestingView) return true;
            if (NetworkTestStation.Instance != null && NetworkTestStation.Instance.InTesterView) return true;
            foreach (CableTypeSelectionUI c in cableChoices) if (c != null && c.IsOpen) return true;
            return false;
        }

        // ==================================================================
        // Building the step messages
        // ==================================================================

        List<string> BuildOverview()
        {
            var steps = new List<string>();
            COC2BothCablesGate gate = COC2BothCablesGate.Instance;

            if (CrimpingStationController.Instance != null)
                steps.Add(gate != null && RequiredCableCount(gate) >= 2
                    ? "Make two UTP cables - a straight-through and a crossover - and test both."
                    : "Make a UTP cable and test it.");
            if (punchStations.Length > 0) steps.Add("Terminate (punch down) the cable run on the patch panel.");
            if (boards.Length > 0) steps.Add("Patch the panel ports to the switch.");
            if (carriers.Length > 0) steps.Add("Run a cable from the patch panel to " + CarrierDevices() + ".");
            if (configs.Length > 0) steps.Add("Give " + MachineList() + " " + (configs.Length > 1 ? "their" : "its") + " IP address" + (configs.Length > 1 ? "es" : "") + ".");
            if (checks.Length > 0) steps.Add("Test the link with the ping command.");
            if (inspection != null) steps.Add("Pass the final inspection.");

            var list = new StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0) list.Append('\n');
                list.Append(i + 1).Append(". ").Append(steps[i]);
            }

            var vars = new Dictionary<string, string>
            {
                { "steps", list.ToString() },
                { "controls", Fill(IsTouch() ? controlsTouch : controlsKeyboard, null) }
            };
            return Fill(overviewLines, vars);
        }

        List<string> BuildCrimpStation()
        {
            List<string> lines = Fill(crimpStationLines, null);
            COC2BothCablesGate gate = COC2BothCablesGate.Instance;
            if (gate != null && RequiredCableCount(gate) >= 2) lines.Add(Fill(crimpBothLine, null));
            lines.Add(Fill(crimpFourStepsLine, null));
            return lines;
        }

        List<string> BuildCableReady(CableCarrier c)
        {
            List<string> lines = Fill(cableReadyLines, CableVars(c));
            if (!firstCableTaught)
            {
                firstCableTaught = true;
                lines.AddRange(Fill(cableReadyTeachLines, CableVars(c)));
            }
            return lines;
        }

        List<string> BuildConfigure()
        {
            var order = new StringBuilder();
            DeviceConfigStation first = null;
            foreach (DeviceConfigStation s in configs)
            {
                if (s == null) continue;
                if (first == null && !s.IsConfigured) first = s;
                if (order.Length > 0) order.Append('\n');
                order.Append(s.stationName).Append(" - ").Append(Values(s, false));
            }

            var vars = new Dictionary<string, string>
            {
                { "joborder", order.ToString() },
                { "first", first != null ? first.stationName : "first computer" }
            };
            return Fill(configureLines, vars);
        }

        List<string> BuildVerify()
        {
            string where;
            DeviceConfigStation here = DeviceConfigStation.OpenStation;
            ConnectivityCheckStation hereCheck = CheckFor(here);

            if (here != null && hereCheck != null && !hereCheck.IsConnected)
            {
                ConnectivityCheckStation other = OtherCheck(hereCheck);
                where = Fill(verifyHereLine, new Dictionary<string, string>
                {
                    { "here", here.stationName },
                    { "target", PingTarget(hereCheck) },
                    { "other", other != null ? other.stationName : "other computer" },
                    { "hereIp", hereCheck.ipAddress }
                });
            }
            else
            {
                ConnectivityCheckStation first = null;
                foreach (ConnectivityCheckStation c in checks)
                    if (c != null && !c.IsConnected) { first = c; break; }
                if (first == null) return null;
                ConnectivityCheckStation other = OtherCheck(first);
                where = Fill(verifyWalkLine, new Dictionary<string, string>
                {
                    { "first", first.stationName },
                    { "target", PingTarget(first) },
                    { "other", other != null ? other.stationName : "other computer" }
                });
            }

            return Fill(verifyLines, new Dictionary<string, string> { { "where", where } });
        }

        // ==================================================================
        // Desktop help (Server / Client)
        // ==================================================================

        void OnShellScreen(WindowsShell shell)
        {
            if (!storyMode || !desktopTips || shell == null) return;

            DeviceConfigStation station = StationFor(shell);
            if (station == null || !station.IsOpen) return;   // not a visit - the shell switching itself off

            string screen = shell.CurrentScreen ?? "";
            if (screen == "cmd") return;                       // the Command Prompt is watched separately

            string purpose = Purpose(station);
            if (purpose == null) return;

            Enqueue(new Message
            {
                key = "tip:" + station.GetInstanceID() + ":" + purpose + ":" + screen,
                walking = false,
                valid = () => shell != null && shell.isActiveAndEnabled && station.IsOpen &&
                              (shell.CurrentScreen ?? "") == screen && Purpose(station) == purpose,
                build = () => BuildDesktopTip(station, shell, screen, purpose)
            });
        }

        /// <summary>What the learner is at this machine to do: "config", "waiting" (done here, another
        /// machine is not), "ping", or null when there is nothing left to do here.</summary>
        string Purpose(DeviceConfigStation station)
        {
            if (!station.IsConfigured) return "config";
            if (!AllConfigured()) return "waiting";
            ConnectivityCheckStation check = CheckFor(station);
            if (check != null && !check.IsConnected) return "ping";
            return null;
        }

        List<string> BuildDesktopTip(DeviceConfigStation station, WindowsShell shell, string screen, string purpose)
        {
            var vars = MachineVars(station);

            if (purpose == "waiting")
                return screen == "" ? Fill(tipDesktopDoneLines, vars) : null;

            if (purpose == "ping")
            {
                if (screen == "") return Fill(tipDesktopPingLines, vars);
                if (screen == "start") return Fill(tipStartPingLines, vars);
                return null;
            }

            // Configuring. The first machine gets the whole route explained; later ones a recap.
            if (fullTipsMachine == null) fullTipsMachine = station;
            bool full = fullTipsMachine == station;

            switch (screen)
            {
                case "": return Fill(full ? tipDesktopLines : tipDesktopRecapLines, vars);
                case "start": return full ? Fill(tipStartConfigLines, vars) : null;
                case "controlpanel": return full ? Fill(tipControlPanelLines, vars) : null;
                case "networkcenter": return full ? Fill(tipNetworkCenterLines, vars) : null;
                case "adapters": return full ? Fill(tipAdaptersLines, vars) : null;
                case "ethernetprops": return full ? Fill(tipEthernetLines, vars) : null;
                case "tcpip":
                    {
                        List<string> lines = Fill(tipTcpIpLines, vars);
                        if (!tcpipTaught)
                        {
                            tcpipTaught = true;
                            lines.AddRange(TeachFields(station));
                        }
                        return lines;
                    }
            }

            // A window this component does not know by id: point at the next one on the shell's
            // marked route, by its title.
            if (!full || shell.expectedPath == null) return null;
            int at = Array.IndexOf(shell.expectedPath, screen);
            if (at < 0 || at + 1 >= shell.expectedPath.Length) return null;
            string next = shell.expectedPath[at + 1];
            foreach (WindowsShell.Screen s in shell.screens)
                if (s != null && s.id == next)
                    return new List<string> { "Next, open " + (string.IsNullOrEmpty(s.title) ? s.id : s.title) + "." };
            return null;
        }

        /// <summary>What each setting means, for the settings this machine actually asks for.</summary>
        List<string> TeachFields(DeviceConfigStation station)
        {
            var lines = new List<string>();
            string ip = Expected(station, "ip");
            string mask = Expected(station, "mask");
            string gateway = Expected(station, "gateway");
            string dns = Expected(station, "dns");

            var vars = new Dictionary<string, string>
            {
                { "ip", ip ?? "" }, { "mask", mask ?? "" }, { "gateway", gateway ?? "" }, { "dns", dns ?? "" },
                { "net", NetworkPart(ip) }
            };

            if (ip != null) lines.Add(Fill(teachIpLine, vars));
            if (mask != null)
                lines.Add(mask == "255.255.255.0" && NetworkPart(ip) != ""
                    ? Fill(teachMaskLine, vars)
                    : "Subnet mask (" + mask + "): it marks which part of the address is the network " +
                      "and which part is the computer. Every machine on this network uses the same mask.");
            if (gateway != null) lines.Add(Fill(teachGatewayLine, vars));
            if (dns != null) lines.Add(Fill(teachDnsLine, vars));
            return lines;
        }

        /// <summary>The Command Prompt, whether it is a desktop window or a station of its own.</summary>
        void WatchConsoles()
        {
            foreach (ConnectivityCheckStation c in checks)
            {
                if (c == null) continue;
                bool open = c.IsOpen;
                bool was;
                consoleWasOpen.TryGetValue(c, out was);
                consoleWasOpen[c] = open;
                if (!open || was) continue;

                ConnectivityCheckStation check = c;
                string purpose = ConsolePurpose(check);
                if (purpose == null) continue;

                Enqueue(new Message
                {
                    key = "cmd:" + check.GetInstanceID() + ":" + purpose,
                    walking = false,
                    valid = () => check.IsOpen && !check.IsChecking && ConsolePurpose(check) == purpose,
                    build = () => BuildConsoleTip(check, purpose)
                });
            }
        }

        string ConsolePurpose(ConnectivityCheckStation check)
        {
            if (check.IsConnected) return null;
            if (check.requiresConfig != null && !check.requiresConfig.IsConfigured) return "noaddress";
            if (check.NotReadyYet() != null) return "peer";
            return "ready";
        }

        List<string> BuildConsoleTip(ConnectivityCheckStation check, string purpose)
        {
            ConnectivityCheckStation other = OtherCheck(check);
            var vars = new Dictionary<string, string>
            {
                { "machine", check.stationName },
                { "other", other != null ? other.stationName : "other computer" },
                { "target", PingTarget(check) },
                { "targetName", other != null ? other.stationName : "other computer" }
            };

            if (purpose == "noaddress") return Fill(tipCmdNoAddressLines, vars);
            if (purpose == "peer") return Fill(tipCmdPeerNotReadyLines, vars);

            List<string> lines = Fill(tipCmdReadyLines, vars);
            if (!cmdTaught)
            {
                cmdTaught = true;
                lines.AddRange(Fill(tipCmdTeachLines, vars));
            }
            return lines;
        }

        // ==================================================================
        // Results
        // ==================================================================

        void OnConfigPassed(DeviceConfigStation station)
        {
            if (storyMode && coverEmptyStationDialogs && station.successDialog == null)
            {
                Enqueue(new Message
                {
                    key = "config:pass:" + station.GetInstanceID(), result = true,
                    build = () =>
                    {
                        var vars = MachineVars(station);
                        vars["values"] = Values(station, false);
                        List<string> lines = Fill(configPassLines, vars);
                        DeviceConfigStation next = NextUnconfigured();
                        if (next != null)
                        {
                            vars["other"] = next.stationName;
                            lines.AddRange(Fill(configNextLines, vars));
                        }
                        return lines;
                    }
                });
            }

            if (coc2Assessment && assessmentResults && station.successDialog == null)
                EnqueueAssessment("asm:config:pass:" + station.GetInstanceID(),
                                  Fill(assessmentConfigPass, MachineVars(station)));
        }

        void OnConfigFailed(DeviceConfigStation station)
        {
            if (storyMode && coverEmptyStationDialogs && station.failDialog == null)
            {
                // Read NOW, while the fields still hold what was typed.
                string problems = Problems(station);
                Enqueue(new Message
                {
                    key = "config:fail:" + station.GetInstanceID(), once = false, result = true,
                    build = () =>
                    {
                        var vars = MachineVars(station);
                        vars["problems"] = problems;
                        return Fill(configFailLines, vars);
                    }
                });
            }

            if (coc2Assessment && assessmentResults && station.failDialog == null)
                EnqueueAssessment("asm:config:fail:" + station.GetInstanceID(),
                                  Fill(assessmentConfigFail, MachineVars(station)), false);
        }

        void OnPingPassed(ConnectivityCheckStation check)
        {
            if (storyMode && coverEmptyStationDialogs)
            {
                bool mine = check.successDialog == null;
                Enqueue(new Message
                {
                    key = "ping:pass:" + check.GetInstanceID(), result = true,
                    build = () =>
                    {
                        ConnectivityCheckStation other = OtherCheck(check);
                        var vars = new Dictionary<string, string>
                        {
                            { "machine", check.stationName },
                            { "target", PingTarget(check) },
                            { "targetName", other != null ? other.stationName : "other computer" },
                            { "other", other != null ? other.stationName : "other computer" },
                            { "otherTarget", other != null ? PingTarget(other) : "" }
                        };

                        var lines = new List<string>();
                        if (mine) lines.AddRange(Fill(pingPassLines, vars));

                        if (AllVerified())
                        {
                            if (check.allMachinesDialog == null && !shownKeys.Contains("ping:all"))
                            {
                                shownKeys.Add("ping:all");
                                lines.AddRange(Fill(allVerifiedLines, vars));
                            }
                        }
                        else if (mine && other != null && !other.IsConnected)
                        {
                            lines.AddRange(Fill(pingOtherWayLines, vars));
                        }
                        return lines;
                    }
                });
            }

            if (coc2Assessment && assessmentResults && check.successDialog == null)
                EnqueueAssessment("asm:ping:pass:" + check.GetInstanceID(),
                                  Fill(assessmentPingPass, new Dictionary<string, string> { { "target", PingTarget(check) } }));
        }

        void OnPingFailed(ConnectivityCheckStation check)
        {
            if (storyMode && coverEmptyStationDialogs && check.failDialog == null)
            {
                // Worked out NOW - the same walk the ping itself made, in the same order.
                string reason = BreakReason(check);
                Enqueue(new Message
                {
                    key = "ping:fail:" + check.GetInstanceID(), once = false, result = true,
                    build = () =>
                    {
                        var vars = new Dictionary<string, string>
                        {
                            { "machine", check.stationName },
                            { "target", PingTarget(check) },
                            { "reason", reason ?? "" }
                        };
                        return Fill(pingFailLines, vars);
                    }
                });
            }

            if (coc2Assessment && assessmentResults && check.failDialog == null)
                EnqueueAssessment("asm:ping:fail:" + check.GetInstanceID(),
                                  Fill(assessmentPingFail, new Dictionary<string, string> { { "target", PingTarget(check) } }), false);
        }

        void OnInspected(bool passed)
        {
            if (inspection == null) return;
            DialogSequence own = passed ? inspection.passedDialog : inspection.failedDialog;
            if (own != null) return;

            if (storyMode && coverEmptyStationDialogs)
                Enqueue(new Message
                {
                    key = passed ? "inspect:pass" : "inspect:fail", once = passed, result = true,
                    build = () => Fill(passed ? inspectPassedLines : inspectFailedLines, null)
                });

            if (coc2Assessment && assessmentResults)
                EnqueueAssessment(passed ? "asm:inspect:pass" : "asm:inspect:fail",
                                  passed ? assessmentInspectPass : assessmentInspectFail, passed);
        }

        void EnqueueAssessment(string key, string line, bool once = true)
        {
            Enqueue(new Message
            {
                key = key, once = once, result = true, ignoreSuppression = true,
                // Asked when it is shown, so ticking Allow Results off mid-run is respected.
                build = () => COC2AssessmentDialogs.ResultsAllowed ? new List<string> { line } : null
            });
        }

        // ------------------------------------------------------------------ cable runs

        void OnCablePickedUp(CableCarrier c)
        {
            if (!coverEmptyStationDialogs || c.pickedUpDialog != null) return;
            // The "cable is ready" instruction already said all of this.
            if (shownKeys.Contains("step:cable:" + c.GetInstanceID())) return;

            Enqueue(new Message
            {
                key = "cable:picked:" + c.GetInstanceID(), result = true,
                valid = () => c.IsHeld && c.EndsPlugged == 0,
                build = () => Fill(cablePickedUpLines, CableVars(c))
            });
        }

        void OnCableEndPlugged(CableCarrier c)
        {
            if (!coverEmptyStationDialogs || c.firstEndPluggedDialog != null) return;
            if (c.EndsPlugged != 1) return;   // the second end: OnComplete speaks

            Enqueue(new Message
            {
                key = "cable:first:" + c.GetInstanceID(), result = true,
                valid = () => !c.IsComplete,
                build = () => Fill(cableFirstEndLines, CableVars(c))
            });
        }

        void OnCableComplete(CableCarrier c)
        {
            if (!coverEmptyStationDialogs || c.completeDialog != null) return;
            Enqueue(new Message
            {
                key = "cable:done:" + c.GetInstanceID(), result = true,
                build = () => Fill(cableCompleteLines, CableVars(c))
            });
        }

        // ------------------------------------------------------------------ walking up too early

        void WatchApproaches()
        {
            CrimpingToolInventory inv = CrimpingToolInventory.Instance;
            CrimpingViewController view = CrimpingViewController.Instance;
            COC2BothCablesGate gate = COC2BothCablesGate.Instance;

            foreach (CrimpingStationInteractable s in crimpStations)
            {
                if (!Arrived(s)) continue;
                if (inv == null || inv.HasTool) continue;
                // The step-1 instruction says the same thing; this is for a learner who walked past it.
                if (announceSteps && !shownKeys.Contains("step:tool")) continue;
                if (view != null && view.noToolDialog != null) continue;
                if (gate != null && gate.IsComplete) continue;
                Enqueue(new Message
                {
                    key = "warn:tool", valid = () => !inv.HasTool,
                    build = () => Fill(warnNoToolLines, null)
                });
            }

            NetworkTestStation tester = NetworkTestStation.Instance;
            if (tester != null && Arrived(tester))
            {
                CableInventory cables = CableInventory.Instance;
                bool firstCableNotTestedYet = !tester.HasTested && (gate == null || gate.PassedCount == 0) &&
                                              State(testTaskId) != TaskState.Done;
                if (cables != null && !cables.HasCable && tester.noCableDialog == null &&
                    !tester.InTesterView && firstCableNotTestedYet)
                {
                    Enqueue(new Message
                    {
                        key = "warn:cable", valid = () => !cables.HasCable && !tester.InTesterView,
                        build = () => Fill(warnNoCableLines, null)
                    });
                }
            }

            foreach (PunchDownStation p in punchStations)
            {
                if (!Arrived(p)) continue;
                if (p.IsUnlocked || p.lockedDialog != null) continue;
                PunchDownStation station = p;
                Enqueue(new Message
                {
                    key = "warn:panel", valid = () => !station.IsUnlocked,
                    build = () => Fill(warnPanelLockedLines, null)
                });
            }
        }

        /// <summary>True on the frame the learner walks into this station's range.</summary>
        bool Arrived(Interactable i)
        {
            if (i == null) return false;
            bool now = i.IsPlayerInRange;
            bool before;
            wasInRange.TryGetValue(i, out before);
            wasInRange[i] = now;
            return now && !before;
        }

        // ==================================================================
        // The message queue
        // ==================================================================

        void Enqueue(Message m)
        {
            if (m == null || m.build == null) return;

            if (!string.IsNullOrEmpty(m.key))
            {
                if (m.once && shownKeys.Contains(m.key)) return;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    if (pending[i].key != m.key) continue;
                    if (m.once) return;          // already waiting
                    pending.RemoveAt(i);         // a newer copy replaces it (a second wrong attempt, say)
                }
            }

            m.queuedAt = Time.unscaledTime;
            pending.Add(m);
        }

        /// <summary>
        /// Two clocks. "Walking" messages - the next step - wait until the learner has been out of
        /// every station, dialog and quiz for Settle Seconds. Messages about the screen in front of
        /// them only wait for the dialog box and the quiz.
        /// </summary>
        void TrackQuietTime()
        {
            DialogManager dm = DialogManager.Instance;
            bool dialog = dm != null && (dm.IsDialogOpen || dm.WasDialogClosedThisFrame);
            bool quiz = QuizOpen() || QuizPending();

            float now = Time.unscaledTime;

            if (dialog || quiz || Restoring()) viewQuietSince = -1f;
            else if (viewQuietSince < 0f) viewQuietSince = now;

            bool walkingBusy = dialog || quiz || Restoring() || AnyStationOpen() ||
                               PlayerInputLock.IsLocked || Time.timeScale == 0f;
            if (walkingBusy) walkQuietSince = -1f;
            else if (walkQuietSince < 0f) walkQuietSince = now;
        }

        void ShowNextMessage()
        {
            if (pending.Count == 0) return;

            DialogManager dm = DialogManager.Instance;
            if (dm == null || dm.IsDialogOpen || dm.WasDialogClosedThisFrame || Restoring()) return;

            for (int i = 0; i < pending.Count; i++)
            {
                Message m = pending[i];

                if (m.valid != null && !m.valid())
                {
                    pending.RemoveAt(i);
                    i--;
                    continue;
                }

                if (m.ready != null && !m.ready()) continue;
                if (!CanShowNow(m)) continue;

                pending.RemoveAt(i);
                Show(m);
                return;
            }
        }

        bool CanShowNow(Message m)
        {
            float now = Time.unscaledTime;

            if (m.result)
            {
                // A reply to what the learner just did goes up straight away - ahead of a quiz that the
                // same action has only just started (the quiz waits for the dialog box). Once the quiz
                // could be on screen, the reply waits for it instead.
                if (QuizOpen() && now - m.queuedAt > 0.35f) return false;
                return true;
            }

            if (m.walking) return walkQuietSince >= 0f && now - walkQuietSince >= settleSeconds;
            return viewQuietSince >= 0f && now - viewQuietSince >= 0.15f;
        }

        void Show(Message m)
        {
            List<string> lines = m.build();
            if (lines == null || lines.Count == 0) return;

            DialogSequence sequence = ScriptableObject.CreateInstance<DialogSequence>();
            sequence.name = "COC2StepGuide " + (m.key ?? "message");
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line) || line.Trim().Length == 0) continue;
                sequence.lines.Add(new DialogLine { speakerName = speakerName, text = line.Trim() });
            }

            if (sequence.lines.Count == 0) { Destroy(sequence); return; }

            if (m.once && !string.IsNullOrEmpty(m.key)) shownKeys.Add(m.key);

            if (logGuide) Debug.Log("[COC2StepGuide] Showing '" + (m.key ?? "message") + "' (" +
                                    sequence.lines.Count + " line(s)).", this);

            DialogManager.Instance.ShowDialog(sequence, () => Destroy(sequence), m.ignoreSuppression);
        }

        bool AnyStationOpen()
        {
            if (InCableMaking()) return true;
            foreach (PunchDownStation s in punchStations) if (s != null && s.InStageView) return true;
            foreach (ConnectivityCheckStation s in checks) if (s != null && s.IsOpen) return true;
            foreach (DeviceConfigStation s in configs) if (s != null && s.IsOpen) return true;
            if (inspection != null && inspection.panel != null && inspection.panel.activeInHierarchy) return true;
            return false;
        }

        static bool QuizOpen()
        {
            return QuizManager.Instance != null && QuizManager.Instance.IsQuizOpen;
        }

        static bool QuizPending()
        {
            string names;
            return QuizTrigger.AnyPending(out names);
        }

        static bool Restoring()
        {
            return SaveManager.Instance != null && SaveManager.Instance.IsRestoringSave;
        }

        // ==================================================================
        // Keeping the dialog box above the desktop
        // ==================================================================

        /// <summary>
        /// While a dialog is up over the Server/Client desktop (or the inspection report), sorts the
        /// dialog box above it, and puts it back as it was the moment the desktop closes.
        ///
        /// The desktop builder puts the desktop on the scene's canvas as its LAST child, and a later
        /// child draws on top - so the dialog box, earlier on the same canvas, ends up underneath.
        /// The dialog is "open" (it is holding the learner's input) and nobody can see it.
        ///
        /// Done with a Canvas on the dialog panel, set to Override Sorting - the same technique
        /// AssessmentIntroPanel and StationExitButton use. A GraphicRaycaster goes with it, because
        /// a tap on a nested canvas is only seen by that canvas's own raycaster.
        /// </summary>
        void KeepDialogAboveDesktop()
        {
            if (!keepDialogAboveDesktop) return;

            DialogManager dm = DialogManager.Instance;
            if (dm == null) { LowerDialog(); return; }

            int above;
            bool screenUp = HighestOpenScreenOrder(out above);

            if (!screenUp) { LowerDialog(); return; }
            if (!dm.IsDialogOpen) return;

            GameObject panel = DialogPanelOf(dm);
            if (panel == null) return;

            if (!panel.activeInHierarchy)
            {
                if (!warnedHiddenDialog)
                {
                    warnedHiddenDialog = true;

                    // The ancestor that is switched off - the thing to look for in the stations' lists.
                    Transform off = panel.transform;
                    while (off != null && off.gameObject.activeSelf) off = off.parent;

                    Debug.LogWarning("[COC2StepGuide] A dialog is open over the desktop, but the dialog " +
                                     "box '" + panel.name + "' is hidden because '" +
                                     (off != null ? off.name : "a parent") + "' is switched off - the " +
                                     "learner cannot see it. Is that object in a DeviceConfigStation's " +
                                     "Hide While Open list?", panel);
                }
                return;
            }

            Lift(panel, above + 1);
        }

        bool HighestOpenScreenOrder(out int highest)
        {
            highest = int.MinValue;
            bool any = false;

            foreach (DeviceConfigStation s in configs)
                if (s != null && s.IsOpen && s.panel != null && s.panel.activeInHierarchy)
                { any = true; highest = Mathf.Max(highest, EffectiveOrder(s.panel)); }

            foreach (ConnectivityCheckStation c in checks)
                if (c != null && c.IsOpen && c.consolePanel != null && c.consolePanel.activeInHierarchy)
                { any = true; highest = Mathf.Max(highest, EffectiveOrder(c.consolePanel)); }

            if (inspection != null && inspection.panel != null && inspection.panel.activeInHierarchy)
            { any = true; highest = Mathf.Max(highest, EffectiveOrder(inspection.panel)); }

            return any;
        }

        void Lift(GameObject panel, int target)
        {
            if (liftedPanel != panel) LowerDialog();

            Canvas own = panel.GetComponent<Canvas>();
            if (own == null)
            {
                Canvas parent = panel.transform.parent != null
                    ? panel.transform.parent.GetComponentInParent<Canvas>() : null;
                own = panel.AddComponent<Canvas>();
                if (parent != null) own.sortingLayerID = parent.rootCanvas.sortingLayerID;
            }
            if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();

            if (liftedPanel == null)
            {
                liftedPanel = panel;
                liftedCanvas = own;
                liftSavedOverride = own.overrideSorting;
                liftSavedOrder = own.sortingOrder;
            }

            target = Mathf.Min(target, short.MaxValue);
            if (EffectiveOrder(panel) >= target && (own.isRootCanvas || own.overrideSorting)) return;

            if (!own.isRootCanvas) own.overrideSorting = true;
            own.sortingOrder = target;
        }

        void LowerDialog()
        {
            if (liftedPanel == null) return;
            if (liftedCanvas != null)
            {
                if (!liftedCanvas.isRootCanvas) liftedCanvas.overrideSorting = liftSavedOverride;
                liftedCanvas.sortingOrder = liftSavedOrder;
            }
            liftedPanel = null;
            liftedCanvas = null;
        }

        /// <summary>The sorting order a UI object is actually drawn at.</summary>
        static int EffectiveOrder(GameObject go)
        {
            Canvas c = go.GetComponentInParent<Canvas>();
            while (c != null && !c.isRootCanvas && !c.overrideSorting)
                c = c.transform.parent != null ? c.transform.parent.GetComponentInParent<Canvas>() : null;
            return c != null ? c.sortingOrder : 0;
        }

        /// <summary>DialogManager keeps its panel private; read it rather than edit a shared script.</summary>
        static GameObject DialogPanelOf(DialogManager dm)
        {
            if (dialogPanelField == null)
                dialogPanelField = typeof(DialogManager).GetField("dialogPanel",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return dialogPanelField != null ? dialogPanelField.GetValue(dm) as GameObject : null;
        }

        // ==================================================================
        // Reading the stations
        // ==================================================================

        DeviceConfigStation StationFor(WindowsShell shell)
        {
            foreach (DeviceConfigStation s in configs)
            {
                if (s == null || s.panel == null) continue;
                if (s.panel == shell.gameObject ||
                    shell.transform.IsChildOf(s.panel.transform) ||
                    s.panel.transform.IsChildOf(shell.transform))
                    return s;
            }
            return DeviceConfigStation.OpenStation;
        }

        ConnectivityCheckStation CheckFor(DeviceConfigStation station)
        {
            if (station == null) return null;
            foreach (ConnectivityCheckStation c in checks)
                if (c != null && c.requiresConfig == station) return c;
            foreach (ConnectivityCheckStation c in checks)
                if (c != null && c.consolePanel != null && station.panel != null &&
                    c.consolePanel.transform.IsChildOf(station.panel.transform)) return c;
            return null;
        }

        ConnectivityCheckStation OtherCheck(ConnectivityCheckStation check)
        {
            if (check == null) return null;
            if (check.peer != null && check.peer != check) return check.peer;
            foreach (ConnectivityCheckStation c in checks)
                if (c != null && c != check) return c;
            return null;
        }

        DeviceConfigStation NextUnconfigured()
        {
            foreach (DeviceConfigStation s in configs) if (s != null && !s.IsConfigured) return s;
            return null;
        }

        /// <summary>The address this station pings - the same rule ConnectivityCheckStation uses.</summary>
        static string PingTarget(ConnectivityCheckStation c)
        {
            if (c == null) return "";
            if (c.peer != null && c.peer != c && !string.IsNullOrEmpty(c.peer.ipAddress)) return c.peer.ipAddress;
            return c.targetAddress;
        }

        /// <summary>The first break on the path, walked exactly as the ping walks it.</summary>
        static string BreakReason(ConnectivityCheckStation c)
        {
            if (c.alwaysPassForTesting) return null;
            string mine = c.FindLocalBreak("this machine");
            if (mine != null) return mine;
            if (c.peer != null && c.peer != c)
            {
                string theirs = c.peer.FindLocalBreak("the " + c.peer.stationName);
                if (theirs != null) return "This machine is fine, but the reply never came back. " + theirs;
            }
            return null;
        }

        /// <summary>Every wrong or empty setting, marked the same way the station marks them.</summary>
        static string Problems(DeviceConfigStation s)
        {
            var sb = new StringBuilder();
            foreach (DeviceConfigStation.ConfigField f in s.fields)
            {
                if (f == null || f.input == null || string.IsNullOrEmpty(f.expectedValue)) continue;
                string typed = (f.input.text ?? "").Trim();

                string line = null;
                if (typed.Length == 0)
                {
                    if (s.requireEveryField) line = "- " + f.label + " is empty. The job order says " + f.expectedValue + ".";
                }
                else if (!Accepts(f, typed))
                {
                    line = "- " + f.label + ": you typed " + typed + ", but the job order says " + f.expectedValue + ".";
                }

                if (line == null) continue;
                if (!string.IsNullOrEmpty(f.wrongHint)) line += " " + f.wrongHint.Trim();
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
            return sb.Length > 0 ? sb.ToString() : "- One of the settings does not match the job order.";
        }

        static bool Accepts(DeviceConfigStation.ConfigField f, string typed)
        {
            if (Same(typed, f.expectedValue)) return true;
            if (f.alsoAccept != null)
                foreach (string alt in f.alsoAccept) if (Same(typed, alt)) return true;
            return false;
        }

        static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The job order's values for one machine - one per line, or on one line.</summary>
        static string Values(DeviceConfigStation s, bool multiline)
        {
            var sb = new StringBuilder();
            foreach (DeviceConfigStation.ConfigField f in s.fields)
            {
                if (f == null || string.IsNullOrEmpty(f.expectedValue)) continue;
                if (sb.Length > 0) sb.Append(multiline ? "\n" : ", ");
                if (multiline) sb.Append("   ");
                sb.Append(multiline ? f.label : f.label.ToLowerInvariant().Replace("ip ", "IP ").Replace("dns", "DNS"))
                  .Append(multiline ? ": " : " ").Append(f.expectedValue);
                if (f.prefill) sb.Append(" (already filled in)");
            }
            return sb.ToString();
        }

        /// <summary>The expected value of the field whose label mentions this word, or null.</summary>
        static string Expected(DeviceConfigStation s, string kind)
        {
            foreach (DeviceConfigStation.ConfigField f in s.fields)
            {
                if (f == null || string.IsNullOrEmpty(f.expectedValue) || string.IsNullOrEmpty(f.label)) continue;
                string label = f.label.ToLowerInvariant();
                bool match;
                switch (kind)
                {
                    case "ip": match = label.Contains("ip") && !label.Contains("mask") && !label.Contains("gateway") && !label.Contains("dns"); break;
                    case "mask": match = label.Contains("mask"); break;
                    case "gateway": match = label.Contains("gateway"); break;
                    case "dns": match = label.Contains("dns"); break;
                    default: match = false; break;
                }
                if (match) return f.expectedValue.Trim();
            }
            return null;
        }

        /// <summary>"192.168.1" from "192.168.1.10"; empty when it is not a dotted address.</summary>
        static string NetworkPart(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return "";
            int last = ip.LastIndexOf('.');
            if (last <= 0) return "";
            return ip.Split('.').Length == 4 ? ip.Substring(0, last) : "";
        }

        Dictionary<string, string> MachineVars(DeviceConfigStation s)
        {
            DeviceConfigStation other = null;
            foreach (DeviceConfigStation c in configs)
                if (c != null && c != s && !c.IsConfigured) { other = c; break; }
            if (other == null)
                foreach (DeviceConfigStation c in configs)
                    if (c != null && c != s) { other = c; break; }

            return new Dictionary<string, string>
            {
                { "machine", s.stationName },
                { "other", other != null ? other.stationName : "other computer" },
                { "values", Values(s, true) }
            };
        }

        Dictionary<string, string> CableVars(CableCarrier c)
        {
            bool touch = IsTouch();
            return new Dictionary<string, string>
            {
                { "cable", c.cableName },
                { "type", string.IsNullOrEmpty(c.cableTypeLabel) ? "straight-through" : c.cableTypeLabel },
                { "first", Friendly(c.firstEndGroups) },
                { "device", Friendly(c.secondEndGroups) },
                { "plug", touch ? "tap Plug" : "press " + c.plugKey },
                { "drop", touch ? "tap Drop" : "press " + c.dropKey }
            };
        }

        static string Friendly(string[] groups)
        {
            if (groups == null || groups.Length == 0 || string.IsNullOrEmpty(groups[0])) return "port";
            switch (groups[0].Trim().ToLowerInvariant())
            {
                case "panel": return "patch panel";
                case "outlet": return "wall outlet";
                default: return groups[0].Trim();
            }
        }

        string CarrierDevices()
        {
            var names = new List<string>();
            foreach (CableCarrier c in carriers)
            {
                if (c == null) continue;
                string d = "the " + Friendly(c.secondEndGroups);
                if (!names.Contains(d)) names.Add(d);
            }
            return JoinAnd(names, "each computer");
        }

        string MachineList()
        {
            var names = new List<string>();
            foreach (DeviceConfigStation s in configs) if (s != null) names.Add("the " + s.stationName);
            return JoinAnd(names, "each computer");
        }

        static string JoinAnd(List<string> items, string fallback)
        {
            if (items.Count == 0) return fallback;
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.GetRange(0, items.Count - 1).ToArray()) + " and " + items[items.Count - 1];
        }

        static int RequiredCableCount(COC2BothCablesGate gate)
        {
            int n = 0;
            if (gate.requiredCableTypes != null)
                foreach (CableTypeSO t in gate.requiredCableTypes) if (t != null) n++;
            return n;
        }

        // ==================================================================
        // Text
        // ==================================================================

        List<string> Fill(string[] lines, Dictionary<string, string> vars)
        {
            var result = new List<string>();
            if (lines == null) return result;
            foreach (string line in lines)
            {
                string filled = Fill(line, vars);
                if (!string.IsNullOrEmpty(filled) && filled.Trim().Length > 0) result.Add(filled);
            }
            return result;
        }

        /// <summary>Replaces {tokens}. Every token also works capitalised - {Interact} gives "Press E".</summary>
        string Fill(string line, Dictionary<string, string> vars)
        {
            if (string.IsNullOrEmpty(line)) return line;

            string result = line;
            if (vars != null)
                foreach (var pair in vars) result = Replace(result, pair.Key, pair.Value);
            foreach (var pair in CommonVars()) result = Replace(result, pair.Key, pair.Value);
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

        Dictionary<string, string> CommonVars()
        {
            bool touch = IsTouch();
            CSSSimulation.Interaction.PlayerInteractor pi = PlayerInteractorInScene();
            string interactKey = pi != null ? pi.interactKey.ToString() : "E";
            string pickupKey = pi != null ? pi.pickupKey.ToString() : "G";

            return new Dictionary<string, string>
            {
                { "interact", touch ? "tap Interact" : "press " + interactKey },
                { "pickup", touch ? "tap Pick Up" : "press " + pickupKey },
                { "leave", touch ? "tap Leave PC" : "press " + interactKey + " or click Leave PC" },
                { "interactKey", interactKey },
                { "pickupKey", pickupKey },
                { "arrow", TaskWaypointArrow.Instance != null ? " Follow the arrow." : "" }
            };
        }

        // Written out in full: the project also has a COC 1 PlayerInteractor in the global namespace,
        // and a bare "PlayerInteractor" here would quietly mean that one.
        CSSSimulation.Interaction.PlayerInteractor cachedInteractor;

        CSSSimulation.Interaction.PlayerInteractor PlayerInteractorInScene()
        {
            if (cachedInteractor == null)
                cachedInteractor = FindAnyObjectByType<CSSSimulation.Interaction.PlayerInteractor>(
                    FindObjectsInactive.Include);
            return cachedInteractor;
        }

        static bool IsTouch()
        {
            return Application.isMobilePlatform || CursorControl.TouchControlsVisible;
        }

        // ==================================================================
        // Small helpers
        // ==================================================================

        static T[] Find<T>() where T : UnityEngine.Object
        {
            // Include: several stations sit on objects that start switched off.
            return FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        static T[] Sorted<T>(T[] items, Func<T, string> nameOf) where T : UnityEngine.Object
        {
            var list = new List<T>();
            foreach (T t in items) if (t != null) list.Add(t);
            list.Sort((a, b) => ServerFirst(nameOf(a), nameOf(b)));
            return list.ToArray();
        }

        /// <summary>The server before anything else, then by name - the order the job is done in.</summary>
        static int ServerFirst(string a, string b)
        {
            bool sa = (a ?? "").ToLowerInvariant().Contains("server");
            bool sb = (b ?? "").ToLowerInvariant().Contains("server");
            if (sa != sb) return sa ? -1 : 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static int ChainDepth(CableCarrier c)
        {
            int depth = 0;
            CableCarrier at = c.requires;
            while (at != null && at != c && depth < 8) { depth++; at = at.requires; }
            return depth;
        }
    }
}
