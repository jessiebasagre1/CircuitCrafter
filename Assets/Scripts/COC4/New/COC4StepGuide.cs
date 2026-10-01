// COC4StepGuide.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using CircuitCrafter.Networking.Unity;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The instructor for COC 4 Story Mode. It tells the learner what to do next, how to do it (which
    /// key, which button, where to walk) and why a technician does it in that order - on the ticket
    /// board and at every repair site.
    ///
    /// WHAT IT ADDS
    ///   TICKET BOARD (hub)
    ///     - An overview of COC 4 and of the troubleshooting method, the first time the board opens.
    ///     - "Read the report, press Start Ticket", then "press Go to Site", as the learner gets there.
    ///     - With tickets unlocking in order: the order rule in the overview, "finish X first" on a
    ///       locked ticket, and "Y is now unlocked" after a ticket is resolved.
    ///     - The result of the visit they have just come back from: resolved (with how many are
    ///       left), diagnosed but not repaired, or left unfinished - and what to do about it.
    ///   REPAIR SITE - the five steps, each announced as the learner reaches it:
    ///     1. Confirm  - walk to the monitor, press E, press POWER and watch the whole start-up.
    ///     2. Examine  - step back, open the system unit, look before touching (with a safety line).
    ///     3. Diagnose - press F and choose the cause that explains everything seen.
    ///     4. Repair   - written for the fault on this ticket: a part to put back from the hotbar, a
    ///                   cable to plug in, a brush to fetch (G) and scrub with, or network settings.
    ///     5. Test     - step back, go to the monitor and press POWER again to prove the repair.
    ///   Plus replies to what the learner just did: the symptom they saw, a wrong diagnosis, a failed
    ///   test, a passed test, and a nudge if they open the case before switching the machine on.
    ///   The waypoint arrow is moved to the next place (monitor, system unit, tool) at each step.
    ///
    /// IT NEVER TALKS OVER THE SITE'S OWN LINES. The arrival report, the station and power-on lines
    /// (in the learner's own voice) and any dialog assets you assigned all play first; the guide's
    /// message waits until the dialog box is closed and the learner is not mid-boot or in the
    /// diagnosis panel.
    ///
    /// WITH THE TICKET INTRO PANEL (COC4TicketIntroPanel) the panel is the arrival: it shows the
    /// report, the plan and the step to start on, so the guide says nothing until Start Job is
    /// pressed. It then adds only the step itself - and not even that for step 1, which the panel
    /// spells out in full.
    ///
    /// NOTHING TO WIRE. It adds itself to the ticket board scene (it has a TicketHubView or
    /// TicketManager) and to any scene reached from a ticket. Every station, fault and panel is
    /// found by itself. To reword a line, add this component to Managers yourself and edit the text
    /// there - the scene's copy is used instead of an automatic one. To switch it off, add it to
    /// Managers and untick the component.
    ///
    /// STORY MODE ONLY. In the COC 4 assessment (or any assessment) it does nothing at all - the
    /// assessment's job order is the only instruction there, by design.
    ///
    /// TEXT TOKENS you can use in any line (each also works capitalised, e.g. {Interact}):
    ///   {interact} "press E" / "tap Use"     {leave} "press E again" / "tap Step Back"
    ///   {diagnose} "press F" / "tap Diagnose" {grab} "press G" / "tap Grab"
    ///   {click} "click" / "tap"               {arrow} " Follow the arrow." when the arrow is used
    /// Other tokens are listed in each field's tooltip.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(330)]
    public class COC4StepGuide : MonoBehaviour
    {
        // ==================================================================
        // Adds itself to COC 4 scenes
        // ==================================================================

        /// <summary>Set false before a scene loads to stop the guide adding itself.</summary>
        public static bool AutoInstall = true;

        public static COC4StepGuide Instance { get; private set; }

        // Once per session: the board overview, the "Start Ticket" / "Go to Site" tips, the safety line.
        static readonly HashSet<string> sessionKeys = new HashSet<string>();
        // Tickets whose site plan has been shown this session.
        static readonly HashSet<string> plannedTickets = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            sessionKeys.Clear();
            plannedTickets.Clear();
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            // The first scene has already loaded by the time this runs.
            AddIfCOC4Scene(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AddIfCOC4Scene(scene);
        }

        static void AddIfCOC4Scene(Scene scene)
        {
            if (!AutoInstall || !scene.IsValid() || !scene.isLoaded) return;

            // Already in the scene (even switched off): that copy is the one in charge.
            if (FindAnyObjectByType<COC4StepGuide>(FindObjectsInactive.Include) != null) return;

            bool hub = FindAnyObjectByType<TicketHubView>(FindObjectsInactive.Include) != null ||
                       FindAnyObjectByType<TicketManager>(FindObjectsInactive.Include) != null;

            bool site = RepairTicketMode.IsActive && (
                FindAnyObjectByType<TicketFaultCoordinator>(FindObjectsInactive.Include) != null ||
                FindAnyObjectByType<RepairStation>(FindObjectsInactive.Include) != null ||
                FindAnyObjectByType<PCPowerStation>(FindObjectsInactive.Include) != null ||
                FindAnyObjectByType<TicketDiagnosisPanel>(FindObjectsInactive.Include) != null);

            if (!hub && !site) return;

            GameObject go = new GameObject("COC4StepGuide (added automatically)");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<COC4StepGuide>();
        }

        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("Speaker")]
        [Tooltip("Name shown at the top of every message from this component. \"Guide\" matches the " +
                 "instruction dialogs in COC 1 and COC 2.")]
        public string speakerName = "Guide";

        [Header("What to show")]
        [Tooltip("Messages on the ticket board: the overview, Start Ticket / Go to Site, and the result " +
                 "of the visit the learner has just come back from.")]
        public bool hubGuide = true;

        [Tooltip("The five steps at a repair site, each announced as the learner reaches it.")]
        public bool siteSteps = true;

        [Tooltip("Replies to what the learner just did: the symptom, a wrong diagnosis, a failed or " +
                 "passed test, opening the case before switching the machine on.")]
        public bool siteReplies = true;

        [Tooltip("Moves the waypoint arrow to the next place at each step - the monitor, the system " +
                 "unit, the tool. Only if the scene has a WaypointIndicator.")]
        public bool pointArrow = true;

        [Header("Timing")]
        [Tooltip("Seconds after the scene starts before the first message, so the arrival report plays first.")]
        [Min(0f)] public float startDelay = 1.2f;

        [Tooltip("Seconds of quiet (no dialog box) before a step message appears.")]
        [Min(0f)] public float settleSeconds = 0.35f;

        [Tooltip("Writes what the guide is doing to the Console.")]
        public bool logGuide = true;

        // ------------------------------------------------------------------
        [Header("Ticket board")]
        [Tooltip("The first time the board opens in a session. Tokens: {click}, {total}.")]
        [TextArea(2, 5)]
        public string[] hubOverviewLines =
        {
            "Welcome to COC 4 - Maintain and Repair Computer Systems and Networks. Here you are the service technician, and every row on this board is a job ticket: a customer's report about a computer that is not working properly.",
            "Every job is done with the same troubleshooting method: CONFIRM the problem, EXAMINE the machine, DIAGNOSE the cause, REPAIR it, then TEST it to prove the repair worked. You cannot skip a step.",
            "The status shows where each job is: Open (not started), In Progress (accepted), Diagnosed - repair pending (cause found, not fixed yet) and Resolved (repaired and tested).",
            "To begin, {click} a ticket that is Open and read what the customer reported."
        };

        [Tooltip("Added to the overview, before its last line, when tickets unlock in order (TicketManager > Unlock In Order).")]
        [TextArea(2, 5)]
        public string hubOrderLine =
            "Tickets unlock one at a time, in order. Finish each job - repaired AND tested - to unlock the next one.";

        [Tooltip("When a locked ticket is showing. Once per session. Tokens: {ticket}, {previous} (the ticket to finish first).")]
        [TextArea(2, 5)]
        public string[] hubLockedLines =
        {
            "This ticket is still locked. Finish \"{previous}\" first - tickets unlock one at a time, in order, just as a technician works through a queue of jobs."
        };

        [Tooltip("Back from a site with a ticket resolved that unlocked the next one. Goes before the last resolved line. Tokens: {next}.")]
        [TextArea(2, 5)]
        public string hubUnlockedLine = "\"{next}\" is now unlocked.";

        [Tooltip("When an Open ticket is showing in the detail panel. Once per session.")]
        [TextArea(2, 5)]
        public string[] hubStartLines =
        {
            "Read the customer's report carefully. It describes what the computer is DOING - the symptom - not what is wrong with it. Finding the cause is your job at the site.",
            "{Click} Start Ticket to accept the job."
        };

        [Tooltip("When an In Progress ticket is showing. Once per session.")]
        [TextArea(2, 5)]
        public string[] hubGoLines =
        {
            "Ticket accepted. {Click} Go to Site to travel to the computer. The first thing you will do there is switch it on and see the problem for yourself."
        };

        [Tooltip("When a Diagnosed ticket is showing. Tokens: {cause}, {ticket}.")]
        [TextArea(2, 5)]
        public string[] hubGoDiagnosedLines =
        {
            "You already found the cause of this one: {cause}. {Click} Go to Site to repair it and test it. You will not have to diagnose it again."
        };

        [Tooltip("Back from a site with the ticket resolved. Tokens: {ticket}, {done}, {total}, {left}.")]
        [TextArea(2, 5)]
        public string[] hubResolvedLines =
        {
            "Ticket resolved - \"{ticket}\" is repaired and tested. That is {done} of {total} tickets done.",
            "{Click} the next Open ticket to continue."
        };

        [Tooltip("Back from a site and every ticket is now resolved. Tokens: {ticket}, {total}.")]
        [TextArea(2, 5)]
        public string[] hubAllDoneLines =
        {
            "Ticket resolved - and that was the last one. All {total} tickets are repaired and tested.",
            "On every job you confirmed the problem, examined the machine, diagnosed the cause, repaired it and proved the repair with a test. That is the COC 4 troubleshooting procedure. Well done!"
        };

        [Tooltip("Back from a site where the cause was found but the repair was not finished. Tokens: {ticket}, {cause}.")]
        [TextArea(2, 5)]
        public string[] hubDiagnosedOnlyLines =
        {
            "You found the cause of \"{ticket}\" ({cause}), but the repair was not finished and tested, so it is now marked Diagnosed - repair pending.",
            "{Click} it, then {click} Go to Site to finish the job. You will not have to diagnose it again."
        };

        [Tooltip("Back from a site with nothing recorded. Tokens: {ticket}.")]
        [TextArea(2, 5)]
        public string[] hubUnfinishedLines =
        {
            "\"{ticket}\" is still In Progress - the job was left before the cause was found.",
            "{Click} it, then {click} Go to Site to carry on: switch the computer on, examine it, then diagnose."
        };

        // ------------------------------------------------------------------
        [Header("Repair site - arrival")]
        [Tooltip("The plan, the first time the learner arrives at a ticket this session.")]
        [TextArea(2, 5)]
        public string[] sitePlanLines =
        {
            "Here is the plan for this job: 1. CONFIRM - switch the computer on and watch what it does. 2. EXAMINE - open the system unit and look inside. 3. DIAGNOSE - name the most likely cause. 4. REPAIR - fix it. 5. TEST - switch it on again to prove it works."
        };

        [Tooltip("The plan at a site with no power-on test (a network ticket).")]
        [TextArea(2, 5)]
        public string[] sitePlanNetworkLines =
        {
            "Here is the plan for this job: 1. CONFIRM - check what the computer can and cannot reach. 2. EXAMINE - look at its network settings. 3. DIAGNOSE - name the most likely cause. 4. REPAIR - correct it. 5. TEST - ping to prove the connection works."
        };

        [Tooltip("Arriving at a ticket that was already diagnosed on an earlier visit. Tokens: {cause}.")]
        [TextArea(2, 5)]
        public string[] siteReturnLines =
        {
            "You diagnosed this machine on your last visit: {cause}. This time, repair it and then switch it on to test it."
        };

        [Header("Repair site - the five steps")]
        [Tooltip("Step 1. Tokens: {interact}, {arrow}.")]
        [TextArea(2, 5)]
        public string[] stepConfirmLines =
        {
            "Step 1 - CONFIRM the problem. Walk up to the monitor{arrow} and {interact}.",
            "{Click} POWER and watch the whole start-up without leaving: the power light and fans, the BIOS screen, the memory count, any beeps, and whether Windows loads. Stepping away before it finishes cancels the test."
        };

        [Tooltip("Step 2. Tokens: {toUnit} (the walk to the system unit), {interact}, {leave}, {arrow}.")]
        [TextArea(2, 5)]
        public string[] stepExamineLines =
        {
            "Step 2 - EXAMINE the machine. {toUnit}"
        };

        [Tooltip("What to look for inside the case. Follows step 2, or step 3 if the learner went straight in.")]
        [TextArea(2, 5)]
        public string lookLine =
            "Look before you touch anything: is every part seated in its slot? Is every cable plugged in? Is anything dusty, loose or damaged? Compare what you see with the symptom.";

        [Tooltip("Step 2 at a site that uses the Examine panel instead of the system unit.")]
        [TextArea(2, 5)]
        public string[] stepExaminePanelLines =
        {
            "Step 2 - EXAMINE the machine. Open Examine and choose the checks you want to run. Each one shows only what you would really see - note the results before you decide anything."
        };

        [Tooltip("Step 3. Tokens: {diagnose}, {penalty}.")]
        [TextArea(2, 5)]
        public string[] stepDiagnoseLines =
        {
            "Step 3 - DIAGNOSE. {Diagnose} to open the diagnosis and choose the cause that explains EVERYTHING you found - what the monitor showed and what you saw inside.",
            "Choose carefully: each wrong answer adds {penalty} seconds to the job and is recorded. You cannot repair anything until the cause is named."
        };

        [Tooltip("Step 3 when the system unit stays shut until the cause is named. Tokens: {diagnose}, {penalty}.")]
        [TextArea(2, 5)]
        public string[] stepDiagnoseLockedLines =
        {
            "Step 3 - DIAGNOSE. The case stays shut until you name the cause, so decide from the symptom you saw. {Diagnose} to open the diagnosis.",
            "Choose carefully: each wrong answer adds {penalty} seconds to the job and is recorded."
        };

        [Tooltip("Step 4 when a tool has to be fetched first. Tokens: {tool}, {findIt} (\"Find it\", or \"Press E again to step back, then find it\"), {grab}, {arrow}.")]
        [TextArea(2, 5)]
        public string[] stepGetToolLines =
        {
            "Step 4 - REPAIR. For this job you need {tool}. {findIt} in the room{arrow}, stand right next to it and {grab} to pick it up."
        };

        [Tooltip("Starts step 4. The lines for each kind of fault follow it on the same page.")]
        [TextArea(2, 5)]
        public string repairHeading = "Step 4 - REPAIR.";

        [Tooltip("Before the repair lines when the learner is outside the case. Tokens: {interact}, {arrow}.")]
        [TextArea(2, 5)]
        public string repairOpenCaseLine = "Go to the system unit{arrow} and {interact} to open it up.";

        [Tooltip("A part put back from the hotbar. Tokens: {part}, {Part}.")]
        [TextArea(2, 5)]
        public string repairPartLine =
            "{Part} is in your hotbar - the row of slots along the bottom of the screen. Drag it from the hotbar onto its empty slot. The slot lights up when you are over the right place; let go to seat it.";

        [Tooltip("Added after the part line when the part is memory (RAM).")]
        [TextArea(2, 5)]
        public string repairRamLine =
            "A memory module only fits one way: the notch has to line up with the key in the slot. Once it is fully seated, the clips at each end close to lock it in. On a real PC, press it straight down firmly until both clips click.";

        [Tooltip("A cable to plug back in. Tokens: {cable}, {Cable}.")]
        [TextArea(2, 5)]
        public string repairCableLine =
            "{Cable} is not connected. Drag its loose plug to its port and let go when it lines up - it snaps in. On a real PC, push it in until the latch clicks so it cannot work loose again.";

        [Tooltip("Dust, once the learner has the tool. Tokens: {tool}, {Tool}.")]
        [TextArea(2, 5)]
        public string repairDustLine =
            "With the case open, {tool} is in your hotbar. Drag it out and rub it back and forth over each dusty area until it is completely clean. It only cleans while you keep moving - holding it still does nothing.";

        [Tooltip("A network setting. Tokens: {host}, {target}, {targetIp}.")]
        [TextArea(2, 5)]
        public string repairNetworkLine =
            "This is a network problem, so there is nothing to fix inside the case. On {host}, open the IPv4 settings and compare the IP address, subnet mask and default gateway with the network it has to reach ({target}). Correct what is wrong, {click} OK, then ping {targetIp} from the Command Prompt to prove the connection works.";

        [Tooltip("Any other kind of fault.")]
        [TextArea(2, 5)]
        public string repairOtherLine = "Repair the fault you diagnosed.";

        [Tooltip("Step 5. Tokens: {toMonitor} (the walk back to the monitor), {interact}, {leave}, {arrow}.")]
        [TextArea(2, 5)]
        public string[] stepTestLines =
        {
            "Step 5 - TEST. The repair is done, but a job is only finished when you have PROVED it works. {toMonitor}"
        };

        [Header("Repair site - replies")]
        [Tooltip("Shown once, the first time the learner gets inside a case this session. Part of the Examine or Repair message.")]
        [TextArea(2, 5)]
        public string safetyLine =
            "Safety first: a computer is always switched off and unplugged from the wall before you work inside it. In a real workshop, wear an anti-static wrist strap (or touch the bare metal of the case) first, so static electricity cannot damage the parts.";

        [Tooltip("After the first power-on shows the fault. The next step follows on the same message. Tokens: {symptom}.")]
        [TextArea(2, 5)]
        public string[] symptomLines =
        {
            "That is the symptom: {symptom} Keep it in mind - whatever cause you choose later has to explain exactly this."
        };

        [Tooltip("Inside the system unit before the machine was ever switched on. Tokens: {leave}.")]
        [TextArea(2, 5)]
        public string[] earlyCaseLines =
        {
            "Hold on - you have not switched the computer on yet. A technician always confirms the problem first, so you know what you are looking for.",
            "{Leave} to step back, go to the monitor and {click} POWER. Then come back in here."
        };

        [Tooltip("The first wrong diagnosis on a visit.")]
        [TextArea(2, 5)]
        public string[] diagnosisWrongLines =
        {
            "Not accepted. Read the explanation on the diagnosis panel - it tells you which of your observations rules that cause out. Then choose again."
        };

        [Tooltip("Any later wrong diagnosis on the same visit.")]
        [TextArea(2, 5)]
        public string[] diagnosisWrongAgainLines =
        {
            "Not that one either. Go back over the symptom and what you saw inside - the right cause explains all of it."
        };

        [Tooltip("Put in front of the next step once the cause is accepted. Tokens: {cause}.")]
        [TextArea(2, 5)]
        public string diagnosisRightLine = "Cause confirmed: {cause}. Well reasoned.";

        [Tooltip("The test after a repair still fails. What is left to repair follows on the same message. Tokens: {symptom}.")]
        [TextArea(2, 5)]
        public string[] testFailedLines =
        {
            "The test failed: {symptom} Something is still wrong. Fixing one fault can change the symptom, so compare this with what you saw before and look inside again."
        };

        [Tooltip("The machine works but the cause was never named. Tokens: {diagnose}.")]
        [TextArea(2, 5)]
        public string[] passedUndiagnosedLines =
        {
            "{Diagnose} and choose the cause to close the job - your repair will count as soon as you do."
        };

        [Tooltip("The test passes and the ticket can be closed. Tokens: {back} (how the learner gets back to the board).")]
        [TextArea(2, 5)]
        public string[] testPassedLines =
        {
            "Test passed! The computer starts and passes every check, so the repair is proven.",
            "You confirmed the problem, examined the machine, diagnosed the cause, repaired it and tested it - the complete procedure. {back}"
        };

        [Tooltip("A site with no power-on test (a network site) once the fault is fixed. Tokens: {back}.")]
        [TextArea(2, 5)]
        public string[] fixedNoTestLines =
        {
            "Fixed - the connection works again, and the ping proved it. {back}"
        };

        [Tooltip("{back} when the site returns to the board by itself.")]
        public string backAutomatic = "You will be taken back to the ticket board in a moment.";

        [Tooltip("{back} when it does not. Tokens: {click}.")]
        public string backButton = "{Click} Return to Hub to close the ticket.";

        // ==================================================================
        // Runtime state
        // ==================================================================

        enum Stage { None, Confirm, Examine, Diagnose, GetTool, Repair, Test, Done }

        class Message
        {
            public string key;
            public bool once = true;              // remembered after showing; a repeat key is ignored
            public Func<List<string>> build;      // lines, worked out when it is shown
            public bool result;                   // a reply to what the learner just did
            public Func<bool> valid;              // dropped once this is false
            public Func<bool> ready;              // waits while this is false
            public float queuedAt;
        }

        readonly List<Message> pending = new List<Message>();
        readonly HashSet<string> shownKeys = new HashSet<string>();
        readonly List<KeyValuePair<UnityEvent, UnityAction>> listeners =
            new List<KeyValuePair<UnityEvent, UnityAction>>();

        bool isHub;
        bool isSite;
        bool started;
        float enabledAt;
        float nextPoll;
        float quietSince = -1f;

        // Hub
        TicketManager manager;
        TicketDetailPanelView detailPanel;
        bool returned;
        string returnedTicketId;
        bool returnedDiagnosed;
        bool returnedFixed;

        // Site
        TicketData ticket;
        PCPowerStation power;
        TicketFaultCoordinator coordinator;
        RepairStation monitor;
        RepairStation unit;
        TicketDiagnosisPanel diagnosisPanel;
        SiteObservationPanel observationPanel;
        ToolBelt toolBelt;
        AutoReturnToHub autoReturn;
        readonly List<TicketFault> faults = new List<TicketFault>();
        string powerObservationId = "power_on_test";
        string unitObservationId = "open_case";
        Stage lastStage = Stage.None;
        bool arrivalShown;
        int lastAttempts;
        bool lastDiagnosed;
        bool diagnosedAtStart;
        bool causeLinePending;
        RepairStation lastCurrent;
        bool resultShownForPass;
        bool wrongAnswerSeen;

        // ==================================================================
        // Unity
        // ==================================================================

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[COC4StepGuide] A second COC4StepGuide on '" + name + "' - the one on '" +
                                 Instance.name + "' is already in charge. Removing this copy.", this);
                Destroy(this);
                return;
            }
            Instance = this;

            // The ticket board clears RepairTicketMode in its Start, so what the learner has just done at
            // the site has to be read now, before that happens. Awake always runs before any Start.
            if (RepairTicketMode.IsActive && FindAnyObjectByType<TicketHubView>(FindObjectsInactive.Include) != null)
            {
                returned = true;
                returnedTicketId = RepairTicketMode.ActiveTicketId;
                returnedDiagnosed = RepairTicketMode.IsDiagnosed;
                returnedFixed = RepairTicketMode.ActiveTicketFixed;
            }
        }

        void OnDestroy()
        {
            foreach (var pair in listeners)
                if (pair.Key != null) pair.Key.RemoveListener(pair.Value);
            listeners.Clear();

            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (COC4AssessmentRun.IsActive || AssessmentMode.IsActive)
            {
                if (logGuide) Debug.Log("[COC4StepGuide] Assessment - the job order is the only instruction. Standing by.", this);
                enabled = false;
                return;
            }

            isHub = FindAnyObjectByType<TicketHubView>(FindObjectsInactive.Include) != null ||
                    (FindAnyObjectByType<TicketManager>(FindObjectsInactive.Include) != null && !RepairTicketMode.IsActive);
            isSite = !isHub && RepairTicketMode.IsActive;

            if (!isHub && !isSite)
            {
                if (logGuide) Debug.Log("[COC4StepGuide] Not the ticket board and no ticket is active - standing by.", this);
                enabled = false;
                return;
            }

            if (DialogManager.Instance == null)
                Debug.LogWarning("[COC4StepGuide] No DialogManager in this scene, so nothing can be shown.", this);

            if (isHub) SetUpHub();
            else SetUpSite();

            enabledAt = Time.unscaledTime;
        }

        void Update()
        {
            if (!started)
            {
                if (Time.unscaledTime - enabledAt < startDelay) return;
                started = true;

                if (isHub) OnHubReady();
                else OnSiteReady();
            }

            TrackQuietTime();

            if (isSite)
            {
                WatchDiagnosis();
                WatchStations();
            }

            if (Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + 0.25f;
                if (isHub) PollHub();
                else PollSite();
            }

            ShowNextMessage();
        }

        // ==================================================================
        // Ticket board
        // ==================================================================

        void SetUpHub()
        {
            manager = TicketManager.Instance != null
                ? TicketManager.Instance
                : FindAnyObjectByType<TicketManager>(FindObjectsInactive.Include);
            detailPanel = FindAnyObjectByType<TicketDetailPanelView>(FindObjectsInactive.Include);

            if (logGuide)
                Debug.Log("[COC4StepGuide] Ticket board guide running" +
                          (returned ? " - back from '" + returnedTicketId + "' (diagnosed " + returnedDiagnosed +
                                      ", fixed " + returnedFixed + ")." : "."), this);
        }

        void OnHubReady()
        {
            if (!hubGuide) return;

            if (returned && !string.IsNullOrEmpty(returnedTicketId))
            {
                TicketData t = TicketById(returnedTicketId);
                var vars = new Dictionary<string, string>
                {
                    { "ticket", t != null ? t.title : returnedTicketId },
                    { "cause", CauseOf(t) }
                };

                string[] lines;
                if (returnedFixed)
                {
                    int done, total;
                    CountTickets(out done, out total);
                    vars["done"] = done.ToString();
                    vars["total"] = total.ToString();
                    vars["left"] = (total - done).ToString();
                    lines = total > 0 && done >= total ? hubAllDoneLines : hubResolvedLines;

                    // The resolved ticket may have unlocked the next one: say so.
                    TicketData next = NextUnlockedTicket();
                    if (lines == hubResolvedLines && next != null && !string.IsNullOrWhiteSpace(hubUnlockedLine) && lines.Length > 0)
                    {
                        vars["next"] = next.title;
                        var withUnlock = new List<string>(lines);
                        withUnlock.Insert(withUnlock.Count - 1, hubUnlockedLine);
                        lines = withUnlock.ToArray();
                    }
                }
                else if (returnedDiagnosed)
                {
                    lines = hubDiagnosedOnlyLines;
                }
                else
                {
                    lines = hubUnfinishedLines;
                }

                string[] chosen = lines;
                Enqueue(new Message { key = "hub:return", result = true, build = () => Fill(chosen, vars) });

                // The overview is for someone arriving fresh; a learner coming back from a site has
                // already been through it.
                sessionKeys.Add("hub:overview");
                return;
            }

            if (sessionKeys.Contains("hub:overview")) return;

            Enqueue(new Message
            {
                key = "hub:overview",
                build = () =>
                {
                    sessionKeys.Add("hub:overview");
                    int done, total;
                    CountTickets(out done, out total);
                    var overview = new List<string>(hubOverviewLines ?? new string[0]);
                    if (manager != null && manager.UnlockInOrder && !string.IsNullOrWhiteSpace(hubOrderLine))
                        overview.Insert(Mathf.Max(0, overview.Count - 1), hubOrderLine);
                    return Fill(overview.ToArray(), new Dictionary<string, string> { { "total", total.ToString() } });
                }
            });
        }

        void PollHub()
        {
            if (!hubGuide || detailPanel == null) return;

            TicketData current = GetField<TicketData>(detailPanel, "_current");
            GameObject root = GetField<GameObject>(detailPanel, "panelRoot");
            bool open = current != null && (root == null ? detailPanel.gameObject.activeInHierarchy : root.activeInHierarchy);
            if (!open) return;

            TicketStatus status = Status(current);
            string key;
            string[] lines;

            // A locked ticket: say which one comes first, not "Start Ticket".
            TicketData blocking = manager != null ? manager.BlockingTicket(current) : null;
            if (blocking != null)
            {
                if (sessionKeys.Contains("hub:locked")) return;
                TicketData lockedOne = current;
                Enqueue(new Message
                {
                    key = "hub:locked",
                    build = () =>
                    {
                        sessionKeys.Add("hub:locked");
                        TicketData first = manager.BlockingTicket(lockedOne);
                        return Fill(hubLockedLines, new Dictionary<string, string>
                        {
                            { "ticket", lockedOne.title },
                            { "previous", first != null ? first.title : "the ticket before it" }
                        });
                    },
                    valid = () =>
                    {
                        TicketData now = GetField<TicketData>(detailPanel, "_current");
                        GameObject r = GetField<GameObject>(detailPanel, "panelRoot");
                        return now == lockedOne && (r == null || r.activeInHierarchy) && manager.BlockingTicket(lockedOne) != null;
                    }
                });
                return;
            }

            switch (status)
            {
                case TicketStatus.Open: key = "hub:start"; lines = hubStartLines; break;
                case TicketStatus.InProgress: key = "hub:go"; lines = hubGoLines; break;
                case TicketStatus.Diagnosed: key = "hub:goDiagnosed:" + current.ticketId; lines = hubGoDiagnosedLines; break;
                default: return;
            }

            if (sessionKeys.Contains(key)) return;

            TicketData showing = current;
            TicketStatus showingStatus = status;
            string[] chosen = lines;
            Enqueue(new Message
            {
                key = key,
                build = () =>
                {
                    sessionKeys.Add(key);
                    return Fill(chosen, new Dictionary<string, string>
                    {
                        { "ticket", showing.title },
                        { "cause", CauseOf(showing) }
                    });
                },
                // Only while that ticket is still on screen with that status.
                valid = () =>
                {
                    TicketData now = GetField<TicketData>(detailPanel, "_current");
                    GameObject r = GetField<GameObject>(detailPanel, "panelRoot");
                    bool stillOpen = now == showing && (r == null || r.activeInHierarchy);
                    return stillOpen && Status(showing) == showingStatus;
                }
            });
        }

        /// <summary>The first Open ticket that can be started now, or null.</summary>
        TicketData NextUnlockedTicket()
        {
            if (manager == null || !manager.UnlockInOrder) return null;
            foreach (TicketData t in manager.AllTickets)
                if (t != null && !string.IsNullOrEmpty(t.ticketId) && manager.GetStatus(t) == TicketStatus.Open && manager.IsUnlocked(t))
                    return t;
            return null;
        }

        TicketStatus Status(TicketData t)
        {
            if (t == null) return TicketStatus.Open;
            return manager != null ? manager.GetStatus(t) : TicketSessionState.Get(t.ticketId);
        }

        TicketData TicketById(string id)
        {
            if (manager == null || string.IsNullOrEmpty(id)) return null;
            foreach (TicketData t in manager.AllTickets)
                if (t != null && t.ticketId == id) return t;
            return null;
        }

        void CountTickets(out int done, out int total)
        {
            done = 0;
            total = 0;
            if (manager == null) return;
            foreach (TicketData t in manager.AllTickets)
            {
                if (t == null || string.IsNullOrEmpty(t.ticketId)) continue;
                total++;
                if (manager.GetStatus(t) == TicketStatus.Resolved) done++;
            }
        }

        static string CauseOf(TicketData t)
        {
            if (t == null || t.diagnosisOptions == null) return "the cause you named";
            foreach (TicketData.DiagnosisOption o in t.diagnosisOptions)
                if (o != null && o.isCorrect && !string.IsNullOrWhiteSpace(o.label)) return LowerFirst(o.label.Trim());
            return "the cause you named";
        }

        // ==================================================================
        // Repair site - setup
        // ==================================================================

        void SetUpSite()
        {
            ticket = RepairTicketMode.ActiveTicket;
            if (ticket != null) ServiceCallState.EnsureTicket(ticket.ticketId);

            power = FindAnyObjectByType<PCPowerStation>(FindObjectsInactive.Include);
            coordinator = FindAnyObjectByType<TicketFaultCoordinator>(FindObjectsInactive.Include);
            diagnosisPanel = FindAnyObjectByType<TicketDiagnosisPanel>(FindObjectsInactive.Include);
            observationPanel = FindAnyObjectByType<SiteObservationPanel>(FindObjectsInactive.Include);
            toolBelt = ToolBelt.Instance != null ? ToolBelt.Instance : FindAnyObjectByType<ToolBelt>(FindObjectsInactive.Include);
            autoReturn = FindAnyObjectByType<AutoReturnToHub>(FindObjectsInactive.Include);

            foreach (TicketFault f in FindObjectsByType<TicketFault>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (f != null && f.IsActiveForThisVisit) faults.Add(f);

            FindStations();

            if (power != null)
            {
                string id = GetField<string>(power, "observationId");
                if (!string.IsNullOrEmpty(id)) powerObservationId = id;
                Listen(power.onBootSucceeded, OnBootSucceeded);
                Listen(power.onBootFailed, OnBootFailed);
            }
            if (unit != null)
            {
                string id = GetField<string>(unit, "observationId");
                unitObservationId = id ?? "";
            }

            diagnosedAtStart = RepairTicketMode.IsDiagnosed;
            lastDiagnosed = diagnosedAtStart;
            lastAttempts = ServiceCallState.DiagnosisAttempts;

            if (logGuide)
            {
                var kinds = new List<string>();
                foreach (TicketFault f in faults) kinds.Add(f.GetType().Name);
                Debug.Log("[COC4StepGuide] Repair-site guide running for '" + RepairTicketMode.ActiveTicketId + "'. " +
                          "Monitor: " + (monitor != null ? monitor.name : "none") +
                          ", system unit: " + (unit != null ? unit.name : "none") +
                          ", power-on test: " + (power != null ? "yes" : "no") +
                          ", diagnosis panel: " + (diagnosisPanel != null ? "yes" : "NO") +
                          ", faults: " + (kinds.Count > 0 ? string.Join(", ", kinds.ToArray()) : "none") +
                          (diagnosedAtStart ? ". Already diagnosed - a return visit." : "."), this);

                if (unit != null && unit.IsLockedByDiagnosis && power != null && diagnosisPanel != null &&
                    !ExaminePanelInUse() && MinimumObservations() > 1)
                    Debug.LogWarning("[COC4StepGuide] The system unit stays shut until the cause is named, but the diagnosis " +
                                     "needs " + MinimumObservations() + " observations and only the power-on test can give one here. " +
                                     "Untick Require Diagnosis To Enter on '" + unit.name + "', or lower Minimum Observations " +
                                     "on the diagnosis panel - otherwise the learner cannot finish.", this);
            }
        }

        void FindStations()
        {
            RepairStation[] stations = FindObjectsByType<RepairStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (power != null)
            {
                monitor = GetField<RepairStation>(power, "monitorStation");
                unit = GetField<RepairStation>(power, "systemUnitStation");
            }
            if (unit == null && toolBelt != null) unit = GetField<RepairStation>(toolBelt, "depositStation");

            if (monitor == null)
                foreach (RepairStation s in stations)
                    if (s != null && s != unit && NameHas(s, "monitor", "screen", "display")) { monitor = s; break; }

            if (unit == null)
                foreach (RepairStation s in stations)
                {
                    if (s == null || s == monitor) continue;
                    if (GetField<bool>(s, "requireDiagnosisToEnter") || GetField<Transform>(s, "movedObject") != null ||
                        NameHas(s, "unit", "case", "tower", "system")) { unit = s; break; }
                }

            // Two stations and only one identified: the other one is the other.
            if (stations.Length == 2)
            {
                if (monitor == null && unit != null) monitor = stations[0] == unit ? stations[1] : stations[0];
                if (unit == null && monitor != null) unit = stations[0] == monitor ? stations[1] : stations[0];
            }
        }

        static bool NameHas(RepairStation s, params string[] words)
        {
            string n = ((s.StationName ?? "") + " " + s.name).ToLowerInvariant();
            foreach (string w in words) if (n.Contains(w)) return true;
            return false;
        }

        void Listen(UnityEvent evt, UnityAction action)
        {
            if (evt == null) return;
            evt.AddListener(action);
            listeners.Add(new KeyValuePair<UnityEvent, UnityAction>(evt, action));
        }

        // ==================================================================
        // Repair site - the step the learner is on
        // ==================================================================

        bool PoweredOnce =>
            power != null && (ServiceCallState.HasObserved(powerObservationId) || ServiceCallState.VerificationRun ||
                              power.HasPassedBootTest);

        bool AllRepaired
        {
            get
            {
                if (faults.Count == 0) return coordinator != null && coordinator.AllFaultsCurrentlyRepaired;
                foreach (TicketFault f in faults) if (f == null || !f.IsRepaired()) return false;
                return true;
            }
        }

        Stage CurrentStage()
        {
            bool diagnosed = RepairTicketMode.IsDiagnosed;

            if (RepairTicketMode.ActiveTicketFixed && (power == null || power.CanCloseTicket)) return Stage.Done;

            if (!diagnosed)
            {
                if (power != null && !PoweredOnce) return Stage.Confirm;
                if (power != null && power.HasPassedBootTest) return Stage.Diagnose;   // works, cause not named
                if (ServiceCallState.ObservationCount < MinimumObservations() && CanStillExamine()) return Stage.Examine;
                return Stage.Diagnose;
            }

            if (!AllRepaired)
            {
                if (NeedsTool() != null) return Stage.GetTool;
                return Stage.Repair;
            }

            if (power != null && !power.HasPassedBootTest) return Stage.Test;
            return Stage.Done;
        }

        /// <summary>True while there is still a way to gather an observation.</summary>
        bool CanStillExamine()
        {
            if (unit != null && !unit.IsLockedByDiagnosis && !string.IsNullOrEmpty(unitObservationId) &&
                !ServiceCallState.HasObserved(unitObservationId)) return true;
            return ExaminePanelInUse();
        }

        int MinimumObservations()
        {
            if (diagnosisPanel == null) return 0;
            return GetField<int>(diagnosisPanel, "minimumObservations", 2);
        }

        bool ExaminePanelInUse()
        {
            if (observationPanel == null || ticket == null) return false;

            // A panel retired at a hardware site sits under a switched-off parent.
            Transform parent = observationPanel.transform.parent;
            if (parent != null && !parent.gameObject.activeInHierarchy) return false;

            var sets = GetField<List<TicketObservationSet>>(observationPanel, "observationSets");
            if (sets == null) return false;
            foreach (TicketObservationSet s in sets)
                if (s != null && s.ticketId == ticket.ticketId) return true;
            return false;
        }

        /// <summary>A cleaning tool the dust still needs and the learner has not picked up, or null.</summary>
        ToolPickup NeedsTool()
        {
            bool dustLeft = false;
            foreach (TicketFault f in faults)
                if (f is DustFault && !f.IsRepaired()) { dustLeft = true; break; }
            if (!dustLeft) return null;

            ToolPickup nearestFree = null;
            foreach (ToolPickup t in ToolPickup.All)
            {
                if (t == null || t.Item == null || !CleaningTool.IsTool(t.Item)) continue;
                if (t.IsCarried || t.IsInHotbar) return null;       // already has one
                if (nearestFree == null) nearestFree = t;
            }
            return nearestFree;
        }

        void PollSite()
        {
            Stage stage = CurrentStage();
            if (stage == lastStage) return;

            Stage previous = lastStage;
            lastStage = stage;

            if (logGuide) Debug.Log("[COC4StepGuide] Step: " + stage + (previous != Stage.None ? " (was " + previous + ")" : "") + ".", this);

            PointArrowFor(stage);

            // A site with no power-on test closes the moment the fault is fixed.
            if (stage == Stage.Done && power == null && siteReplies && RepairTicketMode.ActiveTicketFixed)
                Enqueue(new Message
                {
                    key = "fixedNoTest",
                    result = true,
                    build = () => Fill(fixedNoTestLines, new Dictionary<string, string> { { "back", BackLine() } })
                });

            // Until the arrival message has gone up, it will carry the current step itself.
            if (!arrivalShown) return;

            AnnounceStage(stage);
        }

        void AnnounceStage(Stage stage)
        {
            if (!siteSteps) return;
            if (stage == Stage.None || stage == Stage.Done) return;   // Done is a reply to the test

            Stage s = stage;
            Enqueue(new Message
            {
                key = "stage:" + stage,
                build = () => StageLines(s),
                valid = () => CurrentStage() == s,
                ready = SiteQuietForStep
            });
        }

        /// <summary>Step messages wait for the boot to finish and the diagnosis panel to close.</summary>
        bool SiteQuietForStep()
        {
            if (COC4TicketIntroPanel.IsShowing) return false;
            if (power != null && power.IsBooting) return false;
            if (diagnosisPanel != null && diagnosisPanel.IsOpen) return false;
            if (DragController.Instance != null && DragController.Instance.IsDragging) return false;
            return true;
        }

        void OnSiteReady()
        {
            arrivalShown = true;
            Stage stage = CurrentStage();
            lastStage = stage;
            PointArrowFor(stage);

            string id = RepairTicketMode.ActiveTicketId ?? "";
            bool firstTime = !plannedTickets.Contains(id);

            if (COC4TicketIntroPanel.HandlesArrival)
            {
                // The ticket intro panel is the arrival: the report, the plan and (on a return visit)
                // the cause are on it. Once it is closed, only the step itself is left to say - and
                // not even that for the very first step of a job, which the panel spells out in full
                // (step 1, or the diagnosis at a network site, which has no power-on test).
                plannedTickets.Add(id);
                bool panelSaidIt = stage == Stage.Confirm || (power == null && stage == Stage.Diagnose);
                if (panelSaidIt) shownKeys.Add("stage:" + stage);
                else AnnounceStage(stage);
                return;
            }

            Enqueue(new Message
            {
                key = "arrival",
                build = () =>
                {
                    plannedTickets.Add(id);
                    // The arrival report points the arrow at the monitor when it closes; put it where
                    // this visit actually starts (the system unit, on a return visit).
                    PointArrowFor(CurrentStage());
                    var lines = new List<string>();
                    if (diagnosedAtStart)
                        lines.AddRange(Fill(siteReturnLines, new Dictionary<string, string> { { "cause", CauseOf(ticket) } }));
                    else if (firstTime && siteSteps)
                        lines.AddRange(Fill(power != null ? sitePlanLines : sitePlanNetworkLines, null));

                    Stage now = CurrentStage();
                    lastStage = now;
                    if (siteSteps && now != Stage.Done && now != Stage.None)
                    {
                        lines.AddRange(StageLines(now));
                        shownKeys.Add("stage:" + now);
                    }
                    return lines;
                },
                ready = SiteQuietForStep
            });
        }

        List<string> StageLines(Stage stage)
        {
            var vars = new Dictionary<string, string>();
            var lines = new List<string>();

            string causeLine = null;
            if (causeLinePending && (stage == Stage.GetTool || stage == Stage.Repair || stage == Stage.Test))
            {
                causeLinePending = false;
                causeLine = Fill(diagnosisRightLine, new Dictionary<string, string> { { "cause", CauseOf(ticket) } });
            }

            bool atMonitor = monitor != null && RepairStation.Current == monitor;
            bool inUnit = unit != null && RepairStation.Current == unit;

            switch (stage)
            {
                case Stage.Confirm:
                    lines.AddRange(Fill(stepConfirmLines, vars));
                    break;

                case Stage.Examine:
                    if (unit != null && !unit.IsLockedByDiagnosis && !inUnit)
                    {
                        vars["toUnit"] = (atMonitor ? "{Leave} to step back from the monitor, then walk" : "Walk") +
                                         " to the system unit{arrow} and {interact} to open it up.";
                        List<string> ex = Fill(stepExamineLines, vars);
                        AddSafetyOnce(ex, ex.Count);
                        ex.Add(Fill(lookLine, vars));
                        lines.AddRange(ex);
                    }
                    else if (inUnit)
                    {
                        vars["toUnit"] = "";
                        List<string> ex = Fill(stepExamineLines, vars);
                        AddSafetyOnce(ex, ex.Count);
                        ex.Add(Fill(lookLine, vars));
                        lines.AddRange(ex);
                    }
                    else
                    {
                        lines.AddRange(Fill(stepExaminePanelLines, vars));
                    }
                    break;

                case Stage.Diagnose:
                    vars["penalty"] = PenaltySeconds().ToString();
                    if (power != null && power.HasPassedBootTest)
                    {
                        lines.AddRange(Fill(passedUndiagnosedLines, vars));
                        break;
                    }
                    if (inUnit)
                    {
                        // Went straight in before the Examine step could be shown.
                        AddSafetyOnce(lines, lines.Count);
                        if (!shownKeys.Contains("stage:" + Stage.Examine)) lines.Add(Fill(lookLine, vars));
                    }
                    lines.AddRange(Fill(unit != null && unit.IsLockedByDiagnosis ? stepDiagnoseLockedLines : stepDiagnoseLines, vars));
                    break;

                case Stage.GetTool:
                    {
                        ToolPickup tool = NeedsTool();
                        vars["tool"] = tool != null ? tool.DisplayName : "the cleaning brush";
                        vars["findIt"] = inUnit || atMonitor ? "{Leave} to step back, then find it" : "Find it";
                        lines.AddRange(Fill(stepGetToolLines, vars));
                        break;
                    }

                case Stage.Repair:
                    lines.AddRange(RepairLines(inUnit));
                    break;

                case Stage.Test:
                    vars["toMonitor"] = inUnit
                        ? "{Leave} to step back - the case closes up - then go to the monitor{arrow}, {interact}, then {click} POWER."
                        : atMonitor
                            ? "{Click} POWER and watch it all the way through."
                            : "Go to the monitor{arrow}, {interact}, then {click} POWER.";
                    lines.AddRange(Fill(stepTestLines, vars));
                    break;
            }

            PrefixFirst(lines, causeLine);
            return lines;
        }

        /// <summary>Puts a short sentence on the same page as the first line, rather than on a page of its own.</summary>
        static void PrefixFirst(List<string> lines, string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return;
            if (lines.Count == 0) lines.Add(prefix.Trim());
            else lines[0] = prefix.Trim() + " " + lines[0];
        }

        List<string> RepairLines(bool inUnit)
        {
            var lines = new List<string>();
            bool hardware = false;
            var parts = new List<string>();

            foreach (TicketFault f in faults)
            {
                if (f == null || f.IsRepaired()) continue;

                if (f is PartRemovedFault)
                {
                    hardware = true;
                    PCPartItem[] removed = GetField<PCPartItem[]>(f, "partsToRemove");
                    string part = PartName(removed);
                    parts.Add(Fill(repairPartLine, new Dictionary<string, string> { { "part", part } }));
                    if (IsType(removed, PartType.RAM)) parts.Add(Fill(repairRamLine, null));
                }
                else if (f is CableUnplugFault)
                {
                    hardware = true;
                    CableConnector cable = GetField<CableConnector>(f, "cable");
                    parts.Add(Fill(repairCableLine, new Dictionary<string, string> { { "cable", CableName(cable) } }));
                }
                else if (f is DustFault)
                {
                    hardware = true;
                    ToolPickup tool = HeldCleaningTool();
                    string toolName = tool != null ? tool.DisplayName : "the brush";
                    parts.Add(Fill(repairDustLine, new Dictionary<string, string> { { "tool", toolName } }));
                }
                else if (f is NetworkFault)
                {
                    NetworkedWorkstation station = GetField<NetworkedWorkstation>(f, "station");
                    NetworkedWorkstation target = GetField<NetworkedWorkstation>(f, "pingTarget");
                    parts.Add(Fill(repairNetworkLine, new Dictionary<string, string>
                    {
                        { "host", HostName(station, "this computer") },
                        { "target", HostName(target, "the server") },
                        { "targetIp", TargetAddress(target) }
                    }));
                }
                else
                {
                    parts.Add(Fill(repairOtherLine, null));
                }
            }

            if (hardware && unit != null && !inUnit)
                lines.Add(Fill(repairOpenCaseLine, null));
            if (hardware) AddSafetyOnce(lines, lines.Count);

            lines.AddRange(parts);
            PrefixFirst(lines, Fill(repairHeading, null));
            return lines;
        }

        /// <summary>The safety line, the first time the learner is about to be inside a case this session.</summary>
        void AddSafetyOnce(List<string> lines, int index)
        {
            if (string.IsNullOrWhiteSpace(safetyLine) || sessionKeys.Contains("safety")) return;
            sessionKeys.Add("safety");
            string line = Fill(safetyLine, null);
            lines.Insert(Mathf.Clamp(index, 0, lines.Count), line);
        }

        ToolPickup HeldCleaningTool()
        {
            foreach (ToolPickup t in ToolPickup.All)
                if (t != null && t.Item != null && CleaningTool.IsTool(t.Item) && (t.IsCarried || t.IsInHotbar)) return t;
            return null;
        }

        int PenaltySeconds()
        {
            if (diagnosisPanel == null) return 60;
            return GetField<int>(diagnosisPanel, "wrongAnswerPenaltySeconds", 60);
        }

        // ==================================================================
        // Repair site - replies
        // ==================================================================

        void OnBootFailed()
        {
            if (!siteReplies || power == null) return;

            // Works, but the cause was never named.
            if (power.HasPassedBootTest && !RepairTicketMode.IsDiagnosed)
            {
                var vars = new Dictionary<string, string>();
                Enqueue(new Message
                {
                    key = "passedUndiagnosed",
                    result = true,
                    build = () => Fill(passedUndiagnosedLines, vars),
                    valid = () => !RepairTicketMode.IsDiagnosed
                });
                shownKeys.Add("stage:" + Stage.Diagnose);   // the reply says the same thing
                return;
            }

            bool afterRepair = coordinator != null && coordinator.RepairedFaultCount > 0;
            string symptom = SymptomNow();

            if (afterRepair)
            {
                // The repair step comes again, with only what is still left to do.
                shownKeys.Remove("stage:" + Stage.Repair);
                shownKeys.Remove("stage:" + Stage.GetTool);
                Enqueue(new Message
                {
                    key = "testFailed",
                    once = false,
                    result = true,
                    build = () => WithNextStep(Fill(testFailedLines, new Dictionary<string, string> { { "symptom", symptom } }))
                });
                return;
            }

            // The first look at the fault. Later power-ons before any repair add nothing new.
            Enqueue(new Message
            {
                key = "symptom",
                result = true,
                build = () => WithNextStep(Fill(symptomLines, new Dictionary<string, string> { { "symptom", symptom } }))
            });
        }

        /// <summary>A reply, followed on the same message by the step it leads to - one popup, not two.</summary>
        List<string> WithNextStep(List<string> lines)
        {
            Stage now = CurrentStage();
            string key = "stage:" + now;
            if (siteSteps && now != Stage.None && now != Stage.Done && !shownKeys.Contains(key))
            {
                lines.AddRange(StageLines(now));
                shownKeys.Add(key);
                if (now != lastStage)
                {
                    lastStage = now;
                    PointArrowFor(now);   // the message says "follow the arrow" - it has to be there already
                }
            }
            return lines;
        }

        void OnBootSucceeded()
        {
            if (!siteReplies || power == null || resultShownForPass) return;
            if (!power.CanCloseTicket) return;

            resultShownForPass = true;
            PointArrowFor(Stage.Done);

            Enqueue(new Message
            {
                key = "testPassed",
                result = true,
                build = () => Fill(testPassedLines, new Dictionary<string, string> { { "back", BackLine() } })
            });
        }

        string BackLine()
        {
            bool automatic = autoReturn != null && autoReturn.enabled &&
                             GetField<bool>(autoReturn, "enabledForThisScene", true);
            return Fill(automatic ? backAutomatic : backButton, null);
        }

        /// <summary>What the machine does right now - the same Boot Symptom text the monitor printed.</summary>
        string SymptomNow()
        {
            var blocking = new List<string>();
            var afterBoot = new List<string>();
            string beep = "";

            foreach (TicketFault f in faults)
            {
                if (f == null || f.IsRepaired()) continue;
                if (f.PreventsBoot)
                {
                    AddUnique(blocking, f.BootSymptom);
                    if (beep.Length == 0 && !string.IsNullOrEmpty(f.BeepCode)) beep = f.BeepCode;
                }
                else AddUnique(afterBoot, f.BootSymptom);
            }

            // The faults' own Boot Symptom text - exactly what the monitor printed, never the cause.
            var sb = new StringBuilder();
            if (blocking.Count > 0)
            {
                sb.Append(string.Join(" ", blocking.ToArray()));
                if (beep.Length > 0) sb.Append(" Beep code: " + beep + ".");
            }
            else if (afterBoot.Count > 0)
            {
                sb.Append(string.Join(" ", afterBoot.ToArray()));
            }
            else
            {
                sb.Append("The computer does not start up correctly.");
            }
            return sb.ToString().Trim();
        }

        static void AddUnique(List<string> list, string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            s = s.Trim();
            if (!list.Contains(s)) list.Add(s);
        }

        void WatchDiagnosis()
        {
            if (!siteReplies) return;

            bool diagnosed = RepairTicketMode.IsDiagnosed;
            int attempts = ServiceCallState.DiagnosisAttempts;

            if (diagnosed && !lastDiagnosed)
            {
                // Said in front of the next step (Repair, fetch the tool, or Test).
                causeLinePending = true;
                if (!siteSteps)
                    Enqueue(new Message
                    {
                        key = "diagnosisRight",
                        result = true,
                        build = () => new List<string> { Fill(diagnosisRightLine, new Dictionary<string, string> { { "cause", CauseOf(ticket) } }) },
                        ready = SiteQuietForStep
                    });
            }
            else if (!diagnosed && attempts > lastAttempts)
            {
                bool first = !wrongAnswerSeen;
                wrongAnswerSeen = true;
                string[] lines = first ? diagnosisWrongLines : diagnosisWrongAgainLines;
                Enqueue(new Message
                {
                    key = first ? "diagnosisWrong" : "diagnosisWrongAgain",
                    once = first,
                    result = true,
                    build = () => Fill(lines, null),
                    valid = () => !RepairTicketMode.IsDiagnosed,
                    // The explanation is on the panel; this waits until the learner has read it.
                    ready = () => diagnosisPanel == null || !diagnosisPanel.IsOpen
                });
            }

            lastDiagnosed = diagnosed;
            lastAttempts = attempts;
        }

        void WatchStations()
        {
            RepairStation current = RepairStation.Current;
            if (current == lastCurrent) return;
            lastCurrent = current;

            if (!siteReplies || current == null) return;

            // Straight into the case without ever switching the machine on.
            if (current == unit && power != null && !PoweredOnce && !RepairTicketMode.IsDiagnosed)
            {
                Enqueue(new Message
                {
                    key = "earlyCase",
                    result = true,
                    build = () => Fill(earlyCaseLines, null),
                    valid = () => !PoweredOnce
                });
            }
        }

        // ==================================================================
        // The waypoint arrow
        // ==================================================================

        /// <summary>
        /// The step (1 to 5) the learner is on at this repair site, 6 once the job is done, or 0 on the
        /// ticket board and before the guide has set up. Read by the ticket intro panel.
        /// </summary>
        public int CurrentStepNumber
        {
            get
            {
                if (!isSite) return 0;
                switch (CurrentStage())
                {
                    case Stage.Confirm: return 1;
                    case Stage.Examine: return 2;
                    case Stage.Diagnose: return 3;
                    case Stage.GetTool:
                    case Stage.Repair: return 4;
                    case Stage.Test: return 5;
                    case Stage.Done: return 6;
                    default: return 0;
                }
            }
        }

        /// <summary>Points the waypoint arrow at the place for the current step. False if the guide does not handle the arrow here.</summary>
        public bool PointArrowAtCurrentStep()
        {
            if (!isSite || !pointArrow || WaypointIndicator.Instance == null) return false;
            PointArrowFor(CurrentStage());
            return true;
        }

        /// <summary>
        /// Where the current step happens - the monitor, the system unit, the tool to fetch, the network
        /// PC - or null when there is nowhere to walk to (diagnosing, or the job is done). Read by
        /// COC4WaypointBridge to point the TaskWaypointArrow.
        /// </summary>
        public Transform CurrentTarget
        {
            get { return isSite ? TargetFor(CurrentStage()) : null; }
        }

        /// <summary>"Confirm", "Examine", "Diagnose", "GetTool", "Repair", "Test" or "Done"; empty when not at a repair site.</summary>
        public string CurrentStepName
        {
            get { return isSite ? CurrentStage().ToString() : ""; }
        }

        Transform TargetFor(Stage stage)
        {
            switch (stage)
            {
                case Stage.Confirm:
                case Stage.Test:
                    return StationPoint(monitor);
                case Stage.Examine:
                    return unit != null && !unit.IsLockedByDiagnosis ? StationPoint(unit) : null;
                case Stage.GetTool:
                    {
                        ToolPickup tool = NeedsTool();
                        return tool != null ? tool.transform : null;
                    }
                case Stage.Repair:
                    {
                        Transform target = NetworkTarget();
                        return target != null ? target : StationPoint(unit);
                    }
                case Stage.Diagnose:
                    // At a network site the evidence is on the client PC, so that is where to go.
                    // At a hardware site the diagnosis is made from where the learner already is.
                    return power == null ? NetworkTarget() : null;
                default:
                    return null;   // Done, None
            }
        }

        void PointArrowFor(Stage stage)
        {
            if (!pointArrow || WaypointIndicator.Instance == null) return;

            if (stage == Stage.Done)
            {
                WaypointIndicator.Instance.ClearTarget();
                return;
            }

            Transform target = TargetFor(stage);
            if (target != null) WaypointIndicator.Instance.SetTarget(target);
            else if (stage == Stage.Diagnose) WaypointIndicator.Instance.ClearTarget();
        }

        static Transform StationPoint(RepairStation s)
        {
            if (s == null) return null;
            Transform p = GetField<Transform>(s, "stationPoint");
            return p != null ? p : s.transform;
        }

        Transform NetworkTarget()
        {
            foreach (TicketFault f in faults)
            {
                if (!(f is NetworkFault) || f.IsRepaired()) continue;
                NetworkedWorkstation w = GetField<NetworkedWorkstation>(f, "station");
                if (w != null) return w.transform;
            }
            return null;
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
                    pending.RemoveAt(i);         // a newer copy replaces it
                }
            }

            m.queuedAt = Time.unscaledTime;
            pending.Add(m);
        }

        void TrackQuietTime()
        {
            DialogManager dm = DialogManager.Instance;
            bool dialog = dm != null && (dm.IsDialogOpen || dm.WasDialogClosedThisFrame);
            if (dialog) quietSince = -1f;
            else if (quietSince < 0f) quietSince = Time.unscaledTime;
        }

        void ShowNextMessage()
        {
            if (pending.Count == 0) return;

            // Nothing goes up behind the ticket intro panel.
            if (COC4TicketIntroPanel.IsShowing) return;

            DialogManager dm = DialogManager.Instance;
            if (dm == null || dm.IsDialogOpen || dm.WasDialogClosedThisFrame) return;

            for (int i = 0; i < pending.Count; i++)
            {
                Message m = pending[i];

                bool alreadyShown = m.once && !string.IsNullOrEmpty(m.key) && shownKeys.Contains(m.key);
                if (alreadyShown || (m.valid != null && !m.valid()))
                {
                    pending.RemoveAt(i);
                    i--;
                    continue;
                }

                if (m.ready != null && !m.ready()) continue;

                // Replies go up as soon as the box is free; steps wait for a moment of quiet.
                if (!m.result && (quietSince < 0f || Time.unscaledTime - quietSince < settleSeconds)) continue;

                pending.RemoveAt(i);
                Show(m);
                return;
            }
        }

        void Show(Message m)
        {
            if (m.once && !string.IsNullOrEmpty(m.key)) shownKeys.Add(m.key);

            List<string> lines = m.build();
            if (lines == null || lines.Count == 0) return;

            DialogSequence sequence = ScriptableObject.CreateInstance<DialogSequence>();
            sequence.name = "COC4StepGuide " + (m.key ?? "message");
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line) || line.Trim().Length == 0) continue;
                sequence.lines.Add(new DialogLine { speakerName = speakerName, text = line.Trim() });
            }

            if (sequence.lines.Count == 0) { Destroy(sequence); return; }

            if (logGuide) Debug.Log("[COC4StepGuide] Showing '" + (m.key ?? "message") + "' (" +
                                    sequence.lines.Count + " line(s)).", this);

            DialogManager.Instance.ShowDialog(sequence, () => Destroy(sequence));
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

        /// <summary>Replaces {tokens}, twice, so a token may hold another ({toUnit} holds {interact}).</summary>
        string Fill(string line, Dictionary<string, string> vars)
        {
            if (string.IsNullOrEmpty(line)) return line;

            string result = line;
            Dictionary<string, string> common = CommonVars();
            for (int pass = 0; pass < 2; pass++)
            {
                if (vars != null)
                    foreach (var pair in vars) result = Replace(result, pair.Key, pair.Value);
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

        /// <summary>The control words for this device (shared with the ticket intro panel), plus {arrow}.</summary>
        Dictionary<string, string> CommonVars()
        {
            Dictionary<string, string> vars = COC4Controls.Tokens(unit != null ? unit : monitor, toolBelt);
            bool arrowShown = (pointArrow && WaypointIndicator.Instance != null) || COC4WaypointBridge.IsDriving;
            vars["arrow"] = arrowShown && isSite ? " (follow the arrow)" : "";
            return vars;
        }

        static string PartName(PCPartItem[] parts)
        {
            if (parts == null) return "the part";
            PCPartItem first = null;
            int count = 0;
            foreach (PCPartItem p in parts) if (p != null) { if (first == null) first = p; count++; }
            if (first == null) return "the part";

            bool plural = count > 1;
            switch (first.partType)
            {
                case PartType.RAM: return plural ? "the memory modules (RAM)" : "the memory module (RAM)";
                case PartType.CPU: return "the processor (CPU)";
                case PartType.GPU: return "the graphics card";
                case PartType.PSU: return "the power supply";
                case PartType.Storage:
                case PartType.SSD: return plural ? "the storage drives" : "the storage drive";
                case PartType.Cooler: return "the CPU cooler";
                case PartType.DataCable: return "the data cable";
                default: return plural ? "the parts" : "the part";
            }
        }

        static bool IsType(PCPartItem[] parts, PartType type)
        {
            if (parts == null) return false;
            foreach (PCPartItem p in parts) if (p != null && p.partType == type) return true;
            return false;
        }

        static string CableName(CableConnector cable)
        {
            if (cable == null) return "a cable";
            switch (cable.cableType)
            {
                case CableType.ATX24: return "the 24-pin motherboard power cable";
                case CableType.EPS8: return "the 8-pin CPU power cable";
                case CableType.PCIe6:
                case CableType.PCIe8: return "the graphics card power cable";
                case CableType.SATA: return "the SATA cable";
                case CableType.PowerCable: return "the power cable";
                default: return "a cable";
            }
        }

        static string HostName(NetworkedWorkstation w, string fallback)
        {
            if (w == null || w.Host == null || string.IsNullOrWhiteSpace(w.Host.HostName)) return fallback;
            return w.Host.HostName.Trim();
        }

        static string TargetAddress(NetworkedWorkstation w)
        {
            if (w == null || w.Host == null) return "the server";
            string ip = w.Host.Address.ToString();
            return string.IsNullOrEmpty(ip) || ip == "0.0.0.0" ? HostName(w, "the server") : ip;
        }

        static string LowerFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Keep acronyms ("RAM ...", "PSU ...") as they are.
            if (s.Length > 1 && char.IsUpper(s[1])) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        // ==================================================================
        // Reading private fields - the COC 4 scripts are left untouched
        // ==================================================================

        static readonly Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();

        static T GetField<T>(object target, string field, T fallback = default(T))
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