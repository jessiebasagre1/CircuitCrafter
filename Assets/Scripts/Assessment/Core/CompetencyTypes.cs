namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// The three ways a module can be played. Replaces the current two-state
    /// campaign-vs-assessment split with an explicit middle tier.
    ///
    ///   Guided     - tutorials, waypoints and dialogs on. What the campaign is today.
    ///   Practice   - no tutorials, hints available on request, untimed, not graded.
    ///   Assessment - timed, no help, recorded. The only mode that can award Competent.
    ///
    /// See TrainingMode for the runtime flag. Nothing breaks if a scene never
    /// sets one: TrainingMode falls back to reading the existing AssessmentMode
    /// static, so a build that still uses AssessmentMenuController behaves
    /// exactly as it does today.
    /// </summary>
    public enum SessionMode
    {
        Guided = 0,
        Practice = 1,
        Assessment = 2
    }

    /// <summary>
    /// How far a learner has got with one learning outcome. Deliberately three
    /// states rather than a percentage: this is the vocabulary a competency
    /// report is written in, and a number invites students to grind points
    /// instead of demonstrating a skill.
    ///
    /// Promotion is one-way (see CompetencyLedger) - a learner who has been
    /// judged Competent never drops back because of a later bad run. Getting
    /// worse on a retry is normal and shouldn't erase evidence that already
    /// exists.
    /// </summary>
    public enum CompetencyStatus
    {
        NotAttempted = 0,
        Practised = 1,
        Competent = 2
    }
}
