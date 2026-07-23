using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Sits on the housing object for one connector end (e.g. RJ45_Housing_End1_Body's parent).
    /// Collects the 8 WirePinSlot children, reads what's currently placed in each, and checks
    /// it against a CableTypeSO. Fire OnValidated from here into your DialogManager listener.
    /// </summary>
    public class ConnectorEnd : MonoBehaviour
    {
        [Tooltip("Which end of the cable this represents, 1 or 2. Must match the WirePinSlots below.")]
        public int endNumber = 1;

        [Tooltip("The 8 WirePinSlot children for this connector, in any order " +
                  "(they're matched up by pinNumber automatically).")]
        public WirePinSlot[] slots = new WirePinSlot[8];

        [Tooltip("Fired after CheckCompletion(), true if all 8 slots are filled correctly for the given CableTypeSO.")]
        public UnityEvent<bool> OnValidated;

        /// <summary>Returns true only when all 8 slots have a wire placed.</summary>
        public bool IsFullyArranged()
        {
            foreach (var slot in slots)
            {
                if (slot == null || !slot.IsFilled) return false;
            }
            return true;
        }

        /// <summary>Reads the current wire in each slot into a pin-ordered WireColor[8].</summary>
        public WireColor[] GetArrangement()
        {
            WireColor[] arrangement = new WireColor[8];
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                int idx = slot.pinNumber - 1;
                if (idx >= 0 && idx < 8 && slot.IsFilled)
                {
                    arrangement[idx] = slot.CurrentWire.color;
                }
            }
            return arrangement;
        }

        /// <summary>
        /// Checks the current arrangement against the given cable type definition and
        /// fires OnValidated with the result. Call this whenever a wire is placed/removed,
        /// or on a "Check Cable" button press — whichever fits your crimping station flow.
        /// </summary>
        public bool CheckCompletion(CableTypeSO cableType)
        {
            if (cableType == null)
            {
                Debug.LogError("[ConnectorEnd] No CableTypeSO assigned to check against.");
                return false;
            }

            bool result = IsFullyArranged() && cableType.ValidateEnd(endNumber, GetArrangement());
            OnValidated?.Invoke(result);
            return result;
        }
    }
}