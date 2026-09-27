using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CSSSimulation.Networking;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// The Current Task panel in the corner of the screen.
    ///
    /// ATTACH THIS TO: the ObjectiveHud object on the Guidance canvas (the builder makes it).
    ///
    /// STORY shows, from the first source the scene has:
    ///   1. StoryGuide  - the current task, its instruction, and progress ("3 of 6")
    ///   2. TaskWaypointArrow - the arrow's current step label (COC1/COC2 scenes that have no
    ///      StoryGuide yet still get a Current Task line for free)
    ///   3. the job order's expected outcome
    ///
    /// ASSESSMENT shows only the job order's REQUIRED OUTCOME - never a step, never an
    /// instruction - with the "J  Job order" reminder. Or nothing, if Hide In Assessment is ticked
    /// (for a scene whose AssessmentTaskList already lists the outcomes).
    ///
    /// The Collapse button (or the header) folds it to one line, for small phone screens.
    /// </summary>
    public class ObjectiveHud : MonoBehaviour
    {
        [Header("UI")]
        public GameObject panel;

        [Tooltip("\"CURRENT TASK\" in Story, \"ASSESSMENT\" in an assessment.")]
        public TMP_Text headerText;

        public TMP_Text titleText;
        public TMP_Text instructionText;
        public TMP_Text progressText;

        [Tooltip("\"J  Job order\" - hidden on touch, where the button is used instead.")]
        public TMP_Text keyHintText;

        [Tooltip("Optional. Opens the job order.")]
        public Button jobOrderButton;

        [Tooltip("Optional. Folds the panel to its header line.")]
        public Button collapseButton;

        [Tooltip("The part hidden when collapsed - instruction and progress.")]
        public GameObject detailRoot;

        [Header("Wording")]
        public string storyHeader = "CURRENT TASK";
        public string assessmentHeader = "ASSESSMENT";
        public string doneTitle = "All tasks complete";

        [Header("Assessment")]
        [Tooltip("Hide the panel completely during an assessment.")]
        public bool hideInAssessment = false;

        [Tooltip("Use the TaskWaypointArrow's current step as the task when there is no StoryGuide.")]
        public bool fallBackToWaypointArrow = true;

        private bool collapsed;
        private float nextRefresh;

        private void Awake()
        {
            if (jobOrderButton != null) jobOrderButton.onClick.AddListener(OpenJobOrder);
            if (collapseButton != null) collapseButton.onClick.AddListener(ToggleCollapsed);
        }

        private void OnEnable()
        {
            if (StoryGuide.Instance != null) StoryGuide.Instance.OnStepChanged += HandleStepChanged;
            nextRefresh = 0f;
        }

        private void OnDisable()
        {
            if (StoryGuide.Instance != null) StoryGuide.Instance.OnStepChanged -= HandleStepChanged;
        }

        private void HandleStepChanged(StoryGuide.GuideStep _) => nextRefresh = 0f;

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        public void ToggleCollapsed()
        {
            collapsed = !collapsed;
            nextRefresh = 0f;
        }

        private void OpenJobOrder()
        {
            if (JobOrderPanel.Instance != null) JobOrderPanel.Instance.Open();
        }

        public void Refresh()
        {
            bool assessment = GuidancePolicy.IsAssessment;
            JobOrderData job = JobOrderPanel.Instance != null ? JobOrderPanel.Instance.Data
                             : StoryGuide.Instance != null ? StoryGuide.Instance.jobOrder : null;

            string header, title, instruction = "", progress = "";

            if (assessment)
            {
                if (hideInAssessment) { SetVisible(false); return; }
                header = assessmentHeader;
                title = job != null ? job.Summary(true) : "Complete the job order.";
            }
            else
            {
                header = storyHeader;
                bool showSteps = GuidancePolicy.Allows(GuidanceKind.StepInstructions);
                StoryGuide guide = StoryGuide.Instance;

                if (showSteps && guide != null && guide.isActiveAndEnabled && guide.steps.Count > 0)
                {
                    StoryGuide.GuideStep step = guide.Current;
                    if (step != null)
                    {
                        title = step.title;
                        instruction = step.instruction;
                        int cur, req;
                        if (guide.TryGetProgress(step, out cur, out req)) progress = $"{Mathf.Min(cur, req)} of {req}";
                    }
                    else title = doneTitle;
                }
                else if (showSteps && fallBackToWaypointArrow && TaskWaypointArrow.Instance != null &&
                         TaskWaypointArrow.Instance.ActiveStep != null)
                {
                    title = TaskWaypointArrow.Instance.ActiveStep.label;
                }
                else if (job != null)
                {
                    title = job.Summary(false);
                }
                else
                {
                    SetVisible(false);
                    return;
                }
            }

            SetVisible(true);

            if (headerText != null) headerText.text = header;
            if (titleText != null) titleText.text = title;

            bool showDetail = !collapsed;
            if (detailRoot != null) detailRoot.SetActive(showDetail);

            if (instructionText != null)
            {
                instructionText.gameObject.SetActive(showDetail && !string.IsNullOrEmpty(instruction));
                instructionText.text = instruction;
            }

            if (progressText != null)
            {
                progressText.gameObject.SetActive(showDetail && !string.IsNullOrEmpty(progress));
                progressText.text = progress;
            }

            if (keyHintText != null)
            {
                bool hint = job != null && !GuidanceUtil.TouchWanted();
                keyHintText.gameObject.SetActive(hint);
                if (hint && JobOrderPanel.Instance != null)
                    keyHintText.text = JobOrderPanel.Instance.toggleKey + "  Job order";
            }

            if (jobOrderButton != null) jobOrderButton.gameObject.SetActive(job != null);
        }

        private void SetVisible(bool visible)
        {
            if (panel != null && panel.activeSelf != visible) panel.SetActive(visible);
        }
    }
}
