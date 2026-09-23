using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// The "History" popup opened from a module's row on the competency profile screen: every
    /// past run for that one module, newest first, plus the learner's Assessment-mode high score
    /// for it.
    ///
    /// ATTACH THIS TO: the popup panel built by CompetencyScreenBuilder (or your own, if built by
    /// hand - see SETUP). Assign it to CompetencyProfileView.historyView.
    ///
    /// WHERE THE DATA COMES FROM
    /// Every run - Guided, Practice or Assessment - already writes a complete result to
    /// ResultsRepository as JSON. This reads that straight off disk with LoadAll(); nothing new
    /// had to be saved for the history list itself. The one genuinely new piece of state is the
    /// high score, which lives on CompetencyLedger as a ModuleBestScore - see that class for why.
    ///
    /// WHY A CAP ON HOW MANY RUNS SHOW
    /// The JSON on disk always keeps the complete history. maxRowsShown only limits what gets
    /// instantiated on screen, so a learner who has replayed a module for months doesn't produce
    /// an unusably long scroll (or, worse, hundreds of spawned rows). Newest runs are the ones
    /// worth seeing first, which is also the order LoadAll already returns them in.
    ///
    /// SETUP
    ///   1. A dim full-screen panel with a centred card is the expected look - see the Card
    ///      object CompetencyScreenBuilder makes for the exact structure.
    ///   2. Build one CompetencyHistoryRowView prefab and assign it to Row Prefab.
    ///   3. Leave Best Score Text / Count Text / the empty state unassigned if you don't want
    ///      them - everything here is optional except the row container and prefab.
    ///   4. Wire Close Button to this object being hidden - it already calls Close() itself via
    ///      the listener added in Awake, so an OnClick assignment in the Inspector is not needed
    ///      if the builder wired it, but is harmless if you also add one by hand.
    /// </summary>
    public class CompetencyHistoryView : MonoBehaviour
    {
        [Header("Header")]
        [Tooltip("\"COC3 - History\", filled in by Open(). Optional.")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("The Assessment-mode high score line. Optional.")]
        [SerializeField] private TMP_Text bestScoreText;

        [Tooltip("\"Showing the 15 most recent of 42 runs.\" Optional.")]
        [SerializeField] private TMP_Text countText;

        [Header("List")]
        [Tooltip("The Content object of the scroll view. Rows are spawned in here.")]
        [SerializeField] private RectTransform rowContainer;

        [SerializeField] private CompetencyHistoryRowView rowPrefab;

        [Header("Empty state")]
        [Tooltip("Shown instead of the list when this module has no recorded runs at all. Optional but worth having.")]
        [SerializeField] private GameObject emptyStatePanel;

        [SerializeField] private TMP_Text emptyStateText;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;

        [Header("Detail popup (optional)")]
        [Tooltip("Opened when a row's View button is clicked. Leave empty on a build that doesn't want the full-detail popup - rows still show their summary either way.")]
        [SerializeField] private CompetencyRunDetailView detailView;

        [Header("Clear history (optional)")]
        [Tooltip("Deletes every stored run for the module currently open - the run FILES only. Competency status and the module's high score live in the ledger and are never touched by this. Leave unassigned to not offer the feature at all.")]
        [SerializeField] private Button clearHistoryButton;

        [Tooltip("A small 'are you sure?' confirm strip shown when Clear History is pressed. SAME RULE AS ProfilePickerView's Delete button: leave this (and Clear History Button) unassigned and nothing destructive can happen with one click - Clear History Button hides itself automatically if this is missing.")]
        [SerializeField] private GameObject clearConfirmPanel;

        [Tooltip("The Yes / Clear button on the confirm strip. Wired automatically - leave its own OnClick list empty.")]
        [SerializeField] private Button clearConfirmYesButton;

        [SerializeField] private Button clearConfirmNoButton;

        [Header("Limits")]
        [Tooltip("How many of the most recent runs to render. The stored history is never trimmed - this only bounds what gets drawn.")]
        [SerializeField] private int maxRowsShown = 15;

        private readonly List<GameObject> spawnedRows = new List<GameObject>();
        private string openModuleId;
        private string openModuleTitle;

        // Set at the very start of Open(), before SetActive(true) - see Awake() for why.
        private bool openRequested;

        // ==================================================================

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            // Same rule ProfilePickerView's Delete button already follows for the same reason:
            // a destructive action needs its own confirm step, so if nothing is wired up to
            // provide one, the button that would trigger it is hidden rather than becoming a
            // one-click destructive action.
            if (clearHistoryButton != null)
            {
                if (clearConfirmPanel != null)
                {
                    clearHistoryButton.onClick.AddListener(() => clearConfirmPanel.SetActive(true));
                }
                else
                {
                    clearHistoryButton.gameObject.SetActive(false);
                    Debug.LogWarning("[CompetencyHistoryView] Clear History Button is assigned " +
                                     "but Clear Confirm Panel is not - hiding the button rather " +
                                     "than letting it delete history with no confirmation step. " +
                                     "Assign a confirm panel to show it.", this);
                }
            }

            if (clearConfirmYesButton != null) clearConfirmYesButton.onClick.AddListener(OnClearConfirmed);
            if (clearConfirmNoButton != null) clearConfirmNoButton.onClick.AddListener(HideClearConfirm);

            HideClearConfirm();

            // THE SAME Start/UnityEvent TRAP THIS PROJECT HAS ALREADY HIT AND FIXED FOUR TIMES
            // (PCPowerStation, SiteObservationPanel, TicketDiagnosisPanel, RepairVerificationPanel,
            // SettingsPanel). This popup starts inactive in the built scene, so Awake does not run
            // until the FIRST time something activates it - and Open() calling SetActive(true) for
            // the first time IS that something. Blindly hiding here would undo the very activation
            // that just woke this component up, which is exactly what forces a second click before
            // anything visibly opens. openRequested is set at the very start of Open(), before
            // SetActive(true), so by the time this line runs it can tell "just built, sitting
            // inactive in the scene" apart from "just asked to open".
            if (gameObject.activeSelf && !openRequested) gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            Refresh();

            if (CompetencyLedger.Instance != null)
                CompetencyLedger.Instance.OnLedgerChanged += Refresh;
        }

        private void OnDisable()
        {
            if (CompetencyLedger.Instance != null)
                CompetencyLedger.Instance.OnLedgerChanged -= Refresh;
        }

        // ==================================================================

        /// <summary>Opens the popup for one module. Safe to call any time - everything it shows
        /// is read straight off disk and the ledger, not off live gameplay state.</summary>
        public void Open(string moduleId, string moduleTitle)
        {
            openRequested = true;

            openModuleId = moduleId;
            openModuleTitle = moduleTitle;

            HideClearConfirm();

            if (titleText != null)
            {
                string label = string.IsNullOrEmpty(moduleTitle) ? moduleId : moduleTitle;
                titleText.text = label + " - History";
            }

            // Triggers OnEnable, which calls Refresh() - title and module id are set above so
            // Refresh sees the right module the very first time it runs.
            gameObject.SetActive(true);
        }

        public void Close() => gameObject.SetActive(false);

        /// <summary>Rebuilds the list for whichever module is currently open. Safe to call with
        /// nothing open - it simply does nothing.</summary>
        public void Refresh()
        {
            ClearRows();

            if (string.IsNullOrEmpty(openModuleId)) return;

            RefreshBestScore();

            string profileId = ProfileManager.Instance != null
                ? ProfileManager.Instance.ActiveProfileId
                : "default";

            List<AssessmentResult> forModule = new List<AssessmentResult>();
            foreach (var result in ResultsRepository.LoadAll(profileId))
            {
                if (result != null && result.moduleId == openModuleId) forModule.Add(result);
            }

            if (forModule.Count == 0)
            {
                ShowEmptyState("No runs recorded yet for " +
                                (string.IsNullOrEmpty(openModuleTitle) ? openModuleId : openModuleTitle) + ".");
                if (countText != null) countText.text = "";
                return;
            }

            HideEmptyState();

            int shown = Mathf.Min(forModule.Count, Mathf.Max(1, maxRowsShown));

            if (rowPrefab != null && rowContainer != null)
            {
                for (int i = 0; i < shown; i++)
                {
                    var row = Instantiate(rowPrefab, rowContainer);
                    row.Setup(forModule[i]);

                    // Fresh instance every Refresh (rows are destroyed and rebuilt wholesale, see
                    // ClearRows), so there's nothing stale to unsubscribe from.
                    row.ViewRequested += OpenDetail;

                    spawnedRows.Add(row.gameObject);
                }
            }

            if (countText != null)
            {
                countText.text = forModule.Count > shown
                    ? $"Showing the {shown} most recent of {forModule.Count} runs."
                    : $"{forModule.Count} run{(forModule.Count == 1 ? "" : "s")} recorded.";
            }
        }

        private void RefreshBestScore()
        {
            if (bestScoreText == null) return;

            ModuleBestScore best = CompetencyLedger.Instance != null
                ? CompetencyLedger.Instance.GetModuleBest(openModuleId)
                : null;

            bestScoreText.text = (best != null && best.bestScorePercent >= 0)
                ? $"Personal best: {best.bestScorePercent}%  (Assessment, {AssessmentResult.FormatWhen(best.achievedUtc)})"
                : "Personal best: none yet - complete an Assessment run to set one.";
        }

        // ==================================================================

        /// <summary>Forwards a row's View click to this popup's detail view. Logs rather than
        /// silently doing nothing when no detail view is assigned - "the button does nothing" and
        /// "there is no detail view to open" look identical from the outside otherwise, which is
        /// exactly the kind of quiet failure this project's own builder tries hard to avoid.</summary>
        private void OpenDetail(AssessmentResult result)
        {
            if (detailView != null)
            {
                detailView.Open(result);
                return;
            }

            Debug.LogWarning("[CompetencyHistoryView] View was clicked but no Detail View is " +
                             "assigned on '" + name + "' - there is nothing to open. Assign " +
                             "CompetencyRunDetailView to this component's Detail View field, or " +
                             "rebuild the screen so it's wired automatically.", this);
        }

        private void HideClearConfirm()
        {
            if (clearConfirmPanel != null) clearConfirmPanel.SetActive(false);
        }

        /// <summary>Yes was pressed on the confirm strip. Deletes only the run FILES for the
        /// module currently open - see clearHistoryButton's tooltip for why the ledger (status,
        /// high score) is deliberately left alone.</summary>
        private void OnClearConfirmed()
        {
            HideClearConfirm();

            if (string.IsNullOrEmpty(openModuleId)) return;

            string profileId = ProfileManager.Instance != null
                ? ProfileManager.Instance.ActiveProfileId
                : "default";

            ResultsRepository.DeleteForModule(profileId, openModuleId);

            Refresh();
        }

        private void ShowEmptyState(string message)
        {
            if (emptyStatePanel != null) emptyStatePanel.SetActive(true);
            if (emptyStateText != null) emptyStateText.text = message;
        }

        private void HideEmptyState()
        {
            if (emptyStatePanel != null) emptyStatePanel.SetActive(false);
        }

        private void ClearRows()
        {
            foreach (var row in spawnedRows)
            {
                if (row != null) Destroy(row);
            }
            spawnedRows.Clear();
        }
    }
}