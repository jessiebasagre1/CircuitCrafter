using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI-facing counterpart to ChecklistToolManager. Spawns one
/// ChecklistItemRowView per item, wires each row's Install button back to
/// the manager, and keeps rows in sync with the manager's progress/complete
/// events. Same manager/view split as OSInstallManager/OSInstallView.
///
/// Reused 3x in the scene: once each for Device Manager, Software Center and
/// Windows Update -- put this component (plus its own rowsContent/rowPrefab)
/// on each of the 3 panels and point `manager` at that panel's own
/// ChecklistToolManager. Not a singleton, same as PartitionSelectView.
/// </summary>
public class ChecklistToolView : MonoBehaviour
{
    [SerializeField] private ChecklistToolManager manager;

    [Header("List")]
    [SerializeField] private Transform rowsContent;
    [SerializeField] private ChecklistItemRowView rowPrefab;

    [Header("Optional")]
    [Tooltip("Shown/enabled once every item in this tool is installed.")]
    [SerializeField] private GameObject allCompleteBanner;

    private readonly Dictionary<ChecklistItemData, ChecklistItemRowView> _rows =
        new Dictionary<ChecklistItemData, ChecklistItemRowView>();

    private bool _populated;

    // WHY THE SUBSCRIPTION LIVES HERE AND NOT IN Start().
    //
    // This view sits on a window panel that is switched OFF when the window is closed and
    // switched back ON when it is opened again. OnDisable has always unsubscribed on every close,
    // but Start() runs only ONCE in a component's whole life - so the second time the window opened,
    // nothing subscribed it again. The install still ran and still finished (the manager is on an
    // object that is always active), but the rows never heard about it: no green bar, no
    // "Installing... 40%", no "Installed".
    //
    // Opening the windows in order hid the bug, because then each window is opened exactly once.
    // Subscribing in OnEnable pairs it with OnDisable, so every open is listening.
    private void OnEnable()
    {
        Subscribe();

        // The first open is handled by Start(), which builds the rows. Every LATER open has to
        // catch up with whatever happened while the window was closed.
        if (_populated) RefreshRows();
    }

    private void Start()
    {
        if (manager == null)
        {
            Debug.LogWarning("[ChecklistToolView] No ChecklistToolManager assigned.");
            return;
        }

        Subscribe();   // does nothing new if OnEnable already did

        Populate();
        _populated = true;

        if (allCompleteBanner != null)
            allCompleteBanner.SetActive(manager.AllItemsComplete);
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (manager == null) return;

        // Unsubscribe first, so calling this twice can never double up the handlers - OnEnable and
        // Start() both call it on the very first open.
        Unsubscribe();
        manager.OnItemProgress += HandleItemProgress;
        manager.OnItemInstallComplete += HandleItemComplete;
        manager.OnAllItemsComplete += HandleAllComplete;
    }

    private void Unsubscribe()
    {
        if (manager == null) return;
        manager.OnItemProgress -= HandleItemProgress;
        manager.OnItemInstallComplete -= HandleItemComplete;
        manager.OnAllItemsComplete -= HandleAllComplete;
    }

    /// <summary>Brings the rows back in line with the manager after the window has been closed
    /// and reopened. An install that finished while the window was shut never reached the rows,
    /// so without this the row would still say "Not installed" for something already done.</summary>
    private void RefreshRows()
    {
        foreach (var pair in _rows)
        {
            ChecklistItemRowView row = pair.Value;
            if (row == null) continue;

            if (manager.IsItemComplete(pair.Key))
                row.SetComplete();
            else if (manager.IsInstalling && manager.CurrentItem == pair.Key)
                continue;   // still going: the next progress event repaints this row
            else
                row.SetIdle();
        }

        if (allCompleteBanner != null)
            allCompleteBanner.SetActive(manager.AllItemsComplete);
    }

    private void Populate()
    {
        foreach (Transform child in rowsContent)
            Destroy(child.gameObject);
        _rows.Clear();

        foreach (var item in manager.Items)
        {
            if (item == null) continue;
            var row = Instantiate(rowPrefab, rowsContent);
            row.Setup(item, manager.IsItemComplete(item), HandleInstallClicked);
            _rows[item] = row;
        }
    }

    private void HandleInstallClicked(ChecklistItemData item)
    {
        manager.BeginInstall(item);
    }

    private void HandleItemProgress(ChecklistItemData item, float t)
    {
        if (_rows.TryGetValue(item, out var row)) row.SetProgress(t);
    }

    private void HandleItemComplete(ChecklistItemData item)
    {
        if (_rows.TryGetValue(item, out var row)) row.SetComplete();
    }

    private void HandleAllComplete()
    {
        if (allCompleteBanner != null) allCompleteBanner.SetActive(true);
    }
}