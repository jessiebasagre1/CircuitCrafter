using UnityEngine;

namespace CSSSimulation.Networking
{
    public enum WireColor
    {
        WhiteOrange,
        Orange,
        WhiteGreen,
        Blue,
        WhiteBlue,
        Green,
        WhiteBrown,
        Brown
    }

    public enum WiringStandard
    {
        T568A,
        T568B
    }

    /// <summary>
    /// Defines the correct pin order (position 1-8) for a TIA/EIA-568 wiring standard.
    /// Index 0 in pinOrder = Pin 1, index 7 = Pin 8, as viewed with the clip facing away from you.
    /// </summary>
    [CreateAssetMenu(fileName = "WireStandard", menuName = "CSS Simulation/Networking/Wire Standard")]
    public class WireStandardSO : ScriptableObject
    {
        [Tooltip("Which named standard this asset represents.")]
        public WiringStandard standard;

        [Tooltip("Pin 1 through Pin 8 wire color order.")]
        public WireColor[] pinOrder = new WireColor[8];

        private void Reset()
        {
            pinOrder = standard == WiringStandard.T568A ? DefaultT568A() : DefaultT568B();
        }

        public static WireColor[] DefaultT568A()
        {
            return new WireColor[]
            {
                WireColor.WhiteGreen,
                WireColor.Green,
                WireColor.WhiteOrange,
                WireColor.Blue,
                WireColor.WhiteBlue,
                WireColor.Orange,
                WireColor.WhiteBrown,
                WireColor.Brown
            };
        }

        public static WireColor[] DefaultT568B()
        {
            return new WireColor[]
            {
                WireColor.WhiteOrange,
                WireColor.Orange,
                WireColor.WhiteGreen,
                WireColor.Blue,
                WireColor.WhiteBlue,
                WireColor.Green,
                WireColor.WhiteBrown,
                WireColor.Brown
            };
        }

        /// <summary>Returns the expected wire color at a given pin (1-indexed, 1-8).</summary>
        public WireColor GetExpectedColorAtPin(int pinNumber)
        {
            int idx = pinNumber - 1;
            if (idx < 0 || idx >= pinOrder.Length)
            {
                Debug.LogError($"[WireStandardSO] Pin number {pinNumber} out of range on {name}.");
                return default;
            }
            return pinOrder[idx];
        }

        /// <summary>Checks whether a full 8-wire arrangement matches this standard exactly.</summary>
        public bool Matches(WireColor[] arrangement)
        {
            if (arrangement == null || arrangement.Length != pinOrder.Length) return false;
            for (int i = 0; i < pinOrder.Length; i++)
            {
                if (arrangement[i] != pinOrder[i]) return false;
            }
            return true;
        }
    }
}