using System;
using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// What each mode is allowed to show. ONE asset for the whole game.
    ///
    /// CREATE IT WITH: CircuitCrafter > Guidance > Create Guidance Settings. It must be called
    /// "GuidanceSettings" and live in a folder named Resources, because GuidancePolicy loads it with
    /// Resources.Load - a static cannot be given an Inspector reference. Without the asset the
    /// defaults below apply, which already match the Story/Assessment rules.
    ///
    /// WHY A TABLE AND NOT HARD-CODED RULES. "May the AI Tutor answer during practice?" and "may the
    /// station arrow stay on in the COC1 assessment?" are teaching decisions, not programming ones.
    /// An instructor can change them here without touching code, and every system in the game
    /// follows the change at once.
    /// </summary>
    [CreateAssetMenu(fileName = "GuidanceSettings", menuName = "CircuitCrafter/Guidance/Guidance Settings")]
    public class GuidanceSettings : ScriptableObject
    {
        public const string ResourcesName = "GuidanceSettings";

        [Serializable]
        public class ModeRules
        {
            [Tooltip("Dialogs and tutorial cards that explain how to do a procedure. In Assessment this also silences every ordinary DialogManager popup (the assessment's own messages still play).")]
            public bool procedureTutorials = true;

            [Tooltip("The controls walkthrough - move, look, interact, pick up, drag and drop.")]
            public bool controlsTutorials = true;

            [Tooltip("Cards explaining the hotbar, AI Tutor, objectives panel and tools.")]
            public bool featureTutorials = true;

            [Tooltip("The Current Task text and each step's instruction line.")]
            public bool stepInstructions = true;

            [Tooltip("Hint buttons, hint panels, and the hint lines on stations.")]
            public bool hints = true;

            [Tooltip("Arrows and markers pointing at the next place to go.")]
            public bool waypoints = true;

            [Tooltip("Glows, rings and pulses on the correct object or button.")]
            public bool objectHighlights = true;

            [Tooltip("Aids that contain the answer: the T568 colour chart, part descriptions.")]
            public bool referenceGuides = true;

            [Tooltip("'What went wrong and how to fix it', shown right after a mistake.")]
            public bool correctiveFeedback = true;

            [Tooltip("'Well done - here is why that mattered', shown after a step.")]
            public bool successFeedback = true;

            [Tooltip("The AI Tutor chat.")]
            public bool aiTutor = true;

            [Tooltip("Mid-run knowledge checks.")]
            public bool quizzes = true;

            [Tooltip("The teaching notes on a job order. The scenario and required outcome are always shown.")]
            public bool jobOrderCoaching = true;

            public bool Allows(GuidanceKind kind)
            {
                switch (kind)
                {
                    case GuidanceKind.ProcedureTutorial: return procedureTutorials;
                    case GuidanceKind.ControlsTutorial: return controlsTutorials;
                    case GuidanceKind.FeatureTutorial: return featureTutorials;
                    case GuidanceKind.StepInstructions: return stepInstructions;
                    case GuidanceKind.Hints: return hints;
                    case GuidanceKind.Waypoints: return waypoints;
                    case GuidanceKind.ObjectHighlights: return objectHighlights;
                    case GuidanceKind.ReferenceGuides: return referenceGuides;
                    case GuidanceKind.CorrectiveFeedback: return correctiveFeedback;
                    case GuidanceKind.SuccessFeedback: return successFeedback;
                    case GuidanceKind.AiTutor: return aiTutor;
                    case GuidanceKind.Quizzes: return quizzes;
                    case GuidanceKind.JobOrderCoaching: return jobOrderCoaching;
                    default: return false;   // an unknown kind is help nobody approved
                }
            }

            public static ModeRules AllowEverything() => new ModeRules();

            public static ModeRules AllowNothing() => new ModeRules
            {
                procedureTutorials = false, controlsTutorials = false, featureTutorials = false,
                stepInstructions = false, hints = false, waypoints = false, objectHighlights = false,
                referenceGuides = false, correctiveFeedback = false, successFeedback = false,
                aiTutor = false, quizzes = false, jobOrderCoaching = false
            };

            /// <summary>Unassisted but not graded: help on request, no hand-holding.</summary>
            public static ModeRules PracticeDefaults() => new ModeRules
            {
                procedureTutorials = false, controlsTutorials = false, featureTutorials = false,
                stepInstructions = false, hints = true, waypoints = false, objectHighlights = false,
                referenceGuides = true, correctiveFeedback = true, successFeedback = true,
                aiTutor = true, quizzes = false, jobOrderCoaching = false
            };
        }

        [Header("Story Mode - teach, guide, demonstrate, correct")]
        public ModeRules story = ModeRules.AllowEverything();

        [Header("Practice (not on any menu yet)")]
        public ModeRules practice = ModeRules.PracticeDefaults();

        [Header("Assessment Mode - evaluate and record, no help")]
        [Tooltip("Leave everything OFF. Each tick here is help given during a graded run.")]
        public ModeRules assessment = ModeRules.AllowNothing();

        [Header("Assessment details")]
        [Tooltip("Let the COC1 and COC2 assessment managers point the waypoint at the NEXT STATION after each stage. " +
                 "This is part of their existing design and names a place, never a part or an action. " +
                 "Untick for a stricter exam where the learner finds each station from the job order.")]
        public bool assessmentStationMarkers = true;

        [Tooltip("Shown (only if Show Neutral Rejection Toast is on) when an action is refused during an assessment. " +
                 "Say THAT it was refused, never WHY.")]
        public string neutralRejectionText = "Action not accepted.";

        [Tooltip("Show the neutral text above as a small toast when an assessment action is refused. Off by default - " +
                 "the blocked sound already tells the learner, and the full explanation waits for the review screen.")]
        public bool showNeutralRejectionToast = false;

        [Tooltip("Once an assessment has finished and its review screen is open, let the AI Tutor answer again so " +
                 "the learner can ask about their mistakes.")]
        public bool unlockAiTutorDuringReview = true;

        [Header("Explanations")]
        [Tooltip("The library of mistake explanations, correct procedures and recommended Story Mode activities. " +
                 "Used by Story Mode's error feedback AND by the assessment review screen. " +
                 "CircuitCrafter > Guidance > Create Starter Review Library makes one with rules for the mistakes the game already reports.")]
        public ReviewLibrary reviewLibrary;

        [Header("Story Mode pacing")]
        [Tooltip("Minimum seconds between two tutorial cards, so a learner is never hit by a stack of pop-ups.")]
        [Min(0f)] public float minSecondsBetweenCards = 2f;

        [Tooltip("Seconds a corrective feedback toast stays up.")]
        [Min(1f)] public float feedbackSeconds = 5f;

        [Tooltip("The same feedback message is not repeated within this many seconds - a learner dragging a " +
                 "locked RAM stick five times gets told once.")]
        [Min(0f)] public float repeatFeedbackAfterSeconds = 8f;

        // Compiled into builds on purpose, but only READ in the Editor (see GuidancePolicy.Mode).
        // Wrapping a serialized field in #if UNITY_EDITOR gives the asset a different layout in the
        // Editor and in a build, which Unity reports as a serialization error when the player loads it.
        public enum EditorPreview { Auto, ForceStory, ForceAssessment }

        [Header("Editor only")]
        [Tooltip("Preview a scene as Story or Assessment without going through the menu. Ignored in builds.")]
        public EditorPreview editorPreview = EditorPreview.Auto;

        public ModeRules RulesFor(LearningMode mode)
        {
            switch (mode)
            {
                case LearningMode.Assessment: return assessment ?? ModeRules.AllowNothing();
                case LearningMode.Practice: return practice ?? ModeRules.PracticeDefaults();
                default: return story ?? ModeRules.AllowEverything();
            }
        }

        /// <summary>Resources/GuidanceSettings, or an in-memory copy with the defaults.</summary>
        public static GuidanceSettings LoadOrDefault()
        {
            GuidanceSettings loaded = Resources.Load<GuidanceSettings>(ResourcesName);
            if (loaded != null) return loaded;

            GuidanceSettings fallback = CreateInstance<GuidanceSettings>();
            fallback.name = "GuidanceSettings (built-in defaults)";
            fallback.hideFlags = HideFlags.DontSave;
            return fallback;
        }
    }
}
