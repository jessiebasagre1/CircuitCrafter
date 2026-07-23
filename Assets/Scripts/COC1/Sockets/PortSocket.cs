using UnityEngine;
/// <summary>
/// A port on the motherboard (or PSU) that accepts one specific CableType.
/// Place this on a trigger collider positioned at/near the physical port.
///
/// snapPoint should be the exact position + rotation the connector should sit at when plugged in.
/// Tip: reuse the Empty objects already in your Blender file (e.g. "power_port_3") as snapPoint -
/// they export through FBX as plain transforms, so you likely don't need to hand-place these.
/// </summary>
public class PortSocket : MonoBehaviour
{
    [Header("Compatibility")]
    public CableType acceptedType;
    [Header("Snap Target")]
    public Transform snapPoint;
    [Header("Highlight")]
    [Tooltip("Optional visual (outline, glow, ring, etc.) toggled on while a compatible connector is nearby but not yet plugged in.")]
    public GameObject highlightVisual;
    [HideInInspector] public bool isOccupied;
    private CableConnector current;
    public bool Accepts(CableType type) => type == acceptedType;
    public void SetOccupied(CableConnector connector, bool occupied)
    {
        isOccupied = occupied;
        current = occupied ? connector : null;
        // Hooks for feedback - wire these up to your art/audio:
        // - toggle an emissive "connected" material or LED
        // - play a plug-in click / unplug sound
        // - fire a UnityEvent for tutorial/objective tracking
    }
    public void SetHighlighted(bool on)
    {
        if (highlightVisual != null)
            highlightVisual.SetActive(on);
    }
}