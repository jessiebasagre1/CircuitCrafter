using System.Text;
using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// A job order: who the customer is, what they asked for, what is wrong, and what "done" looks
    /// like. Every Story run and every Assessment starts from one - the same way a real service call
    /// does.
    ///
    /// CREATE: Project window > Create > CircuitCrafter > Guidance > Job Order. The builder's
    /// "Create Sample Job Orders" makes one per COC to edit.
    ///
    /// ONE ASSET, TWO READINGS
    ///   Story      - the customer, request, problem, expected outcome, specifications, AND the
    ///                teaching notes: what you will practise, tools you will need, safety.
    ///   Assessment - a concise scenario, the required outcome, and the specifications. Nothing
    ///                about tools, order or method - the candidate decides those.
    ///
    /// Specifications (an IP scheme, the ISO to use, which cable to build) appear in BOTH, because
    /// they are part of the customer's requirement, not help: a real job sheet states them.
    /// </summary>
    [CreateAssetMenu(fileName = "JobOrder_", menuName = "CircuitCrafter/Guidance/Job Order")]
    public class JobOrderData : ScriptableObject
    {
        [Header("Identity")]
        public string jobNumber = "JO-0001";

        [Tooltip("COC1..COC4.")]
        public string moduleId = "COC1";

        [Tooltip("e.g. \"Install and configure computer systems\".")]
        public string unitTitle = "";

        [Header("Customer")]
        public string customerName = "";

        [Tooltip("e.g. \"Office manager, Sta. Cruz branch\".")]
        public string customerDetail = "";

        [Header("The job")]
        [TextArea(2, 5)]
        [Tooltip("What the customer asked for, in their own words.")]
        public string request = "";

        [TextArea(2, 5)]
        [Tooltip("The problem or situation, as the customer describes it. Never the cause.")]
        public string problem = "";

        [Tooltip("What must be true when the job is done. One line each.")]
        public string[] expectedOutcomes = new string[0];

        [Tooltip("Values the job must use - IP addresses, the ISO name, the cable standard. Shown in both modes.")]
        public string[] specifications = new string[0];

        [Header("Story Mode only - teaching notes")]
        [TextArea(2, 5)]
        [Tooltip("What this job teaches, e.g. \"Safe disassembly and reassembly of a system unit.\"")]
        public string whatYouWillPractise = "";

        [Tooltip("Tools and materials. Story only - in an assessment choosing the tools is part of the task.")]
        public string[] toolsNeeded = new string[0];

        [TextArea(1, 4)]
        [Tooltip("The OHS reminder for this job - ESD strap, power off and unplug, etc.")]
        public string safetyReminder = "";

        [Header("Assessment")]
        [Tooltip("Heading for the assessment briefing. Blank = \"<module> Assessment\".")]
        public string assessmentTitle = "";

        [TextArea(2, 5)]
        [Tooltip("The concise scenario, e.g. \"The customer reports that the computer cannot boot.\"")]
        public string assessmentScenario = "";

        [Tooltip("The required outcome. Say WHAT, never HOW, e.g. \"Diagnose the problem, identify the cause, and perform the necessary corrective action.\"")]
        public string[] assessmentOutcomes = new string[0];

        [Tooltip("For display only. The real limit is set on the assessment manager / launcher.")]
        [Min(0)] public int timeLimitMinutes = 0;

        // ==================================================================

        public string Title(bool assessment)
        {
            if (assessment)
                return string.IsNullOrEmpty(assessmentTitle) ? $"{moduleId} Assessment" : assessmentTitle;

            return string.IsNullOrEmpty(unitTitle) ? $"Job Order {jobNumber}" : $"Job Order {jobNumber}  -  {unitTitle}";
        }

        /// <summary>One line for a HUD - the required outcome in an assessment, the request in Story.</summary>
        public string Summary(bool assessment)
        {
            if (assessment)
            {
                if (assessmentOutcomes != null && assessmentOutcomes.Length > 0 && !string.IsNullOrEmpty(assessmentOutcomes[0]))
                    return assessmentOutcomes[0];
                return string.IsNullOrEmpty(assessmentScenario) ? "Complete the job order." : assessmentScenario;
            }

            if (expectedOutcomes != null && expectedOutcomes.Length > 0 && !string.IsNullOrEmpty(expectedOutcomes[0]))
                return expectedOutcomes[0];
            return request;
        }

        /// <summary>The full job order as rich text for JobOrderPanel.</summary>
        public string BuildText(bool assessment, bool includeCoaching)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(customerName))
            {
                sb.Append("<b>Customer:</b> ").Append(customerName);
                if (!string.IsNullOrEmpty(customerDetail)) sb.Append("  <color=#A9C6F0>(").Append(customerDetail).Append(")</color>");
                sb.Append("\n\n");
            }

            if (assessment)
            {
                Section(sb, "Scenario", string.IsNullOrEmpty(assessmentScenario) ? problem : assessmentScenario);
                Bullets(sb, "Required outcome", assessmentOutcomes != null && assessmentOutcomes.Length > 0 ? assessmentOutcomes : expectedOutcomes);
                Bullets(sb, "Specifications", specifications);
                if (timeLimitMinutes > 0) Section(sb, "Time allowed", timeLimitMinutes + " minutes");
                sb.Append("<color=#A9C6F0>Work independently. No hints, tutorials or AI Tutor are available until you finish. " +
                          "Your results and a full review appear at the end.</color>");
                return sb.ToString().TrimEnd();
            }

            Section(sb, "Request", request);
            Section(sb, "Problem", problem);
            Bullets(sb, "Expected outcome", expectedOutcomes);
            Bullets(sb, "Specifications", specifications);

            if (includeCoaching)
            {
                Section(sb, "What you will practise", whatYouWillPractise);
                Bullets(sb, "Tools and materials", toolsNeeded);
                if (!string.IsNullOrEmpty(safetyReminder))
                    sb.Append("<color=#FFC93C><b>Safety:</b> ").Append(safetyReminder).Append("</color>\n\n");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>Plain sentences for the AI Tutor's system prompt.</summary>
        public string DescribeForTutor()
        {
            var sb = new StringBuilder();
            sb.Append("Job order ").Append(jobNumber).Append(" for ").Append(moduleId);
            if (!string.IsNullOrEmpty(customerName)) sb.Append(", customer ").Append(customerName);
            sb.Append(".");
            if (!string.IsNullOrEmpty(request)) sb.Append(" Request: ").Append(request);
            if (!string.IsNullOrEmpty(problem)) sb.Append(" Problem: ").Append(problem);
            if (expectedOutcomes != null && expectedOutcomes.Length > 0)
                sb.Append(" Expected outcome: ").Append(string.Join("; ", expectedOutcomes)).Append(".");
            if (specifications != null && specifications.Length > 0)
                sb.Append(" Specifications: ").Append(string.Join("; ", specifications)).Append(".");
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string heading, string body)
        {
            if (string.IsNullOrEmpty(body)) return;
            sb.Append("<b><color=#5FD4FF>").Append(heading).Append("</color></b>\n").Append(body).Append("\n\n");
        }

        private static void Bullets(StringBuilder sb, string heading, string[] lines)
        {
            if (lines == null || lines.Length == 0) return;
            bool any = false;
            foreach (string l in lines) if (!string.IsNullOrEmpty(l)) { any = true; break; }
            if (!any) return;

            sb.Append("<b><color=#5FD4FF>").Append(heading).Append("</color></b>\n");
            foreach (string l in lines)
                if (!string.IsNullOrEmpty(l)) sb.Append("•  ").Append(l).Append('\n');
            sb.Append('\n');
        }
    }
}
