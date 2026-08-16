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

        /// <summary>How many of the 8 slots currently have a wire recognized as seated.
        /// Diagnostic helper  if this stays below 8 even though every wire looks visually
        /// placed, the usual cause is a WireIdentity.endNumber on one of the wires not
        /// matching this ConnectorEnd's endNumber, which makes AssignWire() silently
        /// refuse to mark that slot filled (it still logs a warning, easy to miss).</summary>
        public int FilledCount()
        {
            int count = 0;
            foreach (var slot in slots)
            {
                if (slot != null && slot.IsFilled) count++;
            }
            return count;
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
        /// or on a "Check Cable" button press  whichever fits your crimping station flow.
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
        /// <summary>Locks (or unlocks) every wire currently seated in this end's slots so they
        /// can't be dragged back out. Called by CrimpingStationController once a crimp succeeds.</summary>
        public void LockAllWires(bool locked)
        {
            foreach (var slot in slots)
            {
                if (slot == null || !slot.IsFilled) continue;
                var draggable = slot.CurrentWire.GetComponent<WireDraggable>();
                if (draggable != null) draggable.SetLocked(locked);
            }
        }

        /// <summary>Sends every wire currently seated in this end's slots back to its tray
        /// position and empties the slots. Called by CrimpingStationController when an
        /// auto-check finds the arrangement is wrong. No-ops on wires that are already
        /// locked (crimped), same as LockAllWires respects that state.</summary>
        public void ReturnAllWiresHome()
        {
            foreach (var slot in slots)
            {
                if (slot == null || !slot.IsFilled) continue;
                var draggable = slot.CurrentWire.GetComponent<WireDraggable>();
                if (draggable != null) draggable.ForceReturnHome();
            }
        }
    }
}