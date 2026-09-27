using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// The results-and-review screen shown when an assessment ends: overall score, tasks completed,
    /// incorrect actions and missed requirements, the competencies assessed, areas to improve, and
    /// the Story Mode activities to practise next - with each mistake's CORRECT PROCEDURE, which is
    /// withheld during the run and given here, once it is over.
    ///
    /// ATTACH THIS TO: the AssessmentReview object on the Guidance canvas (the builder makes it) in
    /// every scene where an assessment ENDS - the COC1, COC2 and COC3 scenes, and COC4-Results.
    ///
    /// NOTHING TO WIRE TO THE MANAGERS. Every path that finishes a run already ends in
    /// ResultsRepository.Save - AssessmentRecorder, LegacyAssessmentBridge (COC1/COC2),
    /// COC4AssessmentReportView - and Save now raises ResultsRepository.Saved. This listens for it and
    /// opens for any result whose mode is Assessment. Story-mode records are ignored.
    ///
    /// THE OLD REPORT PANELS STILL WORK. Put the module's own report panel in Hide While Open and the
    /// learner reads this first; Close brings the old one back (with its own Back to Menu).
    ///
    /// Explanations come from, in order: the text of the wrong-choice dialog that was withheld
    /// during the run (MistakeJournal), then the ReviewLibrary rule the mistake matches.
    /// </summary>
    public class AssessmentReviewPanel : MonoBehaviour
    {
        public static AssessmentReviewPanel Instance { get; private set; }

        /// <summary>A plain-text summary of the last review, for the AI Tutor once it is unlocked.</summary>
        public static string LastReviewForTutor { get; private set; }

        [Header("UI")]
        public GameObject panel;
        public TMP_Text headerText;

        [Tooltip("The big score line.")]
        public TMP_Text summaryText;

        [Tooltip("Everything else, as one rich-text block inside a scroll view. Rich Text and Word Wrap ON.")]
        public TMP_Text bodyText;

        public ScrollRect scroll;

        [Tooltip("A button kept INACTIVE, cloned once per recommended activity.")]
        public Button activityButtonTemplate;

        [Tooltip("Where the activity buttons go. Usually the template's parent.")]
        public Transform activityContainer;

        public Button closeButton;
        public Button menuButton;

        [Header("Behaviour")]
        [Tooltip("Open by itself when an assessment result is saved.")]
        public bool openAutomatically = true;

        [Tooltip("Seconds after the result is saved - long enough for the stage dialog or time-up message to be seen.")]
        [Min(0f)] public float openDelay = 1f;

        [Tooltip("Only react to this module (COC1..COC4). Blank = any.")]
        public string moduleFilter = "";

        [Tooltip("Hidden while this is open, shown again on Close - the module's own report panel.")]
        public GameObject[] hideWhileOpen = new GameObject[0];

        [Min(1)] public int maxActivities = 3;

        public string mainMenuSceneName = "MainMenu";

        [Header("Colours (rich text)")]
        public string colorHeading = "#5FD4FF";
        public string colorGood = "#3ADB76";
        public string colorBad = "#FF8A6B";
        public string colorMuted = "#A9C6F0";
        public string colorGold = "#FFC93C";

        public bool IsOpen { get; private set; }
        public AssessmentResult Shown { get; private set; }

        private AssessmentResult pending;
        private float openAt;
        private bool recapture;
        private readonly List<GameObject> spawnedButtons = new List<GameObject>();
        private readonly List<GameObject> hiddenByUs = new List<GameObject>();

        // ==================================================================

        private void Awake()
        {
            Instance = this;
            if (panel != null) panel.SetActive(false);
            if (activityButtonTemplate != null) activityButtonTemplate.gameObject.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (menuButton != null) menuButton.onClick.AddListener(BackToMenu);
        }

        private void OnEnable() { ResultsRepository.Saved += HandleSaved; }
        private void OnDisable() { ResultsRepository.Saved -= HandleSaved; }

        private void OnDestroy()
        {
            if (IsOpen) GuidanceUtil.ReleasePlayer(false);
            if (Instance == this) Instance = null;
        }

        private void HandleSaved(AssessmentResult result)
        {
            if (!openAutomatically || result == null) return;
            if (result.Mode != SessionMode.Assessment) return;
            if (!string.IsNullOrEmpty(moduleFilter) && !string.Equals(moduleFilter, result.moduleId, StringComparison.OrdinalIgnoreCase)) return;

            pending = result;
            openAt = Time.unscaledTime + openDelay;
        }

        private void Update()
        {
            if (pending == null || Time.unscaledTime < openAt) return;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            AssessmentResult r = pending;
            pending = null;
            Open(r);
        }

        /// <summary>Opens the most recent saved assessment for a module - for a "Review" button on a menu.</summary>
        public void OpenLatest(string moduleId)
        {
            string profile = ProfileManager.Instance != null ? ProfileManager.Instance.ActiveProfileId : "default";
            AssessmentResult latest = ResultsRepository.LoadLatest(profile, moduleId);
            if (latest != null) Open(latest);
        }

        public void Open(AssessmentResult result)
        {
            if (result == null) return;
            Shown = result;

            ReviewLibrary library = ReviewLibrary.Active;

            if (headerText != null) headerText.text = $"{Readable(result.moduleId)} Assessment - Results and Review";
            if (summaryText != null) summaryText.text = BuildSummary(result);
            string body = BuildBody(result, library);   // also fills 'recommended'
            if (bodyText != null) bodyText.text = body;

            BuildActivityButtons(result, library);

            foreach (GameObject go in hideWhileOpen)
            {
                if (go != null && go.activeSelf) { go.SetActive(false); hiddenByUs.Add(go); }
            }

            if (panel != null) panel.SetActive(true);
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;

            if (!IsOpen)
            {
                IsOpen = true;
                recapture = GuidanceUtil.HoldPlayer();
            }

            LastReviewForTutor = BuildTutorSummary(result, library);
            GuidancePolicy.NotifyRunReviewed();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            if (panel != null) panel.SetActive(false);
            foreach (GameObject go in hiddenByUs) if (go != null) go.SetActive(true);
            hiddenByUs.Clear();

            GuidanceUtil.ReleasePlayer(recapture);
        }

        public void BackToMenu()
        {
            LeaveAssessment();
            if (!string.IsNullOrEmpty(mainMenuSceneName)) SceneManager.LoadScene(mainMenuSceneName);
        }

        /// <summary>Opens a recommended activity in Story Mode.</summary>
        public void Practise(ReviewLibrary.StoryActivity activity)
        {
            if (activity == null) return;
            LeaveAssessment();

            if (string.IsNullOrEmpty(activity.sceneName))
            {
                if (!string.IsNullOrEmpty(mainMenuSceneName)) SceneManager.LoadScene(mainMenuSceneName);
                return;
            }

            // Through SaveManager, exactly like the campaign menu, so the scene opens as a proper
            // Story run (fresh or continued per the project's progress rule) rather than half-set-up.
            if (SaveManager.Instance != null) SaveManager.Instance.SelectMode(activity.moduleId, activity.sceneName);
            else SceneManager.LoadScene(activity.sceneName);
        }

        private static void LeaveAssessment()
        {
            // Everything a finished run may still be holding - the same set the assessment managers'
            // own ReturnToMenu clears.
            TrainingMode.End();                 // also ends AssessmentMode
            DialogManager.SuppressTutorials = false;
            PlayerInputLock.ReleaseAll();
            CursorControl.Free();
        }

        // ==================================================================
        // Text
        // ==================================================================

        private string BuildSummary(AssessmentResult r)
        {
            string verdict = Verdict(r, out bool competent);
            string vColor = competent ? colorGood : colorBad;
            return $"<b>{r.score} / {r.maxScore}</b>  ({r.ScorePercent}%)     " +
                   $"<color={vColor}><b>{verdict}</b></color>\n" +
                   $"<size=70%><color={colorMuted}>Time {AssessmentResult.FormatTime(r.durationSeconds)}   •   " +
                   $"Mistakes {r.mistakeCount}" +
                   (r.FirstAttemptPercent >= 0 ? $"   •   Right first time {r.FirstAttemptPercent}%" : "") +
                   (r.KnowledgeAttempted ? $"   •   Knowledge {r.KnowledgePercent}%" : "") +
                   (string.IsNullOrEmpty(r.learnerName) ? "" : $"   •   {r.learnerName}") +
                   "</color></size>";
        }

        private static string Verdict(AssessmentResult r, out bool competent)
        {
            competent = false;
            if (r.criteria != null && r.criteria.Count > 0)
            {
                competent = true;
                foreach (CriterionOutcome c in r.criteria)
                    if (c != null && c.Status != CompetencyStatus.Competent) { competent = false; break; }
            }
            else
            {
                int pass = CompetencyLedger.Instance != null ? CompetencyLedger.Instance.PassThresholdPercent : 75;
                competent = r.completed && r.ScorePercent >= pass;
            }
            return competent ? "Competent" : "Not Yet Competent";
        }

        private string BuildBody(AssessmentResult r, ReviewLibrary library)
        {
            var sb = new StringBuilder();
            var areas = new Dictionary<string, int>();
            var activityVotes = new Dictionary<string, int>();

            // ---- tasks ------------------------------------------------------
            int done = 0;
            foreach (StageOutcome s in r.stages) if (s != null && s.completed) done++;

            Heading(sb, $"Tasks completed successfully  ({done} of {r.stages.Count})");
            bool anyDone = false;
            foreach (StageOutcome s in r.stages)
            {
                if (s == null || !s.completed) continue;
                anyDone = true;
                sb.Append($"<color={colorGood}>•</color>  {s.displayName}");
                if (s.completedAtSeconds >= 0f) sb.Append($"  <color={colorMuted}>({AssessmentResult.FormatTime(s.completedAtSeconds)})</color>");
                if (s.attempts > 1) sb.Append($"  <color={colorMuted}>- {s.attempts} attempts</color>");
                sb.Append('\n');
            }
            if (!anyDone) sb.Append($"<color={colorMuted}>None.</color>\n");
            sb.Append('\n');

            // ---- missed requirements -------------------------------------------
            var missed = new List<StageOutcome>();
            foreach (StageOutcome s in r.stages) if (s != null && !s.completed) missed.Add(s);

            if (missed.Count > 0)
            {
                Heading(sb, $"Missed requirements  ({missed.Count})");
                foreach (StageOutcome s in missed)
                    sb.Append($"<color={colorBad}>•</color>  {s.displayName}  <color={colorMuted}>- not completed</color>\n");
                if (!r.completed)
                    sb.Append($"<color={colorMuted}>The run did not finish every required task, so it cannot count as Competent.</color>\n");
                sb.Append('\n');
            }

            // ---- incorrect actions ------------------------------------------------
            Heading(sb, $"Incorrect actions  ({r.mistakes.Count})");
            if (r.mistakes.Count == 0)
            {
                sb.Append($"<color={colorGood}>No incorrect actions were recorded.</color>\n\n");
            }
            else
            {
                int n = 0;
                foreach (MistakeEntry m in r.mistakes)
                {
                    if (m == null) continue;
                    n++;

                    ReviewLibrary.MistakeRule rule = library != null ? library.Match(r.moduleId, m.category, m.description) : null;

                    sb.Append($"<b>{n}.</b> {m.description}");
                    string meta = AssessmentResult.FormatTime(m.atSeconds) + "  •  " + m.category;
                    if (!string.IsNullOrEmpty(m.context)) meta += "  •  " + m.context;
                    sb.Append($"\n    <size=85%><color={colorMuted}>{meta}</color></size>\n");

                    string correct = !string.IsNullOrEmpty(m.correction) ? m.correction
                                   : rule != null && !string.IsNullOrEmpty(rule.correctProcedure) ? rule.correctProcedure
                                   : rule != null ? rule.howToFix : "";

                    if (!string.IsNullOrEmpty(correct))
                        sb.Append($"    <color={colorGold}>Correct procedure:</color> {Indent(correct)}\n");

                    string area = rule != null && !string.IsNullOrEmpty(rule.areaToImprove) ? rule.areaToImprove : AreaForCategory(m.category);
                    Count(areas, area);
                    if (rule != null && !string.IsNullOrEmpty(rule.activityId)) Count(activityVotes, rule.activityId);

                    sb.Append('\n');
                }

                if (r.mistakeCount > r.mistakes.Count)
                    sb.Append($"<color={colorMuted}>{r.mistakeCount - r.mistakes.Count} further mistake(s) were counted without details.</color>\n\n");
            }

            // ---- competencies -----------------------------------------------------
            if (r.criteria != null && r.criteria.Count > 0)
            {
                Heading(sb, "Competencies assessed");
                foreach (CriterionOutcome c in r.criteria)
                {
                    if (c == null) continue;
                    bool ok = c.Status == CompetencyStatus.Competent;
                    sb.Append($"<b>{c.outcomeCode}</b>  {c.outcomeTitle}\n    <color={(ok ? colorGood : colorBad)}>{(ok ? "Competent" : "Not yet competent")}</color>" +
                              $"  <color={colorMuted}>(evidence {c.evidenceSatisfied} of {c.evidenceRequired})</color>\n");

                    if (!ok)
                    {
                        ReviewLibrary.OutcomeLink link = library != null ? library.FindOutcome(r.moduleId, c.outcomeCode) : null;
                        Count(areas, link != null && !string.IsNullOrEmpty(link.areaToImprove) ? link.areaToImprove : c.outcomeTitle);
                        if (link != null && !string.IsNullOrEmpty(link.activityId)) Count(activityVotes, link.activityId, 2);
                    }
                }
                sb.Append('\n');
            }

            if (r.KnowledgeAttempted)
            {
                sb.Append($"<color={colorMuted}>Underpinning knowledge: {r.knowledgeCorrect} of {r.knowledgeQuestions} correct ({r.KnowledgePercent}%).</color>\n\n");
            }

            // ---- areas to improve -----------------------------------------------------
            Heading(sb, "Areas that need improvement");
            if (areas.Count == 0 && missed.Count == 0)
            {
                sb.Append($"<color={colorGood}>None recorded - well done. Keep the same method on the next job.</color>\n\n");
            }
            else
            {
                foreach (var kv in Sorted(areas))
                    sb.Append($"•  {kv.Key}" + (kv.Value > 1 ? $"  <color={colorMuted}>({kv.Value})</color>" : "") + "\n");
                foreach (StageOutcome s in missed)
                    if (!areas.ContainsKey(s.displayName)) sb.Append($"•  Completing: {s.displayName}\n");
                sb.Append('\n');
            }

            // ---- recommended activities --------------------------------------------
            var activities = new List<ReviewLibrary.StoryActivity>();
            if (library != null)
            {
                foreach (var kv in Sorted(activityVotes))
                {
                    ReviewLibrary.StoryActivity a = library.FindActivity(kv.Key);
                    if (a != null && !activities.Contains(a)) activities.Add(a);
                    if (activities.Count >= maxActivities) break;
                }
            }

            // Nothing pointed anywhere specific, but the run still fell short (time ran out, stages
            // missed): recommend the module's own Story activities rather than nothing.
            bool fellShort = missed.Count > 0 || !r.completed || areas.Count > 0;
            if (activities.Count == 0 && fellShort && library != null && library.activities != null)
            {
                foreach (ReviewLibrary.StoryActivity a in library.activities)
                {
                    if (a == null || !string.Equals(a.moduleId, r.moduleId, StringComparison.OrdinalIgnoreCase)) continue;
                    activities.Add(a);
                    if (activities.Count >= maxActivities) break;
                }
            }

            if (activities.Count > 0)
            {
                Heading(sb, "Recommended Story Mode practice");
                foreach (ReviewLibrary.StoryActivity a in activities)
                {
                    sb.Append($"•  <b>{a.title}</b>");
                    if (!string.IsNullOrEmpty(a.whatToPractise)) sb.Append($"\n    <color={colorMuted}>{a.whatToPractise}</color>");
                    sb.Append('\n');
                }
            }

            recommended = activities;
            return sb.ToString().TrimEnd();
        }

        private List<ReviewLibrary.StoryActivity> recommended = new List<ReviewLibrary.StoryActivity>();

        private void BuildActivityButtons(AssessmentResult r, ReviewLibrary library)
        {
            foreach (GameObject go in spawnedButtons) if (go != null) Destroy(go);
            spawnedButtons.Clear();

            if (activityButtonTemplate == null) return;
            Transform parent = activityContainer != null ? activityContainer : activityButtonTemplate.transform.parent;

            foreach (ReviewLibrary.StoryActivity a in recommended)
            {
                Button b = Instantiate(activityButtonTemplate, parent);
                b.gameObject.SetActive(true);
                b.name = "Practise_" + a.activityId;

                TMP_Text label = b.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = "Practise: " + a.title;

                ReviewLibrary.StoryActivity captured = a;
                b.onClick.AddListener(() => Practise(captured));
                spawnedButtons.Add(b.gameObject);
            }
        }

        private string BuildTutorSummary(AssessmentResult r, ReviewLibrary library)
        {
            var sb = new StringBuilder();
            sb.Append($"The trainee has just FINISHED the {Readable(r.moduleId)} assessment (score {r.ScorePercent}%, " +
                      $"{r.mistakeCount} mistake(s)). The assessment is over, so you may now explain the correct procedures. ");
            int n = 0;
            foreach (MistakeEntry m in r.mistakes)
            {
                if (m == null || n >= 8) continue;
                n++;
                ReviewLibrary.MistakeRule rule = library != null ? library.Match(r.moduleId, m.category, m.description) : null;
                string correct = !string.IsNullOrEmpty(m.correction) ? m.correction : rule != null ? rule.correctProcedure : "";
                sb.Append($"Mistake {n}: {m.description}. ");
                if (!string.IsNullOrEmpty(correct)) sb.Append($"Correct procedure: {correct} ");
            }
            sb.Append("Help them understand why each mistake mattered and how to do it right next time.");
            return sb.ToString();
        }

        // ------------------------------------------------------------------

        private void Heading(StringBuilder sb, string text)
        {
            sb.Append($"<b><color={colorHeading}>{text.ToUpperInvariant()}</color></b>\n");
        }

        private static string Indent(string text) => (text ?? "").Replace("\n", "\n    ");

        private static void Count(Dictionary<string, int> d, string key, int weight = 1)
        {
            if (string.IsNullOrEmpty(key)) return;
            d.TryGetValue(key, out int v);
            d[key] = v + weight;
        }

        private static List<KeyValuePair<string, int>> Sorted(Dictionary<string, int> d)
        {
            var list = new List<KeyValuePair<string, int>>(d);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        private static string AreaForCategory(string category)
        {
            switch ((category ?? "").ToLowerInvariant())
            {
                case "sequence":
                case "procedure": return "Following the correct procedure order";
                case "identification": return "Identifying and recording the cause";
                case "configuration": return "Entering configuration values accurately";
                case "connectivity": return "Testing and verifying connectivity";
                case "cable": return "Cable termination and testing";
                case "diagnosis": return "Diagnosing from evidence";
                case "verification": return "Verifying the repair";
                case "unresolved": return "Completing each job";
                case "safety": return "Occupational health and safety";
                case "time": return "Working within the time allowed";
                default: return "General procedure";
            }
        }

        private static string Readable(string id) =>
            !string.IsNullOrEmpty(id) && id.Length == 4 && id.StartsWith("COC") ? "COC " + id[3] : (id ?? "");
    }
}
