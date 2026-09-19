// PunchToolStrike.cs
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Plays the 3D punch-down tool's Strike animation when the player drops the tool icon on a
    /// seated pin, and fires the actual termination at the moment of impact.
    ///
    /// WHY IT IS SEPARATE FROM THE ICON. The thing the player drags is a flat UI icon
    /// (PunchToolIcon) and it goes straight back to its holster on release. This script owns a
    /// second, 3D copy of the tool that lives in the stage set. On a valid drop it appears at the
    /// pin, strikes, and disappears - so the icon stays a simple UI piece and the animation stays
    /// a simple 3D piece, and neither has to know how the other works.
    ///
    /// THE PUNCH HAPPENS ON THE IMPACT FRAME, not on release. PunchStageBoard hands this script
    /// the "punch it" action as a callback; the callback runs Impact Seconds into the clip, so
    /// the conductor locks, the punched mark appears and the click sound plays exactly as the
    /// blade lands.
    ///
    /// NO ANIMATOR CONTROLLER WIRING. The controller needs ONE state, named to match Strike State
    /// Name, holding the Strike clip. No parameters, no transitions. This script restarts that
    /// state by name every time.
    ///
    /// Setup (full steps are in the reply that came with this file):
    ///   1. Empty GameObject in the stage set, named PunchToolStrike, with THIS script on it.
    ///   2. Put PunchDownTool.fbx under it as a child. That child gets an Animator whose
    ///      controller has the single Strike state.
    ///   3. Assign Animator and Visual (the child) here. Assign Tool Icon if you want the holster
    ///      icon hidden while the 3D tool works.
    ///   4. PunchStageBoard > Strike (or leave empty - it finds this in the scene).
    ///
    /// If no strike rig is set up, PunchStageBoard falls back to punching instantly, so nothing
    /// that works today can break.
    /// </summary>
    public class PunchToolStrike : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The Animator on the 3D tool model. Left empty, the first one found under Visual is used.")]
        public Animator animator;

        [Tooltip("The 3D tool model (the child). It is hidden until a strike and hidden again after. " +
                 "It must be a CHILD of this object, not this object itself - a coroutine stops when " +
                 "its own GameObject is switched off.")]
        public GameObject visual;

        [Tooltip("Optional. The PunchToolIcon on your stage canvas. Its image is hidden while the 3D " +
                 "tool is striking, so the tool reads as having gone to the pin.")]
        public PunchToolIcon toolIcon;

        [Header("Animation")]
        [Tooltip("Name of the state in the Animator Controller that holds the Strike clip.")]
        public string strikeStateName = "Strike";

        [Tooltip("Seconds into the clip when the blade lands. The Strike clip is 24 fps and bottoms out " +
                 "on frames 10-11, about 0.4 s in. Nudge this if the punch feels early or late.")]
        [Min(0f)] public float impactSeconds = 0.4f;

        [Tooltip("How long the 3D tool stays visible in total. The Strike clip runs about 0.96 s. Lower " +
                 "this to hide the tool sooner (for example if the camera moves off straight after " +
                 "the last conductor).")]
        [Min(0.1f)] public float clipSeconds = 0.96f;

        [Tooltip("1 = the animation as authored. 1.5 feels snappier for repeated punching.")]
        [Min(0.1f)] public float playSpeed = 1f;

        [Header("Placement, relative to the pin's seat")]
        [Tooltip("Where this object sits, in the pin's Seat space, when a strike starts. Do not type " +
                 "this by hand - see Reference Pin below.")]
        public Vector3 localOffset = Vector3.zero;

        [Tooltip("Extra rotation, in degrees, on top of the seat's rotation.")]
        public Vector3 localEuler = Vector3.zero;

        [Tooltip("On: the tool takes the seat's rotation, so it follows a panel that is rotated or " +
                 "tilted. Off: it keeps world-aligned rotation.")]
        public bool useSeatRotation = true;

        [Tooltip("Only used by the 'Capture Offset From Reference Pin' menu command. Assign any pin, " +
                 "move this object by hand until the tool sits correctly on that pin, then " +
                 "right-click this component's header and choose the command.")]
        public PunchStagePin referencePin;

        [Header("Feel")]
        [Tooltip("Hide the holster icon while the 3D tool is striking.")]
        public bool hideIconWhileStriking = true;

        /// <summary>True from the moment a strike starts until the tool is hidden again.</summary>
        public bool IsPlaying { get; private set; }

        Coroutine routine;
        Image iconImage;

        void Awake()
        {
            if (animator == null && visual != null)
                animator = visual.GetComponentInChildren<Animator>(true);

            SetVisible(false);
        }

        void OnDisable()
        {
            // The station switches the stage off when the player leaves. Put everything back the
            // way it was, or the holster icon would stay hidden until the next full reload.
            if (IsPlaying)
            {
                if (routine != null) StopCoroutine(routine);
                End();
            }
        }

        // ------------------------------------------------------------------
        // Called by PunchStageBoard
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts a strike on this pin. Returns false - and does nothing - if it cannot, so the
        /// caller can punch instantly instead. onImpact runs once, on the impact frame.
        /// </summary>
        public bool Play(PunchStagePin pin, Action onImpact)
        {
            if (pin == null) return false;
            if (!CanStart()) return false;

            Transform seat = pin.Seat;
            Quaternion baseRotation = useSeatRotation ? seat.rotation : Quaternion.identity;
            transform.SetPositionAndRotation(
                seat.TransformPoint(localOffset),
                baseRotation * Quaternion.Euler(localEuler));

            routine = StartCoroutine(Run(onImpact));
            return true;
        }

        bool CanStart()
        {
            if (IsPlaying) return false;
            if (!isActiveAndEnabled) return false;
            if (animator == null || visual == null) return false;
            return true;
        }

        IEnumerator Run(Action onImpact)
        {
            IsPlaying = true;
            SetIconHidden(true);
            SetVisible(true);

            float speed = Mathf.Max(0.1f, playSpeed);
            animator.speed = speed;
            animator.Play(strikeStateName, 0, 0f);
            animator.Update(0f);   // pose the first frame now, so there is no one-frame flash

            yield return new WaitForSeconds(impactSeconds / speed);

            if (onImpact != null) onImpact();

            float tail = Mathf.Max(0f, clipSeconds - impactSeconds) / speed;
            yield return new WaitForSeconds(tail);

            End();
        }

        void End()
        {
            SetVisible(false);
            SetIconHidden(false);
            IsPlaying = false;
            routine = null;
        }

        void SetVisible(bool on)
        {
            if (visual != null && visual.activeSelf != on) visual.SetActive(on);
        }

        void SetIconHidden(bool hidden)
        {
            if (!hideIconWhileStriking || toolIcon == null) return;

            if (iconImage == null)
                iconImage = toolIcon.icon != null ? toolIcon.icon : toolIcon.GetComponent<Image>();

            // Disabling the Image (not the GameObject) also turns off its raycast target, so the
            // icon cannot be grabbed again mid-strike, and PunchToolIcon's own bookkeeping is
            // untouched.
            if (iconImage != null) iconImage.enabled = !hidden;
        }

        // ------------------------------------------------------------------
        // Editor helpers - right-click this component's header
        // ------------------------------------------------------------------

        [ContextMenu("Test Strike (Play Mode)")]
        void TestStrike()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[PunchToolStrike] Enter Play mode first, then run Test Strike.", this);
                return;
            }

            if (!CanStart())
            {
                Debug.LogWarning("[PunchToolStrike] Cannot start: check that Animator and Visual are " +
                                 "assigned, and that a strike is not already playing.", this);
                return;
            }

            // Plays where the object currently sits, with no pin and no punch.
            routine = StartCoroutine(Run(null));
        }

        [ContextMenu("Capture Offset From Reference Pin")]
        void CaptureOffset()
        {
            if (referencePin == null)
            {
                Debug.LogWarning("[PunchToolStrike] Assign Reference Pin first.", this);
                return;
            }

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Capture punch tool offset");
#endif
            Transform seat = referencePin.Seat;
            Quaternion baseRotation = useSeatRotation ? seat.rotation : Quaternion.identity;

            localOffset = seat.InverseTransformPoint(transform.position);
            localEuler = (Quaternion.Inverse(baseRotation) * transform.rotation).eulerAngles;

            Debug.Log("[PunchToolStrike] Captured offset " + localOffset + " and rotation " +
                      localEuler + " relative to pin " + referencePin.pinNumber + ".", this);
        }
    }
}
