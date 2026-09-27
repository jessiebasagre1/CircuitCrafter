using System;
using System.Collections.Generic;
using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// What each mistake MEANS, what the correct procedure is, and which Story Mode activity
    /// teaches it. One asset for the whole game.
    ///
    /// CREATE IT WITH: CircuitCrafter > Guidance > Create Starter Review Library. That makes an asset
    /// already holding rules for the mistakes COC1-COC4 report today (removal order, RAM clips,
    /// partitions, Rufus, drivers, cable pinouts, punch-down, IP settings, ping, diagnosis, repair
    /// verification). Assign it to GuidanceSettings > Review Library.
    ///
    /// USED IN TWO PLACES
    ///   Story Mode   - a mistake's toast shows the rule's What Went Wrong and How To Fix.
    ///   Review       - after an assessment, each recorded mistake shows its Correct Procedure,
    ///                  Area To Improve and the recommended activity.
    ///
    /// HOW A RULE MATCHES
    /// A mistake is a category ("sequence", "cable", "configuration"...) and a sentence, e.g.
    /// "Tried to remove 'RAM_1' - 'RAM_1' is still closed". A rule matches when its Module and
    /// Category are blank or equal, AND any one of its Keywords appears in the sentence (not case
    /// sensitive). A rule with no keywords matches on module/category alone - use that for a
    /// catch-all per category. The FIRST matching rule wins, so put specific rules above general ones.
    /// </summary>
    [CreateAssetMenu(fileName = "ReviewLibrary", menuName = "CircuitCrafter/Guidance/Review Library")]
    public class ReviewLibrary : ScriptableObject
    {
        [Serializable]
        public class MistakeRule
        {
            [Tooltip("Only for you, in the Inspector list.")]
            public string ruleName = "Rule";

            [Tooltip("COC1..COC4, or blank for any module.")]
            public string moduleId = "";

            [Tooltip("Mistake category to match, or blank for any. The game reports: general, sequence, " +
                     "procedure, identification, configuration, connectivity, cable, diagnosis, verification, " +
                     "unresolved, time.")]
            public string category = "";

            [Tooltip("Match when ANY of these words appears in the mistake text. Not case sensitive. " +
                     "Leave empty to match on module/category alone.")]
            public string[] keywords = new string[0];

            [Header("Story Mode toast (short)")]
            [Tooltip("Student-facing, one line. Leave blank to show the mistake text itself.")]
            public string whatWentWrong = "";

            [Tooltip("One line. What to do now.")]
            public string howToFix = "";

            [Header("Assessment review (after the run)")]
            [TextArea(2, 6)]
            [Tooltip("The correct procedure, in steps if it helps. Shown only after an assessment has ended.")]
            public string correctProcedure = "";

            [Tooltip("Short label for the 'Areas to improve' list, e.g. 'Component removal sequence'.")]
            public string areaToImprove = "";

            [Tooltip("Activity Id from the Activities list below, recommended when this mistake is made.")]
            public string activityId = "";
        }

        [Serializable]
        public class StoryActivity
        {
            [Tooltip("Unique id rules and outcomes point at, e.g. 'coc1-disassembly'.")]
            public string activityId = "";

            [Tooltip("Shown on the recommendation button, e.g. 'COC1 Story: Disassemble the system unit'.")]
            public string title = "";

            [Tooltip("COC1..COC4. Passed to SaveManager.SelectMode so the scene opens in Story Mode.")]
            public string moduleId = "COC1";

            [Tooltip("The Story Mode scene to load, exactly as in Build Settings. Leave blank to send the " +
                     "learner to the Main Menu with the recommendation shown instead.")]
            public string sceneName = "";

            [TextArea(1, 3)]
            [Tooltip("What to focus on when they get there.")]
            public string whatToPractise = "";
        }

        [Serializable]
        public class OutcomeLink
        {
            public string moduleId = "COC1";

            [Tooltip("The learning outcome code on the CompetencyDefinition, e.g. 'LO1'.")]
            public string outcomeCode = "LO1";

            [Tooltip("Shown in 'Areas to improve' when this outcome was not demonstrated.")]
            public string areaToImprove = "";

            public string activityId = "";
        }

        [Tooltip("Specific rules first - the first match wins.")]
        public List<MistakeRule> rules = new List<MistakeRule>();

        public List<StoryActivity> activities = new List<StoryActivity>();

        [Tooltip("Which activity to recommend when a learning outcome was not shown to be competent.")]
        public List<OutcomeLink> outcomeLinks = new List<OutcomeLink>();

        // ==================================================================

        /// <summary>The library in use: GuidanceSettings' one, else Resources/ReviewLibrary, else null.</summary>
        public static ReviewLibrary Active
        {
            get
            {
                GuidanceSettings s = GuidancePolicy.Settings;
                if (s != null && s.reviewLibrary != null) return s.reviewLibrary;

                if (!triedResources)
                {
                    triedResources = true;
                    fromResources = Resources.Load<ReviewLibrary>("ReviewLibrary");
                }
                return fromResources;
            }
        }

        private static bool triedResources;
        private static ReviewLibrary fromResources;

        /// <summary>The first rule that matches, or null.</summary>
        public MistakeRule Match(string moduleId, string category, string text)
        {
            if (rules == null) return null;
            string haystack = (text ?? "").ToLowerInvariant();

            foreach (MistakeRule rule in rules)
            {
                if (rule == null) continue;
                if (!string.IsNullOrEmpty(rule.moduleId) && !string.IsNullOrEmpty(moduleId) &&
                    !string.Equals(rule.moduleId, moduleId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(rule.category) &&
                    !string.Equals(rule.category, category ?? "", StringComparison.OrdinalIgnoreCase)) continue;

                if (rule.keywords == null || rule.keywords.Length == 0) return rule;

                foreach (string keyword in rule.keywords)
                {
                    if (string.IsNullOrEmpty(keyword)) continue;
                    if (haystack.Contains(keyword.ToLowerInvariant())) return rule;
                }
            }
            return null;
        }

        public StoryActivity FindActivity(string activityId)
        {
            if (string.IsNullOrEmpty(activityId) || activities == null) return null;
            foreach (StoryActivity a in activities)
                if (a != null && a.activityId == activityId) return a;
            return null;
        }

        public OutcomeLink FindOutcome(string moduleId, string outcomeCode)
        {
            if (outcomeLinks == null) return null;
            foreach (OutcomeLink link in outcomeLinks)
            {
                if (link == null) continue;
                if (string.Equals(link.moduleId, moduleId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(link.outcomeCode, outcomeCode, StringComparison.OrdinalIgnoreCase))
                    return link;
            }
            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var ids = new HashSet<string>();
            foreach (StoryActivity a in activities)
            {
                if (a == null || string.IsNullOrEmpty(a.activityId)) continue;
                if (!ids.Add(a.activityId))
                    Debug.LogWarning($"[ReviewLibrary] '{name}' has two activities with the id '{a.activityId}'.", this);
            }

            foreach (MistakeRule r in rules)
            {
                if (r == null || string.IsNullOrEmpty(r.activityId)) continue;
                if (!ids.Contains(r.activityId))
                    Debug.LogWarning($"[ReviewLibrary] Rule '{r.ruleName}' recommends activity '{r.activityId}', which is not in the Activities list.", this);
            }
        }
#endif
    }
}
