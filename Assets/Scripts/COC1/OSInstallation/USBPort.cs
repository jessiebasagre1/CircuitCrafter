using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Represents a physical USB port on the case/motherboard. Plugging is driven
/// by PlayerInteractor's proximity search (is a held drive close to and
/// visible from this port?) rather than precise raycast aim — easier to hit
/// than requiring the player's crosshair land exactly on a small port.
/// </summary>
public class USBPort : MonoBehaviour
{
    // Registry of every enabled port, so PlayerInteractor can search by
    // proximity instead of relying on precise raycast aim.
    private static readonly List<USBPort> allPorts = new List<USBPort>();
    public static IReadOnlyList<USBPort> AllPorts => allPorts;

    private void OnEnable() => allPorts.Add(this);
    private void OnDisable() => allPorts.Remove(this);

    [Tooltip("Optional — snaps the drive's transform to sit neatly in the port once plugged in. Leave unassigned to skip snapping.")]
    [SerializeField] private Transform snapPoint;

    public USBDriveItem CurrentDrive { get; private set; }

    public void PlugIn(USBDriveItem drive)
    {
        if (CurrentDrive != null && CurrentDrive != drive)
        {
            Debug.Log($"{name}: port already occupied by '{CurrentDrive.name}' — remove it first.", this);
            return;
        }

        CurrentDrive = drive;
        drive.IsInsertedInPort = true;

        drive.transform.SetParent(snapPoint != null ? snapPoint : transform, worldPositionStays: false);
        drive.transform.localPosition = Vector3.zero;
        drive.transform.localRotation = Quaternion.identity;

        var col = drive.GetComponent<Collider>();
        if (col != null) col.enabled = true; // re-enable for raycast targeting while plugged in

        Debug.Log($"{name}: '{drive.name}' plugged in.", this);
    }

    public void Unplug()
    {
        if (CurrentDrive == null) return;

        Debug.Log($"{name}: '{CurrentDrive.name}' unplugged.", this);
        CurrentDrive.IsInsertedInPort = false;
        CurrentDrive = null;
    }

    /// <summary>
    /// Optional: wire this up to an InteractableObject.onInteract on the port
    /// itself, so clicking a port with a drive in it pulls the drive back
    /// into the player's hand (only if hands are currently empty).
    /// </summary>
    public void RequestRemoveDrive()
    {
        if (CurrentDrive == null) return;
        if (CarriedItemHolder.Instance == null || CarriedItemHolder.Instance.HeldDrive != null) return;

        USBDriveItem drive = CurrentDrive;
        Unplug();
        CarriedItemHolder.Instance.PickUp(drive);
    }
}