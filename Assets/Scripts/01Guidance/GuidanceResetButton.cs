using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// Put on a Settings-screen button and wire its OnClick to ResetTutorials(), so a learner or
    /// teacher can see every tutorial again.
    /// </summary>
    public class GuidanceResetButton : MonoBehaviour
    {
        public void ResetTutorials()
        {
            TutorialProgress.ResetForActiveProfile();
            CoachFeedback.Info("Tutorials will show again.");
        }
    }
}
