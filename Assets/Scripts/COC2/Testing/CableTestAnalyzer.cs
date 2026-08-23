using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>Raw wiremap status of one master pin, before any judgement about which cable
    /// type the player was SUPPOSED to build. A crossover cable legitimately shows Crossed on
    /// pins 1, 2, 3 and 6 - that's not a fault, it's the cable working as designed.</summary>
    public enum PinTestStatus
    {
        /// <summary>Signal left master pin N and arrived at remote pin N.</summary>
        Straight,
        /// <summary>Signal arrived at a DIFFERENT remote pin - correct for a crossover's 1/2/3/6, a fault otherwise.</summary>
        Crossed,
        /// <summary>No continuity - that conductor doesn't reach the far end at all.</summary>
        Open,
        /// <summary>That wire colour appears more than once on an end, so the tester can't tell which pin answered. Real-world equivalent of a short.</summary>
        Ambiguous
    }

    /// <summary>Result for a single master pin.</summary>
    public struct PinTestResult
    {
        public int masterPin;          // 1-8
        public int remotePin;          // 1-8, or 0 when Open
        public WireColor masterColor;
        public PinTestStatus status;
    }

    /// <summary>Everything one press of the tester's Test button produced.</summary>
    public class CableTestReport
    {
        public PinTestResult[] pins = new PinTestResult[8];

        /// <summary>Every conductor reaches the far end (no Open / Ambiguous pins).</summary>
        public bool continuityOk;

        /// <summary>The wiremap is a clean 1:1 straight-through (every pin N to pin N).</summary>
        public bool isStraightThrough;

        /// <summary>The wiremap is a textbook crossover (1&lt;-&gt;3, 2&lt;-&gt;6, rest straight).</summary>
        public bool isCrossover;

        /// <summary>The wiremap is fully reversed (1-&gt;8, 2-&gt;7 ... 8-&gt;1) - a rollover/console
        /// cable. Not what this station asks for, but worth naming instead of just calling it
        /// "invalid", because it's a real cable a student may have built by mistake.</summary>
        public bool isRollover;

        /// <summary>What the tester thinks it's looking at, derived from each end's standard.</summary>
        public CableType detectedType = CableType.Invalid;

        /// <summary>Wiring standard identified on each end, or null when the end doesn't match
        /// either T568A or T568B (i.e. the player invented their own order).</summary>
        public WiringStandard? endOneStandard;
        public WiringStandard? endTwoStandard;

        /// <summary>A twisted pair was split across non-paired pins. Flagged only when
        /// CableTesterController.detectSplitPairs is on - see the note there, a real basic
        /// continuity tester cannot actually detect this.</summary>
        public bool splitPairDetected;

        /// <summary>True when this cable matches the CableTypeSO the player was told to build.
        /// Set by the analyzer when a target is supplied.</summary>
        public bool matchesTarget;

        /// <summary>Set when a target was supplied, for the readout header.</summary>
        public string targetName = "";

        /// <summary>Human-readable lines for the on-screen readout, one per row.</summary>
        public List<string> readoutLines = new List<string>();

        /// <summary>Which remote pin lights up when the tester is driving this master pin.
        /// Returns 0 for an open conductor (nothing lights on the remote unit).</summary>
        public int RemotePinFor(int masterPin)
        {
            int idx = masterPin - 1;
            if (idx < 0 || idx >= pins.Length) return 0;
            return pins[idx].remotePin;
        }

        /// <summary>Overall verdict shown to the player. A cable "passes" when it has full
        /// continuity AND matches what they were asked to build.</summary>
        public bool Passed => continuityOk && matchesTarget && !splitPairDetected;
    }

    /// <summary>
    /// Pure logic - no MonoBehaviour, no scene references, nothing to wire up in the Inspector.
    /// Turns the two crimped pinouts into a wiremap the way a real RJ45 continuity tester does:
    /// it energises master pin 1, sees which remote pin answers, then pin 2, and so on to pin 8.
    ///
    /// The important idea for the learner: the tester does NOT know what colour a wire is. It
    /// only knows "something I sent out of pin 3 came back in at pin 6". Colours are shown in
    /// the readout purely because the game knows them - a real tester just blinks numbers.
    /// </summary>
    public static class CableTestAnalyzer
    {
        /// <summary>Pin pairs a twisted pair is supposed to land on, per TIA/EIA-568.</summary>
        static readonly int[,] ValidPinPairs = { { 1, 2 }, { 3, 6 }, { 4, 5 }, { 7, 8 } };

        /// <summary>The two conductors that make up each twisted pair.</summary>
        static readonly WireColor[,] ColorPairs =
        {
            { WireColor.WhiteOrange, WireColor.Orange },
            { WireColor.WhiteGreen,  WireColor.Green  },
            { WireColor.WhiteBlue,   WireColor.Blue   },
            { WireColor.WhiteBrown,  WireColor.Brown  }
        };

        /// <summary>
        /// Builds the full report. endOne/endTwo are 8-long WireColor arrays where index 0 is
        /// pin 1 - exactly what ConnectorEnd.GetArrangement() and
        /// CrimpingStationController.GetCrimpedArrangement() return.
        /// </summary>
        public static CableTestReport Analyze(WireColor[] endOne, WireColor[] endTwo,
                                              CableTypeSO target = null, bool detectSplitPairs = false)
        {
            var report = new CableTestReport();

            if (endOne == null || endTwo == null || endOne.Length != 8 || endTwo.Length != 8)
            {
                Debug.LogError("[CableTestAnalyzer] Both ends need a full 8-entry arrangement. " +
                               "Did you call this before both ends were crimped?");
                report.continuityOk = false;
                report.readoutLines.Add("NO CABLE DETECTED");
                return report;
            }

            report.continuityOk = true;

            for (int i = 0; i < 8; i++)
            {
                WireColor color = endOne[i];
                var result = new PinTestResult
                {
                    masterPin = i + 1,
                    masterColor = color
                };

                int occurrences = CountColor(endTwo, color);

                if (occurrences == 0)
                {
                    // That conductor simply isn't present on the far end - open circuit.
                    result.remotePin = 0;
                    result.status = PinTestStatus.Open;
                    report.continuityOk = false;
                }
                else if (occurrences > 1)
                {
                    // Two pins would answer at once. A real tester reads this as a short.
                    result.remotePin = IndexOfColor(endTwo, color) + 1;
                    result.status = PinTestStatus.Ambiguous;
                    report.continuityOk = false;
                }
                else
                {
                    int remoteIndex = IndexOfColor(endTwo, color);
                    result.remotePin = remoteIndex + 1;
                    result.status = (remoteIndex == i) ? PinTestStatus.Straight : PinTestStatus.Crossed;
                }

                report.pins[i] = result;
            }

            report.isStraightThrough = IsIdentityMap(report);
            report.isCrossover = IsCrossoverMap(report);
            report.isRollover = IsRolloverMap(report);

            report.endOneStandard = IdentifyStandard(endOne);
            report.endTwoStandard = IdentifyStandard(endTwo);
            report.detectedType = ResolveDetectedType(report);

            if (detectSplitPairs)
            {
                report.splitPairDetected = HasSplitPair(endOne) || HasSplitPair(endTwo);
            }

            if (target != null)
            {
                report.targetName = string.IsNullOrEmpty(target.displayName) ? target.name : target.displayName;
                report.matchesTarget = target.ValidateEnd(1, endOne) && target.ValidateEnd(2, endTwo);
            }
            else
            {
                // No target set: fall back to "does this work as a real cable at all".
                report.matchesTarget = report.continuityOk && (report.isStraightThrough || report.isCrossover);
            }

            BuildReadout(report);
            return report;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static int CountColor(WireColor[] arrangement, WireColor color)
        {
            int count = 0;
            for (int i = 0; i < arrangement.Length; i++)
            {
                if (arrangement[i] == color) count++;
            }
            return count;
        }

        static int IndexOfColor(WireColor[] arrangement, WireColor color)
        {
            for (int i = 0; i < arrangement.Length; i++)
            {
                if (arrangement[i] == color) return i;
            }
            return -1;
        }

        static bool IsIdentityMap(CableTestReport report)
        {
            foreach (var pin in report.pins)
            {
                if (pin.status != PinTestStatus.Straight) return false;
            }
            return true;
        }

        static bool IsCrossoverMap(CableTestReport report)
        {
            // Textbook crossover swaps the orange and green pairs: 1<->3 and 2<->6.
            int[] expected = { 3, 6, 1, 4, 5, 2, 7, 8 };
            for (int i = 0; i < 8; i++)
            {
                if (report.pins[i].remotePin != expected[i]) return false;
            }
            return true;
        }

        static bool IsRolloverMap(CableTestReport report)
        {
            // Rollover / console cable: pin N at one end lands on pin 9-N at the other.
            for (int i = 0; i < 8; i++)
            {
                if (report.pins[i].remotePin != 8 - i) return false;
            }
            return true;
        }

        /// <summary>Which TIA/EIA-568 standard this end's order matches, or null if it matches neither.</summary>
        public static WiringStandard? IdentifyStandard(WireColor[] arrangement)
        {
            if (Matches(arrangement, WireStandardSO.DefaultT568A())) return WiringStandard.T568A;
            if (Matches(arrangement, WireStandardSO.DefaultT568B())) return WiringStandard.T568B;
            return null;
        }

        static bool Matches(WireColor[] arrangement, WireColor[] reference)
        {
            if (arrangement == null || reference == null || arrangement.Length != reference.Length) return false;
            for (int i = 0; i < reference.Length; i++)
            {
                if (arrangement[i] != reference[i]) return false;
            }
            return true;
        }

        static CableType ResolveDetectedType(CableTestReport report)
        {
            if (!report.continuityOk) return CableType.Invalid;

            if (report.endOneStandard.HasValue && report.endTwoStandard.HasValue)
            {
                return report.endOneStandard.Value == report.endTwoStandard.Value
                    ? CableType.StraightThrough
                    : CableType.Crossover;
            }

            // Neither end is a recognised standard, but the wiremap might still be a real cable.
            if (report.isStraightThrough) return CableType.StraightThrough;
            if (report.isCrossover) return CableType.Crossover;
            if (report.isRollover) return CableType.Rollover;
            return CableType.Invalid;
        }

        /// <summary>True when any twisted pair is split across pins that aren't a valid pair.</summary>
        static bool HasSplitPair(WireColor[] arrangement)
        {
            for (int p = 0; p < ColorPairs.GetLength(0); p++)
            {
                int a = IndexOfColor(arrangement, ColorPairs[p, 0]) + 1;
                int b = IndexOfColor(arrangement, ColorPairs[p, 1]) + 1;
                if (a == 0 || b == 0) continue; // missing conductor is an Open, reported separately

                bool onValidPair = false;
                for (int v = 0; v < ValidPinPairs.GetLength(0); v++)
                {
                    int lo = ValidPinPairs[v, 0], hi = ValidPinPairs[v, 1];
                    if ((a == lo && b == hi) || (a == hi && b == lo)) { onValidPair = true; break; }
                }

                if (!onValidPair) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Readout
        // ------------------------------------------------------------------

        static void BuildReadout(CableTestReport report)
        {
            report.readoutLines.Clear();
            report.readoutLines.Add("WIREMAP TEST");
            report.readoutLines.Add("MASTER  ->  REMOTE");

            foreach (var pin in report.pins)
            {
                string remote = pin.remotePin == 0 ? "--" : pin.remotePin.ToString();
                string colorName = WireColorPalette.ToDisplayName(pin.masterColor);
                string note = StatusNote(pin.status);
                report.readoutLines.Add($"  {pin.masterPin}  ->  {remote}   {colorName} ({note})");
            }

            report.readoutLines.Add("");
            report.readoutLines.Add($"End 1 standard : {StandardName(report.endOneStandard)}");
            report.readoutLines.Add($"End 2 standard : {StandardName(report.endTwoStandard)}");
            report.readoutLines.Add($"Detected cable : {DetectedName(report.detectedType)}");

            if (report.splitPairDetected)
                report.readoutLines.Add("WARNING: split pair - a twisted pair is spread across non-paired pins.");

            if (!string.IsNullOrEmpty(report.targetName))
                report.readoutLines.Add($"Required cable : {report.targetName}");

            report.readoutLines.Add("");
            report.readoutLines.Add(report.Passed ? "RESULT: PASS" : "RESULT: FAIL");

            if (!report.Passed)
                report.readoutLines.Add(FailureHint(report));
        }

        static string StatusNote(PinTestStatus status)
        {
            switch (status)
            {
                case PinTestStatus.Open: return "OPEN";
                case PinTestStatus.Ambiguous: return "SHORT";
                case PinTestStatus.Crossed: return "crossed";
                default: return "ok";
            }
        }

        static string StandardName(WiringStandard? standard)
        {
            return standard.HasValue ? standard.Value.ToString() : "non-standard";
        }

        /// <summary>Player-facing name for a detected cable type.</summary>
        public static string DetectedName(CableType type)
        {
            switch (type)
            {
                case CableType.StraightThrough: return "Straight-Through";
                case CableType.Crossover: return "Crossover";
                case CableType.Rollover: return "Rollover";
                default: return "Invalid / miswired";
            }
        }

        /// <summary>One line telling the player what to fix. Ordered by severity so the most
        /// fundamental problem is the one they see.</summary>
        public static string FailureHint(CableTestReport report)
        {
            var openPins = new StringBuilder();
            foreach (var pin in report.pins)
            {
                if (pin.status == PinTestStatus.Open)
                {
                    if (openPins.Length > 0) openPins.Append(", ");
                    openPins.Append(pin.masterPin);
                }
            }

            if (openPins.Length > 0)
                return $"Open conductor on pin(s) {openPins}. That wire never reaches the far end - " +
                       "check it's fully seated against the connector's front before crimping.";

            foreach (var pin in report.pins)
            {
                if (pin.status == PinTestStatus.Ambiguous)
                    return $"Two conductors answer on pin {pin.masterPin}. Each colour must appear " +
                           "exactly once per end.";
            }

            if (report.splitPairDetected)
                return "Split pair: a twisted pair is spread across pins that aren't a pair. " +
                       "Pairs belong on 1&2, 3&6, 4&5 and 7&8.";

            if (!string.IsNullOrEmpty(report.targetName))
                return $"Continuity is fine, but the pinout isn't a {report.targetName}. " +
                       $"This cable tests as {DetectedName(report.detectedType)}.";

            return "The pinout doesn't match a recognised TIA/EIA-568 cable.";
        }
    }
}
