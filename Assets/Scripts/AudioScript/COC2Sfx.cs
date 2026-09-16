using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Every sound COC 2 makes, in one place.
    ///
    /// ATTACH THIS TO: the same object as your SfxPlayer in the COC 2 scene - the Managers
    /// object. One per scene. It needs an SfxPlayer beside it (or anywhere in the scene),
    /// because that is what owns the AudioSource, the master volume and the pitch rules;
    /// this component only holds the clips and decides which one an event deserves.
    ///
    /// WHY A SEPARATE COMPONENT RATHER THAN MORE FIELDS ON SfxPlayer
    /// SfxPlayer's six fields are the ones every module shares - pick something up, seat it,
    /// stow it, be refused. The eleven below are COC 2's alone: nothing in COC 1 punches down
    /// a conductor or watches a tester chase eight pins. Putting them on the shared component
    /// would mean COC 1's and COC 4's Inspectors growing a row of fields they can never use,
    /// and a learner or a marker opening the wrong one to find out why a sound is missing.
    ///
    /// SfxPlayer.PlayCustom is still what plays them, so the learner's SFX Volume, the
    /// developer's master volume, the pitch jitter and the retrigger guard all apply exactly
    /// as they do everywhere else. There is one mixer in this game, not two.
    ///
    /// EVERY FIELD IS OPTIONAL. Leave a clip empty and that event is silent - it does not
    /// warn, and nothing else changes. That is deliberate: you should be able to wire three
    /// of these, hear how the stage feels, and add the rest later.
    /// </summary>
    [DisallowMultipleComponent]
    public class COC2Sfx : MonoBehaviour
    {
        public static COC2Sfx Instance { get; private set; }

        // ==================================================================
        [Header("Crimping - wires into the RJ45 pins")]

        [Tooltip("A wire is lifted out of the tray, or pulled back out of a pin.\n\n" +
                 "Wants to be quiet and dry - a single conductor being picked up is barely a " +
                 "sound at all, and this one fires more often than any other in the stage.")]
        public SfxEntry wirePickup = new SfxEntry();

        [Tooltip("A wire drops into a pin slot and seats.\n\n" +
                 "THE MOST IMPORTANT SOUND IN THE STAGE. Eight of these in a row IS the crimping " +
                 "task, and it is the only confirmation the learner gets that a wire went in " +
                 "where they aimed it - the visual change is a few millimetres of movement " +
                 "behind a connector housing. Make it crisp and short.")]
        public SfxEntry wireSeated = new SfxEntry();

        [Tooltip("A wire was released somewhere invalid and has gone back to the tray.\n\n" +
                 "Not a failure sound. Dropping a wire short of a pin is a slip, not a mistake, " +
                 "and a harsh buzzer for a slip trains hesitation. Something soft and low.")]
        public SfxEntry wireReturned = new SfxEntry();

        // ==================================================================
        [Header("Patch panel - conductors into the block")]

        [Tooltip("A conductor is laid into a channel, before it is struck.\n\n" +
                 "The counterpart of Wire Seated, and it should NOT be the same clip. A " +
                 "conductor resting in an unpunched channel is a provisional thing - it can " +
                 "still be lifted out - and the sound should be softer than the punch that " +
                 "follows it, or the stage loses its shape.")]
        public SfxEntry conductorSeated = new SfxEntry();

        [Tooltip("A seated but unstruck conductor is lifted back out of its channel.")]
        public SfxEntry conductorLifted = new SfxEntry();

        [Tooltip("The punch-down tool strikes. The conductor is in the IDC contact and trimmed.\n\n" +
                 "THIS IS THE ONE TO GET RIGHT. A real punch-down tool is spring-loaded: it " +
                 "resists, then releases with a hard mechanical CLACK and the offcut drops. It " +
                 "is final - a struck conductor does not come back out - and the sound should " +
                 "carry that finality, because nothing on screen says it as clearly.")]
        public SfxEntry conductorPunched = new SfxEntry();

        [Tooltip("A drop or a strike was refused - wrong pin, out of order, pin already filled.\n\n" +
                 "Pairs with the hint text that appears at the same moment. Keep it brief and " +
                 "unmusical; it is punctuation for the message, not the message.")]
        public SfxEntry actionRefused = new SfxEntry();

        // ==================================================================
        [Header("Cable tester")]

        [Tooltip("The tester is switched on and the run begins.")]
        public SfxEntry testStart = new SfxEntry();

        [Tooltip("One pin in the chase, when the signal got through.\n\n" +
                 "Fires up to eight times a second at the default step duration, so it must be " +
                 "SHORT and quiet - a tick, not a beep. Leave it empty if the chase turns out " +
                 "busier than it is useful.")]
        public SfxEntry testPinOk = new SfxEntry();

        [Tooltip("One pin in the chase, when nothing answered - an open conductor.\n\n" +
                 "Same length as the OK tick, lower and duller. This is the one that teaches: a " +
                 "learner who can HEAR which of the eight steps went wrong does not have to " +
                 "catch the red LED as it passes.")]
        public SfxEntry testPinFault = new SfxEntry();

        [Tooltip("The cable passed. Wiremap correct end to end.")]
        public SfxEntry testPassed = new SfxEntry();

        [Tooltip("The cable failed.\n\n" +
                 "Flat and factual, not a game-over sting. In this module a failed cable is the " +
                 "expected outcome of a first attempt - the stage exists to be retried - and a " +
                 "sound that reads as punishment makes a learner reluctant to test, which is " +
                 "the single worst habit this module could teach.")]
        public SfxEntry testFailed = new SfxEntry();

        // ==================================================================

        [Header("Diagnostics")]
        [Tooltip("Log every COC 2 sound as it is asked for, and say when one has no clip.")]
        public bool logPlays = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[COC2Sfx] Duplicate on '{name}' - one already exists on " +
                                 $"'{Instance.name}'. Destroying this one.", this);
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            // Start, not Awake. SfxPlayer claims its own Instance in Awake, and the order two
            // components on the same object wake in is not defined - checking here means the
            // answer is about the scene rather than about which script Unity happened to run
            // first.
            if (SfxPlayer.Instance == null)
            {
                Debug.LogWarning("[COC2Sfx] There is no SfxPlayer in this scene, so every sound " +
                                 "below will be silent. SfxPlayer owns the AudioSource and the " +
                                 "volume rules; this component only holds the clips. Add an " +
                                 "SfxPlayer (and an AudioSource) to the same object.", this);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ==================================================================
        // Entry points. Static and null-safe on purpose: every call site is one line inside
        // a method that already works, and a missing COC2Sfx must not be able to break the
        // stage it is decorating.
        // ==================================================================

        public static void WirePickedUp() => Play(Instance?.wirePickup, "wirePickup");
        public static void WireSeated() => Play(Instance?.wireSeated, "wireSeated");
        public static void WireReturned() => Play(Instance?.wireReturned, "wireReturned");

        public static void ConductorSeated() => Play(Instance?.conductorSeated, "conductorSeated");
        public static void ConductorLifted() => Play(Instance?.conductorLifted, "conductorLifted");
        public static void ConductorPunched() => Play(Instance?.conductorPunched, "conductorPunched");
        public static void ActionRefused() => Play(Instance?.actionRefused, "actionRefused");

        public static void TestStarted() => Play(Instance?.testStart, "testStart");
        public static void TestPassed() => Play(Instance?.testPassed, "testPassed");
        public static void TestFailed() => Play(Instance?.testFailed, "testFailed");

        /// <summary>One step of the LED chase. <paramref name="ok"/> false means the pin was
        /// open or ambiguous - nothing answered.</summary>
        public static void TestPinStep(bool ok)
        {
            // bypassRetriggerGuard, because the chase deliberately plays the same clip eight
            // times in a row at a fixed interval. The guard exists to stop ONE action being
            // heard twice; here every tick is a separate reading, and swallowing the repeats
            // would leave the learner hearing the first pin and then silence.
            Play(ok ? Instance?.testPinOk : Instance?.testPinFault,
                 ok ? "testPinOk" : "testPinFault",
                 bypassRetriggerGuard: true);
        }

        // ==================================================================

        private static void Play(SfxEntry entry, string fieldName, bool bypassRetriggerGuard = false)
        {
            if (Instance == null) return;

            if (entry == null || !entry.HasSound)
            {
                if (Instance.logPlays)
                    Debug.Log($"[COC2Sfx] '{fieldName}' fired with no clip assigned - silent.", Instance);
                return;
            }

            if (Instance.logPlays) Debug.Log($"[COC2Sfx] {fieldName}", Instance);

            SfxPlayer.PlayCustom(entry, bypassRetriggerGuard);
        }

        // ==================================================================

        [ContextMenu("Report sound setup")]
        private void ReportSoundSetup()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[COC2Sfx] on '{name}':");
            sb.AppendLine(SfxPlayer.Instance == null
                ? "  NO SfxPlayer IN THIS SCENE - everything below is silent regardless of clips."
                : $"  SfxPlayer found on '{SfxPlayer.Instance.name}'.");

            Describe(sb, "Wire pickup", wirePickup);
            Describe(sb, "Wire seated", wireSeated);
            Describe(sb, "Wire returned", wireReturned);
            Describe(sb, "Conductor seated", conductorSeated);
            Describe(sb, "Conductor lifted", conductorLifted);
            Describe(sb, "Conductor punched", conductorPunched);
            Describe(sb, "Action refused", actionRefused);
            Describe(sb, "Test start", testStart);
            Describe(sb, "Test pin OK", testPinOk);
            Describe(sb, "Test pin fault", testPinFault);
            Describe(sb, "Test passed", testPassed);
            Describe(sb, "Test failed", testFailed);

            Debug.Log(sb.ToString(), this);
        }

        private static void Describe(System.Text.StringBuilder sb, string label, SfxEntry entry)
        {
            if (entry == null || !entry.HasSound)
            {
                sb.AppendLine($"  {label}: no clip - SILENT");
                return;
            }

            AudioClip c = entry.clip;
            int alts = entry.alternates != null ? entry.alternates.Length : 0;
            sb.AppendLine($"  {label}: '{(c != null ? c.name : "(alternates only)")}'" +
                          (alts > 0 ? $" +{alts} alternate(s)" : "") +
                          $"  volume {entry.volume:F2}" +
                          (entry.ignorePitchJitter ? "  [fixed pitch]" : ""));
        }

        [ContextMenu("Test all sounds")]
        private void TestAllSounds()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[COC2Sfx] Enter Play mode first - nothing plays audio in the Editor " +
                                 "while stopped.", this);
                return;
            }
            StartCoroutine(PlayEachInTurn());
        }

        private System.Collections.IEnumerator PlayEachInTurn()
        {
            string[] labels =
            {
                "wirePickup", "wireSeated", "wireReturned",
                "conductorSeated", "conductorLifted", "conductorPunched", "actionRefused",
                "testStart", "testPinOk", "testPinFault", "testPassed", "testFailed",
            };

            SfxEntry[] all =
            {
                wirePickup, wireSeated, wireReturned,
                conductorSeated, conductorLifted, conductorPunched, actionRefused,
                testStart, testPinOk, testPinFault, testPassed, testFailed,
            };

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !all[i].HasSound) continue;
                Debug.Log($"[COC2Sfx] playing {labels[i]}", this);
                SfxPlayer.PlayCustom(all[i], bypassRetriggerGuard: true);
                yield return new WaitForSecondsRealtime(0.7f);
            }
        }
    }
}
