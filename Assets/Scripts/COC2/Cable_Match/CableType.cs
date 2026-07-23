using UnityEngine;

namespace CSSSimulation.Networking
{
    public enum CableType
    {
        StraightThrough,
        Crossover,
        Rollover,
        Invalid
    }

    /// <summary>
    /// Derives the resulting cable type from the wiring standard used on each end, and
    /// provides the expected pin arrangement for each end so the crimping/testing stations
    /// can validate a player-arranged wire set against it.
    /// </summary>
    [CreateAssetMenu(fileName = "CableType", menuName = "CSS Simulation/Networking/Cable Type Definition")]
    public class CableTypeSO : ScriptableObject
    {
        [Tooltip("Wiring standard used on Connector End 1.")]
        public WireStandardSO endAStandard;

        [Tooltip("Wiring standard used on Connector End 2.")]
        public WireStandardSO endBStandard;

        [Tooltip("Display name shown to the player, e.g. 'Straight-Through (T568B / T568B)'.")]
        public string displayName;

        public CableType ResolveCableType()
        {
            if (endAStandard == null || endBStandard == null) return CableType.Invalid;

            if (endAStandard.standard == endBStandard.standard)
            {
                return CableType.StraightThrough;
            }

            // T568A on one end, T568B on the other = crossover per TIA/EIA-568 spec.
            bool isAB = (endAStandard.standard == WiringStandard.T568A && endBStandard.standard == WiringStandard.T568B)
                     || (endAStandard.standard == WiringStandard.T568B && endBStandard.standard == WiringStandard.T568A);

            return isAB ? CableType.Crossover : CableType.Invalid;
        }

        /// <summary>Expected wire color at a given pin (1-8) on a given end (1 or 2).</summary>
        public WireColor GetExpectedColorAtPin(int endNumber, int pinNumber)
        {
            WireStandardSO target = endNumber == 1 ? endAStandard : endBStandard;
            if (target == null)
            {
                Debug.LogError($"[CableTypeSO] Missing standard for end {endNumber} on {name}.");
                return default;
            }
            return target.GetExpectedColorAtPin(pinNumber);
        }

        /// <summary>Validates a full 8-wire arrangement on one end of the cable against this definition.</summary>
        public bool ValidateEnd(int endNumber, WireColor[] arrangement)
        {
            WireStandardSO target = endNumber == 1 ? endAStandard : endBStandard;
            return target != null && target.Matches(arrangement);
        }
    }
}