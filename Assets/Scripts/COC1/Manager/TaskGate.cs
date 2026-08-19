using UnityEngine;

/// <summary>
/// A reusable, embeddable "is this unlocked yet" condition -- not a
/// MonoBehaviour, just a plain serializable field you drop directly onto
/// whatever needs to stay locked (DesktopIconButton, PowerButtonInput, or
/// anything else). Shows up in the Inspector as its own foldout, same as
/// ServiceReportPanel.RequiredTask[] does.
///
/// Checked live against TaskManager/OSInstallManager every time -- nothing
/// here is cached or persisted, so it's automatically correct after a save
/// reload with no extra code, same philosophy as ChecklistToolManager.AllItemsComplete.
/// </summary>
[System.Serializable]
public class TaskGate
{
    [Tooltip("TaskManager taskId(s) that must ALL be complete for this gate to unlock. Leave at size 0 if you only need Require OS Install Complete below.")]
    public string[] requiredTaskIds;

    [Tooltip("If checked, this gate also requires the OS install to have reached the Complete stage.")]
    public bool requireOSInstallComplete;

    [Tooltip("Optional -- shown if the player tries to click/press something this gate is blocking, e.g. \"Create a bootable USB drive first.\"")]
    public DialogSequence lockedMessage;

    public bool IsUnlocked()
    {
        if (requireOSInstallComplete)
        {
            if (OSInstallManager.Instance == null) return false;
            if (OSInstallManager.Instance.CurrentStage != OSInstallManager.InstallStage.Complete) return false;
        }

        if (requiredTaskIds != null && requiredTaskIds.Length > 0)
        {
            if (TaskManager.Instance == null) return false;

            foreach (var id in requiredTaskIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!TaskManager.Instance.IsTaskComplete(id)) return false;
            }
        }

        return true;
    }

    public void ShowLockedMessage()
    {
        if (lockedMessage != null && DialogManager.Instance != null)
            DialogManager.Instance.ShowDialog(lockedMessage);
    }
}