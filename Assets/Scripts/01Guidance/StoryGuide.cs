using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using CSSSimulation.Networking;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// Story Mode's step-by-step coach for one scene. Holds the job order and an ORDERED list of
    /// steps; for whichever step is current it drives the Current Task HUD, shows that step's tutorial
    /// card the first time, highlights the objects involved, and praises the step when it is done.
    ///
    ///   Explain      - the step's Instruction on the HUD, and its Tutorial card the first time
    ///   Demonstrate  - the card's picture / flipbook, the step's Demo (GuideGhostDemo), highlights
    ///   Perform      - the learner does it; nothing here blocks them
    ///   Check        - Done When reads the game's own state (TaskManager, the waypoint arrow...)
    ///   Feedback     - Completion Feedback as a short toast; mistakes are explained by CoachFeedback
    ///
    /// ATTACH THIS TO: an object under Managers in each Story scene, e.g. "StoryGuide".
    ///
    /// IT READS STATE, IT IS NOT TOLD. Like TaskWaypointArrow, every frame it walks the list from the
    /// top and the first step not done is current. A reload, a restarted procedure or a step done out
    /// of order is simply picked up on the next frame.
    ///
    /// ALREADY HAVE A TaskWaypointArrow? Right-click this component > Import Steps From
    /// TaskWaypointArrow. Each arrow step becomes a guide step that finishes when the arrow's does, so
    /// the arrow keeps pointing and this adds the explanation, tutorial and highlights. Then fill in
    /// the Instruction lines.
    ///
    /// NEVER IN AN ASSESSMENT. It switches itself off when StepInstructions are withheld. It still
    /// exists (Instance is set), so the HUD and job order panel can read its Job Order.
    /// </summary>
    [DefaultExecutionOrder(310)]   // after TaskWaypointArrow (300), so its answers are settled
    public class StoryGuide : MonoBehaviour
    {
        public enum DoneWhen
        {
            /// <summary>TaskManager reports this task id complete.</summary>
            TaskComplete = 0,
            /// <summary>The TaskWaypointArrow step at this index is done.</summary>
            WaypointStepDone = 1,
            ObjectActive = 2,
            ObjectInactive = 3,
            /// <summary>The scene's AssessmentRecorder has closed this stage (COC3/COC4 stages).</summary>
            RecorderStage = 4,
            /// <summary>Only CompleteStep / CompleteCurrentStep from a UnityEvent.</summary>
            Manual = 5,
            /// <summary>The player walked within Near Distance of Near Target.</summary>
            PlayerNear = 6
        }

        [Serializable]
        public class GuideStep
        {
            [Tooltip("Short unique id, for CompleteStep(\"id\") and the logs.")]
            public string stepId = "";

            [Header("Explain")]
            [Tooltip("The Current Task, as an instruction: \"Remove the side panel\".")]
            public string title = "Next task";

            [TextArea(2, 4)]
            [Tooltip("How, in one or two short sentences. Shown under the title on the HUD.")]
            public string instruction = "";

            [TextArea(1, 3)]
            [Tooltip("Optional. Why this step is done - given to the AI Tutor and shown on hover-free devices under the instruction.")]
            public string whyItMatters = "";

            [Tooltip("Shown the first time this step begins, for this learner. Leave empty for steps that need no card.")]
            public TutorialCardData tutorial;

            [Header("Demonstrate")]
            [Tooltip("Glow while this step is current - the parts, screws, ports or buttons involved.")]
            public GameObject[] highlight = new GameObject[0];

            [Tooltip("Tooltip beside the first highlighted object, e.g. \"RAM retention clips\".")]
            public string highlightLabel = "";

            [Tooltip("Optional. Played by the tutorial card's Show Me button.")]
            public GuideGhostDemo demo;

            [Tooltip("Optional. Point the WaypointIndicator here - only used when the scene has no TaskWaypointArrow of its own.")]
            public Transform waypointTarget;

            [Header("Check")]
            public DoneWhen doneWhen = DoneWhen.TaskComplete;

            [Tooltip("Task Complete: the TaskManager task id.  Recorder Stage: the stage id.")]
            public string taskId = "";

            [Tooltip("Waypoint Step Done: the index in TaskWaypointArrow's Steps list.")]
            public int waypointStepIndex = 0;

            [Tooltip("Object Active / Object Inactive: the object to watch.")]
            public GameObject witness;

            [Tooltip("Player Near: where.")]
            public Transform nearTarget;
            [Min(0.1f)] public float nearDistance = 2.5f;

            [Tooltip("Show TaskManager progress on the HUD, e.g. \"3 of 6\". Needs Task Complete.")]
            public bool showProgress = false;

            [Header("Feedback")]
            [TextArea(1, 3)]
            [Tooltip("Toast when the step is done: what went right and why it matters. Keep it to one line.")]
            public string completionFeedback = "";

            public UnityEvent onBegin = new UnityEvent();
            public UnityEvent onComplete = new UnityEvent();

            [NonSerialized] public bool manualDone;
            [NonSerialized] public bool latched;
        }

        public static StoryGuide Instance { get; private set; }

        [Header("Job order")]
        [Tooltip("This scene's job order. JobOrderPanel and the HUD read it from here.")]
        public JobOrderData jobOrder;

        [Header("Steps, in order")]
        public List<GuideStep> steps = new List<GuideStep>();

        [Header("References")]
        [Tooltip("The player's body. Empty = the PlayerMovement in the scene. Only Player Near steps need it.")]
        public Transform player;

        [Header("Behaviour")]
        [Tooltip("Once a step is done it stays done, even if the state it read goes back (a part put down again).")]
        public bool latchCompletedSteps = true;

        [Tooltip("Seconds after the scene starts during which completed steps are NOT praised - that is a loaded save or a restore, not the learner's work.")]
        [Min(0f)] public float startupGrace = 1.5f;

        public bool logStepChanges = true;

        [Header("Events")]
        public UnityEvent onAllStepsComplete = new UnityEvent();

        /// <summary>Raised when the current step changes. Null means every step is done.</summary>
        public event Action<GuideStep> OnStepChanged;

        public int CurrentIndex { get; private set; } = -2;   // -2 = not evaluated yet, -1 = all done
        public GuideStep Current => CurrentIndex >= 0 && CurrentIndex < steps.Count ? steps[CurrentIndex] : null;
        public bool AllComplete => CurrentIndex == -1;

        private float startedAt;
        private GuideStep guidedStep;
        private bool setWaypoint;

        // ==================================================================

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            EndGuidance();
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            startedAt = Time.time;

            if (player == null)
            {
                var mover = FindAnyObjectByType<PlayerMovement>();
                if (mover != null) player = mover.transform;
            }

            if (!GuidancePolicy.Allows(GuidanceKind.StepInstructions))
            {
                if (logStepChanges) Debug.Log($"[StoryGuide] Standing down - step guidance is withheld in {GuidancePolicy.Mode}.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (!GuidancePolicy.Allows(GuidanceKind.StepInstructions))
            {
                EndGuidance();
                return;
            }

            int next = FirstUnfinished();
            if (next != CurrentIndex) ChangeStep(next);
        }

        // ------------------------------------------------------------------

        private int FirstUnfinished()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                GuideStep s = steps[i];
                if (s == null) continue;
                if (!IsDone(s)) return i;
            }
            return -1;
        }

        private bool IsDone(GuideStep s)
        {
            if (s.latched) return true;
            bool done = Evaluate(s);
            if (done && latchCompletedSteps) s.latched = true;
            return done;
        }

        private bool Evaluate(GuideStep s)
        {
            switch (s.doneWhen)
            {
                case DoneWhen.TaskComplete:
                    return TaskManager.Instance != null && !string.IsNullOrEmpty(s.taskId) &&
                           TaskManager.Instance.IsTaskComplete(s.taskId);

                case DoneWhen.WaypointStepDone:
                    return TaskWaypointArrow.Instance != null &&
                           TaskWaypointArrow.Instance.IsStepComplete(s.waypointStepIndex);

                case DoneWhen.ObjectActive:
                    return s.witness != null && s.witness.activeInHierarchy;

                case DoneWhen.ObjectInactive:
                    return s.witness != null && !s.witness.activeInHierarchy;

                case DoneWhen.RecorderStage:
                    return CircuitCrafter.Assessment.AssessmentRecorder.Active != null &&
                           CircuitCrafter.Assessment.AssessmentRecorder.Active.IsStageComplete(s.taskId);

                case DoneWhen.PlayerNear:
                    return player != null && s.nearTarget != null &&
                           Vector3.Distance(player.position, s.nearTarget.position) <= s.nearDistance;

                default:
                    return s.manualDone;
            }
        }

        private void ChangeStep(int next)
        {
            GuideStep previous = Current;
            bool firstEvaluation = CurrentIndex == -2;
            bool live = !firstEvaluation && Time.time - startedAt >= startupGrace &&
                        (SaveManager.Instance == null || !SaveManager.Instance.IsRestoringSave);

            EndGuidance();

            // Feedback for the step just finished - only when it is the learner's work happening
            // now, not a loaded save or a restore replaying completed tasks at startup.
            if (previous != null && live && (previous.latched || Evaluate(previous)))
            {
                if (!string.IsNullOrEmpty(previous.completionFeedback)) CoachFeedback.Praise(previous.completionFeedback);
                previous.onComplete?.Invoke();
            }

            CurrentIndex = next;
            GuideStep current = Current;

            if (logStepChanges)
                Debug.Log(current != null
                    ? $"[StoryGuide] Current task {CurrentIndex + 1}/{steps.Count}: '{current.title}'."
                    : "[StoryGuide] Every step is complete.", this);

            if (current != null)
            {
                BeginGuidance(current);
                current.onBegin?.Invoke();
            }
            else if (!firstEvaluation)
            {
                // Not on the first evaluation: a save that loads already finished must not re-run
                // whatever this event is wired to.
                onAllStepsComplete?.Invoke();
            }

            OnStepChanged?.Invoke(current);
        }

        private void BeginGuidance(GuideStep step)
        {
            guidedStep = step;

            if (step.tutorial != null)
            {
                Action showMe = null;
                if (step.demo != null) showMe = step.demo.Play;
                TutorialCardPanel.TryShow(step.tutorial, showMe);
            }

            if (GuidancePolicy.Allows(GuidanceKind.ObjectHighlights) && step.highlight != null)
            {
                bool first = true;
                foreach (GameObject go in step.highlight)
                {
                    if (go == null) continue;
                    GuideHighlight.Show(go, first ? step.highlightLabel : "");
                    first = false;
                }
            }

            // Only when the scene has no TaskWaypointArrow: that arrow already points at every step,
            // and two indicators pointing at different things is worse than one.
            if (step.waypointTarget != null && TaskWaypointArrow.Instance == null &&
                WaypointIndicator.Instance != null && GuidancePolicy.Allows(GuidanceKind.Waypoints))
            {
                WaypointIndicator.Instance.SetTarget(step.waypointTarget);
                setWaypoint = true;
            }
        }

        private void EndGuidance()
        {
            if (guidedStep == null) return;

            if (guidedStep.highlight != null)
                foreach (GameObject go in guidedStep.highlight) GuideHighlight.Hide(go);

            if (guidedStep.tutorial != null && TutorialCardPanel.Instance != null)
                TutorialCardPanel.Instance.Complete(guidedStep.tutorial);

            if (setWaypoint && WaypointIndicator.Instance != null) WaypointIndicator.Instance.ClearTarget();
            setWaypoint = false;

            guidedStep = null;
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>For Manual steps. Wire to any UnityEvent with the step's id.</summary>
        public void CompleteStep(string stepId)
        {
            foreach (GuideStep s in steps)
                if (s != null && s.stepId == stepId) s.manualDone = true;
        }

        public void CompleteCurrentStep()
        {
            if (Current != null) Current.manualDone = true;
        }

        /// <summary>Un-finishes every step - for a procedure reset.</summary>
        public void ResetSteps()
        {
            foreach (GuideStep s in steps)
            {
                if (s == null) continue;
                s.manualDone = false;
                s.latched = false;
            }
        }

        /// <summary>"3 of 6" for the HUD, when the step asks for it and TaskManager knows.</summary>
        public bool TryGetProgress(GuideStep step, out int current, out int required)
        {
            current = 0;
            required = 0;
            if (step == null || !step.showProgress || TaskManager.Instance == null || string.IsNullOrEmpty(step.taskId))
                return false;
            return TaskManager.Instance.TryGetProgress(step.taskId, out current, out required) && required > 1;
        }

        /// <summary>The job order and where the learner is in it, for the AI Tutor's system prompt.
        /// Null when there is nothing useful to say.</summary>
        public string DescribeForTutor()
        {
            var sb = new StringBuilder();
            if (jobOrder != null) sb.AppendLine(jobOrder.DescribeForTutor());

            if (steps.Count > 0)
            {
                GuideStep current = Current;
                sb.AppendLine(current != null
                    ? $"The trainee's current task is: \"{current.title}\". {current.instruction} {current.whyItMatters}".Trim()
                    : "The trainee has finished every task in this scene.");

                sb.AppendLine("The tasks in order (> marks the current one; earlier ones are done):");
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i] == null) continue;
                    sb.AppendLine((i == CurrentIndex ? "  > " : "    ") + steps[i].title);
                }
            }

            string text = sb.ToString().Trim();
            return text.Length == 0 ? null : text;
        }

#if UNITY_EDITOR
        [ContextMenu("Import Steps From TaskWaypointArrow")]
        public void ImportFromWaypointArrow()
        {
            TaskWaypointArrow arrow = FindAnyObjectByType<TaskWaypointArrow>(FindObjectsInactive.Include);
            if (arrow == null || arrow.steps == null || arrow.steps.Count == 0)
            {
                Debug.LogWarning("[StoryGuide] No TaskWaypointArrow with steps in this scene to import from.", this);
                return;
            }

            UnityEditor.Undo.RecordObject(this, "Import Steps From TaskWaypointArrow");

            int added = 0;
            for (int i = 0; i < arrow.steps.Count; i++)
            {
                TaskWaypointArrow.Step a = arrow.steps[i];
                if (a == null) continue;

                bool exists = false;
                foreach (GuideStep g in steps)
                    if (g != null && g.doneWhen == DoneWhen.WaypointStepDone && g.waypointStepIndex == i) { exists = true; break; }
                if (exists) continue;

                steps.Add(new GuideStep
                {
                    stepId = "arrow_" + i,
                    title = a.label,
                    instruction = "",
                    doneWhen = DoneWhen.WaypointStepDone,
                    waypointStepIndex = i,
                    highlight = a.target != null ? new[] { a.target.gameObject } : new GameObject[0]
                });
                added++;
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[StoryGuide] Imported {added} step(s) from '{arrow.name}'. Fill in each Instruction line - " +
                      "that is the part the arrow never had. Clear Highlight where the arrow's target is a whole station rather than the thing to touch.", this);
        }
#endif
    }
}
