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

        [Header("Extra rows under each module")]
        [Tooltip("Add a QUIZ row under every module that has a quiz - the underpinning-knowledge " +
                 "score. It follows the LATEST attempt, so every story run moves it; the module " +
                 "header keeps the best.")]
        [SerializeField] private bool showQuizRow = true;

        [Tooltip("Gold, the colour this project already reserves for anything earned. Used for the " +
                 "quiz row and the COC4 faults row so they read as one kind of thing, distinct from " +
                 "the graded outcome rows above them.")]
        [SerializeField] private Color extraRowColour = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

        [Tooltip("The module with no quiz, which gets a repaired-faults row instead. Normally COC4. " +
                 "Leave blank to give every module a quiz row and none a faults row.")]
        [SerializeField] private string ticketsModuleId = "COC4";

        [Tooltip("How many service-call tickets that module has in total - the number the bar has " +
                 "to reach to be full.\n\n" +
                 "Typed in rather than counted, because the tickets live in the COC4 scene and this " +
                 "screen normally runs on the Main Menu where that scene is not loaded. Set it to " +
                 "the number of TicketData assets the ticket hub offers. 0 hides the row.")]
        [SerializeField] private int ticketsInModule = 2;

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

        [Header("History popup (optional)")]
        [Tooltip("Opened when a module header's History button is clicked. Leave empty on a build that doesn't want a history popup.")]
        [SerializeField] private CompetencyHistoryView historyView;

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

                    // Fresh instance every Refresh (rows are destroyed and rebuilt wholesale, see
                    // ClearRows), so there's nothing to unsubscribe from - the old subscription
                    // dies with the old row.
                    header.HistoryRequested += OpenHistory;

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

                SpawnExtraRow(ledger, definition.moduleId);
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

        /// <summary>
        /// One extra row beneath a module's outcomes: its quiz score, or - for the module that has
        /// no quiz - the service calls it has repaired.
        ///
        /// BOTH ARE STORY-MODE FIGURES, AND THAT IS WHY THEY ARE NOT OUTCOME ROWS. The outcomes
        /// above them are evidence: what the learner has PROVEN in a graded run. A quiz here is
        /// practice - it only ever runs in story mode - and a repaired fault in story mode is work
        /// done with the game leading. Mixing them into the same rows would put practice figures
        /// under a heading that claims proof, which is the one thing this screen must not do.
        /// Hence the separate row, and the gold rather than the graded palette.
        /// </summary>
        private void SpawnExtraRow(CompetencyLedger ledger, string moduleId)
        {
            if (outcomeRowPrefab == null || rowContainer == null || string.IsNullOrEmpty(moduleId))
                return;

            bool isTicketModule = !string.IsNullOrEmpty(ticketsModuleId)
                && string.Equals(moduleId.Trim(), ticketsModuleId.Trim(),
                                 System.StringComparison.OrdinalIgnoreCase);

            if (isTicketModule) SpawnTicketRow(moduleId);
            else if (showQuizRow) SpawnQuizRow(ledger, moduleId);
        }

        private void SpawnQuizRow(CompetencyLedger ledger, string moduleId)
        {
            KnowledgeRecord knowledge = ledger.GetKnowledge(moduleId);

            // latestPercent is what the bar follows; bestPercent stands in for a ledger written
            // before that field existed and not yet migrated.
            int percent = -1;
            if (knowledge != null)
                percent = knowledge.latestScored ? knowledge.latestPercent : knowledge.bestPercent;

            int threshold = ledger.KnowledgeThresholdPercent;

            string status = percent < 0
                ? "Not taken"
                : percent >= threshold ? "Above the pass mark" : $"{threshold}% needed";

            string detail = knowledge == null || knowledge.attempts <= 0
                ? "No attempts yet"
                : knowledge.attempts == 1 ? "1 attempt" : $"{knowledge.attempts} attempts";

            var row = Instantiate(outcomeRowPrefab, rowContainer);
            row.SetupMetric("QUIZ", "Underpinning knowledge (quiz)", percent, status, detail,
                            extraRowColour);
            spawnedRows.Add(row.gameObject);
        }

        private void SpawnTicketRow(string moduleId)
        {
            if (ticketsInModule <= 0) return;

            int repaired = CountRepairedTickets(moduleId);
            int percent = Mathf.Clamp(Mathf.RoundToInt(100f * repaired / ticketsInModule), 0, 100);

            string status = repaired >= ticketsInModule ? "All faults repaired" : "In progress";
            string detail = $"{repaired} of {ticketsInModule} repaired";

            var row = Instantiate(outcomeRowPrefab, rowContainer);
            row.SetupMetric("FAULTS", "Service calls repaired (story)", percent, status, detail,
                            extraRowColour);
            spawnedRows.Add(row.gameObject);
        }

        /// <summary>
        /// How many distinct tickets this learner has actually REPAIRED in story mode.
        ///
        /// COUNTED FROM THE RESULT FILES, the same source COC4StoryTicketRecorder uses to decide
        /// what it has already recorded - so the two can never disagree about which tickets are
        /// done. The ledger cannot answer this: it stores outcomes, not tickets, and every ticket
        /// evidences the same three COC4 outcomes.
        ///
        /// THE REPAIR STAGE IS THE TEST, NOT THE TICKET STAGE. The recorder writes
        /// coc4_ticket_&lt;id&gt; as completed for every service call it records, including one the
        /// learner only diagnosed - so counting those would fill the bar for work not done. The
        /// coc4_repair stage in the same result is what says the machine was actually fixed.
        ///
        /// Guided runs only, because the bar is explicitly about story mode.
        /// </summary>
        private int CountRepairedTickets(string moduleId)
        {
            string profileId = ProfileManager.Instance != null
                ? ProfileManager.Instance.ActiveProfileId
                : "default";

            List<AssessmentResult> results = ResultsRepository.LoadAll(profileId);
            if (results == null) return 0;

            const string prefix = "coc4_ticket_";
            var repaired = new HashSet<string>();

            foreach (AssessmentResult result in results)
            {
                if (result == null || result.stages == null) continue;
                if (!string.Equals(result.moduleId, moduleId, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (result.Mode != SessionMode.Guided) continue;

                bool fixedIt = false;
                foreach (StageOutcome stage in result.stages)
                {
                    if (stage != null && stage.stageId == "coc4_repair" && stage.completed)
                    {
                        fixedIt = true;
                        break;
                    }
                }

                if (!fixedIt) continue;

                foreach (StageOutcome stage in result.stages)
                {
                    if (stage == null || string.IsNullOrEmpty(stage.stageId)) continue;
                    if (!stage.stageId.StartsWith(prefix)) continue;

                    string ticketId = stage.stageId.Substring(prefix.Length);
                    if (!string.IsNullOrEmpty(ticketId)) repaired.Add(ticketId);
                }
            }

            return repaired.Count;
        }

        /// <summary>Forwards a module header's History click to this screen's popup. A no-op if
        /// no popup is assigned, so a build that doesn't want the feature can simply leave the
        /// field empty rather than needing a special build configuration.</summary>
        private void OpenHistory(string moduleId, string moduleTitle)
        {
            if (historyView != null) historyView.Open(moduleId, moduleTitle);
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