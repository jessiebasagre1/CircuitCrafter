using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Place on (or as a child trigger near) the UTP cable / crimping station in the
    /// 3D world. Pressing E while in range switches into the close-up crimping view.
    /// </summary>
    public class CrimpingStationInteractable : Interactable
    {
        public override void Interact()
        {
            if (CrimpingViewController.Instance != null)
                CrimpingViewController.Instance.EnterCrimpingView();
        }
    }
}