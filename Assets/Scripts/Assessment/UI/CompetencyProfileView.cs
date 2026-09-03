using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// The competency profile screen: every learning outcome across COC1-COC4,
    /// with the learner's standing on each.
    ///
    /// This is what the whole Tier 0 data layer exists to produce. It gives a
    /// learner a reason to replay the outcomes they have not yet demonstrated
    /// rather than the one they enjoy most, and it doubles as a rehearsal for
    /// the real assessment result summary.
    ///
    /// ATTACH THIS TO: the competency screen panel on your Main Menu canvas.
    /// Add the panel to MenuNavigator's Screens list like any other screen, and
    /// wire a menu button to MenuNavigator > Show (GameObject) with this panel
    /// in the slot.
    ///
    /// SETUP
    ///   1. Under the panel, make a vertical scroll view. Assign its Content
    ///      object to Row Container, and give that Content a Vertical Layout
    ///      Group plus a Content Size Fitter (vertical = Preferred Size), or the
    ///      rows will pile up on top of each other.
    ///   2. Build two prefabs - one CompetencyModuleHeaderView, one
    ///      CompetencyOutcomeRowView - and assign them below.
    ///   3. Leave everything else unassigned if you want a plain list.
    ///
    /// WHERE THE DATA COMES FROM
    /// Entirely from CompetencyLedger, never from live TaskManager state. That
    /// matters because this screen normally runs on the Main Menu, where no
    /// gameplay scene - and therefore no TaskManager - exists. Everything shown
    /// here was recorded at the end of a run and persisted to the learner's
    /// ledger file.
    /// </summary>
    public class CompetencyProfileView : MonoBehaviour
    {
        [Header("Header")]
        [Tooltip("Shows the learner's name and section. Optional.")]
        [SerializeField] private TMP_Text learnerText;

        [Tooltip("Shows e.g. \"6 of 14 outcomes competent\" across all modules. Optional.")]
        [SerializeField] private TMP_Text overallText;

        [Tooltip("Optional 0-1 bar for the overall figure.")]
        [SerializeField] private Slider overallBar;

        [Header("List")]
        [Tooltip("The Content object of your scroll view. Rows are spawned in here.")]
        [SerializeField] private RectTransform rowContainer;

        [SerializeField] private CompetencyModuleHeaderView moduleHeaderPrefab;
        [SerializeField] private CompetencyOutcomeRowView outcomeRowPrefab;

        [Header("Empty state")]
        [Tooltip("Shown instead of the list when there are no CompetencyDefinition assets at all. Optional but worth having - an empty screen otherwise looks broken.")]
        [SerializeField] private GameObject emptyStatePanel;

        [SerializeField] private TMP_Text emptyStateText;

        [Header("Optional buttons")]
        [Tooltip("Exports every stored result for this learner as a long-format CSV. Optional.")]
        [SerializeField] private Button exportButton;

        [Tooltip("Shown briefly after an export, with the path written. Optional.")]
        [SerializeField] private TMP_Text exportStatusText;

        [SerializeField] private Button refreshButton;

        private readonly List<GameObject> spawnedRows = new List<GameObject>();

        // ==================================================================

        private void Awake()
        {
            if (exportButton != null) exportButton.onClick.AddListener(ExportCsv);
            if (refreshButton != null) refreshButton.onClick.AddListener(Refresh);
        }

        private void OnEnable()
        {
            // Rebuilt every time the screen opens rather than kept in sync
            // continuously - it is a screen you visit, and a rebuild of at most a
            // few dozen rows is far cheaper than the bookkeeping to update them
            // in place.
            Refresh();

            if (CompetencyLedger.Instance != null)
                CompetencyLedger.Instance.OnLedgerChanged += Refresh;

            if (ProfileManager.Instance != null)
                ProfileManager.Instance.OnActiveProfileChanged += HandleProfileChanged;
        }

        private void OnDisable()
        {
            if (CompetencyLedger.Instance != null)
                CompetencyLedger.Instance.OnLedgerChanged -= Refresh;

            if (ProfileManager.Instance != null)
                ProfileManager.Instance.OnActiveProfileChanged -= HandleProfileChanged;

            if (exportStatusText != null) exportStatusText.text = "";
        }

        private void HandleProfileChanged(LearnerProfile profile) => Refresh();

        // ==================================================================

        /// <summary>Rebuilds the whole screen. Safe to call any time; also wired
        /// to the optional Refresh button.</summary>
        public void Refresh()
        {
            ClearRows();

            if (learnerText != null)
            {
                var profile = ProfileManager.Instance != null ? ProfileManager.Instance.ActiveProfile : null;
                learnerText.text = profile != null ? profile.Label : "No learner selected";
            }

            var ledger = CompetencyLedger.Instance;
            if (ledger == null)
            {
                ShowEmptyState("The competency ledger isn't running.\n\nAdd the AssessmentManagers prefab to this scene.");
                return;
            }

            if (ledger.Definitions == null || ledger.Definitions.Count == 0)
            {
                ShowEmptyState("No competency definitions have been set up yet.\n\nCreate them with CircuitCrafter > Generate Competency Definitions, then assign all four to the Competency Ledger.");
                return;
            }

            HideEmptyState();

            int totalCompetent = 0;
            int totalOutcomes = 0;

            foreach (var definition in ledger.Definitions)
            {
                if (definition == null) continue;

                ledger.GetModuleProgress(definition.moduleId, out int competent, out int total);
                totalCompetent += competent;
                totalOutcomes += total;

                if (moduleHeaderPrefab != null && rowContainer != null)
                {
                    var header = Instantiate(moduleHeaderPrefab, rowContainer);
                    header.Setup(definition, competent, total);
                    spawnedRows.Add(header.gameObject);
                }

                if (outcomeRowPrefab == null || rowContainer == null) continue;

                foreach (var outcome in definition.outcomes)
                {
                    if (outcome == null || string.IsNullOrEmpty(outcome.code)) continue;

                    // Deliberately shows outcomes with no record too. Hiding what
                    // hasn't been attempted would turn the screen into a trophy
                    // cabinet; showing it makes it a map of what's left.
                    OutcomeRecord record = FindRecord(ledger, definition.moduleId, outcome.code);

                    var row = Instantiate(outcomeRowPrefab, rowContainer);
                    row.Setup(outcome, record);
                    spawnedRows.Add(row.gameObject);
                }
            }

            if (overallText != null)
            {
                overallText.text = totalOutcomes > 0
                    ? $"{totalCompetent} of {totalOutcomes} outcomes competent"
                    : "No outcomes defined";
            }

            if (overallBar != null)
            {
                overallBar.maxValue = Mathf.Max(1, totalOutcomes);
                overallBar.value = totalCompetent;
            }
        }

        private static OutcomeRecord FindRecord(CompetencyLedger ledger, string moduleId, string code)
        {
            foreach (var record in ledger.GetRecords(moduleId))
            {
                if (record != null && record.outcomeCode == code) return record;
            }
            return null;
        }

        // ==================================================================

        private void ExportCsv()
        {
            string profileId = ProfileManager.Instance != null
                ? ProfileManager.Instance.ActiveProfileId
                : "default";

            string path = ResultsRepository.ExportOutcomesCsv(profileId);

            if (exportStatusText == null) return;

            exportStatusText.text = string.IsNullOrEmpty(path)
                ? "Export failed - see the console for details."
                : $"Exported to:\n{path}";
        }

        private void ShowEmptyState(string message)
        {
            if (emptyStatePanel != null) emptyStatePanel.SetActive(true);
            if (emptyStateText != null) emptyStateText.text = message;

            if (overallText != null) overallText.text = "";
            if (overallBar != null) overallBar.value = 0;

            // The message is worth a console line too - the panel is optional, and
            // without it this state is a blank screen with no explanation.
            Debug.LogWarning("[CompetencyProfileView] " + message.Replace("\n\n", " "), this);
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
