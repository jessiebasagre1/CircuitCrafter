using System;
using UnityEngine;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// The bridge between the simulated desktop and COC 3's existing step list.
    ///
    /// ATTACH THIS TO: the same always-active Managers object as COC3Manager.
    ///
    /// HOW IT AVOIDS TOUCHING COC3Manager AT ALL
    /// A desktop-driven step is marked as ButtonInteraction on its StepData asset. Look at
    /// ScreenshotStep.Validate(): ButtonInteraction returns true without comparing anything. So
    /// the marking has already moved to WinTaskGate, and all this has to do is press the same
    /// submit COC 3 already uses - which awards the step's points, logs it PASSED, and advances.
    ///
    /// Nothing in COC3Manager, ScreenshotStep or the scoring changes. The report at the end of the
    /// run reads exactly as it did before.
    ///
    /// A step with no binding behaves as it always has - so you can convert the module a few steps
    /// at a time rather than all at once, and a half-converted scene is still playable.
    /// </summary>
    public class COC3StepGate : MonoBehaviour
    {
        [Serializable]
        public class Binding
        {
            [Tooltip("The stepId on the StepData asset, e.g. \"Step_02_ComputerName\".")]
            public string stepId = "";

            [Tooltip("The actionId reported by the WinTaskGate that completes it, " +
                     "e.g. \"set_computer_name\".")]
            public string actionId = "";
        }

        [Tooltip("Left empty, the scene's COC3Manager is found automatically.")]
        public COC3Manager manager;

        [Tooltip("One row per desktop-driven step. Steps not listed keep their old behaviour.")]
        public Binding[] bindings;

        [Tooltip("Log every action and whether it matched the current step. Leave on while you are " +
                 "building steps out; it is the fastest way to see a mistyped id.")]
        public bool verbose = true;

        [Header("Doing the right thing at the wrong time")]
        [Tooltip("Optional. Told when the learner completes a task the CURRENT step is not asking " +
                 "for, so the work is not silently ignored. Left empty, only the Console says so.")]
        public FeedbackManager feedback;

        [Tooltip("Shown when a bound action arrives for a step that is not the current one. " +
                 "{step} is replaced with the step now on screen.")]
        public string outOfOrderMessage =
            "That is a later step. Finish the one on screen first.";

        // ------------------------------------------------------------------

        private void Awake()
        {
            if (manager == null) manager = FindAnyObjectByType<COC3Manager>();
        }

        private void OnEnable()
        {
            WinActionBus.OnAction += HandleAction;
        }

        private void OnDisable()
        {
            WinActionBus.OnAction -= HandleAction;
        }

        // ------------------------------------------------------------------

        private void HandleAction(string actionId, bool success)
        {
            // A rejected attempt has already shown the learner a Windows error inside the dialog.
            // Advancing on it, or scoring it here, would double up on that feedback.
            if (!success) return;

            if (manager == null)
            {
                Debug.LogWarning("[COC3StepGate] No COC3Manager, so nothing can be completed.", this);
                return;
            }

            ScreenshotStep current = ActiveStep();

            if (current == null || current.stepData == null)
            {
                if (verbose) Debug.Log($"[COC3StepGate] '{actionId}' arrived with no step on screen.");
                return;
            }

            string expected = ExpectedActionFor(current.stepData.stepId);

            if (string.IsNullOrEmpty(expected))
            {
                if (verbose)
                    Debug.Log($"[COC3StepGate] '{current.stepData.stepId}' has no binding - left alone.");

                // The commonest case by far: the learner is on an Informational step - the job
                // order, or an explanation - and has gone ahead and done a later task on the
                // desktop. The work is real and correct, and refusing it in silence looks like
                // the simulation is broken rather than the order being wrong.
                NudgeOutOfOrder(current.stepData.stepId);
                return;
            }

            if (expected != actionId)
            {
                if (verbose)
                    Debug.Log($"[COC3StepGate] '{actionId}' does not complete " +
                              $"'{current.stepData.stepId}' (it wants '{expected}').");

                NudgeOutOfOrder(current.stepData.stepId);
                return;
            }

            if (current.stepData.stepType != StepType.ButtonInteraction)
            {
                Debug.LogWarning($"[COC3StepGate] '{current.stepData.stepId}' is bound to a desktop " +
                                 $"action but its Step Type is {current.stepData.stepType}. Set it to " +
                                 "ButtonInteraction, or ScreenshotStep will try to validate an input " +
                                 "field that the learner never used and refuse the step.", this);
                return;
            }

            if (verbose) Debug.Log($"[COC3StepGate] '{actionId}' completes '{current.stepData.stepId}'.");

            manager.SubmitCurrentStep();
        }

        /// <summary>
        /// Says out loud that a correct action arrived at the wrong moment.
        ///
        /// Deliberately not a mistake and not a penalty - the learner did the task properly, they
        /// just did it early. Scoring it would punish initiative.
        /// </summary>
        private void NudgeOutOfOrder(string currentStepId)
        {
            if (feedback == null) return;
            if (string.IsNullOrEmpty(outOfOrderMessage)) return;

            feedback.ShowFailure(outOfOrderMessage.Replace("{step}", currentStepId));
        }

        private string ExpectedActionFor(string stepId)
        {
            if (bindings == null) return null;

            foreach (var b in bindings)
            {
                if (b == null) continue;
                if (b.stepId == stepId) return b.actionId;
            }
            return null;
        }

        /// <summary>
        /// The step on screen. currentStepIndex is private on COC3Manager, so this reads the one
        /// panel that is switched on - the same approach COC3Tutorial already uses.
        /// </summary>
        private ScreenshotStep ActiveStep()
        {
            if (manager.steps == null) return null;

            for (int i = 0; i < manager.steps.Count; i++)
            {
                var s = manager.steps[i];
                if (s != null && s.gameObject.activeInHierarchy) return s;
            }
            return null;
        }
    }
}