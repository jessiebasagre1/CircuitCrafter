using UnityEngine;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// One thing wrong with the machine at a repair site.
    ///
    /// WHAT THIS REPLACES
    /// Today every fault needs a hand-written PAIR of scripts - a Setup that
    /// breaks something at Start(), and a Reporter that watches for the fix. Both
    /// repeat the same twenty lines of gating (is a ticket active, is it MY
    /// ticket, is there a RequiredTaskId), the same diagnosis check, and the same
    /// reporting. Six files for three faults, and a seventh and eighth for every
    /// fault after that.
    ///
    /// Here a fault says only two things: how to break it, and how to tell it's
    /// been fixed. Everything else - gating, task registration, the diagnosis
    /// gate, progress reporting, assessment records - lives once, in
    /// TicketFaultCoordinator. A new kind of fault is a small class, not a pair.
    ///
    /// WHY COMPONENTS AND NOT PURE ScriptableObject DATA
    /// The roadmap called this "faults as data". In Unity it can't quite be: a
    /// fault has to point at a specific cable, socket or machine IN THE SCENE,
    /// and a ScriptableObject cannot hold a scene reference. So the honest
    /// version is a component per fault carrying its scene references, with all
    /// the behaviour shared in a base class. That gets the real benefit - one
    /// small class per fault kind, authored in the Inspector - without pretending
    /// a constraint away.
    ///
    /// MULTIPLE FAULTS PER TICKET WORK. Put two on the site and the coordinator
    /// registers a two-step task; the ticket closes when both are fixed. That is
    /// what makes a genuinely diagnostic ticket possible - an unplugged cable AND
    /// a wrong address, where fixing one thing changes the symptom without
    /// curing it.
    /// </summary>
    public abstract class TicketFault : MonoBehaviour
    {
        [Header("Scoping")]
        [Tooltip("Only inject this fault when this ticket is the active one, e.g. \"ticket_coc1_ram_missing\". Leave blank ONLY if this scene will never host another ticket - a repair site shared by two tickets would otherwise show both faults at once.")]
        [SerializeField] protected string activeForTicketId = "";

        [Tooltip("Shown in the report and in feedback, e.g. \"PSU cable unplugged\". Keep it short and factual - it is what the learner is told they fixed.")]
        [SerializeField] private string faultSummary = "";

        [Tooltip("Optional. What the power-on test calls this check, e.g. \"Display output\" or \"Memory detected\". Written as the thing being TESTED, not the thing that was wrong - so the verification reads like a boot test rather than a list of faults. Falls back to Fault Summary.")]
        [SerializeField] private string verificationLabel = "";

        /// <summary>True when this fault belongs to the ticket the player is
        /// currently out on. Decided in Awake, before anything reads it.</summary>
        public bool IsActiveForThisVisit { get; private set; }

        /// <summary>Set by the coordinator once this fault has been counted as
        /// repaired, so it is never counted twice.</summary>
        public bool HasBeenCredited { get; set; }

        public string Summary => string.IsNullOrWhiteSpace(faultSummary) ? GetType().Name : faultSummary;

        /// <summary>What the power-on test calls this check.</summary>
        public string VerificationLabel =>
            string.IsNullOrWhiteSpace(verificationLabel) ? Summary : verificationLabel;

        // ------------------------------------------------------------------

        /// <summary>
        /// Decided in Awake, not Start, and deliberately so: it reads only the
        /// RepairTicketMode statics, needs no other object to exist yet, and the
        /// coordinator's Start() has to be able to trust it. Injection still
        /// happens in Start, because THAT does depend on every other Awake having
        /// run - a part can't be removed from a socket that hasn't recorded its
        /// installed offset yet.
        /// </summary>
        protected virtual void Awake()
        {
            IsActiveForThisVisit = RepairTicketMode.IsActive
                && (string.IsNullOrEmpty(activeForTicketId)
                    || RepairTicketMode.ActiveTicketId == activeForTicketId);
        }

        protected virtual void Start()
        {
            if (!IsActiveForThisVisit)
            {
                // A different ticket sent the player here, or this is normal
                // campaign play. Leave the machine healthy.
                enabled = false;
                return;
            }

            Inject();
            Debug.Log($"[{GetType().Name}] Injected '{Summary}' for ticket '{RepairTicketMode.ActiveTicketId}'.", this);
        }

        /// <summary>Break the thing. Called once, at Start, only when this fault
        /// belongs to the active ticket.</summary>
        protected abstract void Inject();

        /// <summary>True once the learner has genuinely fixed it. Polled by the
        /// coordinator - keep it cheap.</summary>
        public abstract bool IsRepaired();
    }
}