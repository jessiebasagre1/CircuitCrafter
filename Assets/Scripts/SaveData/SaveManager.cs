using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Captures and restores game progress: which parts are installed, which
/// cables are plugged, RAM latch states, USB drive bootable/inserted state,
/// and how far the OS install has progressed. Saved as JSON to
/// Application.persistentDataPath.
///
/// IMPORTANT ASSUMPTIONS — please confirm/correct against your real scripts:
///   - CableConnector.isPlugged is a plain public bool field (settable
///     directly). This save system does NOT reposition the cable's actual
///     transform/rope — it only restores the flag. If your CableConnector
///     needs a proper Plug()/Unplug() method call to look right physically
///     (move the connector transform, update VerletRope anchors), let me
///     know and I'll wire that in instead of the direct field set.
///   - RAMLatch has both Open() and Close() methods. Only Close() was
///     confirmed from RAMPartItem's code — Open() is assumed by symmetry.
///     If it doesn't exist, add one, or tell me RAMLatch's real API.
///   - Scenes always start with every PCPartItem already installed in its
///     HomeSocket (matches PCPartItem.Awake() capturing HomeSocket from the
///     parent socket). Loading a save therefore only ever REMOVES parts
///     marked as not-installed — it can't re-insert a part into a socket it
///     doesn't already start in, since HomeSocket is lost once removed.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Header("Scene References (for restoring things that need object lookups)")]
    [Tooltip("Same list you assigned on BootableUSBCreator — used to look up an ISOImageData by name when restoring a drive's written image.")]
    [SerializeField] private ISOImageData[] knownIsoImages;

    private const string SaveFileName = "savegame.json";
    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public bool HasSaveFile() => File.Exists(SavePath);

    public void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }

    // ---------------------------------------------------------------
    // Save
    // ---------------------------------------------------------------

    /// <summary>Call this at any checkpoint (task completed, stage advanced,
    /// USB created) — also called automatically by OSInstallManager and
    /// BootableUSBCreator at their own checkpoints.</summary>
    public void SaveCheckpoint()
    {
        var data = new GameSaveData
        {
            savedAtUtc = DateTime.UtcNow.ToString("o")
        };

        CaptureParts(data);
        //CaptureCables(data);
        //CaptureRamLatches(data);
        CaptureUsbDrives(data);
        CaptureOSInstall(data);

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(SavePath, json);

        Debug.Log($"[SaveManager] Saved checkpoint to {SavePath}");
    }

    private void CaptureParts(GameSaveData data)
    {
        // includeInactive: true — removed parts get SetActive(false) after
        // their shrink animation (see PCPartItem.ShrinkThenDisable), so a
        // normal FindObjectsOfType would miss them entirely.
        var allParts = FindObjectsOfType<PCPartItem>(includeInactive: true);
        foreach (var part in allParts)
        {
            if (string.IsNullOrEmpty(part.partId)) continue;

            data.parts.Add(new PartEntry
            {
                partId = part.partId,
                isInstalled = part.HomeSocket != null
            });
        }
    }

    //private void CaptureCables(GameSaveData data)
    //{
    //    var allCables = FindObjectsOfType<CableConnector>(includeInactive: true);
    //    foreach (var cable in allCables)
    //    {
    //        data.cables.Add(new CableEntry
    //        {
    //            cableName = cable.name,
    //            isPlugged = cable.isPlugged
    //        });
    //    }
    //}

    //private void CaptureRamLatches(GameSaveData data)
    //{
    //    var allLatches = FindObjectsOfType<RAMLatch>(includeInactive: true);
    //    foreach (var latch in allLatches)
    //    {
    //        data.ramLatches.Add(new RamLatchEntry
    //        {
    //            latchName = latch.name,
    //            isOpen = latch.IsOpen
    //        });
    //    }
    //}

    private void CaptureUsbDrives(GameSaveData data)
    {
        foreach (var drive in USBDriveItem.AllDrives)
        {
            if (drive == null) continue;

            data.usbDrives.Add(new UsbDriveEntry
            {
                driveName = drive.name,
                isBootable = drive.IsBootable,
                writtenImageName = drive.WrittenImage != null ? drive.WrittenImage.displayName : "",
                isInsertedInPort = drive.IsInsertedInPort
            });
        }
    }

    private void CaptureOSInstall(GameSaveData data)
    {
        data.osInstall.stageIndex = OSInstallManager.Instance != null
            ? OSInstallManager.Instance.CurrentStageIndex
            : -1;
    }

    // ---------------------------------------------------------------
    // Load
    // ---------------------------------------------------------------

    public void LoadSave()
    {
        if (!HasSaveFile())
        {
            Debug.LogWarning("[SaveManager] No save file found at " + SavePath);
            return;
        }

        string json = File.ReadAllText(SavePath);
        var data = JsonUtility.FromJson<GameSaveData>(json);

        RestoreParts(data);
        RestoreCables(data);
        //RestoreRamLatches(data);
        RestoreUsbDrives(data);
        RestoreOSInstall(data);

        Debug.Log($"[SaveManager] Loaded save from {SavePath} (saved at {data.savedAtUtc})");
    }

    private void RestoreParts(GameSaveData data)
    {
        var allParts = FindObjectsOfType<PCPartItem>(includeInactive: true);
        var byId = new Dictionary<string, PCPartItem>();
        foreach (var part in allParts)
        {
            if (!string.IsNullOrEmpty(part.partId) && !byId.ContainsKey(part.partId))
                byId.Add(part.partId, part);
        }

        foreach (var entry in data.parts)
        {
            if (!byId.TryGetValue(entry.partId, out var part)) continue;

            bool currentlyInstalled = part.HomeSocket != null;

            if (entry.isInstalled && !currentlyInstalled)
            {
                // See class-level assumption note: re-inserting isn't
                // supported without a known target socket. Log so it's
                // obvious during testing rather than silently wrong.
                Debug.LogWarning($"[SaveManager] '{entry.partId}' was saved as installed but starts removed in this scene — can't auto re-insert without a socket reference.");
            }
            else if (!entry.isInstalled && currentlyInstalled)
            {
                part.Pickup();
            }
        }
    }

    private void RestoreCables(GameSaveData data)
    {
        var allCables = FindObjectsOfType<CableConnector>(includeInactive: true);
        var byName = new Dictionary<string, CableConnector>();
        foreach (var cable in allCables)
        {
            if (!byName.ContainsKey(cable.name))
                byName.Add(cable.name, cable);
        }

        foreach (var entry in data.cables)
        {
            if (!byName.TryGetValue(entry.cableName, out var cable)) continue;

            // ASSUMPTION: isPlugged is a plain public field. This does not
            // reposition the connector transform — see class doc comment.
            cable.isPlugged = entry.isPlugged;
        }
    }

    //private void RestoreRamLatches(GameSaveData data)
    //{
    //    var allLatches = FindObjectsOfType<RAMLatch>(includeInactive: true);
    //    var byName = new Dictionary<string, RAMLatch>();
    //    foreach (var latch in allLatches)
    //    {
    //        if (!byName.ContainsKey(latch.name))
    //            byName.Add(latch.name, latch);
    //    }

    //    foreach (var entry in data.ramLatches)
    //    {
    //        if (!byName.TryGetValue(entry.latchName, out var latch)) continue;

    //        // ASSUMPTION: RAMLatch has an Open() method mirroring Close().
    //        if (entry.isOpen) latch.Open();
    //        else latch.Close();
    //    }
    //}

    private void RestoreUsbDrives(GameSaveData data)
    {
        var byName = new Dictionary<string, USBDriveItem>();
        foreach (var drive in USBDriveItem.AllDrives)
        {
            if (drive != null && !byName.ContainsKey(drive.name))
                byName.Add(drive.name, drive);
        }

        foreach (var entry in data.usbDrives)
        {
            if (!byName.TryGetValue(entry.driveName, out var drive)) continue;

            if (entry.isBootable)
            {
                ISOImageData image = FindIsoByName(entry.writtenImageName);
                drive.MarkAsBootable(image);
            }
            else
            {
                drive.ClearBootableState();
            }

            // Route through USBPort.PlugIn/Unplug rather than setting the
            // flag directly, so the drive's transform actually snaps back
            // into the port visually on load.
            USBPort targetPort = FindPortHolding(drive) ?? FindFirstEmptyPort();
            if (entry.isInsertedInPort && targetPort != null)
                targetPort.PlugIn(drive);
            else if (!entry.isInsertedInPort && targetPort != null && targetPort.CurrentDrive == drive)
                targetPort.Unplug();
        }
    }

    private ISOImageData FindIsoByName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName) || knownIsoImages == null) return null;
        foreach (var iso in knownIsoImages)
        {
            if (iso != null && iso.displayName == displayName) return iso;
        }
        return null;
    }

    private USBPort FindPortHolding(USBDriveItem drive)
    {
        foreach (var port in USBPort.AllPorts)
        {
            if (port != null && port.CurrentDrive == drive) return port;
        }
        return null;
    }

    private USBPort FindFirstEmptyPort()
    {
        foreach (var port in USBPort.AllPorts)
        {
            if (port != null && port.CurrentDrive == null) return port;
        }
        return null;
    }

    private void RestoreOSInstall(GameSaveData data)
    {
        if (OSInstallManager.Instance == null) return;
        if (data.osInstall.stageIndex >= 0)
            OSInstallManager.Instance.RestoreToStage(data.osInstall.stageIndex);
    }
}