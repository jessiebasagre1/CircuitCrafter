// CrimpingToolPickup.cs
using UnityEngine;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    [RequireComponent(typeof(Collider))]
    public class CrimpingToolPickup : Interactable
    {
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