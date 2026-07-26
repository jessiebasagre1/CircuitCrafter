using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Represents a physical USB flash drive in the world. Starts non-bootable 
/// BootableUSBCreator flips IsBootable once the player finishes the
/// create-bootable-USB flow. IsInsertedInPort is set by USBPort once
/// PlayerInteractor plugs this drive in.
///
/// Setup: add an InteractableObject component alongside this one, and in its
/// onInteract UnityEvent, drag this GameObject in and pick
/// USBDriveItem > RequestPickup() from the dropdown  that's what makes
/// clicking the drive pick it up. (Also picked up automatically by
/// PlayerInteractor's proximity system if you're using that instead.)
/// </summary>
public class USBDriveItem : MonoBehaviour
{
    // Registry of every enabled drive in the scene, so PlayerInteractor can
    // do a proximity search instead of relying on precise raycast aim.
    private static readonly List<USBDriveItem> allDrives = new List<USBDriveItem>();
    public static IReadOnlyList<USBDriveItem> AllDrives => allDrives;

    private void OnEnable() => allDrives.Add(this);
    private void OnDisable() => allDrives.Remove(this);

    [Header("Hover Highlight")]
    [Tooltip("Optional  outline/glow object shown while the player is looking at this drive and it can currently be picked up. Same idea as PCPartItem's pickupReadyVisual. Leave unassigned to skip.")]
    [SerializeField] private GameObject hoverHighlight;
    bool hoverHighlightActive;

    [Tooltip("True once BootableUSBCreator has finished writing an image to this drive.")]
    public bool IsBootable { get; private set; }

    [Tooltip("Which ISO (if any) this drive currently has written to it  null until created.")]
    public ISOImageData WrittenImage { get; private set; }

    [Tooltip("True once set by USBPort when this drive is physically plugged into the PC.")]
    public bool IsInsertedInPort;

    /// <summary>The specific USBPort this drive is currently plugged into, or
    /// null if it's not plugged in anywhere. Set by USBPort.PlugIn/Unplug.
    /// Check THIS (not just IsInsertedInPort) when a station cares which
    /// port specifically  IsInsertedInPort alone can't tell the reference
    /// PC's port apart from the target PC's port.</summary>
    public USBPort CurrentPort { get; internal set; }

    public void MarkAsBootable(ISOImageData image)
    {
        IsBootable = true;
        WrittenImage = image;
    }

    /// <summary>Call if the player reformats/erases the drive for a different image.</summary>
    public void ClearBootableState()
    {
        IsBootable = false;
        WrittenImage = null;
    }

    /// <summary>Called by PlayerInteractor every frame the hover target changes  not per-frame spam.</summary>
    public void SetHighlighted(bool on)
    {
        if (hoverHighlight == null) return;
        if (on == hoverHighlightActive) return;

        hoverHighlightActive = on;
        hoverHighlight.SetActive(on);
    }

    /// <summary>Wire this up to InteractableObject.onInteract in the Inspector.</summary>
    public void RequestPickup()
    {
        if (IsInsertedInPort) return; // pull it out via the port instead, not directly
        if (CarriedItemHolder.Instance != null)
            CarriedItemHolder.Instance.PickUp(this);
    }
}