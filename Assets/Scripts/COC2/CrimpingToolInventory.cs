// CrimpingToolInventory.cs
using UnityEngine;

namespace CSSSimulation.Networking
{
    public class CrimpingToolInventory : MonoBehaviour
    {
        public static CrimpingToolInventory Instance { get; private set; }
        public bool HasTool { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void PickUp()
        {
            if (HasTool) return;
            HasTool = true;
            Debug.Log("[CrimpingToolInventory] Crimping tool picked up - stored in pocket " +
                      "(illusion only, there's no real pocket object/transform anywhere in the scene).");
        }
    }
}