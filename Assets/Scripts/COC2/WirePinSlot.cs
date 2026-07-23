using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Represents a single pin position (1-8) inside an RJ45 connector housing.
    /// Place 8 of these as children of a housing, one per pin slot, positioned to match
    /// the real connector's pin order. Assignment now happens explicitly via
    /// WireDragController on drop (see AssignWire), matching how PartSocket works —
    /// no more automatic trigger-based detection.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WirePinSlot : MonoBehaviour
    {
        [Tooltip("Pin position this slot represents, 1 through 8.")]
        [Range(1, 8)] public int pinNumber = 1;

        [Tooltip("Which connector end this slot belongs to (1 or 2).")]
        public int endNumber = 1;

        [Header("Highlight (optional)")]
        [Tooltip("Optional visual (outline, glow, etc.) shown while a valid dragged wire hovers over this slot.")]
        public GameObject highlightVisual;

        [Header("Attach Alignment")]
        [Tooltip("Local position offset applied to a wire once it snaps into this slot, in case the wire " +
                 "mesh's pivot isn't exactly at its connecting tip. Tweak this in the Inspector (Play mode " +
                 "is fine for testing values live) until the wire visually lines up, same idea as " +
                 "PCPartItem's installPositionCorrection.")]
        public Vector3 attachPositionOffset = Vector3.zero;
        [Tooltip("Local rotation offset in degrees, applied the same way.")]
        public Vector3 attachRotationEulerOffset = Vector3.zero;

        public WireIdentity CurrentWire { get; private set; }
        public bool IsFilled => CurrentWire != null;
        public bool IsEmpty => CurrentWire == null;

        void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        public bool AssignWire(WireIdentity wire)
        {
            if (wire == null) return false;
            if (wire.endNumber != endNumber)
            {
                Debug.LogWarning($"[WirePinSlot] {wire.name} belongs to End {wire.endNumber}, " +
                                  $"can't be placed in an End {endNumber} slot.");
                return false;
            }
            CurrentWire = wire;
            return true;
        }

        public void ClearWire()
        {
            CurrentWire = null;
        }

        public void SetHighlighted(bool state)
        {
            if (highlightVisual != null) highlightVisual.SetActive(state);
        }
    }
}