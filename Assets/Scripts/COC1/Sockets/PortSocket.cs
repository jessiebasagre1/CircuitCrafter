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

    private CableConnector current;

    /// <summary>
    /// True while a connector is plugged in. Deliberately computed from `current` rather than
    /// stored as its own serialized bool: `current` is a runtime-only reference that always
    /// resets to null on scene load, so deriving from it guarantees this can never disagree
    /// with reality after a scene is saved, duplicated, or reloaded. (A previous version stored
    /// this as an independent [HideInInspector] bool, which COULD get baked into a scene file
    /// as `true` from whatever state it was last saved in, while `current` silently reset to
    /// null - leaving the socket permanently "occupied" with nothing actually plugged in.)
    /// </summary>
    public bool isOccupied => current != null;

    public bool Accepts(CableType type) => type == acceptedType;
    public void SetOccupied(CableConnector connector, bool occupied)
    {
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