using System;
using System.Collections.Generic;
using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// The interactive controls walkthrough: move, look, interact, pick up, drag and drop - each one
    /// taught by DOING it, not by reading about it.
    ///
    /// For every step: a card explains the control (Explain), with a picture of the key or gesture
    /// (Demonstrate); the learner tries it (Perform); this watches for it actually happening (Check);
    /// and a short "Nice" toast confirms it (Feedback). Then the next one. The learner can press Got
    /// It to move on at any time, or Skip to end the walkthrough.
    ///
    /// ATTACH THIS TO: an object in the FIRST Story scene the learner plays (COC1). Once finished or
    /// skipped it never runs again for that learner - in any scene - unless tutorials are reset.
    ///
    /// Never runs in an assessment (ControlsTutorial is withheld there by GuidanceSettings).
    ///
    /// The builder's "Create Starter Tutorial Cards" makes one card per step below; drag them in.
    /// </summary>
    public class ControlsTutorial : MonoBehaviour
    {
        public enum Check
        {
            /// <summary>The player walked at least Amount metres.</summary>
            Move = 0,
            /// <summary>The camera turned at least Amount degrees.</summary>
            Look = 1,
            /// <summary>Key pressed (desktop). On touch, the card's Got It is the check.</summary>
            KeyPressed = 2,
            /// <summary>A part is being dragged (DragController).</summary>
            DragStarted = 3,
            /// <summary>A drag that had started has ended - the part was dropped somewhere.</summary>
            DragEnded = 4,
            /// <summary>Any hotbar slot holds a part.</summary>
            HotbarHasItem = 5,
            /// <summary>A TaskManager task completed.</summary>
            TaskComplete = 6,
            /// <summary>An object became active - e.g. a station's close-up camera.</summary>
            ObjectActive = 7,
            /// <summary>Just wait Amount seconds - for a read-only tip.</summary>
            Timer = 8,
            /// <summary>Only Got It (or CompleteCurrent from a UnityEvent) moves on.</summary>
            Manual = 9
        }

        [Serializable]
        public class ControlStep
        {
            public TutorialCardData card;
            public Check check = Check.Move;

            [Tooltip("Move: metres. Look: degrees. Timer: seconds.")]
            public float amount = 2f;

            [Tooltip("Key Pressed: which key (E to interact, Q to plug, G to pick up...).")]
            public KeyCode key = KeyCode.E;

            [Tooltip("Task Complete: the task id.")]
            public string taskId = "";

            [Tooltip("Object Active: the object to watch.")]
            public GameObject witness;

            [Tooltip("Shown as a short 'Nice' toast when the check passes. Leave blank for none.")]
            public string successText = "";
        }

        [Tooltip("Remembered per learner. Change it only if you want everyone to see the walkthrough again.")]
        public string tutorialId = "controls-basics";

        public List<ControlStep> steps = new List<ControlStep>();

        [Header("References (found automatically when empty)")]
        [Tooltip("The player's body - the object with the CharacterController.")]
        public Transform player;

        [Tooltip("The first-person camera.")]
        public Transform lookCamera;

        [Header("Timing")]
        [Tooltip("Seconds after the scene starts (and after any intro dialog) before the first step.")]
        [Min(0f)] public float startDelay = 1.5f;

        [Tooltip("Wait while the player is held still - an intro dialog, a job order, a station.")]
        public bool waitForPlayerControl = true;

        public int CurrentIndex { get; private set; } = -1;
        public bool IsRunning => CurrentIndex >= 0 && CurrentIndex < steps.Count;

        public static ControlsTutorial Instance { get; private set; }

        /// <summary>True while the walkthrough is waiting to start or running. TutorialCardPanel holds
        /// every other card back meanwhile - "open the side panel" means nothing to someone who has
        /// not yet been shown how to walk up to the PC.</summary>
        public bool HoldsCardQueue => isActiveAndEnabled && (CurrentIndex < 0 || IsRunning);

        private void Awake() { Instance = this; }

        private float readyAt;
        private Vector3 lastPos;
        private float travelled;
        private Quaternion lastRot;
        private float turned;
        private float stepStartedAt;
        private bool dragSeen;
        private bool subscribed;

        // ==================================================================

        private void Start()
        {
            if (!GuidancePolicy.Allows(GuidanceKind.ControlsTutorial) || TutorialProgress.HasSeen(tutorialId) ||
                steps == null || steps.Count == 0)
            {
                enabled = false;
                return;
            }

            if (player == null)
            {
                var mover = FindAnyObjectByType<PlayerMovement>();
                if (mover != null) player = mover.transform;
            }
            if (lookCamera == null && Camera.main != null) lookCamera = Camera.main.transform;

            readyAt = Time.time + startDelay;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!GuidancePolicy.Allows(GuidanceKind.ControlsTutorial)) { StopAll(false); return; }

            if (CurrentIndex < 0)
            {
                if (Time.time < readyAt || !PlayerHasControl()) return;
                if (TutorialCardPanel.Instance == null)
                {
                    Debug.LogWarning("[ControlsTutorial] No TutorialCardPanel in this scene - run CircuitCrafter > Guidance > Build Guidance UI.", this);
                    enabled = false;
                    return;
                }
                Subscribe();
                Begin(0);
                return;
            }

            if (!IsRunning) return;
            if (StepDone(steps[CurrentIndex])) Advance(passed: true);
        }

        private bool PlayerHasControl()
        {
            if (!waitForPlayerControl) return true;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return false;
            return !PlayerInputLock.IsLocked;
        }

        // ------------------------------------------------------------------

        private void Begin(int index)
        {
            CurrentIndex = index;
            if (!IsRunning) { Finish(); return; }

            ControlStep step = steps[index];
            travelled = 0f;
            turned = 0f;
            dragSeen = false;
            stepStartedAt = Time.time;
            if (player != null) lastPos = player.position;
            if (lookCamera != null) lastRot = lookCamera.rotation;

            if (step.card == null) { Advance(passed: false); return; }

            TutorialCardPanel.Instance.PresentCoachCard(step.card, $"{index + 1} of {steps.Count}");
        }

        private bool StepDone(ControlStep step)
        {
            switch (step.check)
            {
                case Check.Move:
                    if (player == null) return false;
                    Vector3 p = player.position;
                    travelled += Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(lastPos.x, 0f, lastPos.z));
                    lastPos = p;
                    return travelled >= Mathf.Max(0.1f, step.amount);

                case Check.Look:
                    if (lookCamera == null) return false;
                    turned += Quaternion.Angle(lastRot, lookCamera.rotation);
                    lastRot = lookCamera.rotation;
                    return turned >= Mathf.Max(1f, step.amount);

                case Check.KeyPressed:
                    return GuidanceUtil.KeyPressed(step.key);

                case Check.DragStarted:
                    return DragController.Instance != null && DragController.Instance.IsDragging;

                case Check.DragEnded:
                    if (DragController.Instance == null) return false;
                    if (DragController.Instance.IsDragging) dragSeen = true;
                    return dragSeen && !DragController.Instance.IsDragging;

                case Check.HotbarHasItem:
                    if (HotbarManager.Instance == null || HotbarManager.Instance.slots == null) return false;
                    foreach (HotbarSlot slot in HotbarManager.Instance.slots)
                        if (slot != null && !slot.IsEmpty) return true;
                    return false;

                case Check.TaskComplete:
                    return TaskManager.Instance != null && !string.IsNullOrEmpty(step.taskId) &&
                           TaskManager.Instance.IsTaskComplete(step.taskId);

                case Check.ObjectActive:
                    return step.witness != null && step.witness.activeInHierarchy;

                case Check.Timer:
                    return Time.time - stepStartedAt >= Mathf.Max(1f, step.amount);

                default:
                    return false;
            }
        }

        /// <summary>For Manual steps - wire to any UnityEvent.</summary>
        public void CompleteCurrent()
        {
            if (IsRunning) Advance(passed: true);
        }

        private void Advance(bool passed)
        {
            if (!IsRunning) return;

            ControlStep step = steps[CurrentIndex];
            if (step.card != null && TutorialCardPanel.Instance != null) TutorialCardPanel.Instance.Complete(step.card);

            if (passed && !string.IsNullOrEmpty(step.successText)) CoachFeedback.Praise(step.successText, "Nice");

            Begin(CurrentIndex + 1);
        }

        private void Finish()
        {
            TutorialProgress.MarkSeen(tutorialId);
            StopAll(true);
        }

        private void StopAll(bool finished)
        {
            if (IsRunning && TutorialCardPanel.Instance != null && steps[CurrentIndex].card != null)
                TutorialCardPanel.Instance.Complete(steps[CurrentIndex].card);

            CurrentIndex = steps != null ? steps.Count : 0;
            Unsubscribe();
            enabled = false;

            if (finished) Debug.Log("[ControlsTutorial] Walkthrough finished.", this);
        }

        // ------------------------------------------------------------------

        private void Subscribe()
        {
            if (subscribed || TutorialCardPanel.Instance == null) return;
            TutorialCardPanel.Instance.OnCardClosed += HandleCardClosed;
            TutorialCardPanel.Instance.OnSkipRequested += HandleSkip;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || TutorialCardPanel.Instance == null) { subscribed = false; return; }
            TutorialCardPanel.Instance.OnCardClosed -= HandleCardClosed;
            TutorialCardPanel.Instance.OnSkipRequested -= HandleSkip;
            subscribed = false;
        }

        /// <summary>Got It on the current card moves on without the check - the learner is in charge.</summary>
        private void HandleCardClosed(TutorialCardData card, bool byLearner)
        {
            if (!byLearner || !IsRunning) return;
            if (steps[CurrentIndex].card == card) Advance(passed: false);
        }

        private void HandleSkip()
        {
            Debug.Log("[ControlsTutorial] Skipped by the learner.", this);
            Finish();
        }
    }
}
