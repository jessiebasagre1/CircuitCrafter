using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Name the probable cause - once you have actually looked at the machine.
    ///
    /// ATTACH THIS TO: a diagnosis panel in each repair-site scene. It replaces
    /// the diagnosis half of TicketDiagnosticPanelView; use one or the other, not
    /// both, or a ticket can be diagnosed twice.
    ///
    /// TWO CHANGES FROM THE OLD PANEL, AND BOTH MATTER.
    ///
    /// 1. IT WILL NOT ACCEPT A GUESS FROM SOMEONE WHO HAS NOT LOOKED.
    /// Below Minimum Observations the options are locked and the panel says so.
    /// That is not just to force good habits - the feedback on a wrong option
    /// names the evidence that rules it out ("the fans are running, so the PSU is
    /// delivering power"), so letting someone click through the options blind
    /// would hand them the symptom for free. The gate protects the content as
    /// much as the learner.
    ///
    /// 2. A WRONG ANSWER COSTS TIME.
    /// The old panel let a learner try all four options at no cost, which is not
    /// diagnosis, it is enumeration. Each wrong answer now adds to the service
    /// call and is recorded as a mistake - and the feedback explains which
    /// observation contradicts that cause, so a wrong guess still teaches
    /// something rather than just saying no.
    /// </summary>
    public class TicketDiagnosisPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("One button per diagnosis option, in the same order as the ticket's Diagnosis Options. Spares are hidden automatically.")]
        [SerializeField] private List<Button> optionButtons = new List<Button>();

        [SerializeField] private List<TMP_Text> optionLabels = new List<TMP_Text>();

        [Tooltip("Where the verdict and its reasoning appear. Always assign this - it carries the teaching.")]
        [SerializeField] private TMP_Text feedbackText;

        [Tooltip("Optional - shown once the cause has been correctly identified.")]
        [SerializeField] private GameObject diagnosedBanner;

        [SerializeField] private Button closeButton;

        [Header("Rules")]
        [Tooltip("How many observations must be made before a diagnosis can be submitted. 1 is enough to stop blind guessing; 2 makes them actually gather a picture.")]
        [Min(0)]
        [SerializeField] private int minimumObservations = 2;

        [Tooltip("Seconds added to the service call for each wrong answer.")]
        [Min(0)]
        [SerializeField] private int wrongAnswerPenaltySeconds = 60;

        [Tooltip("Shown when the learner tries to diagnose before examining the machine.")]
        [TextArea(2, 4)]
        [SerializeField]
        private string lockedMessage =
            "You haven't examined the machine yet. Check it over first - a cause you can't point to evidence for is a guess.";

        [Tooltip("Leave on. When this panel is opened away from a station - mid-walk, anywhere on the site - it frees the mouse and freezes the player, then puts both back on close.\n\nWITHOUT IT THE PANEL IS UNUSABLE AWAY FROM A STATION. While walking, MouseLook holds the cursor locked and hidden, so the options would be visible and unclickable. At a station the cursor is already free and the player already frozen, so this does nothing there and correctly leaves the station's own state alone.")]
        [SerializeField] private bool freeCursorWhenAwayFromStation = true;

        [Header("Assessment (optional)")]
        [SerializeField] private string stageId = "";

        private TicketData ticket;

        // ==================================================================

        private bool openRequested;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void Start()
        {
            // Hidden in Start, not Awake. Panel Root is normally this same
            // GameObject, and deactivating it during Awake stops Unity ever
            // calling Start on this component - see SiteObservationPanel.Awake
            // for the full version of this trap. Nothing here depends on Start
            // today, but the two panels should fail the same way, not
            // differently.
            // Guarded by openRequested. If this panel's object starts INACTIVE in
            // the scene, Unity never runs Start - but a UnityEvent still reaches
            // Open(), which activates the object, which makes Unity run Start in
            // that same frame, which would hide the panel again. The learner sees
            // nothing and has to trigger it a second time. The flag stops Start
            // closing something that has just been asked to open.
            if (!openRequested && panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnEnable() => SiteObservationPanel.OnObservationMade += HandleObservationMade;

        private void OnDisable()
        {
            SiteObservationPanel.OnObservationMade -= HandleObservationMade;
            if (owningInput == this) owningInput = null;

            // Belt and braces. Close() releases first and so never reaches this,
            // but anything else that switches the panel off - a scene teardown, a
            // UnityEvent wired straight to SetActive(false) - would otherwise leave
            // the input lock held by a component that no longer exists.
            ReleaseControl();
        }

        /// <summary>
        /// The panel currently holding the shared key, or null.
        ///
        /// WHY A STATIC RATHER THAN THE TWO COMPONENTS KNOWING ABOUT EACH OTHER
        /// RepairStation and this panel can both be bound to E, and one press
        /// would otherwise open the diagnosis AND walk the learner out of the
        /// station in the same frame. RepairStation checks this before reading its
        /// own key, so whichever one is genuinely in play gets the press. It is the
        /// same trick DialogManager uses with openedFrame, generalised to two
        /// components instead of one.
        /// </summary>
        private static TicketDiagnosisPanel owningInput;

        private static int consumedFrame = -1;

        /// <summary>True while the panel is showing.</summary>
        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>Called by whoever polls the shortcut key, on the frame it was
        /// pressed, so RepairStation does not act on the same press.
        ///
        /// The polling deliberately does NOT happen in this component. Open() hides
        /// this panel's own GameObject, and Unity does not run Update on a component
        /// whose object is switched off - so a key read from here would work exactly
        /// once, while the panel was open, and never open it. RepairWorkGate lives
        /// on Managers, which is never deactivated, so it does the reading.</summary>
        public static void NoteKeyConsumed()
        {
            consumedFrame = Time.frameCount;
        }

        /// <summary>True while this panel owns the shared key - read by
        /// RepairStation so a single press does not do two things.</summary>
        public static bool BlocksStationInput =>
            owningInput != null || Time.frameCount == consumedFrame;

        private void HandleObservationMade(int count)
        {
            // Unlocks live, so a learner who opens this too early, reads the
            // message, goes and looks, then comes back finds it ready.
            if (panelRoot != null && panelRoot.activeSelf) RefreshOptions();
        }

        // ==================================================================

        /// <summary>Wire to the diagnostic tool's interaction or a button.</summary>
        public void Open()
        {
            openRequested = true;
            if (!RepairTicketMode.IsActive || RepairTicketMode.ActiveTicket == null)
            {
                Debug.LogWarning("[TicketDiagnosisPanel] No active ticket to diagnose.", this);
                return;
            }

            ticket = RepairTicketMode.ActiveTicket;
            ServiceCallState.EnsureTicket(ticket.ticketId);

            if (panelRoot != null) panelRoot.SetActive(true);
            owningInput = this;
            if (feedbackText != null) feedbackText.text = "";

            TakeControl();

            RefreshOptions();
            RefreshBanner();
        }

        public void Close()
        {
            if (owningInput == this) owningInput = null;

            // Released BEFORE the object is switched off. Close() usually
            // deactivates this component's own GameObject, and anything left until
            // after that runs in OnDisable or not at all - which is how a learner
            // ends up frozen in place with no panel on screen and nothing in the
            // Console to say why.
            ReleaseControl();

            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ==================================================================
        // Mouse and movement, when opened away from a station
        // ==================================================================

        private bool tookControl;

        /// <summary>
        /// Frees the mouse and freezes the player - but only when this panel was
        /// opened somewhere the station system is not already doing both.
        ///
        /// The station case is deliberately left alone. RepairStation owns the
        /// cursor and the input lock while the learner is inside one, and a second
        /// component setting the same two things would fight it on the way out: the
        /// panel would hand the cursor back to a locked state while the learner was
        /// still standing at an open case, and the case would become unusable.
        /// </summary>
        private void TakeControl()
        {
            if (!freeCursorWhenAwayFromStation) return;
            if (tookControl) return;
            if (RepairStation.Current != null) return;

            tookControl = true;
            PlayerInputLock.Acquire();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ReleaseControl()
        {
            if (!tookControl) return;
            tookControl = false;

            PlayerInputLock.Release();

            // Re-checked rather than assumed. A learner can open this in the open,
            // walk nowhere, and close it - but they can also have entered a station
            // by some other route in between, and re-locking the cursor there would
            // leave them unable to click anything at the station.
            if (RepairStation.Current == null)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private bool HasEnoughEvidence => ServiceCallState.ObservationCount >= minimumObservations;

        private void RefreshOptions()
        {
            if (ticket == null) return;

            var options = ticket.diagnosisOptions;
            bool unlocked = HasEnoughEvidence && !RepairTicketMode.IsDiagnosed;

            for (int i = 0; i < optionButtons.Count; i++)
            {
                if (optionButtons[i] == null) continue;

                bool has = options != null && i < options.Length;
                optionButtons[i].gameObject.SetActive(has);
                if (!has) continue;

                if (i < optionLabels.Count && optionLabels[i] != null)
                    optionLabels[i].text = options[i].label;

                optionButtons[i].interactable = unlocked;

                int captured = i;
                optionButtons[i].onClick.RemoveAllListeners();
                optionButtons[i].onClick.AddListener(() => Submit(captured));
            }

            if (feedbackText != null && !HasEnoughEvidence && !RepairTicketMode.IsDiagnosed)
            {
                int short_ = minimumObservations - ServiceCallState.ObservationCount;
                feedbackText.text = $"{lockedMessage}\n\n({short_} more check{(short_ == 1 ? "" : "s")} needed.)";
            }
        }

        private void Submit(int index)
        {
            if (ticket?.diagnosisOptions == null || index >= ticket.diagnosisOptions.Length) return;
            if (!HasEnoughEvidence || RepairTicketMode.IsDiagnosed) return;

            var option = ticket.diagnosisOptions[index];
            bool first = ServiceCallState.DiagnosisAttempts == 0;

            ServiceCallState.RecordDiagnosisAttempt(option.isCorrect ? 0 : wrongAnswerPenaltySeconds);

            if (!string.IsNullOrEmpty(stageId) && first)
                AssessmentRecorder.Attempt(stageId, option.isCorrect);

            if (option.isCorrect)
            {
                RepairTicketMode.MarkDiagnosed();

                if (!string.IsNullOrEmpty(stageId)) AssessmentRecorder.Stage(stageId);

                if (feedbackText != null)
                    feedbackText.text = option.feedback + $"\n\nDiagnosed after {ServiceCallState.DiagnosisAttempts} attempt(s), {ServiceCallState.FormatTime()} on site.";

                Debug.Log($"[TicketDiagnosisPanel] Correct diagnosis. {ServiceCallState.Summary()}", this);
            }
            else
            {
                if (!string.IsNullOrEmpty(stageId))
                    AssessmentRecorder.Mistake("diagnosis", $"Incorrect cause offered: {option.label}");

                if (feedbackText != null)
                    feedbackText.text = option.feedback + $"\n\n(+{wrongAnswerPenaltySeconds}s. Time on site: {ServiceCallState.FormatTime()}.)";

                Debug.Log($"[TicketDiagnosisPanel] Incorrect diagnosis '{option.label}' (+{wrongAnswerPenaltySeconds}s).", this);
            }

            RefreshOptions();
            RefreshBanner();
        }

        private void RefreshBanner()
        {
            if (diagnosedBanner != null) diagnosedBanner.SetActive(RepairTicketMode.IsDiagnosed);
        }
    }
}