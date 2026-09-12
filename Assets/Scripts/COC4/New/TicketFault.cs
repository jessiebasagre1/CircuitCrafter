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
    /// Here a fault says only three things: how to break it, how to tell it's
    /// been fixed, and what the machine DOES when you press power. Everything
    /// else - gating, task registration, the diagnosis gate, progress reporting,
    /// assessment records - lives once, in TicketFaultCoordinator. A new kind of
    /// fault is a small class, not a pair.
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
        /// <summary>What pressing the power button looks like with this fault present.</summary>
        public enum BootImpact
        {
            /// <summary>Use whatever this kind of fault normally does. Correct for
            /// almost every fault - only override when a particular one behaves
            /// differently from others of its type.</summary>
            Auto = 0,

            /// <summary>The machine never reaches Windows.</summary>
            StopsBoot = 1,

            /// <summary>The machine boots fine; the problem shows up afterwards.</summary>
            BootsButFaulty = 2,
        }


        [Header("Scoping")]
        [Tooltip("Only inject this fault when this ticket is the active one, e.g. \"ticket_coc1_ram_missing\". Leave blank ONLY if this scene will never host another ticket - a repair site shared by two tickets would otherwise show both faults at once.")]
        [SerializeField] protected string activeForTicketId = "";

        [Tooltip("Shown in the report and in feedback, e.g. \"PSU cable unplugged\". Keep it short and factual - it is what the learner is told they fixed.")]
        [SerializeField] private string faultSummary = "";

        [Tooltip("Optional. What the power-on test calls this check, e.g. \"Display output\" or \"Memory detected\". Written as the thing being TESTED, not the thing that was wrong - so the verification reads like a boot test rather than a list of faults. Falls back to Fault Summary.")]
        [SerializeField] private string verificationLabel = "";

        [Header("What the machine does when powered on")]
        [Tooltip("The SYMPTOM the learner sees on the monitor before they have fixed anything, e.g. \"The fans spin and the power light is on, but the screen stays black.\"\n\nWrite what the machine DOES, never what is wrong with it - naming the fault here hands the learner the diagnosis for free and defeats the whole exercise. Leave blank for a sensible default for this kind of fault.")]
        [TextArea(2, 4)]
        [SerializeField] private string bootSymptom = "";

        [Tooltip("Leave on Auto unless this particular fault is unusual. Auto means: a missing part or an unplugged cable stops the machine booting, a network fault does not.")]
        [SerializeField] private BootImpact bootImpact = BootImpact.Auto;

        [Tooltip("Optional. The beep pattern a real machine makes for this kind of failure, e.g. " +
                 "\"1 long, 3 short\" for a memory error. Shown and PLAYED on the boot screen.\n\n" +
                 "This is a symptom, not the cause, so it gives nothing away - a learner still has " +
                 "to know what the pattern means, which is exactly the point. Only used when this " +
                 "fault stops the machine booting; leave it blank for a steady tone.")]
        [SerializeField] private string beepCode = "";

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

        /// <summary>What the learner sees on the monitor. Never names the fault.</summary>
        public string BootSymptom =>
            string.IsNullOrWhiteSpace(bootSymptom) ? DefaultBootSymptom : bootSymptom;

        /// <summary>The beep pattern for this fault, or empty for a steady tone.</summary>
        public string BeepCode => beepCode == null ? "" : beepCode.Trim();

        /// <summary>Overridden per fault kind. A subclass knows what its own kind of
        /// breakage looks like from the front of the machine; the Inspector field
        /// above is there for the cases where it doesn't.</summary>
        protected virtual string DefaultBootSymptom =>
            "The computer does not start up correctly.";

        /// <summary>Overridden per fault kind - see BootImpact.Auto.</summary>
        protected virtual BootImpact DefaultBootImpact => BootImpact.StopsBoot;

        /// <summary>True when this fault stops the machine reaching Windows at all,
        /// as opposed to one that lets it boot and shows up afterwards. The boot
        /// screen needs the difference: a machine with a network fault really does
        /// reach the desktop, and pretending otherwise teaches the wrong symptom.</summary>
        public bool PreventsBoot =>
            (bootImpact == BootImpact.Auto ? DefaultBootImpact : bootImpact) == BootImpact.StopsBoot;

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