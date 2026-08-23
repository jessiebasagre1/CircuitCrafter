// CableInventory.cs
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The crimped UTP cable "in the player's pocket" - a direct twin of CrimpingToolInventory,
    /// deliberately identical in shape so the two behave the same way and read the same way.
    /// There is no real pocket object anywhere; this is just a bool that other scripts ask about.
    ///
    /// Setup: put this on the same always-active Managers object that holds CrimpingToolInventory.
    /// Nothing to assign.
    /// </summary>
    public class CableInventory : MonoBehaviour
    {
        public static CableInventory Instance { get; private set; }

        /// <summary>True once the player has picked the finished cable up off the table.</summary>
        public bool HasCable { get; private set; }

        /// <summary>Fires when the cable is picked up - handy for a "now take it to the tester" dialog.</summary>
        public System.Action OnCablePickedUp;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void PickUp()
        {
            if (HasCable) return;
            HasCable = true;
            Debug.Log("[CableInventory] Crimped UTP cable picked up - stored in pocket.");
            OnCablePickedUp?.Invoke();
        }

        /// <summary>Called by NetworkTestStation once the cable is plugged into the tester, so the
        /// player isn't still carrying it afterwards. Call PickUp() again to hand it back.</summary>
        public void Consume()
        {
            HasCable = false;
        }
    }
}
