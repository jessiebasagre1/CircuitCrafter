using System.Collections;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Crimping-stage twin of CrimpToolStripController. Attach to the ROOT of your imported
    /// crimping animation prefab (the object holding the Animator), exactly the same way
    /// CrimpToolStripController sits on the root of UTP_Wire_Stripping_Animation.
    ///
    /// Same interaction shape as stripping, deliberately: the player drags the crimping tool
    /// (CrimpToolCrimpDraggable) near one connector end's drop anchor, releases, and this takes
    /// over - snap-lerps the tool into the pose the baked clip was authored at, hides the
    /// draggable copy, plays the clip, then reports the finished end back to
    /// CrimpingStationController.
    ///
    /// The difference from stripping is that this runs TWICE, once per connector end. Because
    /// a baked Blender clip is authored at one fixed location, End 2 is handled by moving the
    /// whole rig (rigRoot) onto that end's rigPose before replaying the same clip. If your clip
    /// already covers both ends, or your ends sit close enough that one pose reads fine for
    /// both, just leave rigPose empty on both entries and nothing gets moved.
    ///
    /// Setup:
    ///   1. Drag your crimping animation prefab into the scene, next to the crimping station.
    ///   2. Attach this script to that prefab's ROOT (the object with the Animator on it).
    ///   3. Assign draggedToolTransform - the SAME GameObject as
    ///      CrimpingViewController.crimpingToolVisual (this project uses one tool object for
    ///      stripping and crimping both).
    ///   4. Fill in the `ends` list with two entries, endNumber 1 and 2. For each, assign
    ///      toolDropAnchor (an empty GameObject at that connector's crimp point - this is both
    ///      the drop zone AND the pose the tool snaps to), and optionally rigPose,
    ///      realConnectorVisual and crimpedVisual.
    ///   5. Set playTrigger / clipName to match your Animator Controller and clip name.
    ///   6. Leave rigVisual EMPTY unless your prefab already happens to group its meshes under a
    ///      single child. With it empty the rig hides itself by switching its Renderers off, which
    ///      needs no reparenting - and reparenting meshes under an animated rig would break the
    ///      clip's animation paths.
    ///   7. Leave this component's GameObject active; CrimpingViewController enables/disables
    ///      the DRAGGABLE, not this.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CrimpToolCrimpController : MonoBehaviour
    {
        /// <summary>Everything this controller needs to know about crimping ONE connector end.</summary>
        [System.Serializable]
        public class CrimpEndTarget
        {
            [Tooltip("1 or 2 - must match the ConnectorEnd.endNumber this represents.")]
            public int endNumber = 1;

            [Tooltip("Empty GameObject at this connector's crimp point. Doubles as the drop zone the " +
                     "player drags the tool near, AND the exact pose the tool snaps to before the clip " +
                     "plays - same role toolIdleAnchor plays in CrimpToolStripController.")]
            public Transform toolDropAnchor;

            [Tooltip("Optional. Where the whole animation rig (rigRoot) is moved to before playing the " +
                     "clip for this end. Needed only because a baked clip is authored at one fixed spot - " +
                     "leave empty on both ends if the rig already sits where it should for both crimps.")]
            public Transform rigPose;

            [Tooltip("Optional. The player's REAL connector object for this end (the one carrying the 8 " +
                     "WirePinSlots). Hidden while the clip plays so it doesn't overlap the rig's own baked " +
                     "connector mesh, then shown again afterwards.")]
            public GameObject realConnectorVisual;

            [Tooltip("Optional. A 'finished / crimped' version of the connector mesh, swapped in once the " +
                     "clip finishes - e.g. one with the plastic tab pressed down. Leave empty to skip.")]
            public GameObject crimpedVisual;

            [HideInInspector] public bool done;
        }

        public static CrimpToolCrimpController Instance { get; private set; }

        [Header("References")]
        [SerializeField] private Animator animator;

        [Tooltip("The tool object the player drags. Assign the SAME GameObject that " +
                 "CrimpToolCrimpDraggable / CrimpingViewController.crimpingToolVisual lives on.")]
        [SerializeField] private Transform draggedToolTransform;

        [Tooltip("The transform moved onto CrimpEndTarget.rigPose before each crimp. Leave empty to use " +
                 "this GameObject's own transform (the animation prefab root), which is what you want " +
                 "unless your rig is nested oddly.")]
        [SerializeField] private Transform rigRoot;

        [Tooltip("Optional. A CHILD object grouping the rig's baked meshes, shown ONLY while the clip is " +
                 "playing. Usually you should LEAVE THIS EMPTY: with no object assigned this script hides " +
                 "the rig by switching off every Renderer underneath it instead, which achieves the same " +
                 "thing without you having to reparent meshes (reparenting would break the clip's " +
                 "animation paths and silently stop the crimp from animating).")]
        [SerializeField] private GameObject rigVisual;

        [Tooltip("Only used when rigVisual is empty. Untick if the rig should just stay visible in its " +
                 "idle pose the whole time - e.g. the rig IS your scene's crimping tool prop.")]
        [SerializeField] private bool hideRigWhenIdle = true;

        [SerializeField] private float snapDuration = 0.25f;

        [Header("Ends")]
        [Tooltip("One entry per connector end. Add exactly two: endNumber 1 and endNumber 2.")]
        [SerializeField] private CrimpEndTarget[] ends = new CrimpEndTarget[2];

        [Header("Animator")]
        [SerializeField] private string playTrigger = "PlayCrimp";
        [Tooltip("Must match the clip name inside your Animator Controller exactly - it's used to look " +
                 "up the clip's length so this knows when the crimp is finished.")]
        [SerializeField] private string clipName = "CrimpTool_Crimp_RJ45";
        [Tooltip("Extra seconds waited on top of the clip length, to cover the Idle->Crimp transition " +
                 "blend. If the stage advances a touch early, raise this slightly.")]
        [SerializeField] private float transitionAllowance = 0.1f;
        [Tooltip("Fallback clip length in seconds, used only if clipName isn't found on the controller.")]
        [SerializeField] private float fallbackClipLength = 4f;

        /// <summary>True while a crimp clip is actually playing - CrimpToolCrimpDraggable
        /// checks this so the tool can't be grabbed mid-animation.</summary>
        public bool IsCrimping { get; private set; }

        /// <summary>Fires with the end number (1 or 2) each time a crimp clip finishes.
        /// CrimpingStationController.CrimpEnd() is the main listener.</summary>
        public System.Action<int> OnEndCrimpComplete;

        private Renderer[] rigRenderers;

        /// <summary>Shows or hides the animation rig - via rigVisual if one was assigned, otherwise
        /// by switching its Renderers on and off.</summary>
        private void SetRigVisible(bool visible)
        {
            if (rigVisual != null)
            {
                rigVisual.SetActive(visible);
                return;
            }

            if (rigRenderers == null) return;
            foreach (var renderer in rigRenderers)
            {
                if (renderer != null) renderer.enabled = visible;
            }
        }

        private void Awake()
        {
            Instance = this;
            if (animator == null) animator = GetComponent<Animator>();
            if (rigRoot == null) rigRoot = transform;

            // Guard against the easiest way to break this: pointing rigVisual at this component's
            // OWN GameObject. Deactivating that would stop this script's coroutines from ever
            // running, and the crimp would silently never finish.
            if (rigVisual == gameObject)
            {
                Debug.LogError("[CrimpToolCrimpController] rigVisual is set to this script's own GameObject. " +
                               "Point it at a CHILD holding the baked meshes instead, or leave it empty.", this);
                rigVisual = null;
            }

            // No grouping child assigned: fall back to toggling the rig's Renderers. Same visual
            // result, but it needs no scene restructuring - which matters, because moving meshes
            // around under an animated rig rewrites the paths the clip binds to and quietly breaks
            // the animation.
            if (rigVisual == null && hideRigWhenIdle)
                rigRenderers = GetComponentsInChildren<Renderer>(true);

            SetRigVisible(false);
        }

        private void Reset()
        {
            animator = GetComponent<Animator>();
            rigRoot = transform;
        }

        /// <summary>Read-only lookup used by CrimpToolCrimpDraggable to work out which end (if any)
        /// the player just dropped the tool on.</summary>
        public CrimpEndTarget[] Ends => ends;

        /// <summary>True once this end's crimp clip has finished playing.</summary>
        public bool IsEndCrimped(int endNumber)
        {
            var target = FindEnd(endNumber);
            return target != null && target.done;
        }

        /// <summary>True when every configured end has been crimped.</summary>
        public bool AllEndsCrimped()
        {
            if (ends == null || ends.Length == 0) return false;
            foreach (var end in ends)
            {
                if (end != null && !end.done) return false;
            }
            return true;
        }

        /// <summary>
        /// Called by CrimpToolCrimpDraggable once the player releases the tool close enough to
        /// this end's toolDropAnchor. Mirrors CrimpToolStripController.TryDropOnStripSocket().
        /// Refuses if that end isn't ready (pins not all seated) or is already crimped, so a
        /// stray drop can't skip a step.
        /// </summary>
        public void TryDropOnCrimpAnchor(int endNumber)
        {
            if (IsCrimping) return;

            var target = FindEnd(endNumber);
            if (target == null)
            {
                Debug.LogWarning($"[CrimpToolCrimpController] No entry in `ends` for end {endNumber}.", this);
                return;
            }

            if (target.done)
            {
                Debug.Log($"[CrimpToolCrimpController] End {endNumber} is already crimped.", this);
                return;
            }

            // The station is the authority on whether this end is arrangeable/crimpable - it knows
            // whether all 8 pins are seated. Keeps the "is this legal yet" rule in one place.
            var station = CrimpingStationController.Instance;
            if (station != null && !station.CanCrimpEnd(endNumber))
            {
                Debug.Log($"[CrimpToolCrimpController] End {endNumber} isn't ready to crimp yet - " +
                          "all 8 pins need a wire in them first.", this);
                return;
            }

            StartCoroutine(SnapAndPlay(target));
        }

        private IEnumerator SnapAndPlay(CrimpEndTarget target)
        {
            IsCrimping = true;

            // Put the rig where this end's crimp happens BEFORE the snap-lerp, so the tool eases
            // toward the pose it will actually be handed off to rather than an old one.
            if (target.rigPose != null && rigRoot != null)
                rigRoot.SetPositionAndRotation(target.rigPose.position, target.rigPose.rotation);

            // Smooth hand-off from "wherever the player dropped it" to the clip's authored start
            // pose, so there's no visual pop. Same routine as CrimpToolStripController.SnapAndPlay().
            if (target.toolDropAnchor != null && draggedToolTransform != null)
            {
                Vector3 startPos = draggedToolTransform.position;
                Quaternion startRot = draggedToolTransform.rotation;
                float t = 0f;
                while (t < snapDuration)
                {
                    t += Time.deltaTime;
                    float f = Mathf.Clamp01(t / snapDuration);
                    draggedToolTransform.position = Vector3.Lerp(startPos, target.toolDropAnchor.position, f);
                    draggedToolTransform.rotation = Quaternion.Slerp(startRot, target.toolDropAnchor.rotation, f);
                    yield return null;
                }
                draggedToolTransform.SetPositionAndRotation(target.toolDropAnchor.position, target.toolDropAnchor.rotation);
            }

            // Hide the draggable copy and the player's real connector, show the rig's baked meshes.
            // Without this you'd see two crimping tools and two connectors overlapping.
            if (draggedToolTransform != null) draggedToolTransform.gameObject.SetActive(false);
            if (target.realConnectorVisual != null) target.realConnectorVisual.SetActive(false);
            SetRigVisible(true);

            animator.ResetTrigger(playTrigger);
            animator.SetTrigger(playTrigger);

            yield return new WaitForSeconds(GetClipLength() + transitionAllowance);

            SetRigVisible(false);
            if (target.realConnectorVisual != null) target.realConnectorVisual.SetActive(true);

            // Optional cosmetic swap to a "crimped" connector mesh.
            if (target.crimpedVisual != null)
            {
                target.crimpedVisual.SetActive(true);
                if (target.realConnectorVisual != null && target.realConnectorVisual != target.crimpedVisual)
                    target.realConnectorVisual.SetActive(false);
            }

            // Bring the draggable tool back so the player can crimp the OTHER end. Its own
            // OnEnable() re-centres it at its holster pose, same as during stripping.
            if (draggedToolTransform != null) draggedToolTransform.gameObject.SetActive(true);

            target.done = true;
            IsCrimping = false;

            Debug.Log($"[CrimpToolCrimpController] End {target.endNumber} crimped.", this);
            OnEndCrimpComplete?.Invoke(target.endNumber);
        }

        /// <summary>Clears the crimped flags so the whole crimping stage can be replayed
        /// (e.g. a Retry button, or picking a new cable type). Mirrors
        /// CrimpToolStripController.ResetForReplay().</summary>
        public void ResetForReplay()
        {
            IsCrimping = false;
            StopAllCoroutines();

            if (ends != null)
            {
                foreach (var end in ends)
                {
                    if (end == null) continue;
                    end.done = false;
                    if (end.crimpedVisual != null) end.crimpedVisual.SetActive(false);
                    if (end.realConnectorVisual != null) end.realConnectorVisual.SetActive(true);
                }
            }

            SetRigVisible(false);
            if (draggedToolTransform != null) draggedToolTransform.gameObject.SetActive(true);

            if (animator != null)
            {
                animator.Play(clipName, 0, 0f);
                animator.Update(0f);
            }
        }

        private CrimpEndTarget FindEnd(int endNumber)
        {
            if (ends == null) return null;
            foreach (var end in ends)
            {
                if (end != null && end.endNumber == endNumber) return end;
            }
            return null;
        }

        private float GetClipLength()
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller != null)
            {
                foreach (var clip in controller.animationClips)
                {
                    if (clip.name == clipName) return clip.length;
                }
                Debug.LogWarning($"[CrimpToolCrimpController] No clip named '{clipName}' on the Animator " +
                                 $"Controller - falling back to {fallbackClipLength}s. Check the Clip Name field.", this);
            }
            return fallbackClipLength;
        }
    }
}
