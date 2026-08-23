using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The tester's text readout. Deliberately built the same way as COC3's PingSimulation - a
    /// single TMP_Text that fills in line by line - so there are no per-row prefabs to build.
    /// Everything here is display-only; CableTestAnalyzer decides what's true, this just prints it.
    ///
    /// Setup:
    ///   1. Under a Canvas (world-space glued to the tester reads best, but a screen-space panel
    ///      inside TestingViewController.testingUI is fine too), make a panel with one TMP_Text
    ///      for the readout.
    ///   2. Add this script to the panel and assign readoutText.
    ///   3. Optionally add a separate verdict label (big PASS/FAIL) and assign verdictText, plus
    ///      a panel Image for resultBackground if you want it to flash green/red.
    ///   4. Optionally assign liveStepText - a single line updated as each pin is energised, e.g.
    ///      "Pin 3 -> Pin 6". Nice for making the sweep readable while it's running.
    ///   5. Assign this panel to CableTesterController.resultUI.
    /// </summary>
    public class CableTestResultUI : MonoBehaviour
    {
        [Header("Text")]
        [Tooltip("The main multi-line readout - the wiremap table and the verdict.")]
        public TMP_Text readoutText;
        [Tooltip("Optional big PASS / FAIL label.")]
        public TMP_Text verdictText;
        [Tooltip("Optional single line updated live as each pin is energised, e.g. 'Pin 3 -> Pin 6'.")]
        public TMP_Text liveStepText;

        [Header("Colors")]
        public Color passColor = new Color(0.15f, 0.8f, 0.3f);
        public Color failColor = new Color(0.9f, 0.25f, 0.2f);
        public Color neutralColor = new Color(0.75f, 0.75f, 0.75f);
        [Tooltip("Optional panel background tinted to the pass/fail colour.")]
        public Image resultBackground;
        [Range(0f, 1f)] public float backgroundAlpha = 0.25f;

        void Awake()
        {
            Clear();
        }

        /// <summary>Blank slate - no test has been run yet.</summary>
        public void Clear()
        {
            if (readoutText != null) readoutText.text = "";
            if (liveStepText != null) liveStepText.text = "";
            if (verdictText != null)
            {
                verdictText.text = "";
                verdictText.color = neutralColor;
            }
            SetBackground(neutralColor);
        }

        /// <summary>One-off message, e.g. "Plug both ends into the tester."</summary>
        public void ShowMessage(string message)
        {
            if (readoutText != null) readoutText.text = message;
            if (verdictText != null)
            {
                verdictText.text = "";
                verdictText.color = neutralColor;
            }
            SetBackground(neutralColor);
        }

        /// <summary>Called when a test starts.</summary>
        public void ShowTesting()
        {
            if (readoutText != null) readoutText.text = "TESTING...";
            if (liveStepText != null) liveStepText.text = "";
            if (verdictText != null)
            {
                verdictText.text = "";
                verdictText.color = neutralColor;
            }
            SetBackground(neutralColor);
        }

        /// <summary>Called once per pin as the LED chase runs.</summary>
        public void HighlightStep(PinTestResult pin)
        {
            if (liveStepText == null) return;

            if (pin.remotePin == 0)
            {
                liveStepText.text = $"Pin {pin.masterPin}  ->  no reply";
                liveStepText.color = failColor;
                return;
            }

            liveStepText.text = $"Pin {pin.masterPin}  ->  Pin {pin.remotePin}";
            liveStepText.color = pin.status == PinTestStatus.Ambiguous ? failColor : neutralColor;
        }

        /// <summary>Called once the sequence finishes, with the full report.</summary>
        public void ShowReport(CableTestReport report)
        {
            if (report == null) return;

            if (readoutText != null)
            {
                var builder = new StringBuilder();
                foreach (string line in report.readoutLines)
                {
                    builder.AppendLine(line);
                }
                readoutText.text = builder.ToString();
            }

            if (liveStepText != null) liveStepText.text = "";

            Color color = report.Passed ? passColor : failColor;

            if (verdictText != null)
            {
                verdictText.text = report.Passed ? "PASS" : "FAIL";
                verdictText.color = color;
            }

            SetBackground(color);
        }

        void SetBackground(Color color)
        {
            if (resultBackground == null) return;
            color.a = backgroundAlpha;
            resultBackground.color = color;
        }
    }
}
