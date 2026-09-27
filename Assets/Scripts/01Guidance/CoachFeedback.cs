using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// Short, on-the-spot feedback: what went wrong and how to fix it, or why a step you just did
    /// mattered. The "Check the result -> Give feedback" end of the learning loop.
    ///
    /// NOTHING TO ATTACH - call it from anywhere. It shows through the scene's FeedbackToast (built
    /// by CircuitCrafter > Guidance > Build Guidance UI). A scene without one simply logs.
    ///
    ///   CoachFeedback.Mistake("The RAM clips are still closed.", "Open both clips, then lift.");
    ///   CoachFeedback.Praise("Side panel off. Always ground yourself before touching parts.");
    ///   CoachFeedback.Info("Job order updated.");
    ///
    /// MODE RULES (from GuidancePolicy)
    ///   Mistake - Story only. In an assessment it shows NOTHING by default (or a neutral "Action not
    ///             accepted." if GuidanceSettings says so). It never records - the assessment
    ///             managers already do, with the explanation kept for the review screen.
    ///   Praise  - Story only.
    ///   Info    - Always. Use it only for facts that teach nothing about the task.
    ///
    /// Leave the fix out and it is looked up in the ReviewLibrary by the mistake text, so the
    /// existing COC1/COC2 mistake sentences get a proper explanation without being rewritten.
    /// </summary>
    public static class CoachFeedback
    {
        private static string lastMessage;
        private static float lastShownAt = -999f;

        /// <summary>Explains a mistake in Story Mode. Silent (or neutral) in an assessment.</summary>
        public static void Mistake(string whatHappened, string howToFix = null, string category = null,
                                   string moduleId = null, Object context = null)
        {
            if (string.IsNullOrEmpty(whatHappened)) return;

            if (!GuidancePolicy.Allows(GuidanceKind.CorrectiveFeedback))
            {
                if (GuidancePolicy.IsAssessment && GuidancePolicy.Settings.showNeutralRejectionToast)
                    Show(FeedbackToast.Tone.Neutral, "", GuidancePolicy.NeutralRejection, null);
                return;
            }

            string headline = whatHappened;

            ReviewLibrary library = ReviewLibrary.Active;
            if (library != null)
            {
                ReviewLibrary.MistakeRule rule = library.Match(moduleId, category, whatHappened);
                if (rule != null)
                {
                    if (!string.IsNullOrEmpty(rule.whatWentWrong)) headline = rule.whatWentWrong;
                    if (string.IsNullOrEmpty(howToFix)) howToFix = rule.howToFix;
                }
            }

            // Not while a dialog is up: a part with its own blocked-warning dialog has just explained
            // itself, and a toast repeating it underneath is noise.
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            Show(FeedbackToast.Tone.Mistake, "Not quite", headline, howToFix);
        }

        /// <summary>Positive feedback after a step. Story only.</summary>
        public static void Praise(string text, string title = "Well done")
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!GuidancePolicy.Allows(GuidanceKind.SuccessFeedback)) return;
            Show(FeedbackToast.Tone.Success, title, text, null);
        }

        /// <summary>A neutral note, shown in every mode. Must not teach the task.</summary>
        public static void Info(string text, string title = "")
        {
            if (string.IsNullOrEmpty(text)) return;
            Show(FeedbackToast.Tone.Neutral, title, text, null);
        }

        private static void Show(FeedbackToast.Tone tone, string title, string body, string fix)
        {
            GuidanceSettings settings = GuidancePolicy.Settings;
            string key = tone + "|" + body;

            if (key == lastMessage && Time.unscaledTime - lastShownAt < settings.repeatFeedbackAfterSeconds)
                return;

            lastMessage = key;
            lastShownAt = Time.unscaledTime;

            if (FeedbackToast.Instance != null)
            {
                FeedbackToast.Instance.Show(tone, title, body, fix, settings.feedbackSeconds);
            }
            else
            {
                Debug.Log($"[CoachFeedback] ({tone}) {title}: {body}" +
                          (string.IsNullOrEmpty(fix) ? "" : $"  |  Fix: {fix}") +
                          "  - no FeedbackToast in this scene, so it was only logged.");
            }
        }
    }
}
