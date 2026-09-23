using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// The band that introduces one module's block of outcomes on the competency
    /// profile screen.
    ///
    /// PUT THIS ON: a prefab, and assign it to
    /// CompetencyProfileView.moduleHeaderPrefab.
    ///
    /// Suggested layout:
    ///   [COC1] [Install and configure computer systems] [2 of 5 competent] [bar]
    ///
    /// All fields optional except moduleTitleText.
    /// </summary>
    public class CompetencyModuleHeaderView : MonoBehaviour
    {
        [Tooltip("The module id, e.g. \"COC1\". Optional.")]
        [SerializeField] private TMP_Text moduleIdText;

        [Tooltip("The unit title, e.g. \"Install and configure computer systems\".")]
        [SerializeField] private TMP_Text moduleTitleText;

        [Tooltip("Shows e.g. \"2 of 5 competent\". Optional.")]
        [SerializeField] private TMP_Text summaryText;

        [Tooltip("Optional 0-1 bar of competent outcomes over total outcomes.")]
        [SerializeField] private Slider progressBar;

        [Tooltip("Optional - switched on only when every outcome in the module is Competent.")]
        [SerializeField] private GameObject completeBadge;

        [Header("Evidence lines (optional)")]
        [Tooltip("Shows whether the PERFORMANCE requirement has been met - the graded run itself.\n\n" +
                 "TESDA's Evidence Plan lists demonstration and written test as two separate " +
                 "requirements, and an assessor has to see each one on its own. Showing them as one " +
                 "figure is the thing competency-based assessment specifically does not do.")]
        [SerializeField] private TMP_Text performanceEvidenceText;

        [Tooltip("Shows whether the UNDERPINNING KNOWLEDGE requirement has been met - the quiz.")]
        [SerializeField] private TMP_Text knowledgeEvidenceText;

        [Tooltip("Optional - the whole knowledge row, hidden when this ledger does not require " +
                 "knowledge evidence, so a build with no quizzes shows no empty line.")]
        [SerializeField] private GameObject knowledgeRow;

        [Tooltip("Colour for a requirement that has been met.")]
        [SerializeField] private Color metColour = new Color32(0x1B, 0x7F, 0x4B, 0xFF);

        [Tooltip("Colour for a requirement not yet met. Deliberately neutral, not red - not yet " +
                 "done is not a failure.")]
        [SerializeField] private Color notMetColour = new Color32(0x6B, 0x72, 0x80, 0xFF);

        [Header("History (optional)")]
        [Tooltip("\"Best: 92%\" - this module's Assessment-mode high score. Optional.")]
        [SerializeField] private TMP_Text bestScoreText;

        [Tooltip("Gold, matching the palette this project reserves for anything earned.")]
        [SerializeField] private Color bestScoreColour = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

        [Tooltip("Opens CompetencyHistoryView for this module. Optional - leave empty on a build that doesn't want a history popup.")]
        [SerializeField] private Button historyButton;

        /// <summary>Fired when History Button is clicked, with this row's module id and title.
        /// CompetencyProfileView subscribes to this on every row it spawns and forwards it to its
        /// own CompetencyHistoryView.</summary>
        public event Action<string, string> HistoryRequested;

        private string currentModuleId;
        private string currentModuleTitle;

        private void Awake()
        {
            // Registered once, in Awake, against the FIELDS below rather than against whatever
            // definition happened to be passed to Setup at the time - Setup can be, and is,
            // called again later (a Refresh recreates every row from scratch, so in practice this
            // never fires twice on the same instance, but reading the fields rather than closing
            // over a parameter is what makes that safe even if that ever changes).
            if (historyButton != null)
                historyButton.onClick.AddListener(
                    () => HistoryRequested?.Invoke(currentModuleId, currentModuleTitle));
        }

        public void Setup(CompetencyDefinition definition, int competent, int total)
        {
            if (definition == null) return;

            currentModuleId = definition.moduleId;
            currentModuleTitle = definition.unitTitle;

            if (moduleIdText != null) moduleIdText.text = definition.moduleId;
            if (moduleTitleText != null) moduleTitleText.text = definition.unitTitle;

            FillEvidenceLines(definition.moduleId, competent, total);
            FillBestScore(definition.moduleId);

            if (summaryText != null)
                summaryText.text = total > 0 ? $"{competent} of {total} competent" : "No outcomes defined";

            if (progressBar != null)
            {
                // maxValue is floored at 1 so a module with no outcomes yet shows an
                // empty bar rather than dividing by zero and rendering full.
                progressBar.maxValue = Mathf.Max(1, total);
                progressBar.value = competent;
            }

            if (completeBadge != null)
                completeBadge.SetActive(total > 0 && competent == total);
        }

        /// <summary>
        /// The two evidence requirements, side by side and never blended.
        ///
        /// NO PERCENTAGE ON THE PERFORMANCE LINE. The outcome rows already carry the judgement, and
        /// a percentage next to them teaches the wrong shape - competency-based assessment is
        /// met/not met, which is why CompetencyStatus has three words and no number. The knowledge
        /// line does print its percentage, because a written test genuinely has a mark and the
        /// learner needs to know how far off they are.
        /// </summary>
        private void FillEvidenceLines(string moduleId, int competent, int total)
        {
            CompetencyLedger ledger = CompetencyLedger.Instance;

            if (performanceEvidenceText != null)
            {
                bool met = total > 0 && competent == total;
                performanceEvidenceText.text =
                    "Performance (demonstration): " + (met ? "met" : "not yet met");
                performanceEvidenceText.color = met ? metColour : notMetColour;
            }

            if (ledger == null) return;

            // A build with no quizzes hides the row rather than showing a line that can never be
            // met - an empty requirement on an evidence screen reads as a failure.
            if (knowledgeRow != null) knowledgeRow.SetActive(ledger.RequiresKnowledgeEvidence);
            if (!ledger.RequiresKnowledgeEvidence) return;

            if (knowledgeEvidenceText == null) return;

            KnowledgeRecord knowledge = ledger.GetKnowledge(moduleId);
            int need = ledger.KnowledgeThresholdPercent;

            if (knowledge == null || knowledge.bestPercent < 0)
            {
                knowledgeEvidenceText.text = $"Knowledge (written test): not taken  ({need}% needed)";
                knowledgeEvidenceText.color = notMetColour;
                return;
            }

            if (knowledge.passed)
            {
                knowledgeEvidenceText.text = $"Knowledge (written test): met  ({knowledge.bestPercent}%)";
                knowledgeEvidenceText.color = metColour;
                return;
            }

            // Best-ever is shown even when it is below the line, and even when it is above it but
            // was scored in a practice run - "82%, needs a graded run" is a far more useful thing to
            // read than "not met".
            bool aboveLine = knowledge.bestPercent >= need;
            knowledgeEvidenceText.text = aboveLine
                ? $"Knowledge (written test): {knowledge.bestPercent}% in practice - needs a graded run"
                : $"Knowledge (written test): {knowledge.bestPercent}%, {need}% needed";
            knowledgeEvidenceText.color = notMetColour;
        }

        /// <summary>
        /// "Best: 92%" in gold, or a neutral "Best: -" when no Assessment run has ever completed
        /// for this module. Deliberately silent about WHY there's no best yet - the History popup
        /// this sits beside is where that story (or its absence) gets told in full.
        /// </summary>
        private void FillBestScore(string moduleId)
        {
            if (bestScoreText == null) return;

            CompetencyLedger ledger = CompetencyLedger.Instance;
            ModuleBestScore best = ledger != null ? ledger.GetModuleBest(moduleId) : null;

            if (best != null && best.bestScorePercent >= 0)
            {
                bestScoreText.text = $"Best: {best.bestScorePercent}%";
                bestScoreText.color = bestScoreColour;
            }
            else
            {
                bestScoreText.text = "Best: -";
                bestScoreText.color = notMetColour;
            }
        }
    }
}