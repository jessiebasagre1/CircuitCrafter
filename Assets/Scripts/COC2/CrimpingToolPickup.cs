// CrimpingToolPickup.cs
using UnityEngine;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    [RequireComponent(typeof(Collider))]
    public class CrimpingToolPickup : Interactable
    {
        // The tool goes into the player's hands, so it answers to G and to the Pick Up button.
        protected override InputChannel DefaultChannel { get { return InputChannel.Pickup; } }

        public override void Interact()
        {
            if (CrimpingToolInventory.Instance == null)
            {
                Debug.LogWarning("[CrimpingToolPickup] No CrimpingToolInventory found in the scene.", this);
                return;
            }
            if (CrimpingToolInventory.Instance.HasTool) return;

            CrimpingToolInventory.Instance.PickUp();
            gameObject.SetActive(false); // "goes into the pocket"
        }
    }
}