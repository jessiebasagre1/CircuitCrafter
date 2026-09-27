using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// The job order screen: opens at the start of a run, reopens any time with J (or the HUD's Job
    /// Order button).
    ///
    /// ATTACH THIS TO: the JobOrder panel on the Guidance canvas (the builder makes it). Assign the
    /// scene's JobOrderData, or leave it empty to use StoryGuide's.
    ///
    /// STORY: the customer, request, problem, expected outcome, specifications, and the teaching
    /// notes (what you will practise, tools, safety).
    /// ASSESSMENT: the scenario, the required outcome and the specifications - nothing about tools,
    /// order or method. If AssessmentIntroPanel already shows the briefing (give it the same job
    /// order), leave Open On Start In Assessment off so it is not shown twice; J still reopens it.
    ///
    /// The job order itself is never withheld - it is the customer's requirement, not help. Only its
    /// teaching notes follow GuidancePolicy (JobOrderCoaching).
    /// </summary>
    public class JobOrderPanel : MonoBehaviour
    {
        public static JobOrderPanel Instance { get; private set; }

        [Header("Data")]
        [Tooltip("This scene's job order. Empty = use StoryGuide's.")]
        public JobOrderData jobOrder;

        [Header("UI")]
        public GameObject panel;
        public TMP_Text headerText;
        public TMP_Text bodyText;
        public Button closeButton;

        [Tooltip("Optional. A scroll view around the body, reset to the top on open.")]
        public ScrollRect scroll;

        [Tooltip("Optional. A HUD button that opens this panel.")]
        public Button openButton;

        [Header("Behaviour")]
        [Tooltip("Open by itself when a Story scene starts (not on Continue).")]
        public bool openOnStartInStory = true;

        [Tooltip("Open by itself when an assessment starts. Leave off when AssessmentIntroPanel shows the same brief.")]
        public bool openOnStartInAssessment = false;

        [Tooltip("Keyboard shortcut to open and close. Ignored while typing into a field.")]
        public KeyCode toggleKey = KeyCode.J;

        [Tooltip("Seconds after scene start before opening, so the scene is visible first.")]
        [Min(0f)] public float openDelay = 0.6f;

        public bool IsOpen { get; private set; }

        private bool recapture;
        private float openAt = -1f;

        // ==================================================================

        private void Awake()
        {
            Instance = this;
            if (panel != null) panel.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (openButton != null) openButton.onClick.AddListener(Open);
        }

        private void OnDestroy()
        {
            if (IsOpen) GuidanceUtil.ReleasePlayer(false);
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (Data == null) return;

            bool continuing = SaveManager.Instance != null && SaveManager.Instance.IsContinuingSave;
            bool wanted = GuidancePolicy.IsAssessment ? openOnStartInAssessment : (openOnStartInStory && !continuing);
            if (wanted) openAt = Time.time + openDelay;
        }

        public JobOrderData Data => jobOrder != null ? jobOrder : (StoryGuide.Instance != null ? StoryGuide.Instance.jobOrder : null);

        private void Update()
        {
            if (openAt >= 0f && Time.time >= openAt)
            {
                // Never over a dialog - the intro dialog plays first, then the job order.
                if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;
                openAt = -1f;
                Open();
                return;
            }

            // J only. Escape is deliberately NOT handled here: the pause menu owns it, and two
            // scripts reacting to one Escape press is how a panel closes and the pause menu opens
            // in the same frame.
            if (GuidanceUtil.KeyPressed(toggleKey))
            {
                if (IsOpen) Close();
                else Open();
            }
        }

        public void Open()
        {
            JobOrderData data = Data;
            if (data == null || IsOpen) return;

            bool assessment = GuidancePolicy.IsAssessment;

            if (headerText != null) headerText.text = data.Title(assessment);
            if (bodyText != null)
                bodyText.text = data.BuildText(assessment, GuidancePolicy.Allows(GuidanceKind.JobOrderCoaching));

            if (panel != null) panel.SetActive(true);
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;

            IsOpen = true;
            recapture = GuidanceUtil.HoldPlayer();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (panel != null) panel.SetActive(false);
            GuidanceUtil.ReleasePlayer(recapture);
        }
    }
}
