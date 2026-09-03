using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// Which mode the current run is in, and for which module.
    ///
    /// WHY THIS EXISTS ALONGSIDE AssessmentMode RATHER THAN REPLACING IT
    /// AssessmentMode is a two-state flag: assessing, or not. That leaves the
    /// middle tier - practice, where hints are allowed but the run still counts
    /// as evidence - with nowhere to live. TrainingMode adds it WITHOUT touching
    /// AssessmentMode, so every existing script that reads
    /// AssessmentMode.IsAssessing("COC1") keeps working unchanged.
    ///
    /// The trick is the fallback in Current: if nothing has called Begin() this
    /// session (i.e. the player came through the existing AssessmentMenuController
    /// or MainMenuController, which know nothing about this class), Current is
    /// DERIVED from AssessmentMode instead of guessed. So:
    ///
    ///   Old menu -> AssessmentMode.Begin("COC1")  -> Current == Assessment
    ///   Old menu -> plain campaign SelectMode()   -> Current == Guided
    ///   New menu -> TrainingMode.Begin(id, mode)  -> Current == whatever was asked for
    ///
    /// All three are correct with no edits to the menus. Wire the menus to
    /// Begin() whenever you're ready for the practice tier; until then this
    /// class is a read-only lens over what you already have.
    ///
    /// Same lifetime reasoning as AssessmentMode and RepairTicketMode: a plain
    /// static survives SceneManager.LoadScene, dies with the application, and is
    /// never written to the save file. Which mode you're playing in is a property
    /// of this session's navigation, not of saved progress.
    /// </summary>
    public static class TrainingMode
    {
        private static bool hasExplicitMode;
        private static SessionMode explicitMode;
        private static string explicitModuleId;

        /// <summary>The mode this run is in. Falls back to reading AssessmentMode
        /// when nothing has called Begin() - see the class comment.</summary>
        public static SessionMode Current
        {
            get
            {
                if (hasExplicitMode) return explicitMode;
                return AssessmentMode.IsActive ? SessionMode.Assessment : SessionMode.Guided;
            }
        }

        /// <summary>Which module is being played, e.g. "COC1".</summary>
        public static string ModuleId
        {
            get
            {
                if (hasExplicitMode && !string.IsNullOrEmpty(explicitModuleId)) return explicitModuleId;
                if (!string.IsNullOrEmpty(AssessmentMode.ModeId)) return AssessmentMode.ModeId;
                return SaveManager.Instance != null ? SaveManager.Instance.CurrentGameMode : null;
            }
        }

        /// <summary>True only in Assessment. Use this to gate timers, hide hints
        /// and switch off tutorials in NEW code; existing code can keep using
        /// AssessmentMode.IsAssessing and stays correct either way.</summary>
        public static bool IsGraded => Current == SessionMode.Assessment;

        /// <summary>True in Guided and Practice. Practice is the tier where a
        /// learner may ask for a hint without it being a failure.</summary>
        public static bool HintsAllowed => Current != SessionMode.Assessment;

        /// <summary>True in Practice and Assessment - the two tiers where the
        /// learner is working without being led. A Guided run still produces a
        /// result and still counts as Practised evidence; it just can't be the
        /// basis for a Competent judgement.</summary>
        public static bool IsUnassisted => Current != SessionMode.Guided;

        /// <summary>Call this from a menu button INSTEAD OF AssessmentMode.Begin.
        /// It keeps AssessmentMode in sync automatically, so scripts that read
        /// the old flag behave correctly without being touched.</summary>
        public static void Begin(string moduleId, SessionMode mode)
        {
            hasExplicitMode = true;
            explicitMode = mode;
            explicitModuleId = moduleId;

            // Keep the legacy flag honest. Everything already in the project reads
            // this, and a Practice run must NOT look like an assessment to those
            // scripts - it should behave like a campaign run that happens not to
            // show tutorials.
            if (mode == SessionMode.Assessment) AssessmentMode.Begin(moduleId);
            else AssessmentMode.End();

            Debug.Log($"[TrainingMode] {moduleId} started in {mode} mode.");
        }

        /// <summary>Call when returning to the main menu. Safe to call repeatedly.
        /// MenuNavigator already calls AssessmentMode.End() and
        /// TicketSessionState.Clear() there - add this line beside them.</summary>
        public static void End()
        {
            if (hasExplicitMode)
                Debug.Log($"[TrainingMode] {explicitModuleId} ended ({explicitMode}).");

            hasExplicitMode = false;
            explicitModuleId = null;
            explicitMode = SessionMode.Guided;
            AssessmentMode.End();
        }
    }
}
