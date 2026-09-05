using UnityEngine;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// A machine choked with dust. The repair is not a part swap - it is work.
    ///
    /// ATTACH THIS TO: the TicketFaults object, like every other fault. Assign the
    /// DustPatch objects it should switch on.
    ///
    /// WHY THIS FAULT IS WORTH HAVING
    /// Every other fault in the library is fixed by putting the right thing in the
    /// right place, and a learner can pass all of them without ever doing the
    /// unglamorous half of the job. Cleaning is most of real servicing, it is
    /// explicitly in the CSS NC II competency, and it is the one repair where the
    /// answer is "keep going until it is actually done" rather than "click the
    /// correct object". It also teaches tool selection, which nothing else here
    /// does: the wrong tool simply will not shift it.
    ///
    /// AUTHOR THE PATCHES INACTIVE. Same rule as the rest of the project - the
    /// scene is clean, and this switches the dirt on at runtime for its own ticket.
    ///
    /// IT DOES NOT STOP THE MACHINE BOOTING, and that is the honest symptom: a
    /// dusty PC starts fine and then runs hot and loud, and shuts itself down under
    /// load. Boot Impact defaults to Boots But Faulty for exactly that reason.
    /// </summary>
    public class DustFault : TicketFault
    {
        [Header("What is dirty")]
        [Tooltip("Every patch of dirt this fault switches on. Author them INACTIVE in the scene - this activates them, and only for its own ticket.")]
        [SerializeField] private DustPatch[] patches;

        [Header("What counts as fixed")]
        [Tooltip("ON (the default) means every patch has to be cleaned. Turn it off to accept a single patch, which is almost never what you want for a cleaning job.")]
        [SerializeField] private bool requireAllPatches = true;

        /// <summary>Dust does not stop a machine starting. It starts, runs hot, and
        /// gives up later - which is a different and more interesting symptom to
        /// diagnose than a black screen.</summary>
        protected override BootImpact DefaultBootImpact => BootImpact.BootsButFaulty;

        protected override string DefaultBootSymptom =>
            "It starts, but the fans run loud and fast the whole time and the case is hot to the touch.";

        // ==================================================================

        protected override void Inject()
        {
            if (patches == null || patches.Length == 0)
            {
                Debug.LogWarning("[DustFault] No dust patches assigned - the machine will look perfectly clean and the ticket can never be closed.", this);
                return;
            }

            int shown = 0;
            foreach (var patch in patches)
            {
                if (patch == null) continue;
                patch.MakeDirty();
                shown++;
            }

            if (shown == 0)
                Debug.LogWarning("[DustFault] Every entry in Patches is empty - nothing was made dirty.", this);
        }

        public override bool IsRepaired()
        {
            if (patches == null || patches.Length == 0) return false;

            if (requireAllPatches)
            {
                foreach (var patch in patches)
                {
                    if (patch == null || !patch.IsClean) return false;
                }
                return true;
            }

            foreach (var patch in patches)
            {
                if (patch != null && patch.IsClean) return true;
            }
            return false;
        }

        /// <summary>How far through the cleaning the learner is, 0 to 1. Useful for
        /// a progress bar, and for a report line that says "left it half done".</summary>
        public float Progress
        {
            get
            {
                if (patches == null || patches.Length == 0) return 0f;

                float total = 0f;
                int counted = 0;
                foreach (var patch in patches)
                {
                    if (patch == null) continue;
                    total += patch.Cleanliness;
                    counted++;
                }
                return counted == 0 ? 0f : total / counted;
            }
        }
    }
}
