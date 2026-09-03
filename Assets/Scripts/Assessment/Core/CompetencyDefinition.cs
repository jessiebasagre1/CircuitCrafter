using System;
using System.Collections.Generic;
using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// The bridge between the task ids your gameplay already reports and the
    /// language a competency report has to be written in.
    ///
    /// Create ONE asset per unit of competency:
    ///   Right-click in the Project window -> Create -> CircuitCrafter -> Competency Definition
    ///
    /// Then list that unit's learning outcomes, and under each one the
    /// TaskManager task ids that count as evidence for it. Those ids are the
    /// same strings you already use - "assemble_pc", "crimp_cable",
    /// SystemConfigTaskIds.InstallDrivers and so on - so nothing in gameplay
    /// has to change. The asset is pure mapping.
    ///
    /// WHY A ScriptableObject AND NOT A CONST TABLE IN CODE
    /// Same reasoning as TicketData and ChecklistItemData: the outcome titles
    /// and criteria come from a published document that gets revised, and you
    /// want to fix a wording without a recompile. It also means an instructor
    /// reviewing the mapping can read it in the Inspector instead of in C#.
    ///
    /// IMPORTANT: Inspector fields can't reference C# consts, so when filling in
    /// evidenceTaskIds you must type the exact string - check it against
    /// SystemConfigTaskIds and PCDisassemblySetup rather than retyping from
    /// memory. A typo here doesn't throw; it just silently never satisfies the
    /// outcome, which is a slow bug to find.
    /// </summary>
    [CreateAssetMenu(fileName = "Competency_COC1", menuName = "CircuitCrafter/Competency Definition")]
    public class CompetencyDefinition : ScriptableObject
    {
        [Header("Unit")]
        [Tooltip("Must match the module id used everywhere else - \"COC1\", \"COC2\", \"COC3\", \"COC4\". This is the key AssessmentRecorder and CompetencyLedger look it up by.")]
        public string moduleId = "COC1";

        [Tooltip("The unit code from the Training Regulations, if you want it printed on reports. Optional.")]
        public string unitCode = "";

        [Tooltip("The unit title, e.g. \"Install and configure computer systems\".")]
        public string unitTitle = "Install and configure computer systems";

        [Header("Learning Outcomes")]
        [Tooltip("In order. Each one lists the task ids that evidence it.")]
        public List<LearningOutcome> outcomes = new List<LearningOutcome>();

        [Serializable]
        public class LearningOutcome
        {
            [Tooltip("Short code used as the ledger key, e.g. \"LO1\". Must be unique within this asset and must not change once learners have records against it.")]
            public string code = "LO1";

            [Tooltip("e.g. \"Assemble computer hardware\".")]
            public string title = "";

            [TextArea(2, 5)]
            [Tooltip("Optional - the assessment criteria, shown on the competency profile screen so the learner knows what is actually being judged.")]
            public string criteria = "";

            [Tooltip("TaskManager task ids that count as evidence for this outcome. Type them exactly as they appear in code.")]
            public List<string> evidenceTaskIds = new List<string>();

            [Tooltip("How many of the evidence tasks must be complete. Leave at 0 to require ALL of them, which is the usual case. Set it lower when several tasks are alternative routes to the same skill.")]
            [Min(0)]
            public int requiredEvidenceCount = 0;
        }

        /// <summary>Finds an outcome by its code. Returns null if there isn't one -
        /// callers should treat that as "this outcome was removed from the asset
        /// after a learner earned a record against it", not as an error.</summary>
        public LearningOutcome Find(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            foreach (var outcome in outcomes)
            {
                if (outcome != null && outcome.code == code) return outcome;
            }
            return null;
        }

        /// <summary>
        /// Counts how much evidence exists for one outcome right now.
        /// isTaskComplete is passed in rather than read from TaskManager directly
        /// so this stays testable, and so a report can be rebuilt later from a
        /// stored result instead of from live scene state.
        /// </summary>
        public bool Evaluate(LearningOutcome outcome, Func<string, bool> isTaskComplete,
                             out int satisfied, out int required)
        {
            satisfied = 0;
            required = 0;

            if (outcome == null || outcome.evidenceTaskIds == null) return false;

            foreach (string taskId in outcome.evidenceTaskIds)
            {
                if (string.IsNullOrEmpty(taskId)) continue;
                required++;
                if (isTaskComplete != null && isTaskComplete(taskId)) satisfied++;
            }

            if (required == 0) return false;

            // requiredEvidenceCount 0 means "all of them" - the common case, and a
            // safer default than 1 because an asset filled in hastily then demands
            // everything rather than accepting a single task as proof of an outcome.
            int needed = outcome.requiredEvidenceCount > 0
                ? Mathf.Min(outcome.requiredEvidenceCount, required)
                : required;

            required = needed;
            return satisfied >= needed;
        }

        /// <summary>Convenience overload that reads live TaskManager state.</summary>
        public bool Evaluate(LearningOutcome outcome, out int satisfied, out int required)
        {
            return Evaluate(outcome,
                            id => TaskManager.Instance != null && TaskManager.Instance.IsTaskComplete(id),
                            out satisfied, out required);
        }

#if UNITY_EDITOR
        /// <summary>Editor-only sanity check. Duplicate or blank outcome codes are
        /// the one authoring mistake here that corrupts ledger records rather than
        /// just failing to match, so it's worth shouting about while you're
        /// filling the asset in rather than after a class has played it.</summary>
        private void OnValidate()
        {
            var seen = new HashSet<string>();
            foreach (var outcome in outcomes)
            {
                if (outcome == null) continue;

                if (string.IsNullOrWhiteSpace(outcome.code))
                {
                    Debug.LogWarning($"[CompetencyDefinition] '{name}' has an outcome with a blank code - it can never be recorded.", this);
                    continue;
                }

                if (!seen.Add(outcome.code))
                    Debug.LogWarning($"[CompetencyDefinition] '{name}' has two outcomes with the code '{outcome.code}'. Ledger records for them will collide.", this);
            }
        }
#endif
    }
}
