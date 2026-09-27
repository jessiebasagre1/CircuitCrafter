using CSSSimulation.Networking;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// What the AI Tutor is told about where the learner is, built from the guidance layer.
    /// AiTutorChat asks this first and falls back to its own TaskWaypointArrow reading when it
    /// returns null, so a scene with no StoryGuide and no job order behaves exactly as before.
    ///
    ///   After an assessment, on the review screen - the run's mistakes and correct procedures,
    ///                                                so the tutor can go through them.
    ///   Story with a StoryGuide                    - the job order, the current task, the task list.
    ///   Story with only a job order                - the job order, plus the arrow's step list.
    ///   During an assessment                       - never asked: the tutor is locked.
    /// </summary>
    public static class GuidanceTutorContext
    {
        public static string Describe()
        {
            if (GuidancePolicy.IsAssessment)
            {
                // Only reachable once the review has unlocked the tutor.
                return GuidancePolicy.ReviewUnlocked ? AssessmentReviewPanel.LastReviewForTutor : null;
            }

            StoryGuide guide = StoryGuide.Instance;
            if (guide != null && guide.steps != null && guide.steps.Count > 0)
                return guide.DescribeForTutor();

            JobOrderData job = JobOrderPanel.Instance != null ? JobOrderPanel.Instance.Data
                             : guide != null ? guide.jobOrder : null;
            if (job == null) return null;

            string text = job.DescribeForTutor();

            TaskWaypointArrow arrow = TaskWaypointArrow.Instance;
            if (arrow != null && arrow.ActiveStep != null)
                text += "\nThe trainee's current task is: \"" + arrow.ActiveStep.label + "\".";

            return text;
        }
    }
}
