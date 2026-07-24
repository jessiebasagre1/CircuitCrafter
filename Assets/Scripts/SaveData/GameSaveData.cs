using System;
using System.Collections.Generic;

/// <summary>
/// Everything captured in a save file. JsonUtility can't serialize
/// Dictionary directly, so each category uses a flat list of small
/// [Serializable] entry structs instead, keyed by name/id.
/// </summary>
[Serializable]
public class GameSaveData
{
    public List<PartEntry> parts = new List<PartEntry>();
    public List<CableEntry> cables = new List<CableEntry>();
    public List<RamLatchEntry> ramLatches = new List<RamLatchEntry>();
    public List<UsbDriveEntry> usbDrives = new List<UsbDriveEntry>();
    public OSInstallEntry osInstall = new OSInstallEntry();

    /// <summary>When this save was written — useful for a "Continue" menu
    /// showing a timestamp, and for debugging which save is stale.</summary>
    public string savedAtUtc;
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
    public string cableName;    // GameObject name — CableConnector doesn't expose its own id field currently
    public bool isPlugged;
}

[Serializable]
public class RamLatchEntry
{
    public string latchName;    // GameObject name
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