using UnityEngine;
/// <summary>
/// Place on each mount point on the motherboard/case (CPU socket, RAM slot,
/// PCIe slot, etc). Give it a (trigger) Collider on a dedicated "Sockets"
/// layer so DragController can raycast against it independently of other
/// geometry. The exact position a part snaps back to is handled by
/// PCPartItem (it remembers its own originally-assembled offset), so this
/// object's own pivot doesn't need to be perfectly centered on anything.
/// </summary>
public class PartSocket : MonoBehaviour
{
    public PartType acceptedType;
    public PCPartItem Occupant { get; set; }
    public bool IsEmpty => Occupant == null;
    [Header("Highlight")]
    [Tooltip("Optional visual (outline, glow, ring, etc.) toggled on while a compatible part is being dragged over this socket. Same idea as PortSocket's highlight in the cable system.")]
    public GameObject highlightVisual;
    public void SetHighlighted(bool highlighted)
    {
        if (highlightVisual != null)
            highlightVisual.SetActive(highlighted);
    }
    /// <summary>
    /// The local position/rotation a part should have when correctly
    /// installed in this socket. Recorded automatically from whichever part
    /// starts here in the scene, so any part of a matching type dropped in
    /// later reuses this same known-good offset.
    /// </summary>
    public Vector3 InstalledLocalPosition { get; private set; }
    public Quaternion InstalledLocalRotation { get; private set; } = Quaternion.identity;
    public bool HasInstalledOffset { get; private set; }
    public void RecordInstalledOffset(Vector3 localPosition, Quaternion localRotation)
    {
        InstalledLocalPosition = localPosition;
        InstalledLocalRotation = localRotation;
        HasInstalledOffset = true;
        Debug.Log($"{name}: recorded installed offset — localPosition {localPosition}, localRotation euler {localRotation.eulerAngles}", this);
    }
}