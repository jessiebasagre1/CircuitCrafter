using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// The band that introduces one module's block of outcomes on the competency
    /// profile screen.
    ///
    /// PUT THIS ON: a prefab, and assign it to
    /// CompetencyProfileView.moduleHeaderPrefab.
    ///
    /// Suggested layout:
    ///   [COC1] [Install and configure computer systems] [2 of 5 competent] [bar]
    ///
    /// All fields optional except moduleTitleText.
    /// </summary>
    public class CompetencyModuleHeaderView : MonoBehaviour
    {
        [Tooltip("The module id, e.g. \"COC1\". Optional.")]
        [SerializeField] private TMP_Text moduleIdText;

        [Tooltip("The unit title, e.g. \"Install and configure computer systems\".")]
        [SerializeField] private TMP_Text moduleTitleText;

        [Tooltip("Shows e.g. \"2 of 5 competent\". Optional.")]
        [SerializeField] private TMP_Text summaryText;

        [Tooltip("Optional 0-1 bar of competent outcomes over total outcomes.")]
        [SerializeField] private Slider progressBar;

        [Tooltip("Optional - switched on only when every outcome in the module is Competent.")]
        [SerializeField] private GameObject completeBadge;

        public void Setup(CompetencyDefinition definition, int competent, int total)
        {
            if (definition == null) return;

            if (moduleIdText != null) moduleIdText.text = definition.moduleId;
            if (moduleTitleText != null) moduleTitleText.text = definition.unitTitle;

            if (summaryText != null)
                summaryText.text = total > 0 ? $"{competent} of {total} competent" : "No outcomes defined";

            if (progressBar != null)
            {
                // maxValue is floored at 1 so a module with no outcomes yet shows an
                // empty bar rather than dividing by zero and rendering full.
                progressBar.maxValue = Mathf.Max(1, total);
                progressBar.value = competent;
            }

            if (completeBadge != null)
                completeBadge.SetActive(total > 0 && competent == total);
        }
    }
}
