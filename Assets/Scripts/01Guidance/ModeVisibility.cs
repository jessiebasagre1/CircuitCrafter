using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// Shows or hides THIS GameObject depending on the mode - no code needed.
    ///
    /// ATTACH THIS TO: any piece of guidance that already exists in a scene and has no mode check
    /// of its own - a COC3 step's instruction label, a hint button, a "Guide" toggle, a highlight
    /// ring, an instruction strip on a station. Pick what kind of help it is; GuidancePolicy decides.
    ///
    ///   Show When Allowed  - pick the kind (e.g. Hints). Visible whenever that kind of help is
    ///                        allowed in the current mode, hidden when it is not. This is the usual
    ///                        choice: the rule lives in GuidanceSettings, not here.
    ///   Story Only         - visible in Story, hidden in Practice and Assessment.
    ///   Assessment Only    - the reverse. For an exam-only label or button.
    ///
    /// It checks every time the object is switched on AND once a second while on, because some
    /// scripts switch their own panels on after this has already run. It never switches an object
    /// ON that some other script has switched off - it only ever removes things.
    ///
    /// Put it on the object you want hidden, not on a parent that also holds things that must stay.
    /// </summary>
    [DisallowMultipleComponent]
    public class ModeVisibility : MonoBehaviour
    {
        public enum Rule { ShowWhenAllowed = 0, StoryOnly = 1, AssessmentOnly = 2 }

        [Tooltip("How to decide. Show When Allowed follows GuidanceSettings for the kind below.")]
        public Rule rule = Rule.ShowWhenAllowed;

        [Tooltip("What kind of help this object is. Used by Show When Allowed.")]
        public GuidanceKind kind = GuidanceKind.Hints;

        [Tooltip("Hide by switching the GameObject off (default). Untick to hide only its visuals " +
                 "(CanvasGroup alpha 0, not clickable) - use that when a script elsewhere still " +
                 "needs this object active to run.")]
        public bool deactivateObject = true;

        [Tooltip("Log each decision. Handy while you are placing these.")]
        public bool log;

        private CanvasGroup group;
        private float nextCheck;

        private void OnEnable()
        {
            // SetActive(false) on an object while it is still being activated makes Unity complain
            // ("GameObject is already being activated or deactivated"), so the switch-off happens on
            // this object's first Update instead - still before the frame is drawn in the normal case.
            // The CanvasGroup route has no such restriction and applies at once.
            if (deactivateObject) nextCheck = 0f;
            else Apply();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            Apply();
        }

        /// <summary>True if this object should be visible right now.</summary>
        public bool Wanted
        {
            get
            {
                switch (rule)
                {
                    case Rule.StoryOnly: return GuidancePolicy.IsStory;
                    case Rule.AssessmentOnly: return GuidancePolicy.IsAssessment;
                    default: return GuidancePolicy.Allows(kind);
                }
            }
        }

        public void Apply()
        {
            bool wanted = Wanted;

            if (log)
                Debug.Log($"[ModeVisibility] '{name}' {(wanted ? "shown" : "hidden")} ({rule}, {kind}) - {GuidancePolicy.Describe()}", this);

            if (deactivateObject)
            {
                // Only ever switches OFF. Switching on is left to whoever owns this object -
                // otherwise this would fight a panel that is meant to be closed.
                if (!wanted && gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }

            if (group == null) group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();

            group.alpha = wanted ? 1f : 0f;
            group.interactable = wanted;
            group.blocksRaycasts = wanted;
        }
    }
}
