using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The RJ45 LAN cable tester itself. Reads the pinout the player actually crimped out of
    /// CrimpingStationController, hands it to CableTestAnalyzer, then plays the result back as
    /// the LED chase a real tester does: master pin 1 lights, the remote unit lights whichever
    /// pin answered, pause, pin 2, and so on to pin 8.
    ///
    /// That chase IS the network-testing animation. An open conductor shows up as a master LED
    /// lighting with nothing answering on the remote - which is exactly what the learner needs
    /// to recognise on real hardware, so it's worth letting it play out rather than jumping
    /// straight to a PASS/FAIL stamp.
    ///
    /// Setup:
    ///   1. Add this to your tester object at the crimping bench.
    ///   2. Assign masterUnit and remoteUnit (the two CableTesterUnit components).
    ///   3. Assign station - the CrimpingStationController for this cable.
    ///   4. Optionally assign resultUI (CableTestResultUI) for the text readout.
    ///   5. Optionally assign testerAnimator + a trigger name if your tester model has its own
    ///      clip (a switch flicking, the unit powering up). It plays before the LED sequence.
    ///   6. Wire the tester's Test button OnClick to RunTest(). If you'd rather it start by
    ///      itself the moment both plugs are seated, tick autoStartWhenBothPlugsSeated instead.
    ///   7. Set taskId to whatever you RegisterTask()'d for the testing step, or leave blank.
    /// </summary>
    public class CableTesterController : MonoBehaviour
    {
        public static CableTesterController Instance { get; private set; }

        [Header("Units")]
        public CableTesterUnit masterUnit;
        public CableTesterUnit remoteUnit;

        [Header("Refs")]
        [Tooltip("Where the crimped pinout comes from. Leave empty to fall back to CrimpingStationController.Instance.")]
        public CrimpingStationController station;
        [Tooltip("Optional text readout panel.")]
        public CableTestResultUI resultUI;

        [Header("Tester Animation (optional)")]
        [Tooltip("Animator on the tester model, if it has its own clip - e.g. the power switch flicking over.")]
        public Animator testerAnimator;
        [Tooltip("Trigger fired on testerAnimator when a test starts. Leave blank to skip.")]
        public string testerTrigger = "PowerOn";
        [Tooltip("Seconds waited after firing testerTrigger, before the LED sequence begins.")]
        public float testerAnimationLength = 0.75f;

        [Header("Sequence Timing")]
        [Tooltip("How long each pin stays lit. 0.35-0.5 reads well; real testers run about 2 pins a second.")]
        public float stepDuration = 0.4f;
        [Tooltip("Dark gap between pins, so consecutive LEDs read as separate steps rather than a smear.")]
        public float gapDuration = 0.12f;
        [Tooltip("Seconds all 8 LEDs flash on before the sequence, like a real tester's power-on self test. 0 to skip.")]
        public float selfTestFlash = 0.35f;
        [Tooltip("How many times the wiremap sweep repeats before the verdict is shown. Real testers loop " +
                 "continuously; 1 or 2 passes is enough in a game.")]
        [Range(1, 5)] public int passes = 1;
        [Tooltip("Seconds held on the final result before OnTestComplete fires.")]
        public float resultHold = 0.6f;

        [Header("Behaviour")]
        [Tooltip("Start the test automatically the moment both plugs are seated in the tester, " +
                 "instead of waiting for a Test button press.")]
        public bool autoStartWhenBothPlugsSeated = false;
        [Tooltip("Flag split pairs. A REAL basic continuity tester cannot detect these - it needs a " +
                 "wiremap+crosstalk certifier - so leave this off if you want the sim to stay honest " +
                 "about what this class of tool can see. Turn it on if you want the extra teaching point.")]
        public bool detectSplitPairs = false;

        [Header("Task Reporting (optional)")]
        [Tooltip("Matches a taskId registered with TaskManager.RegisterTask(). Leave blank to skip.")]
        public string taskId = "test_cable";
        [Tooltip("Tick to report task progress only when the cable actually passes. Untick to report " +
                 "as soon as the player performs the test, pass or fail.")]
        public bool reportTaskOnlyOnPass = true;

        [Header("Events")]
        [Tooltip("Fired when a test begins - hide the Test button, etc.")]
        public UnityEvent OnTestStarted;
        [Tooltip("Fired once the sequence finishes: true if the cable passed.")]
        public UnityEvent<bool> OnTestResult;

        /// <summary>True while the LED sequence is playing.</summary>
        public bool IsTesting { get; private set; }

        /// <summary>The last report produced, or null if no test has run yet.</summary>
        public CableTestReport LastReport { get; private set; }

        /// <summary>Code-side twin of OnTestResult, for scripts that would rather subscribe than
        /// be wired in the Inspector.</summary>
        public System.Action<CableTestReport> OnTestComplete;

        void Awake()
        {
            Instance = this;
            if (station == null) station = CrimpingStationController.Instance;
        }

        void Start()
        {
            if (station == null) station = CrimpingStationController.Instance;
            AllLedsOff();
        }

        /// <summary>Called by CableTesterPlugController each time a plug seats. Starts the test
        /// by itself if autoStartWhenBothPlugsSeated is on.</summary>
        public void NotifyPlugSeated()
        {
            if (!autoStartWhenBothPlugsSeated) return;
            if (CableTesterPlugController.Instance != null && !CableTesterPlugController.Instance.AllPlugsSeated()) return;
            RunTest();
        }

        /// <summary>Wire the Test button's OnClick to this.</summary>
        public void RunTest()
        {
            if (IsTesting) return;

            if (station == null) station = CrimpingStationController.Instance;
            if (station == null)
            {
                Debug.LogError("[CableTesterController] No CrimpingStationController to read the crimped cable from.", this);
                return;
            }

            // Both plugs have to be physically in the tester before it can read anything.
            var plugs = CableTesterPlugController.Instance;
            if (plugs != null && !plugs.AllPlugsSeated())
            {
                Debug.Log("[CableTesterController] Both cable ends need to be plugged into the tester first.", this);
                if (resultUI != null) resultUI.ShowMessage("Plug both ends of the cable into the tester.");
                return;
            }

            WireColor[] endOne = station.GetCrimpedArrangement(1);
            WireColor[] endTwo = station.GetCrimpedArrangement(2);

            if (endOne == null || endTwo == null)
            {
                Debug.Log("[CableTesterController] The cable hasn't been crimped on both ends yet.", this);
                if (resultUI != null) resultUI.ShowMessage("Crimp both ends of the cable first.");
                return;
            }

            StartCoroutine(TestRoutine(endOne, endTwo));
        }

        IEnumerator TestRoutine(WireColor[] endOne, WireColor[] endTwo)
        {
            IsTesting = true;
            OnTestStarted?.Invoke();

            CableTestReport report = CableTestAnalyzer.Analyze(endOne, endTwo, station.targetCableType, detectSplitPairs);
            LastReport = report;

            if (resultUI != null) resultUI.ShowTesting();

            // Optional model animation (switch flick / power-up) before the LEDs start.
            if (testerAnimator != null && !string.IsNullOrEmpty(testerTrigger))
            {
                testerAnimator.ResetTrigger(testerTrigger);
                testerAnimator.SetTrigger(testerTrigger);
                yield return new WaitForSeconds(testerAnimationLength);
            }

            // Power-on self test: everything lights, then everything goes dark.
            if (selfTestFlash > 0f)
            {
                if (masterUnit != null) masterUnit.FlashAll(true);
                if (remoteUnit != null) remoteUnit.FlashAll(true);
                yield return new WaitForSeconds(selfTestFlash);
                AllLedsOff();
                yield return new WaitForSeconds(gapDuration);
            }

            for (int pass = 0; pass < passes; pass++)
            {
                for (int masterPin = 1; masterPin <= 8; masterPin++)
                {
                    PinTestResult pin = report.pins[masterPin - 1];
                    bool faulty = pin.status == PinTestStatus.Open || pin.status == PinTestStatus.Ambiguous;

                    // Master always lights - the tester is driving that pin whether or not anything
                    // answers. Red when nothing does, which is the tell for an open conductor.
                    if (masterUnit != null)
                        masterUnit.SetPin(masterPin, true, faulty ? masterUnit.faultColor : masterUnit.okColor);

                    // Remote lights only the pin that actually received the signal. Nothing lights
                    // on an open, which is the whole point of watching this play out.
                    if (remoteUnit != null && pin.remotePin > 0 && !faulty)
                        remoteUnit.SetPin(pin.remotePin, true, remoteUnit.okColor);

                    if (resultUI != null) resultUI.HighlightStep(pin);

                    yield return new WaitForSeconds(stepDuration);

                    AllLedsOff();
                    yield return new WaitForSeconds(gapDuration);
                }
            }

            // Hold the final state: green on every good pin, red on every fault, so the player
            // can read the whole wiremap at a glance once the chase stops.
            ShowFinalState(report);

            if (resultUI != null) resultUI.ShowReport(report);

            yield return new WaitForSeconds(resultHold);

            if (!string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
            {
                if (!reportTaskOnlyOnPass || report.Passed)
                    TaskManager.Instance.ReportProgress(taskId);
            }

            Debug.Log($"[CableTesterController] Test finished - {(report.Passed ? "PASS" : "FAIL")}. " +
                      $"Detected {CableTestAnalyzer.DetectedName(report.detectedType)}.", this);

            IsTesting = false;
            OnTestResult?.Invoke(report.Passed);
            OnTestComplete?.Invoke(report);
        }

        void ShowFinalState(CableTestReport report)
        {
            for (int masterPin = 1; masterPin <= 8; masterPin++)
            {
                PinTestResult pin = report.pins[masterPin - 1];
                bool faulty = pin.status == PinTestStatus.Open || pin.status == PinTestStatus.Ambiguous;

                if (masterUnit != null)
                    masterUnit.SetPin(masterPin, true, faulty ? masterUnit.faultColor : masterUnit.okColor);

                if (remoteUnit != null && pin.remotePin > 0 && !faulty)
                    remoteUnit.SetPin(pin.remotePin, true, remoteUnit.okColor);
            }
        }

        /// <summary>Clears both units. Also wired to a Reset button if you want one.</summary>
        public void AllLedsOff()
        {
            if (masterUnit != null) masterUnit.AllOff();
            if (remoteUnit != null) remoteUnit.AllOff();
        }

        /// <summary>Clears the tester back to idle so the player can test again.</summary>
        public void ResetTester()
        {
            StopAllCoroutines();
            IsTesting = false;
            LastReport = null;
            AllLedsOff();
            if (resultUI != null) resultUI.Clear();
        }
    }
}
