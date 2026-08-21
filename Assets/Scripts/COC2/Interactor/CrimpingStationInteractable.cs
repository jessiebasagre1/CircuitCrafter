// CrimpingStationInteractable.cs
using CSSSimulation.Interaction;
using UnityEngine;

namespace CSSSimulation.Networking
{
    public class CrimpingStationInteractable : Interactable
    {
        [Tooltip("Shown before the crimping view opens, so the player picks which cable " +
                 "they're building this attempt (Straight-Through or Crossover). Leave " +
                 "unassigned to skip the choice and enter the crimping view directly, " +
                 "using whatever CrimpingStationController.targetCableType is already set to.")]
        public CableTypeSelectionUI cableTypeSelection;

        public override void Interact()
        {
            // If a cable type selector is set up, show it first - it's responsible for
            // calling StrippingViewController.EnterStrippingView() itself once the player
            // picks. Otherwise, fall back to entering stripping right away (CrimpingViewController
            // still decides whether to show the "no tool" warning, via StrippingViewController
            // reusing WarnIfNoTool() before letting the player in).
            if (cableTypeSelection != null)
            {
                cableTypeSelection.Open();
            }
            else if (StrippingViewController.Instance != null)
            {
                StrippingViewController.Instance.EnterStrippingView();
            }
        }
    }
}