using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Wire equivalent of PCPartItem. Unlike PC parts, wires don't shrink into a hotbar —
    /// they stay visible and move through 3D space while dragged, since the point is
    /// watching them get placed into the correct pin. Requires a WireIdentity on the
    /// same object so the drag controller and slots know this wire's color/end.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(WireIdentity))]
    public class WireDraggable : MonoBehaviour
    {
        public WirePinSlot HomeSlot { get; private set; }

        Vector3 homeLocalPosition;
        Quaternion homeLocalRotation;
        Transform homeParent;

        void Awake()
        {
            // Remember where this wire started (its "loose wire tray" position)
            // so it can snap back there if dropped somewhere invalid.
            homeLocalPosition = transform.localPosition;
            homeLocalRotation = transform.localRotation;
            homeParent = transform.parent;
        }

        /// <summary>Unity calls this automatically when this object's collider is clicked — no extra setup needed.</summary>
        void OnMouseDown()
        {
            TryBeginDrag();
        }

        /// <summary>Also callable directly if you end up building a custom click/raycast system later.</summary>
        public void TryBeginDrag()
        {
            if (WireDragController.Instance == null)
            {
                Debug.LogWarning($"{name}: can't drag — no WireDragController found in this scene.", this);
                return;
            }

            // If this wire is already seated in a slot, vacate it first.
            if (HomeSlot != null)
            {
                HomeSlot.ClearWire();
                HomeSlot = null;
            }

            WireDragController.Instance.BeginDrag(this);
        }

        /// <summary>Called by WireDragController when the wire is released over a valid, empty, matching-end slot.</summary>
        public void SnapIntoSlot(WirePinSlot slot)
        {
            HomeSlot = slot;
            slot.AssignWire(GetComponent<WireIdentity>());

            transform.SetParent(slot.transform, worldPositionStays: false);
            transform.localPosition = slot.attachPositionOffset;
            transform.localRotation = Quaternion.Euler(slot.attachRotationEulerOffset);
        }

        /// <summary>Called by WireDragController when the wire is released somewhere invalid.</summary>
        public void ReturnHome()
        {
            transform.SetParent(homeParent, worldPositionStays: false);
            transform.localPosition = homeLocalPosition;
            transform.localRotation = homeLocalRotation;
        }
    }
}