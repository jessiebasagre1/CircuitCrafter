using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Generic "only visible while inside the crimping view, and no dialog is
    /// covering the screen" binder. Same idea as COC1's HotbarVisibilityController:
    /// continuously derives the correct visible state every frame from ground
    /// truth (CrimpingViewController.InCrimpingView + DialogManager.IsDialogOpen)
    /// instead of relying on scattered SetActive() calls sprinkled across other
    /// scripts, which can drift out of sync.
    ///
    /// Use this for CrimpingGuidePanel_EndA / CrimpingGuidePanel_EndB, or any other
    /// future crimping-only UI that can't simply be parented under
    /// CrimpingViewController.crimpingUI (e.g. it lives under a different Canvas,
    /// or needs independent positioning). If reparenting under crimpingUI works
    /// for your hierarchy, that's the simpler zero-code option -- crimpingUI is
    /// already toggled automatically inside SetCrimpingViewActive().
    ///
    /// Setup:
    ///   1. Attach this to a small always-on object (NOT one of the guide panels
    ///      themselves -- same reason as HotbarVisibilityController: if this
    ///      script's own GameObject gets disabled, its Update() stops running and
    ///      it can never re-enable anything). A "UI Coordinators" object, or the
    ///      same object CrimpingStationController lives on, works fine.
    ///   2. Drag CrimpingGuidePanel_EndA and CrimpingGuidePanel_EndB (and anything
    ///      else that should only show up mid-crimp) into the array below.
    /// </summary>
    public class CrimpingViewVisibility : MonoBehaviour
    {
        [Tooltip("Objects that should only be active while the player is inside the crimping view (CrimpingViewController.InCrimpingView == true) -- e.g. CrimpingGuidePanel_EndA, CrimpingGuidePanel_EndB. Hidden the rest of the time, automatically.")]
        [SerializeField] private GameObject[] onlyVisibleInCrimpingView;

        void Update()
        {
            if (onlyVisibleInCrimpingView == null || onlyVisibleInCrimpingView.Length == 0) return;

            bool inView = CrimpingViewController.Instance != null && CrimpingViewController.Instance.InCrimpingView;
            bool dialogOpen = DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen;
            bool shouldBeVisible = inView && !dialogOpen;

            foreach (var obj in onlyVisibleInCrimpingView)
            {
                if (obj != null && obj.activeSelf != shouldBeVisible)
                    obj.SetActive(shouldBeVisible);
            }
        }
    }
}