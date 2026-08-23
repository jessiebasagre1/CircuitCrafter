using System.Collections;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Testing-stage twin of CrimpToolCrimpController: owns the two tester jacks and plays the
    /// "plug the RJ45 in" animation when the player drags a cable end onto one.
    ///
    /// Same three-part shape used everywhere else in this station, so it should feel familiar:
    ///   CablePlugDraggable  = the thing the player drags   (like CrimpToolCrimpDraggable)
    ///   this                = snap, animate, report        (like CrimpToolCrimpController)
    ///   TestingViewController = camera/stage plumbing       (like StrippingViewController)
    ///
    /// Setup:
    ///   1. Add this to your tester object (the same one carrying CableTesterController is fine).
    ///   2. Fill in `jacks` with two entries: endNumber 1 (master jack) and endNumber 2 (remote jack).
    ///   3. For each jack assign plugDropAnchor - an empty GameObject sitting exactly where the
    ///      RJ45 sits when fully seated. This is both the drop zone and the final pose.
    ///   4. Assign plugTransform for each jack - the object the player drags. That's normally the
    ///      matching RJ45 housing from the bench; if dragging the real housing looks wrong because
    ///      your UTP jacket mesh can't stretch, use a proxy RJ45 prop parked next to the tester
    ///      instead and hide the bench cable while the testing view is open.
    ///   5. If you have a plug-in clip, assign jackAnimator + playTrigger + clipName per jack.
    ///      Leave the animator empty to just snap-lerp the plug in with no clip, which still reads
    ///      fine.
    /// </summary>
    public class CableTesterPlugController : MonoBehaviour
    {
        /// <summary>One jack on the tester, and everything needed to seat a plug into it.</summary>
        [System.Serializable]
        public class TesterJack
        {
            [Tooltip("Which cable end this jack accepts: 1 = master unit, 2 = remote unit. " +
                     "Must match the endNumber on the matching CablePlugDraggable.")]
            public int endNumber = 1;

            [Tooltip("Empty GameObject at the fully-seated position/rotation of the RJ45 in this jack. " +
                     "Doubles as the drop zone the player has to drag near.")]
            public Transform plugDropAnchor;

            [Tooltip("The object the player drags into this jack - the RJ45 housing for this end, " +
                     "or a proxy plug prop. Must have a CablePlugDraggable on it with a matching endNumber.")]
            public Transform plugTransform;

            [Tooltip("Optional. Animator holding this jack's plug-in clip. Leave empty to just snap the plug in.")]
            public Animator jackAnimator;
            [Tooltip("Trigger fired on jackAnimator. Ignored when jackAnimator is empty.")]
            public string playTrigger = "PlugIn";
            [Tooltip("Clip name used to look up the clip's length. Leave blank to use fallbackClipLength.")]
            public string clipName = "";

            [HideInInspector] public bool seated;
        }

        public static CableTesterPlugController Instance { get; private set; }

        [Header("Jacks")]
        [Tooltip("Add exactly two entries: endNumber 1 and endNumber 2.")]
        [SerializeField] private TesterJack[] jacks = new TesterJack[2];

        [Header("Motion")]
        [Tooltip("Seconds the plug takes to ease from where it was dropped into the jack.")]
        [SerializeField] private float snapDuration = 0.25f;
        [Tooltip("Used when a jack has an animator but its clipName isn't found (or is blank).")]
        [SerializeField] private float fallbackClipLength = 1f;
        [Tooltip("Extra seconds waited on top of the clip length, to cover the animator's transition blend.")]
        [SerializeField] private float transitionAllowance = 0.1f;

        [Header("Refs")]
        [Tooltip("Told each time a plug seats, so it can auto-start the test if that's enabled. " +
                 "Leave empty to fall back to CableTesterController.Instance.")]
        [SerializeField] private CableTesterController tester;

        /// <summary>True while a plug-in animation is playing.</summary>
        public bool IsSeatingPlug { get; private set; }

        /// <summary>Fires with the end number each time a plug finishes seating.</summary>
        public System.Action<int> OnPlugSeated;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            if (tester == null) tester = CableTesterController.Instance;
        }

        public TesterJack[] Jacks => jacks;

        public bool IsPlugSeated(int endNumber)
        {
            var jack = FindJack(endNumber);
            return jack != null && jack.seated;
        }

        /// <summary>True when every configured jack has its plug in. CableTesterController checks
        /// this before it will run a test.</summary>
        public bool AllPlugsSeated()
        {
            if (jacks == null || jacks.Length == 0) return false;
            foreach (var jack in jacks)
            {
                if (jack != null && !jack.seated) return false;
            }
            return true;
        }

        /// <summary>Called by CablePlugDraggable once the player releases a plug near this jack.</summary>
        public void TryDropOnJack(int endNumber)
        {
            if (IsSeatingPlug) return;

            var jack = FindJack(endNumber);
            if (jack == null)
            {
                Debug.LogWarning($"[CableTesterPlugController] No jack configured for end {endNumber}.", this);
                return;
            }

            if (jack.seated) return;

            // A cable end that hasn't been crimped has nothing to test - refuse it rather than
            // letting the player plug a loose bundle of wires into the tester.
            var station = CrimpingStationController.Instance;
            if (station != null && !station.IsEndCrimped(endNumber))
            {
                Debug.Log($"[CableTesterPlugController] End {endNumber} hasn't been crimped yet.", this);
                return;
            }

            StartCoroutine(SeatPlug(jack));
        }

        IEnumerator SeatPlug(TesterJack jack)
        {
            IsSeatingPlug = true;

            if (jack.plugDropAnchor != null && jack.plugTransform != null)
            {
                Vector3 startPos = jack.plugTransform.position;
                Quaternion startRot = jack.plugTransform.rotation;
                float t = 0f;
                while (t < snapDuration)
                {
                    t += Time.deltaTime;
                    float f = Mathf.Clamp01(t / snapDuration);
                    jack.plugTransform.position = Vector3.Lerp(startPos, jack.plugDropAnchor.position, f);
                    jack.plugTransform.rotation = Quaternion.Slerp(startRot, jack.plugDropAnchor.rotation, f);
                    yield return null;
                }
                jack.plugTransform.SetPositionAndRotation(jack.plugDropAnchor.position, jack.plugDropAnchor.rotation);
            }

            if (jack.jackAnimator != null && !string.IsNullOrEmpty(jack.playTrigger))
            {
                jack.jackAnimator.ResetTrigger(jack.playTrigger);
                jack.jackAnimator.SetTrigger(jack.playTrigger);
                yield return new WaitForSeconds(GetClipLength(jack) + transitionAllowance);
            }

            jack.seated = true;
            IsSeatingPlug = false;

            // Stop the plug being draggable again once it's home - a seated RJ45 shouldn't wobble
            // back out mid-test.
            if (jack.plugTransform != null)
            {
                var draggable = jack.plugTransform.GetComponent<CablePlugDraggable>();
                if (draggable != null) draggable.enabled = false;
            }

            Debug.Log($"[CableTesterPlugController] End {jack.endNumber} plugged into the tester.", this);
            OnPlugSeated?.Invoke(jack.endNumber);

            if (tester == null) tester = CableTesterController.Instance;
            if (tester != null) tester.NotifyPlugSeated();
        }

        /// <summary>Unseats both plugs so the testing stage can be replayed.</summary>
        public void ResetPlugs()
        {
            StopAllCoroutines();
            IsSeatingPlug = false;

            if (jacks == null) return;
            foreach (var jack in jacks)
            {
                if (jack == null) continue;
                jack.seated = false;
                if (jack.plugTransform == null) continue;
                var draggable = jack.plugTransform.GetComponent<CablePlugDraggable>();
                if (draggable != null) draggable.ResetToHome();
            }
        }

        TesterJack FindJack(int endNumber)
        {
            if (jacks == null) return null;
            foreach (var jack in jacks)
            {
                if (jack != null && jack.endNumber == endNumber) return jack;
            }
            return null;
        }

        float GetClipLength(TesterJack jack)
        {
            if (jack.jackAnimator != null && !string.IsNullOrEmpty(jack.clipName))
            {
                var controller = jack.jackAnimator.runtimeAnimatorController;
                if (controller != null)
                {
                    foreach (var clip in controller.animationClips)
                    {
                        if (clip.name == jack.clipName) return clip.length;
                    }
                }
            }
            return fallbackClipLength;
        }
    }
}
