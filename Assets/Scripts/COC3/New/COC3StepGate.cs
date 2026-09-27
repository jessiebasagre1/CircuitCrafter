using System;
using System.Collections.Generic;
using TMPro;
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
            [TooltipAttribute("The stepId on the StepData asset, e.g. \"Step_02_ComputerName\".")]
            public string stepId = "";

            [TooltipAttribute("The actionId reported by the WinTaskGate that completes it, " +
                     "e.g. \"set_computer_name\".")]
            public string actionId = "";
        }

        [TooltipAttribute("Left empty, the scene's COC3Manager is found automatically.")]
        /// <summary>
        /// The stepId on screen right now, or empty when nothing is.
        ///
        /// Published as a static so a WinTaskGate can ask "is this my step?" without holding a
        /// reference to anything. That is what lets ONE dialog serve two different steps - the
        /// New Folder box is used at step 12 and again at step 23, wanting a different answer each
        /// time, exactly as the real one would be.
        /// </summary>
        public static string CurrentStepId { get; private set; }

        public COC3Manager manager;

        [TooltipAttribute("One row per desktop-driven step. Steps not listed keep their old behaviour.")]
        public Binding[] bindings;

        [TooltipAttribute("Log every action and whether it matched the current step. Leave on while you are " +
                 "building steps out; it is the fastest way to see a mistyped id.")]
        public bool verbose = true;

        [HeaderAttribute("Desktop visibility")]
        [TooltipAttribute("The COC3Desktop object. Shown only while a step that HAS a binding is on screen, " +
                 "and hidden the rest of the time.\n\n" +
                 "This is what lets the desktop and the old full-screen step panels share a scene. " +
                 "Both are full-screen, so whichever is in front wins: with the desktop in front " +
                 "you cannot reach Next on an unconverted step, and with it behind you cannot " +
                 "reach the desktop. Showing it only when it is the thing being used removes the " +
                 "conflict rather than trying to order around it.\n\n" +
                 "Leave empty to manage the desktop yourself.")]
        public GameObject desktopRoot;

        [TooltipAttribute("Optional. A label on the desktop itself, filled with the current step's " +
                 "instruction.\n\n" +
                 "The desktop is drawn IN FRONT of the step panels, so the instruction strip the " +
                 "Step Panel Converter puts across the top of each panel is hidden behind it. That " +
                 "leaves the learner looking at a dialog with no idea which value it wants - which " +
                 "is not a difficulty worth having. The desktop builder creates this banner and " +
                 "wires it up for you.")]
        public TMP_Text desktopInstruction;

        [HeaderAttribute("Doing the right thing at the wrong time")]
        [TooltipAttribute("Optional. Told when the learner completes a task the CURRENT step is not asking " +
                 "for, so the work is not silently ignored. Left empty, only the Console says so.")]
        public FeedbackManager feedback;

        [TooltipAttribute("Shown when a bound action arrives for a step that is not the current one. " +
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
            CurrentStepId = "";
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Polls which step is on screen and shows or hides the desktop to match.
        ///
        /// Polling rather than an event because COC3Manager exposes neither the current index nor
        /// a step-changed callback, and COC3Tutorial already reads the active panel the same way -
        /// so this adds no new assumption, just one more reader of the same fact.
        /// </summary>
        private void Update()
        {
            ScreenshotStep current = ActiveStep();

            // Set BEFORE the desktopRoot guard. A scene that manages the desktop itself still needs
            // the step id published, or every step-scoped gate in it goes dead.
            CurrentStepId = current != null && current.stepData != null ? current.stepData.stepId : "";

            if (desktopRoot == null) return;

            bool wanted = current != null
                          && current.stepData != null
                          && !string.IsNullOrEmpty(ExpectedActionFor(current.stepData.stepId));

            // A quiz is modal. Without this the simulated desktop stays up behind it and can still
            // be clicked - and hiding it from anywhere else does not work, because this line runs
            // every frame and would switch it straight back on.
            if (QuizManager.Instance != null && QuizManager.Instance.IsQuizOpen) wanted = false;

            if (desktopRoot.activeSelf != wanted)
                desktopRoot.SetActive(wanted);

            ShowInstruction(current);
        }

        /// <summary>
        /// Copies the step's own instruction onto the desktop banner.
        ///
        /// Read from the step's instructionLabel rather than from StepData, so whatever you have
        /// edited on the panel is what the learner reads - one source of truth, and no second copy
        /// to fall out of date.
        /// </summary>
        private void ShowInstruction(ScreenshotStep current)
        {
            if (desktopInstruction == null) return;

            string text = InstructionFor(current);

            // Compared before assigning: writing to a TMP_Text marks it dirty and rebuilds the
            // mesh, and this runs every frame.
            if (desktopInstruction.text != text)
                desktopInstruction.text = text;
        }

        /// <summary>
        /// The instruction to show, from the panel's label if it has one and from the StepData
        /// asset if it does not.
        ///
        /// WHY THE FALLBACK
        /// The label is normally the right source - ScreenshotStep.Setup() copies the asset into
        /// it, and anything you edit on the panel is what the learner reads. But a panel whose
        /// Instruction Label was never assigned - a duplicated hotspot panel, an older one that
        /// predates the desktop - copies nothing, and the banner goes BLANK with nothing in the
        /// Console to say why. That is what happened on step 20.
        ///
        /// Reading the asset directly when the label is missing means a panel can be wired
        /// imperfectly and the learner still gets their instruction.
        /// </summary>
        private string InstructionFor(ScreenshotStep current)
        {
            if (current == null) return string.Empty;

            if (current.instructionLabel != null &&
                !string.IsNullOrEmpty(current.instructionLabel.text))
                return current.instructionLabel.text;

            // GUIDANCE: the fallback below reads the Story instruction straight off the asset, which
            // would put step-by-step help on the banner during an assessment. Use the requirement.
            if (current.stepData != null &&
                !CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.StepInstructions))
                return string.IsNullOrEmpty(current.stepData.assessmentPrompt)
                    ? "Continue the server configuration required by the job order."
                    : current.stepData.assessmentPrompt;

            if (current.stepData != null && !string.IsNullOrEmpty(current.stepData.instructionText))
            {
                WarnOnce(current, "has no Instruction Label assigned, so the banner is reading the " +
                                  "StepData asset directly. Assign the label on the panel when you " +
                                  "get a chance.");

                return current.stepData.instructionText;
            }

            if (current.stepData != null)
                WarnOnce(current, "has no instruction text at all - neither on the panel nor on the " +
                                  "StepData asset. Run Tools > COC 3 > Instruction Writer.");

            return string.Empty;
        }

        /// <summary>One line per step, not one per frame. This runs in Update.</summary>
        private void WarnOnce(ScreenshotStep step, string message)
        {
            string id = step.stepData != null ? step.stepData.stepId : step.name;
            if (!warnedAbout.Add(id)) return;

            Debug.LogWarning("[COC3StepGate] '" + id + "' " + message, step);
        }

        private readonly HashSet<string> warnedAbout = new HashSet<string>();

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

                // The learner is on an Informational step - the job order, or an explanation - and
                // has gone ahead and done a later task on the desktop. The work is real and
                // correct, and refusing it in silence looks like the simulation is broken rather
                // than the order being wrong.
                //
                // Same test as below: an unbound action is a guard on the way somewhere, not a
                // step performed early.
                if (IsBoundToAnyStep(actionId))
                    NudgeOutOfOrder(current.stepData.stepId);

                return;
            }

            if (expected != actionId)
            {
                if (verbose)
                    Debug.Log($"[COC3StepGate] '{actionId}' is not what completes " +
                              $"'{current.stepData.stepId}' (that is '{expected}').");

                // Only complain if this action completes some OTHER step. An action bound to
                // nothing is an internal guard - open_ipv4, create_group, open_password_policy -
                // and those are steps ALONG the way to the current one, not steps out of order.
                // Telling the learner "that is a later step" the moment they do the right thing is
                // worse than saying nothing at all.
                if (IsBoundToAnyStep(actionId))
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

        /// <summary>
        /// True when some step in the list is completed by this action.
        ///
        /// False means it is an INTERNAL GUARD: a window that checks the learner picked the right
        /// node, or container, or option before it lets them through, reported so the desktop can
        /// remember it happened. Those fire constantly during a normal correct run and must never
        /// produce a warning.
        /// </summary>
        private bool IsBoundToAnyStep(string actionId)
        {
            if (bindings == null || string.IsNullOrEmpty(actionId)) return false;

            foreach (var b in bindings)
            {
                if (b == null) continue;
                if (b.actionId == actionId) return true;
            }
            return false;
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