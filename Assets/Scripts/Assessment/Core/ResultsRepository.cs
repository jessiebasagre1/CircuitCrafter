using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// Where results go to survive. A static class rather than a MonoBehaviour
    /// because it holds no state - every method is a file operation keyed by
    /// profile id.
    ///
    /// LAYOUT ON DISK (under Application.persistentDataPath)
    ///   Results/{profileId}/{resultId}.json   one full record per run
    ///   Results/{profileId}/results.csv       one row per run, appended
    ///   Exports/{filename}.csv                on-demand exports for analysis
    ///
    /// TWO FORMATS ON PURPOSE
    /// The JSON is the evidence: complete, self-describing, readable back into an
    /// AssessmentResult years later. The CSV is the convenience: it opens in
    /// Excel or a stats package without anyone writing a parser, which is what
    /// actually determines whether the data gets analysed.
    ///
    /// EVERY WRITE IS WRAPPED
    /// A failed write must never take the run down with it - the learner has
    /// already done the work, and losing the report is bad but losing the session
    /// is worse. Failures are logged and swallowed.
    /// </summary>
    public static class ResultsRepository
    {
        private const string ResultsFolder = "Results";
        private const string ExportsFolder = "Exports";
        private const string SummaryCsv = "results.csv";

        private static string RootFor(string profileId)
        {
            string safe = string.IsNullOrEmpty(profileId) ? "default" : profileId;
            return Path.Combine(Application.persistentDataPath, ResultsFolder, safe);
        }

        public static string ExportsPath => Path.Combine(Application.persistentDataPath, ExportsFolder);

        // ------------------------------------------------------------------
        // Write
        // ------------------------------------------------------------------

        /// <summary>
        /// Raised after every Save - whether or not the file write succeeded, because a learner who
        /// just finished should see their review even on a machine with a full disk. The assessment
        /// review screen (AssessmentReviewPanel) listens to this, which is how it opens for COC1-COC4
        /// without any of the four finishing paths having to know it exists.
        /// </summary>
        public static event Action<AssessmentResult> Saved;

        /// <summary>Writes one result as JSON and appends its summary row to the
        /// per-profile CSV. Returns the JSON path, or null if the write failed.</summary>
        public static string Save(AssessmentResult result)
        {
            if (result == null) return null;

            if (string.IsNullOrEmpty(result.resultId))
                result.resultId = Guid.NewGuid().ToString("N").Substring(0, 12);

            // GUIDANCE: an assessment's mistakes, with the explanations that were withheld during the
            // run, are folded in before the file is written - so the saved record, the review screen
            // and the history screen all carry them. Does nothing for Story records.
            if (result.Mode == SessionMode.Assessment)
                CircuitCrafter.Guidance.MistakeJournal.MergeInto(result);

            string path = WriteFiles(result);

            try { Saved?.Invoke(result); }
            catch (Exception e) { Debug.LogError($"[ResultsRepository] A Saved listener threw: {e}"); }

            return path;
        }

        private static string WriteFiles(AssessmentResult result)
        {
            try
            {
                string dir = RootFor(result.profileId);
                Directory.CreateDirectory(dir);

                string jsonPath = Path.Combine(dir, result.resultId + ".json");
                File.WriteAllText(jsonPath, result.ToJson());

                AppendSummaryRow(dir, result);

                Debug.Log($"[ResultsRepository] Saved result '{result.resultId}' for {result.moduleId} to {jsonPath}");
                return jsonPath;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ResultsRepository] Couldn't save result: {e.Message}");
                return null;
            }
        }

        private static void AppendSummaryRow(string dir, AssessmentResult result)
        {
            string csvPath = Path.Combine(dir, SummaryCsv);
            bool needsHeader = !File.Exists(csvPath);

            var sb = new StringBuilder();
            if (needsHeader)
            {
                sb.AppendLine("result_id,finished_utc,module,mode,learner,section,completed," +
                              "duration_seconds,score,max_score,score_percent," +
                              "mistakes,graded_attempts,first_attempt_correct,first_attempt_percent," +
                              "stages_completed,stages_total,outcomes_competent,outcomes_total," +
                              // Appended, never inserted. A CSV that already has rows keeps its
                              // existing header, and new columns landing at the END means the old
                              // rows still line up when both are opened in one spreadsheet.
                              "knowledge_questions,knowledge_correct,knowledge_percent");
            }

            int competent = 0;
            foreach (var criterion in result.criteria)
            {
                if (criterion != null && criterion.Status == CompetencyStatus.Competent) competent++;
            }

            sb.AppendLine(string.Join(",",
                Csv(result.resultId),
                Csv(result.finishedUtc),
                Csv(result.moduleId),
                Csv(result.Mode.ToString()),
                Csv(result.learnerName),
                Csv(result.learnerSection),
                result.completed ? "1" : "0",
                Num(result.durationSeconds),
                result.score.ToString(CultureInfo.InvariantCulture),
                result.maxScore.ToString(CultureInfo.InvariantCulture),
                result.ScorePercent.ToString(CultureInfo.InvariantCulture),
                result.mistakeCount.ToString(CultureInfo.InvariantCulture),
                result.gradedAttempts.ToString(CultureInfo.InvariantCulture),
                result.firstAttemptCorrect.ToString(CultureInfo.InvariantCulture),
                result.FirstAttemptPercent.ToString(CultureInfo.InvariantCulture),
                result.StagesCompleted.ToString(CultureInfo.InvariantCulture),
                result.stages.Count.ToString(CultureInfo.InvariantCulture),
                competent.ToString(CultureInfo.InvariantCulture),
                result.criteria.Count.ToString(CultureInfo.InvariantCulture),
                result.knowledgeQuestions.ToString(CultureInfo.InvariantCulture),
                result.knowledgeCorrect.ToString(CultureInfo.InvariantCulture),
                result.KnowledgePercent.ToString(CultureInfo.InvariantCulture)));

            File.AppendAllText(csvPath, sb.ToString());
        }

        // ------------------------------------------------------------------
        // Read
        // ------------------------------------------------------------------

        /// <summary>Every stored result for a profile, newest first. Unreadable
        /// files are skipped with a warning rather than aborting the whole load -
        /// one corrupt record shouldn't hide the rest of a learner's history.</summary>
        public static List<AssessmentResult> LoadAll(string profileId)
        {
            var results = new List<AssessmentResult>();
            string dir = RootFor(profileId);

            if (!Directory.Exists(dir)) return results;

            foreach (string path in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var result = AssessmentResult.FromJson(File.ReadAllText(path));
                    if (result != null) results.Add(result);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ResultsRepository] Skipped unreadable result '{path}': {e.Message}");
                }
            }

            results.Sort((a, b) => string.CompareOrdinal(b.finishedUtc, a.finishedUtc));
            return results;
        }

        /// <summary>The most recent result for one module, or null. Useful for a
        /// "last attempt" line on a menu.</summary>
        public static AssessmentResult LoadLatest(string profileId, string moduleId)
        {
            foreach (var result in LoadAll(profileId))
            {
                if (result.moduleId == moduleId) return result;
            }
            return null;
        }

        public static void DeleteAll(string profileId)
        {
            try
            {
                string dir = RootFor(profileId);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ResultsRepository] Couldn't delete results for '{profileId}': {e.Message}");
            }
        }

        /// <summary>
        /// Deletes every stored result for ONE module, leaving every other module's history
        /// untouched - what the Competency screen's Clear History button runs. Deliberately
        /// narrower than DeleteAll: this is meant for cleaning up a module's log without wiping a
        /// learner's whole history, so a module id is required rather than optional.
        ///
        /// Leaves the ledger alone. Competency status and the module's high score are the
        /// official record; this only removes the raw run files behind them, so a learner's
        /// earned standing survives even if their history for that module is cleared out from
        /// under it. If a full undo - status AND history together - is ever wanted, that is
        /// what Reset Progress (SaveManager.ResetAllProgress) is for.
        /// </summary>
        public static void DeleteForModule(string profileId, string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return;

            string dir = RootFor(profileId);
            if (!Directory.Exists(dir)) return;

            int deleted = 0;
            foreach (string path in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var result = AssessmentResult.FromJson(File.ReadAllText(path));
                    if (result != null && result.moduleId == moduleId)
                    {
                        File.Delete(path);
                        deleted++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ResultsRepository] Couldn't check '{path}' while clearing '{moduleId}': {e.Message}");
                }
            }

            RemoveModuleRowsFromCsv(dir, moduleId);
            Debug.Log($"[ResultsRepository] Cleared {deleted} stored result(s) for '{moduleId}'.");
        }

        /// <summary>Rewrites the profile's summary CSV without one module's rows, so it stays
        /// consistent with what DeleteForModule just removed from disk rather than going on
        /// describing runs that no longer exist.</summary>
        private static void RemoveModuleRowsFromCsv(string dir, string moduleId)
        {
            string csvPath = Path.Combine(dir, SummaryCsv);
            if (!File.Exists(csvPath)) return;

            try
            {
                string[] lines = File.ReadAllLines(csvPath);
                if (lines.Length <= 1) return; // header only, or empty - nothing to remove

                var kept = new List<string> { lines[0] };

                for (int i = 1; i < lines.Length; i++)
                {
                    // "module" is the THIRD column (see AppendSummaryRow's header). Unlike
                    // learner/section further along, nothing before it is ever comma- or
                    // quote-escaped (result_id is a GUID fragment, finished_utc a plain ISO
                    // timestamp) - so a naive split lands index 2 on the right value without
                    // needing a full CSV parser here.
                    string[] cells = lines[i].Split(',');
                    if (cells.Length > 2 && cells[2] == moduleId) continue;
                    kept.Add(lines[i]);
                }

                File.WriteAllLines(csvPath, kept);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ResultsRepository] Couldn't rewrite '{csvPath}' after clearing '{moduleId}': {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Export
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes a per-outcome CSV - one row per learning outcome per run, which
        /// is the shape you want for analysis. Returns the path written, or null.
        ///
        /// The run-level summary is already in each profile's results.csv; this is
        /// the long-format companion, and the two together cover most of what a
        /// study needs without anyone writing code.
        /// </summary>
        public static string ExportOutcomesCsv(string profileId, string fileName = null)
        {
            try
            {
                Directory.CreateDirectory(ExportsPath);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string path = Path.Combine(ExportsPath,
                    string.IsNullOrEmpty(fileName) ? $"outcomes-{stamp}.csv" : fileName);

                var sb = new StringBuilder();
                sb.AppendLine("result_id,finished_utc,module,mode,learner,section," +
                              "outcome_code,outcome_title,status,evidence_satisfied,evidence_required");

                foreach (var result in LoadAll(profileId))
                {
                    foreach (var criterion in result.criteria)
                    {
                        if (criterion == null) continue;

                        sb.AppendLine(string.Join(",",
                            Csv(result.resultId),
                            Csv(result.finishedUtc),
                            Csv(result.moduleId),
                            Csv(result.Mode.ToString()),
                            Csv(result.learnerName),
                            Csv(result.learnerSection),
                            Csv(criterion.outcomeCode),
                            Csv(criterion.outcomeTitle),
                            Csv(criterion.Status.ToString()),
                            criterion.evidenceSatisfied.ToString(CultureInfo.InvariantCulture),
                            criterion.evidenceRequired.ToString(CultureInfo.InvariantCulture)));
                    }
                }

                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[ResultsRepository] Exported outcomes to {path}");
                return path;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ResultsRepository] Export failed: {e.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------

        /// <summary>RFC 4180 quoting. Learner names contain commas and apostrophes
        /// often enough that skipping this corrupts real files, and a corrupt CSV
        /// usually isn't noticed until someone tries to analyse it.</summary>
        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            string escaped = value.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{escaped}\"" : escaped;
        }

        /// <summary>InvariantCulture matters here: on a machine with a comma
        /// decimal separator, a plain ToString() writes "12,5" and silently
        /// splits the row into an extra column.</summary>
        private static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}