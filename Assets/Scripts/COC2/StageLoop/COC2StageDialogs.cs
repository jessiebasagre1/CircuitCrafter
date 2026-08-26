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
        enum Stage { None, Stripping, Wiring, Crimping, Testing }

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

        // ------------------------------------------------------------------

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
        readonly bool[] shown = new bool[5]; // indexed by (int)Stage
        DialogSequence[] cache = new DialogSequence[5];
        bool resultHandled;
        bool testerWasTested;

        void Awake()
        {
            Instance = this;

            if (keepPhysicsInSyncWithTransforms)
                Physics.autoSyncTransforms = true;
        }

        void Start()
        {
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
                default: return null;
            }
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
        /// <summary>Clears the remembered verdict so the NEXT attempt's pass/fail dialog fires again.
        /// Called by COC2BothCablesGate after it winds the procedure back for the second cable type.</summary>
        public void ResetForNextAttempt(bool replayTutorials)
        {
            resultHandled = false;
            testerWasTested = false;
            currentStage = Stage.None;

            if (replayTutorials)
                for (int i = 0; i < shown.Length; i++) shown[i] = false;
        }
        void HandlePass()
        {
            OnTestPassed?.Invoke();

            DialogSequence sequence = passAsset != null ? passAsset : Build(passLines);
            Show(sequence, null);
        }

        void HandleFail()
        {
            OnTestFailed?.Invoke();

            // Built fresh each time - the diagnosis line changes with the fault.
            DialogSequence sequence = failAsset != null ? failAsset : BuildFailSequence();

            Show(sequence, restartAutomaticallyOnFail ? (System.Action)Restart : null);
        }

        void Restart()
        {
            resultHandled = false;
            testerWasTested = false;

            if (replayTutorialsOnRetry)
            {
                for (int i = 0; i < shown.Length; i++) shown[i] = false;
            }

            // The stage the player was in has gone away underneath us; clear it so the stripping
            // view registers as a fresh entry rather than "no change since last frame".
            currentStage = Stage.None;

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
