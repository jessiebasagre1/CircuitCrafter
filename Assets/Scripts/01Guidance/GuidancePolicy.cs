using UnityEngine;
using CircuitCrafter.Assessment;
using CircuitCrafter.Repair;

namespace CircuitCrafter.Guidance
{
    /// <summary>The three ways a learner can be playing, as the guidance layer sees them.</summary>
    public enum LearningMode
    {
        /// <summary>Story / campaign. Teach, guide, demonstrate, correct.</summary>
        Story = 0,

        /// <summary>TrainingMode.Practice. Not wired to any menu yet - kept so the rules for it
        /// exist the day it is.</summary>
        Practice = 1,

        /// <summary>A graded run. Evaluate and record; withhold help until it is over.</summary>
        Assessment = 2
    }

    /// <summary>
    /// Every kind of help the game can give. Each instructional system asks
    /// GuidancePolicy.Allows(kind) before it shows anything, so the rules for Story and
    /// Assessment live in ONE place (GuidanceSettings) instead of being re-decided in forty
    /// scripts.
    ///
    /// ADDED AT THE END ONLY. Unity stores enums by number; inserting a value in the middle
    /// would silently change the meaning of every ModeVisibility already saved in a scene.
    /// </summary>
    public enum GuidanceKind
    {
        /// <summary>Dialogs and cards that explain HOW to do a procedure.</summary>
        ProcedureTutorial = 0,

        /// <summary>How to move, look, interact, pick up, drag and drop.</summary>
        ControlsTutorial = 1,

        /// <summary>How the hotbar, AI Tutor, objectives panel and tools work.</summary>
        FeatureTutorial = 2,

        /// <summary>The Current Task text and per-step instruction lines.</summary>
        StepInstructions = 3,

        /// <summary>Hint buttons, hint panels and hint lines on stations.</summary>
        Hints = 4,

        /// <summary>Arrows and markers that point at the next place to go.</summary>
        Waypoints = 5,

        /// <summary>Glows, rings and pulses on the object or button to use next.</summary>
        ObjectHighlights = 6,

        /// <summary>Reference aids that hold the answer - a T568B colour chart, a part's
        /// "this goes in the DIMM slot" description.</summary>
        ReferenceGuides = 7,

        /// <summary>"What went wrong and how to fix it", shown right after a mistake.</summary>
        CorrectiveFeedback = 8,

        /// <summary>"Well done - here is why that step mattered."</summary>
        SuccessFeedback = 9,

        /// <summary>The AI Tutor chat.</summary>
        AiTutor = 10,

        /// <summary>Mid-run knowledge checks. QuizManager and QuizTrigger already stand down in
        /// an assessment by themselves; this is here so new code can ask the same question.</summary>
        Quizzes = 11,

        /// <summary>The job order's teaching notes (why the customer needs it, what you will
        /// practise). The scenario and required outcome are always shown - see JobOrderPanel.</summary>
        JobOrderCoaching = 12
    }

    /// <summary>
    /// "May I help the learner right now?" - asked by every tutorial, hint, highlight, waypoint and
    /// the AI Tutor before they show anything.
    ///
    /// NOTHING TO ATTACH. It is a static, like AssessmentMode and TrainingMode, and it reads them
    /// rather than keeping a flag of its own - so it can never disagree with the menus about which
    /// mode this run is in:
    ///
    ///   COC4AssessmentRun.IsActive  -> Assessment   (spans six scenes, so checked first)
    ///   AssessmentMode.IsActive     -> Assessment   (COC1-3 assessment, and COC4's results scene)
    ///   TrainingMode.Practice       -> Practice
    ///   anything else               -> Story
    ///
    /// WHY IsActive AND NOT IsAssessing(moduleId). A COC4 assessment loads COC1-style repair sites.
    /// A check written as IsAssessing("COC1") in one of those scenes answers "no", and the COC1
    /// help switches back on in the middle of the COC4 exam. Any assessment, for any module, means
    /// no help.
    ///
    /// WHAT EACH MODE ALLOWS is set on the GuidanceSettings asset (Resources/GuidanceSettings).
    /// With no asset, the built-in defaults apply: Story allows everything, Assessment allows
    /// nothing.
    /// </summary>
    public static class GuidancePolicy
    {
        private static GuidanceSettings settings;

        /// <summary>The rules in force. Loaded from Resources/GuidanceSettings, or built-in
        /// defaults when there is no such asset. Never null.</summary>
        public static GuidanceSettings Settings
        {
            get
            {
                if (settings == null) settings = GuidanceSettings.LoadOrDefault();
                return settings;
            }
        }

        /// <summary>Which mode this run is in. Read fresh every call - it is three bool reads.</summary>
        public static LearningMode Mode
        {
            get
            {
#if UNITY_EDITOR
                // Editor-only preview, so a scene can be checked in Assessment without going
                // through the menu. Compiled out of builds, so it can never leak into a class.
                if (Settings.editorPreview == GuidanceSettings.EditorPreview.ForceStory) return LearningMode.Story;
                if (Settings.editorPreview == GuidanceSettings.EditorPreview.ForceAssessment) return LearningMode.Assessment;
#endif
                if (COC4AssessmentRun.IsActive) return LearningMode.Assessment;
                if (AssessmentMode.IsActive) return LearningMode.Assessment;

                switch (TrainingMode.Current)
                {
                    case SessionMode.Assessment: return LearningMode.Assessment;
                    case SessionMode.Practice: return LearningMode.Practice;
                    default: return LearningMode.Story;
                }
            }
        }

        public static bool IsAssessment => Mode == LearningMode.Assessment;
        public static bool IsStory => Mode == LearningMode.Story;

        /// <summary>
        /// The one question every help system asks. True when this kind of help may be shown in
        /// the current mode.
        ///
        /// The AI Tutor has one extra rule: once an assessment has FINISHED and its review screen
        /// is open, the tutor may come back (if GuidanceSettings allows) so the learner can ask
        /// about their mistakes. Nothing else unlocks during review - the scene is still the
        /// assessment scene, and its tutorials would only get in the way of reading the results.
        /// </summary>
        public static bool Allows(GuidanceKind kind)
        {
            LearningMode mode = Mode;

            if (kind == GuidanceKind.AiTutor && mode == LearningMode.Assessment &&
                ReviewUnlocked && Settings.unlockAiTutorDuringReview)
                return true;

            return Settings.RulesFor(mode).Allows(kind);
        }

        /// <summary>Convenience for scripts that only care "is the learner being graded right now".</summary>
        public static bool Withholds(GuidanceKind kind) => !Allows(kind);

        /// <summary>
        /// Whether the COC1/COC2 assessment managers may point the waypoint at the NEXT STATION.
        /// That arrow is part of those modules' existing assessment design - it names a place,
        /// never a part or an action - so it is its own switch rather than being lumped in with
        /// story waypoints. Turn it off in GuidanceSettings for a stricter exam.
        /// </summary>
        public static bool AllowsStationMarkersInAssessment => Settings.assessmentStationMarkers;

        /// <summary>What to say instead of an explanation when an action is refused in an
        /// assessment. Neutral on purpose: it confirms the action did not work, never why.</summary>
        public static string NeutralRejection =>
            string.IsNullOrEmpty(Settings.neutralRejectionText) ? "Action not accepted." : Settings.neutralRejectionText;

        // ------------------------------------------------------------------
        // Review state
        // ------------------------------------------------------------------

        /// <summary>True once a finished assessment's review screen has been opened. Reset when
        /// the next run begins (MistakeJournal.BeginRun).</summary>
        public static bool ReviewUnlocked { get; private set; }

        /// <summary>Called by AssessmentReviewPanel when it opens a finished run.</summary>
        public static void NotifyRunReviewed() { ReviewUnlocked = true; }

        /// <summary>Called by MistakeJournal.BeginRun, i.e. at the start of every graded run.</summary>
        public static void ResetReview() { ReviewUnlocked = false; }

        /// <summary>For logs and the audit window.</summary>
        public static string Describe()
        {
            return $"Mode={Mode} (COC4Run={COC4AssessmentRun.IsActive}, AssessmentMode={AssessmentMode.IsActive}" +
                   $"{(AssessmentMode.IsActive ? ":" + AssessmentMode.ModeId : "")}, TrainingMode={TrainingMode.Current}), " +
                   $"settings='{(Settings != null ? Settings.name : "none")}'";
        }

        /// <summary>Drops the cached settings so an edited asset is re-read. Editor use.</summary>
        public static void ReloadSettings() { settings = null; }
    }
}
