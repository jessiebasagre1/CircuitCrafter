using UnityEngine;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// A component that isn't seated: RAM, GPU, SSD, anything with a socket.
    ///
    /// REPLACES FOUR SCRIPTS: RAMRemovedFaultSetup, PartRemovedFaultSetup,
    /// RAMPresenceFaultTicketReporter and PartSocketFaultTicketReporter. RAM's
    /// "any one slot is enough" rule and a single part's "this exact socket" rule
    /// are the same rule with a different Require All setting, so they don't need
    /// separate classes.
    ///
    /// AUTHOR THE SCENE WITH EVERYTHING SEATED NORMALLY.
    /// This removes the parts at runtime rather than you leaving the sockets
    /// empty in the editor, and that matters for two reasons the original scripts
    /// worked out the hard way: PCPartItem.TryBeginDrag needs a non-null
    /// HomeSocket, so a part that never started in a socket can't be dragged; and
    /// a socket that never held a part has no recorded installed offset, so the
    /// first part dropped into it later lands wrong.
    /// </summary>
    public class PartRemovedFault : TicketFault
    {
        [Header("What to remove")]
        [Tooltip("Every part to pull out at scene start. Each must be seated normally in the editor. For a 'no RAM at all' fault, list all the sticks.")]
        [SerializeField] private PCPartItem[] partsToRemove;

        [Header("What counts as fixed")]
        [Tooltip("The sockets watched for the repair. Usually the same sockets the parts came out of.")]
        [SerializeField] private PartSocket[] socketsToWatch;

        [Tooltip("OFF (the default) means any ONE of these sockets being filled counts as fixed - the right rule for RAM, where a single seated stick boots the machine. Turn ON when every listed socket genuinely has to be filled.")]
        [SerializeField] private bool requireAllSockets;

        /// <summary>Kept vague on purpose. A missing stick of RAM, a missing drive
        /// and a missing graphics card all fail differently, and this class cannot
        /// tell which one it is holding - so the default says only what is true of
        /// all three, and the Boot Symptom field on the Inspector is where the real
        /// symptom goes. Filling it in is worth the thirty seconds: "three short
        /// beeps and no display" is a diagnosable clue, "it doesn't start" is not.</summary>
        protected override string DefaultBootSymptom =>
            "The computer powers up, but it never reaches Windows.";

        protected override void Inject()
        {
            if (partsToRemove == null || partsToRemove.Length == 0)
            {
                Debug.LogWarning("[PartRemovedFault] No parts assigned - nothing will be removed and the machine will look healthy.", this);
                return;
            }

            int moved = 0;

            foreach (var part in partsToRemove)
            {
                if (part == null) continue;

                // Into the hotbar, not the void: the learner has to be able to
                // find the part and put it back. PickupInstant only detaches and
                // hides the 3D object - it knows nothing about the hotbar UI, so
                // the slot has to be filled here, exactly as SaveManager's restore
                // does it.
                var slot = HotbarManager.Instance != null ? HotbarManager.Instance.GetFirstEmptySlot() : null;
                if (slot == null)
                {
                    Debug.LogWarning($"[PartRemovedFault] No empty hotbar slot for '{part.name}' - add more slots, or remove fewer parts. The part has been left seated.", this);
                    continue;
                }

                part.PickupInstant();
                slot.SetItem(part);
                moved++;
            }

            if (moved == 0)
                Debug.LogWarning("[PartRemovedFault] Nothing was actually removed - the fault will not be visible to the learner.", this);
        }

        public override bool IsRepaired()
        {
            if (socketsToWatch == null || socketsToWatch.Length == 0) return false;

            if (requireAllSockets)
            {
                foreach (var socket in socketsToWatch)
                {
                    if (socket == null || socket.IsEmpty) return false;
                }
                return true;
            }

            foreach (var socket in socketsToWatch)
            {
                if (socket != null && !socket.IsEmpty) return true;
            }
            return false;
        }
    }
}