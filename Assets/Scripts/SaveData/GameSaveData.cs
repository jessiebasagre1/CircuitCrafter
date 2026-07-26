using System;
using System.Collections.Generic;

/// <summary>
/// Everything captured in a single save file. JsonUtility can't serialize
/// Dictionary directly, so each category uses a flat list of small
/// [Serializable] entry structs instead, keyed by name/id.
/// </summary>
[Serializable]
public class GameSaveData
{
    /// <summary>Which mode's scene-state this save currently holds
    /// (parts/cables/etc below belong to this mode's scene).</summary>
    public string gameMode; // e.g. "COC1"

    /// <summary>Every mode the player has finished — drives which mode
    /// buttons are unlocked on the main menu.</summary>
    public List<string> completedModes = new List<string>();

    public List<PartEntry> parts = new List<PartEntry>();
    public List<CableEntry> cables = new List<CableEntry>();
    public List<RamLatchEntry> ramLatches = new List<RamLatchEntry>();
    public List<UsbDriveEntry> usbDrives = new List<UsbDriveEntry>();
    public OSInstallEntry osInstall = new OSInstallEntry();

    // Plug-in system — any future MonoBehaviour implementing ISaveable gets
    // captured/restored here automatically, without ever editing SaveManager.
    public List<GenericEntry> genericEntries = new List<GenericEntry>();

    /// <summary>When this save was written — useful for debugging which
    /// save is stale.</summary>
    public string savedAtUtc;

    /// <summary>Short human-readable progress summary (e.g. "Installing OS").</summary>
    public string progressLabel;
}

[Serializable]
public class PartEntry
{
    public string partId;       // PCPartItem.partId
    public bool isInstalled;    // true = seated in its HomeSocket, false = removed/hotbar
}

[Serializable]
public class CableEntry
{
    public string cableName;    // GameObject name
    public bool isPlugged;
    public string socketName;   // PortSocket.name this cable was plugged into, empty if unplugged
}

[Serializable]
public class RamLatchEntry
{
    public string latchName;    // GameObject name — the RAMLock component's GameObject
    public bool isOpen;
}

[Serializable]
public class UsbDriveEntry
{
    public string driveName; // GameObject name — key for matching on load
    public bool isBootable;
    public string writtenImageName; // ISOImageData.displayName, empty if none
    public bool isInsertedInPort;
}

[Serializable]
public class OSInstallEntry
{
    public int stageIndex; // index into OSInstallManager's stages list, -1 if not started
}

[Serializable]
public class GenericEntry
{
    public string key;  // matches ISaveable.SaveKey
    public string json; // whatever that ISaveable serialized itself into
}