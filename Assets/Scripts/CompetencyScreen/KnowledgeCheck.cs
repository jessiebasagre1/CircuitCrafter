using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// Collects the quiz answers given during the current run, so a finished run carries
    /// UNDERPINNING KNOWLEDGE evidence alongside its performance evidence.
    ///
    /// THERE IS NOTHING TO ATTACH. It is a static tally, reset at the start of every run and read
    /// once at the end by CompetencyLedger.
    ///
    /// WHY KNOWLEDGE IS KEPT SEPARATE FROM THE SCORE
    /// Quiz points used to be added straight into ScoreManager, which is the same score the ledger
    /// checks against its pass threshold. That quietly averaged two things TESDA never averages: a
    /// weak performance could be pushed over the line by good quiz answers, and a strong one dragged
    /// under by bad ones. The Evidence Plan for a unit of competency lists "Demonstration with oral
    /// questioning" and "Written test" as SEPARATE requirements, and an assessor has to see both met
    /// - not one number that could be either.
    ///
    /// So the tally lives here, the performance score stays what it was, and the ledger asks for
    /// both before it will award Competent.
    ///
    /// PER MODULE, NOT PER OUTCOME
    /// TESDA issues one written test per unit of competency, not one per learning outcome. Splitting
    /// ten questions across five LOs would mean inventing a mapping the Training Regulations do not
    /// have, and every question would then carry a fifth of an outcome's fate. One knowledge result
    /// per module is both simpler and closer to the real instrument.
    ///
    /// FIRST ANSWERS ONLY
    /// A quiz here is answered once per question - QuizView locks the toggles as soon as a choice is
    /// made - so every answer counted is a first answer. That is the figure worth having; a retry
    /// loop would measure persistence, which the run's score already does.
    /// </summary>
    public static class KnowledgeCheck
    {
        private static string moduleId = "";
        private static int questions;
        private static int correct;
        private static int quizzes;

        /// <summary>Questions answered in this run so far.</summary>
        public static int Questions => questions;

        /// <summary>Of those, how many were right.</summary>
        public static int Correct => correct;

        /// <summary>How many separate quizzes contributed.</summary>
        public static int Quizzes => quizzes;

        /// <summary>True once at least one question has been answered this run.</summary>
        public static bool Attempted => questions > 0;

        /// <summary>Percentage correct, or -1 when nothing was answered - so a report can print
        /// "not taken" rather than a misleading 0%.</summary>
        public static int Percent
        {
            get
            {
                if (questions <= 0) return -1;
                return Mathf.Clamp(Mathf.RoundToInt(100f * correct / questions), 0, 100);
            }
        }

        /// <summary>
        /// Clears the tally for a new run. Called from Begin on the recorder and the bridge - and
        /// from a RuntimeInitializeOnLoadMethod below, because a static survives entering Play mode
        /// in the Editor and last session's answers would otherwise still be counted.
        /// </summary>
        public static void Begin(string module)
        {
            moduleId = module ?? "";
            questions = 0;
            correct = 0;
            quizzes = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnPlay() { Begin(""); }

        /// <summary>Adds one finished quiz. Called by QuizManager.</summary>
        public static void Report(string quizTaskId, int totalQuestions, int correctAnswers)
        {
            if (totalQuestions <= 0) return;

            questions += totalQuestions;
            correct += Mathf.Clamp(correctAnswers, 0, totalQuestions);
            quizzes++;

            Debug.Log($"[KnowledgeCheck] Recorded quiz '{quizTaskId}': {correctAnswers}/{totalQuestions}. " +
                      $"Run total so far: {correct}/{questions} ({Percent}%)" +
                      (string.IsNullOrEmpty(moduleId) ? "" : $" for {moduleId}") + ".");
        }

        /// <summary>
        /// Copies the tally onto a finished run.
        ///
        /// Called from CompetencyLedger.RecordResult, which runs BEFORE ResultsRepository.Save in
        /// both the recorder and the legacy bridge - so the JSON evidence file and the CSV row both
        /// carry these numbers without either of those scripts needing to change.
        ///
        /// Skips a result that already carries knowledge data, so calling it twice is harmless.
        /// </summary>
        public static void FillInto(AssessmentResult result)
        {
            if (result == null) return;
            if (result.knowledgeQuestions > 0) return;   // already filled

            result.knowledgeQuestions = questions;
            result.knowledgeCorrect = correct;
            result.knowledgeQuizzes = quizzes;
        }
    }
}
