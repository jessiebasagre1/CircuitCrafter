// COC2StageDialogs.cs
using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Every tutorial popup in COC2, in one component.
    ///
    /// It does two jobs:
    ///
    ///   1. TUTORIAL DIALOGS. Watches the four stage view controllers and, the first time each
    ///      stage opens, explains what the stage is and what the tools are. Stripping, wiring,
    ///      crimping and testing each get their own briefing.
    ///
    ///   2. RESULT DIALOGS. Listens for the tester's verdict and shows a pass or fail message.
    ///      On a fail it also names the actual fault (open conductor, short, wrong pinout - read
    ///      from CableTestAnalyzer, not guessed) and then hands off to COC2ProcedureReset, which
    ///      winds the whole procedure back to stripping.
    ///
    ///   3. PATCH CORDS. The plugging phases after the punch-down get their own briefing, and their
    ///      pass/fail messages talk about patch cords - not about conductors and IDC contacts.
    ///
    /// Nothing here edits an existing script. It detects stages by polling the InStrippingView /
    /// InCrimpingView / InCrimpView / InTesterView properties those controllers already expose, so
    /// this is add-and-assign only.
    ///
    /// THE TEXT IS ALREADY WRITTEN. Every stage below ships with its lines filled in, including
    /// the equipment definitions, so this works the moment you add the component - no
    /// DialogSequence assets to create. Edit the text straight in the Inspector if you want to
    /// reword it. If you'd rather use a real DialogSequence asset for a stage (for reuse or
    /// translation), drop it in that stage's "Asset" slot and it wins over the typed lines.
    ///
    /// Setup:
    ///   1. Add this to the always-active Managers object, next to DialogManager.
    ///   2. Add COC2ProcedureReset to the same object and drag it into Procedure Reset below.
    ///   3. That's it. Press Play.
    /// </summary>
    public class COC2StageDialogs : MonoBehaviour
    {
        // PatchCords added at the END - the enum is not serialised, but keeping the order stable
        // keeps the shown[] slots meaning the same thing.
        enum Stage { None, Stripping, Wiring, Crimping, Testing, PatchPanel, PatchCords }

        const int StageCount = 7;

        public static COC2StageDialogs Instance { get; private set; }

        [Header("Speaker")]
        [Tooltip("Name shown at the top of every one of these popups.")]
        public string speakerName = "Instructor";

        // ------------------------------------------------------------------
        // Stage 1 - Stripping
        // ------------------------------------------------------------------

        [Header("Stage 1 - Stripping")]
        [Tooltip("Optional. A DialogSequence asset used INSTEAD of the lines below.")]
        public DialogSequence strippingAsset;
        [TextArea(2, 6)]
        public string[] strippingLines =
        {
            "Stage 1 of 4: stripping. Before you touch anything, know what you're holding.",

            "The UTP cable is four twisted pairs of copper - eight conductors in total - inside one " +
            "outer jacket. UTP stands for Unshielded Twisted Pair. The twisting is what cancels out " +
            "electrical interference, so never untwist more of it than you have to.",

            "The crimping tool is really three tools in one: a cutter blade, a stripper notch, and " +
            "the die that presses an RJ45 connector shut. For this stage you only need the stripper " +
            "notch.",

            "Stripping means taking about one inch of the outer jacket off WITHOUT nicking the eight " +
            "conductors underneath. A nicked conductor will still pass a continuity test today and " +
            "break in the ceiling six months from now.",

            "Drag the crimping tool onto the end of the cable to strip it."
        };

        // ------------------------------------------------------------------
        // Stage 2 - Wiring
        // ------------------------------------------------------------------

        [Header("Stage 2 - Wiring")]
        public DialogSequence wiringAsset;
        [TextArea(2, 6)]
        public string[] wiringLines =
        {
            "Stage 2 of 4: arranging the conductors.",

            "The RJ45 is the eight-position, eight-contact plug on the end of a network cable - " +
            "eight pins, eight tiny gold blades. Hold it with the locking clip facing away from you " +
            "and the pins toward you: pin 1 is on the left, pin 8 on the right.",

            "T568B, pin 1 through 8: white-orange, orange, white-green, blue, white-blue, green, " +
            "white-brown, brown. T568A is the same list with the orange and green pairs swapped.",

            "A STRAIGHT-THROUGH cable uses the same standard on both ends - that's PC to switch, or " +
            "PC to router. A CROSSOVER uses T568A on one end and T568B on the other - that's PC to " +
            "PC, or switch to switch.",

            "Untwist only as far as the connector, flatten the conductors into order, and drag each " +
            "one into its pin. Both ends need all eight before you can crimp."
        };

        // ------------------------------------------------------------------
        // Stage 3 - Crimping
        // ------------------------------------------------------------------

        [Header("Stage 3 - Crimping")]
        public DialogSequence crimpingAsset;
        [TextArea(2, 6)]
        public string[] crimpingLines =
        {
            "Stage 3 of 4: crimping. This is the permanent part.",

            "Inside the crimping tool is a die - a shaped set of teeth. Squeezing the handles drives " +
            "the RJ45's eight gold contacts down through the insulation of each conductor to bite the " +
            "copper, and at the same moment presses a plastic wedge onto the outer jacket so the " +
            "cable can't be pulled back out.",

            "Check three things before you squeeze. One: the colours are still in order. Two: every " +
            "conductor is pushed all the way to the front wall of the plug. Three: the outer jacket " +
            "is inside the plug, not just the bare wires - that wedge has nothing to grip otherwise.",

            "One squeeze per end, and there is no undo. A bad crimp gets cut off and the end is " +
            "rebuilt from scratch.",

            "Drag the crimping tool onto each connector to crimp it."
        };

        // ------------------------------------------------------------------
        // Stage 4 - Testing
        // ------------------------------------------------------------------

        [Header("Stage 4 - Testing")]
        public DialogSequence testingAsset;
        [TextArea(2, 6)]
        public string[] testingLines =
        {
            "Stage 4 of 4: testing.",

            "This is an RJ45 LAN cable tester. It comes in two parts. The MAIN unit sends a signal " +
            "out of pin 1, then pin 2, and so on up to pin 8. The REMOTE unit lights up whichever pin " +
            "that signal actually arrived on.",

            "The tester has no idea what colour any wire is. All it can tell you is 'something I sent " +
            "out of pin 3 came back in at pin 6'. That map of which pin reaches which is called the " +
            "wiremap, and it's the only thing that matters.",

            "Both rows counting 1 to 8 in step is a good straight-through cable. A pin that never " +
            "lights is an OPEN - that conductor doesn't reach the far end. Lights arriving out of " +
            "order mean the pinout is wrong. Two lighting at once is a SHORT.",

            "Drag the cable's connector into the tester's socket to run the test."
        };

        // ------------------------------------------------------------------
        // Stage 5 - Patch panel
        // ------------------------------------------------------------------

        [Header("Stage 5 - Patch panel")]
        public DialogSequence patchPanelAsset;
        [TextArea(2, 6)]
        public string[] patchPanelLines =
        {
            "Stage 5 of 5: terminating the run onto a patch panel.",

            "A patch panel is the fixed end of structured cabling. Every permanent cable run in the " +
            "building comes back to a rack and terminates here - not on a plug, but on an IDC block. " +
            "IDC stands for Insulation Displacement Contact.",

            "That's the difference between the two halves of this competency. A PATCH CORD is short, " +
            "flexible, stranded, and gets an RJ45 crimped on each end - that's the cable you just " +
            "built. A HORIZONTAL RUN is permanent and solid-core, and gets punched down at both ends: " +
            "patch panel at the rack, wall outlet at the desk.",

            "The punch-down tool does two jobs in one stroke. It presses the conductor into the IDC " +
            "contact, whose blades slice through the insulation to reach the copper, and it trims the " +
            "tail flush. Never do this with a screwdriver - it seats the wire without cutting it and " +
            "leaves a whisker that can short to its neighbour.",

            "The panel is printed with both colour codes, T568A and T568B. Pick whichever the rest of " +
            "the installation uses and stay on it. Mixing standards between the panel and the outlet " +
            "is how you build a crossover by accident.",

            "Place all eight conductors into their positions, then drag the punch-down tool onto the " +
            "panel to seat them."
        };

        // ------------------------------------------------------------------
        // Stage 6 - Patch cords
        // ------------------------------------------------------------------

        [Header("Stage 6 - Patch cords")]
        [Tooltip("Shown when a plugging phase of the patch panel station starts - the cords from the " +
                 "panel front to the switch. Skipped for a phase that has its own Intro Dialog.\n\n" +
                 "{socket} becomes the phase's Socket Noun ('switch port'), {phase} its Phase Name.")]
        public DialogSequence patchCordsAsset;
        [TextArea(2, 6)]
        public string[] patchCordsLines =
        {
            "Next: the patch cords ({phase}). The punch-down joined the cable run to the BACK of the " +
            "patch panel. Now the FRONT of each panel port has to be connected to the switch.",

            "Each short patch cord already has one end in a panel port. Drag its loose end into the " +
            "{socket} with the SAME number: panel port 1 to {socket} 1, port 2 to {socket} 2, and so on.",

            "Matching numbers keep the rack readable. When a user's connection fails, a technician can " +
            "follow panel port 3 straight to {socket} 3 without tracing the cable by hand."
        };

        [Tooltip("Used instead of the second line above when the phase accepts any free socket " +
                 "(Match Partner Port Number off).")]
        [TextArea(2, 4)]
        public string patchCordsAnyPortLine =
            "Each short patch cord already has one end in a panel port. Drag its loose end into a free {socket}.";

        // ------------------------------------------------------------------
        // Results
        // ------------------------------------------------------------------

        [Header("Result - PASS")]
        public DialogSequence passAsset;
        [TextArea(2, 6)]
        public string[] passLines =
        {
            "Both rows lit 1 through 8 in step. Full continuity on all eight conductors, and the " +
            "pinout matches the cable you were asked to build.",

            "That's a working cable, and that's the complete procedure: strip, arrange, crimp, test."
        };

        [Header("Result - FAIL")]
        public DialogSequence failAsset;
        [Tooltip("Tick to insert the tester's actual diagnosis as an extra line - which pin is open, " +
                 "which two are shorted, or what the cable tests as instead. Read from CableTestAnalyzer, " +
                 "so it always matches what the LEDs just did.")]
        public bool includeFailureReason = true;
        [TextArea(2, 6)]
        public string[] failLines =
        {
            "The tester says this cable will not carry a link."
        };
        [Tooltip("Shown after the diagnosis line, to explain why the whole procedure starts over.")]
        [TextArea(2, 6)]
        public string[] failClosingLines =
        {
            "A crimped RJ45 cannot be opened back up. In the field you would cut both ends off and " +
            "rebuild the cable from the jacket up - so that's exactly what we're going to do.",

            "Back to stripping. Take your time with the colour order this run."
        };

        [Tooltip("ASSESSMENT ONLY - shown instead of the fail dialog above, which explains the fault and is " +
                 "withheld in an assessment. It states the result, never the cause, and holds the rebuild " +
                 "until the learner has read it. Without it the cable silently reset the moment it failed.")]
        [TextArea(2, 6)]
        public string[] assessmentFailLines =
        {
            "The cable failed the tester.",
            "A crimped connector cannot be reopened. Cut both ends off and build the cable again."
        };

        [Tooltip("ASSESSMENT ONLY - shown instead of the patch panel fail dialog, which names the positions " +
                 "that are wrong. States the result, never the cause. COC2AssessmentDialogs > Show Failure " +
                 "Details switches the full message back on.")]
        [TextArea(2, 6)]
        public string[] assessmentPanelFailLines =
        {
            "That termination did not pass inspection.",
            "The conductors are back in the tray. Terminate the port again."
        };

        [Header("Result - PATCH PANEL")]
        [Tooltip("Shown when the punch-down follows the standard.")]
        [TextArea(2, 6)]
        public string[] panelPassLines =
        {
            "All eight conductors follow the standard, seated in their contacts and trimmed flush. " +
            "That port is ready to patch.",

            "And that's both halves of installing network cables: a crimped patch cord, and a " +
            "punched-down permanent run."
        };
        [Tooltip("Shown when it doesn't. The offending positions are inserted between these lines " +
                 "and the closing ones.")]
        [TextArea(2, 6)]
        public string[] panelFailLines =
        {
            "That termination wouldn't pass inspection."
        };
        [TextArea(2, 6)]
        public string[] panelFailClosingLines =
        {
            "Punched-down conductors get cut out and re-dressed, and you lose an inch of cable every " +
            "time you do it - which is why the colour code gets checked before the tool comes out.",

            "The conductors are back in the tray. Work the panel's printed code position by position."
        };

        // ------------------------------------------------------------------

        [Header("Result - PATCH CORDS")]
        [Tooltip("Shown when a plugging phase passes. It used to show the punch-down lines above, which " +
                 "talk about conductors and contacts. {socket} = the phase's Socket Noun.")]
        [TextArea(2, 6)]
        public string[] cordPassLines =
        {
            "Every patch cord is in its matching {socket}, so each panel port now reaches the switch."
        };
        [Tooltip("Shown when a plugging phase fails. The reason from the phase goes between these and " +
                 "the closing lines.")]
        [TextArea(2, 6)]
        public string[] cordFailLines =
        {
            "Some patch cords are not in the right {socket}."
        };
        [TextArea(2, 6)]
        public string[] cordFailClosingLines =
        {
            "The cords have been pulled back out. Run each one to the {socket} with the same number as " +
            "its panel port."
        };
        [Tooltip("ASSESSMENT ONLY - instead of the patch cord fail lines. States the result, never the cause.")]
        [TextArea(2, 6)]
        public string[] assessmentCordFailLines =
        {
            "The patch cords did not pass inspection.",
            "Run the patch cords again."
        };

        [Header("Retry")]
        [Tooltip("The COC2ProcedureReset that winds the procedure back after a failed test. Leave " +
                 "empty and a failure only shows the dialog - nothing resets.")]
        public COC2ProcedureReset procedureReset;

        [Tooltip("Untick if you'd rather drive the reset yourself from OnTestFailed below - e.g. from " +
                 "a 'Try Again' button the player has to press.")]
        public bool restartAutomaticallyOnFail = true;

        [Header("Replays")]
        [Tooltip("Tick to brief the player again on every retry. Untick and each stage is explained " +
                 "once per session, however many attempts they take.")]
        public bool replayTutorialsOnRetry = true;

        [Header("Events")]
        public UnityEvent OnTestPassed;
        public UnityEvent OnTestFailed;

        [Header("Safety")]
        [Tooltip("Leave this ON.\n\n" +
                 "Since 2018 Unity only pushes a moved Transform into the physics engine during " +
                 "FixedUpdate. If your DialogManager has Pause Game While Open ticked it sets " +
                 "Time.timeScale = 0, FixedUpdate stops running entirely, and any collider on an " +
                 "object that got repositioned stays stuck at its OLD pose for as long as the dialog " +
                 "is up. That is exactly the bug that made the crimping tool impossible to click - " +
                 "the mesh was on screen, the raycast went straight past it.\n\n" +
                 "These tutorials open a dialog at the start of every stage, right when the tool is " +
                 "being repositioned, so that trap is now one tick away. Turning auto-sync on costs " +
                 "a negligible amount in a scene this size and removes the whole class of problem.")]
        public bool keepPhysicsInSyncWithTransforms = true;

        // ------------------------------------------------------------------

        Stage currentStage = Stage.None;
        readonly bool[] shown = new bool[StageCount]; // indexed by (int)Stage
        DialogSequence[] cache = new DialogSequence[StageCount];
        bool resultHandled;
        bool testerWasTested;

        // Patch cord phases: which have been briefed, and the frame a phase last reported a result on
        // (a phase result arrives through ShowPanelResult, the same door as a punch-down result).
        PatchCordBoard[] cordBoards = new PatchCordBoard[0];
        readonly System.Collections.Generic.HashSet<PatchCordBoard> briefedBoards =
            new System.Collections.Generic.HashSet<PatchCordBoard>();
        int cordResultFrame = -1;
        PatchCordBoard cordResultBoard;

        void Awake()
        {
            Instance = this;

            if (keepPhysicsInSyncWithTransforms)
                Physics.autoSyncTransforms = true;
        }

        void Start()
        {
            // Every plugging phase in the scene. Their pass/fail events fire just BEFORE the phase hands
            // its result to PunchDownStation, which then calls ShowPanelResult in the same frame - so
            // noting the frame here is how ShowPanelResult tells a cord result from a punch-down result.
            cordBoards = FindObjectsByType<PatchCordBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (PatchCordBoard b in cordBoards)
            {
                if (b == null) continue;
                PatchCordBoard board = b;
                if (board.OnPhasePassed != null) board.OnPhasePassed.AddListener(() => NoteCordResult(board));
                if (board.OnPhaseFailed != null) board.OnPhaseFailed.AddListener(() => NoteCordResult(board));
            }

            if (NetworkTestStation.Instance == null)
            {
                Debug.LogWarning("[COC2StageDialogs] No NetworkTestStation in the scene - the pass/fail " +
                                 "dialogs will never fire. The stage tutorials still work.", this);
            }
        }

        // ------------------------------------------------------------------
        // Stage detection
        // ------------------------------------------------------------------

        void Update()
        {
            WatchForTestResult();

            Stage now = DetectStage();
            if (now == currentStage) return;

            currentStage = now;
            if (now != Stage.None) ShowTutorial(now);
        }

        /// <summary>
        /// Watches NetworkTestStation.HasTested flip false -> true.
        ///
        /// Deliberately NOT subscribed to NetworkTestStation.OnTestResult. That field is a
        /// UnityEvent&lt;bool&gt;, and an open generic UnityEvent is a well-known Unity trap - it only
        /// serialises at all from 2020.1 onward, and a field that fails to serialise comes back
        /// null, so an AddListener() call there can silently do nothing and take the entire
        /// pass/fail flow down with it. A public bool property can't fail that way.
        ///
        /// The verdict is recomputed here with the exact same call NetworkTestStation makes
        /// internally, so the dialog can never disagree with the LEDs.
        /// </summary>
        void WatchForTestResult()
        {
            var tester = NetworkTestStation.Instance;
            if (tester == null) return;

            bool tested = tester.HasTested;

            // The reset clears HasTested; that's a wind-back, not a new result.
            if (!tested)
            {
                testerWasTested = false;
                return;
            }

            if (testerWasTested) return;
            testerWasTested = true;

            HandleTestResult(EvaluateCable());
        }

        /// <summary>Same judgement NetworkTestStation.EvaluateCable() makes: read what was actually
        /// crimped and analyse it. No randomness anywhere in this path.</summary>
        bool EvaluateCable()
        {
            var station = CrimpingStationController.Instance;
            if (station == null) return false;

            WireColor[] endOne = station.GetCrimpedArrangement(1);
            WireColor[] endTwo = station.GetCrimpedArrangement(2);
            if (endOne == null || endTwo == null) return false;

            return CableTestAnalyzer.Analyze(endOne, endTwo, station.targetCableType).Passed;
        }

        /// <summary>Which stage the player is looking at right now. Checked latest-stage-first so a
        /// single frame where two views overlap during a hand-off resolves to the new one.</summary>
        Stage DetectStage()
        {
            // A plugging phase runs inside the punch-down station's view, so it is checked first.
            if (RunningCordBoard() != null)
                return Stage.PatchCords;

            // The punch-down stage. This used to read PatchPanelStation, which drove the 3D panel
            // directly; the stage now lives on its own set with its own camera, so it is
            // PunchDownStation that owns the view. Nothing else about stage 5 changed - the
            // briefing, the pass copy and the fail copy below are all still used as they were.
            if (PunchDownStation.Instance != null && PunchDownStation.Instance.InStageView)
                return Stage.PatchPanel;

            if (NetworkTestStation.Instance != null && NetworkTestStation.Instance.InTesterView)
                return Stage.Testing;

            if (CrimpStageViewController.Instance != null && CrimpStageViewController.Instance.InCrimpView)
                return Stage.Crimping;

            if (CrimpingViewController.Instance != null && CrimpingViewController.Instance.InCrimpingView)
                return Stage.Wiring;

            if (StrippingViewController.Instance != null && StrippingViewController.Instance.InStrippingView)
                return Stage.Stripping;

            return Stage.None;
        }

        void ShowTutorial(Stage stage)
        {
            if (stage == Stage.PatchCords) { ShowCordBriefing(); return; }

            int i = (int)stage;
            if (shown[i]) return;
            shown[i] = true;

            DialogSequence sequence = SequenceFor(stage);
            if (sequence == null) return;

            Show(sequence, null);
        }

        DialogSequence SequenceFor(Stage stage)
        {
            int i = (int)stage;

            // An assigned asset always wins, and needs no caching - it IS the asset.
            DialogSequence asset = AssetFor(stage);
            if (asset != null) return asset;

            // Built from the typed lines the first time it's needed, then kept. Rebuilding it every
            // time would leak a ScriptableObject per popup.
            if (cache[i] == null) cache[i] = Build(LinesFor(stage));
            return cache[i];
        }

        DialogSequence AssetFor(Stage stage)
        {
            switch (stage)
            {
                case Stage.Stripping: return strippingAsset;
                case Stage.Wiring: return wiringAsset;
                case Stage.Crimping: return crimpingAsset;
                case Stage.Testing: return testingAsset;
                case Stage.PatchPanel: return patchPanelAsset;
                default: return null;
            }
        }

        string[] LinesFor(Stage stage)
        {
            switch (stage)
            {
                case Stage.Stripping: return strippingLines;
                case Stage.Wiring: return wiringLines;
                case Stage.Crimping: return crimpingLines;
                case Stage.Testing: return testingLines;
                case Stage.PatchPanel: return patchPanelLines;
                default: return null;
            }
        }

        // ------------------------------------------------------------------
        // Patch panel result
        // ------------------------------------------------------------------

        /// <summary>
        /// Called by PunchDownStation once a punch-down has been judged. The station supplies the
        /// reason because it judges against a WireStandardSO, not a CableTypeSO - a patch panel
        /// doesn't care what's on the far end, only that it follows the standard the installation
        /// uses.
        ///
        /// onClosed runs when the player finishes reading, which is what drives the panel reset.
        /// It is always invoked, even when there is no DialogManager, so the station can never
        /// stall waiting on a popup that never appeared.
        /// </summary>
        public void ShowPanelResult(bool passed, string reason, System.Action onClosed)
        {
            var sequence = ScriptableObject.CreateInstance<DialogSequence>();

            // A patch cord phase reports through here too. Its result needs cord lines, not the
            // punch-down ones about conductors and IDC contacts.
            bool cords = cordResultFrame == Time.frameCount;
            string socket = cords && cordResultBoard != null && !string.IsNullOrEmpty(cordResultBoard.socketNoun)
                ? cordResultBoard.socketNoun : "switch port";

            if (passed)
            {
                AddLines(sequence, cords ? CordLines(cordPassLines, socket) : panelPassLines);
            }
            else
            {
                AddLines(sequence, cords ? CordLines(cordFailLines, socket) : panelFailLines);
                if (includeFailureReason && !string.IsNullOrEmpty(reason)) AddLine(sequence, reason);
                AddLines(sequence, cords ? CordLines(cordFailClosingLines, socket) : panelFailClosingLines);
            }

            // A failed panel sends the player round this stage again, so the briefing should be
            // allowed to reappear if they've asked for replays. A failed cord phase does not - the
            // player stays in the cord phase, and the punch-down briefing would be the wrong one.
            if (!passed && !cords && replayTutorialsOnRetry) shown[(int)Stage.PatchPanel] = false;

            // Same reason currentStage is cleared on a cable-test retry: the stage is about to
            // change underneath us and must register as a fresh entry, not "no change".
            if (!passed && !cords) currentStage = Stage.None;

            if (AssessmentMode.IsActive)
            {
                if (passed) ShowResult(sequence, onClosed);
                else if (COC2AssessmentDialogs.FailureDetailsShown) ShowFailure(sequence, onClosed);
                // Result only - and always shown, or the port would reset with no word of why.
                else ShowFailure(Build(cords ? assessmentCordFailLines : assessmentPanelFailLines), onClosed);
                return;
            }

            Show(sequence, onClosed);
        }

        // ------------------------------------------------------------------
        // Pass / fail
        // ------------------------------------------------------------------

        /// <summary>Public so you can ALSO wire NetworkTestStation > On Test Result to it in the
        /// Inspector if you prefer seeing the connection there. Doing both is harmless - whichever
        /// arrives first wins and the second is ignored.</summary>
        public void ReportTestResult(bool passed)
        {
            testerWasTested = true;
            HandleTestResult(passed);
        }

        void HandleTestResult(bool passed)
        {
            // Guarded because the result can arrive from the poll below AND from an Inspector-wired
            // OnTestResult call. Without this the dialog would show twice.
            if (resultHandled) return;
            resultHandled = true;

            if (passed) HandlePass();
            else HandleFail();
        }

        void HandlePass()
        {
            OnTestPassed?.Invoke();

            DialogSequence sequence = passAsset != null ? passAsset : Build(passLines);
            ShowResult(sequence, null);
        }

        void HandleFail()
        {
            OnTestFailed?.Invoke();

            // Built fresh each time - the diagnosis line changes with the fault.
            DialogSequence sequence = failAsset != null ? failAsset : BuildFailSequence();

            System.Action afterwards = restartAutomaticallyOnFail ? (System.Action)Restart : null;

            // In an assessment the fail dialog is withheld (it explains the fault), and a withheld
            // dialog fires its callback at once - so the cable used to reset the instant it failed,
            // with no word to the learner. Say the result, not the cause, and let them read it first.
            if (AssessmentMode.IsActive)
            {
                // Show Failure Details (COC2AssessmentDialogs): the full message, tester reading and all.
                if (COC2AssessmentDialogs.FailureDetailsShown) { ShowFailure(sequence, afterwards); return; }

                if (assessmentFailLines != null && assessmentFailLines.Length > 0)
                {
                    ShowFailure(Build(assessmentFailLines), afterwards);
                    return;
                }
            }

            Show(sequence, afterwards);
        }

        /// <summary>A pass or complete message. In an assessment it is shown only while
        /// COC2AssessmentDialogs > Allow Results is ticked; in Story Mode it is shown as always.</summary>
        void ShowResult(DialogSequence sequence, System.Action onComplete)
        {
            if (AssessmentMode.IsActive && COC2AssessmentDialogs.ResultsAllowed && DialogManager.Instance != null)
            {
                DialogManager.Instance.ShowDialog(sequence, onComplete, true);
                return;
            }
            Show(sequence, onComplete);
        }

        /// <summary>A failure message in an assessment. Always shown: the reset waits for the learner to
        /// close it, so without it the cable or port would reset with nothing said.</summary>
        void ShowFailure(DialogSequence sequence, System.Action onComplete)
        {
            if (DialogManager.Instance == null) { onComplete?.Invoke(); return; }
            DialogManager.Instance.ShowDialog(sequence, onComplete, true);
        }

        /// <summary>
        /// Winds this component's own memory back so the player can go round again: the test result
        /// is un-handled, the tester is re-armed, and the stage briefings are allowed to reappear.
        ///
        /// This is the DIALOG half of a retry and nothing else. It deliberately does NOT call
        /// COC2ProcedureReset - anything driving its own retry (a gate moving the player from the
        /// first cable to the second, a Try Again button, a stage that resets only itself) already
        /// owns that decision and would get a double reset if this made it too. Restart() below is
        /// the version that does both, and it is what a failed cable test uses.
        ///
        /// Uses the Replay Tutorials On Retry setting from the Inspector.
        /// </summary>
        public void ResetForNextAttempt()
        {
            ResetForNextAttempt(replayTutorialsOnRetry);
        }

        /// <summary>
        /// As above, but decides about the briefings at the call site instead of reading the
        /// Inspector - for a caller that wants them replayed on a full restart but suppressed when
        /// the player is only repeating one stage.
        /// </summary>
        public void ResetForNextAttempt(bool replayTutorials)
        {
            resultHandled = false;
            testerWasTested = false;

            if (replayTutorials)
            {
                for (int i = 0; i < shown.Length; i++) shown[i] = false;
                briefedBoards.Clear();
            }

            // The stage the player was in has gone away underneath us; clear it so the stripping
            // view registers as a fresh entry rather than "no change since last frame".
            currentStage = Stage.None;
        }

        void Restart()
        {
            ResetForNextAttempt(replayTutorialsOnRetry);

            if (procedureReset != null)
            {
                procedureReset.RestartProcedure();
            }
            else
            {
                Debug.LogWarning("[COC2StageDialogs] Test failed but no COC2ProcedureReset is assigned, " +
                                 "so nothing was reset. Drag one into the Procedure Reset field.", this);
            }
        }

        DialogSequence BuildFailSequence()
        {
            var sequence = ScriptableObject.CreateInstance<DialogSequence>();

            AddLines(sequence, failLines);

            if (includeFailureReason)
            {
                string reason = DiagnoseFailure();
                if (!string.IsNullOrEmpty(reason)) AddLine(sequence, reason);
            }

            AddLines(sequence, failClosingLines);
            return sequence;
        }

        /// <summary>Re-reads the crimped pinouts and works out what to tell the player. Must run
        /// BEFORE the reset clears the snapshots - which it does, because the dialog is shown first
        /// and the reset only happens once the player closes it.</summary>
        string DiagnoseFailure()
        {
            var station = CrimpingStationController.Instance;
            if (station == null) return null;

            WireColor[] endOne = station.GetCrimpedArrangement(1);
            WireColor[] endTwo = station.GetCrimpedArrangement(2);
            if (endOne == null || endTwo == null) return null;

            CableTestReport report = CableTestAnalyzer.Analyze(endOne, endTwo, station.targetCableType);

            // A physical fault outranks a colour-order mistake - fix the open before worrying about
            // which standard it is.
            if (!report.continuityOk || report.splitPairDetected)
                return CableTestAnalyzer.FailureHint(report);

            // Continuity is fine, so this is a pinout mistake, and in THIS station that's the only
            // failure a player can actually produce (each colour exists exactly once per end, so
            // opens and shorts are impossible here). CableTestAnalyzer's generic hint would just say
            // "the pinout isn't a Straight-Through" - naming the offending pins is what a trainee
            // can act on.
            string pins = MismatchedPins(station.targetCableType, endOne, endTwo);
            if (!string.IsNullOrEmpty(pins)) return pins;

            return CableTestAnalyzer.FailureHint(report);
        }

        /// <summary>Compares both crimped ends against the cable the player was asked to build and
        /// names the pins that differ. Returns null when the target isn't set or nothing differs.</summary>
        string MismatchedPins(CableTypeSO target, WireColor[] endOne, WireColor[] endTwo)
        {
            if (target == null) return null;

            var detail = new System.Text.StringBuilder();
            int named = 0;
            int total = 0;

            total += DescribeEnd(detail, target, 1, endOne, ref named);
            total += DescribeEnd(detail, target, 2, endTwo, ref named);

            if (total == 0) return null;

            string cableName = string.IsNullOrEmpty(target.displayName) ? target.name : target.displayName;

            var message = new System.Text.StringBuilder();
            message.Append("Every conductor has continuity, so the crimp itself was sound - it's the colour order that's wrong for a ");
            message.Append(cableName);
            message.Append(". ");
            message.Append(detail);

            if (total > named)
                message.Append(" (" + (total - named) + " more pin(s) are also out of place.)");

            return message.ToString();
        }

        /// <summary>Appends up to maxPinsNamed descriptions for one end. Returns the TOTAL number of
        /// wrong pins on that end, which can be more than the number described.</summary>
        int DescribeEnd(System.Text.StringBuilder detail, CableTypeSO target, int endNumber,
                        WireColor[] arrangement, ref int named)
        {
            const int maxPinsNamed = 4;
            int wrong = 0;

            for (int pin = 1; pin <= 8; pin++)
            {
                WireColor expected = target.GetExpectedColorAtPin(endNumber, pin);
                WireColor actual = arrangement[pin - 1];
                if (actual == expected) continue;

                wrong++;
                if (named >= maxPinsNamed) continue;
                named++;

                if (detail.Length > 0) detail.Append(" ");
                detail.Append("End " + endNumber + " pin " + pin + " has " +
                              WireColorPalette.ToDisplayName(actual) + ", it should be " +
                              WireColorPalette.ToDisplayName(expected) + ".");
            }

            return wrong;
        }

        // ------------------------------------------------------------------
        // Patch cords
        // ------------------------------------------------------------------

        void NoteCordResult(PatchCordBoard board)
        {
            cordResultFrame = Time.frameCount;
            cordResultBoard = board;
        }

        /// <summary>The plugging phase running right now, or null.</summary>
        PatchCordBoard RunningCordBoard()
        {
            foreach (PatchCordBoard b in cordBoards)
                if (b != null && b.IsRunning) return b;
            return null;
        }

        /// <summary>Briefs each plugging phase once, as it starts - unless it has an Intro Dialog of its own.</summary>
        void ShowCordBriefing()
        {
            PatchCordBoard board = RunningCordBoard();
            if (board == null || briefedBoards.Contains(board)) return;
            briefedBoards.Add(board);

            if (board.introDialog != null) return;   // the phase shows its own

            if (patchCordsAsset != null) { Show(patchCordsAsset, null); return; }

            string socket = string.IsNullOrEmpty(board.socketNoun) ? "switch port" : board.socketNoun;
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < patchCordsLines.Length; i++)
            {
                string line = patchCordsLines[i];
                // The second line promises matching numbers; a phase that takes any free socket gets
                // the plain version instead.
                if (i == 1 && !board.matchPartnerPortNumber) line = patchCordsAnyPortLine;
                lines.Add(line);
            }

            string phase = string.IsNullOrEmpty(board.phaseName) ? "panel to switch" : board.phaseName;
            Show(Build(CordLines(lines.ToArray(), socket, phase)), null);
        }

        static string[] CordLines(string[] lines, string socket, string phase = "")
        {
            if (lines == null) return null;
            var result = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                result[i] = lines[i] == null ? null
                    : lines[i].Replace("{socket}", socket).Replace("{phase}", phase);
            return result;
        }

        // ------------------------------------------------------------------
        // Plumbing
        // ------------------------------------------------------------------

        void Show(DialogSequence sequence, System.Action onComplete)
        {
            if (DialogManager.Instance == null)
            {
                Debug.LogWarning("[COC2StageDialogs] No DialogManager in the scene.", this);
                onComplete?.Invoke(); // don't strand a caller waiting on the callback
                return;
            }

            DialogManager.Instance.ShowDialog(sequence, onComplete);
        }

        DialogSequence Build(string[] lines)
        {
            var sequence = ScriptableObject.CreateInstance<DialogSequence>();
            AddLines(sequence, lines);
            return sequence;
        }

        void AddLines(DialogSequence sequence, string[] lines)
        {
            if (lines == null) return;
            foreach (var line in lines) AddLine(sequence, line);
        }

        void AddLine(DialogSequence sequence, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            sequence.lines.Add(new DialogLine { speakerName = speakerName, text = text });
        }
    }
}