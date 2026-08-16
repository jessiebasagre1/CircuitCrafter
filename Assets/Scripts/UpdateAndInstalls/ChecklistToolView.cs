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

    private void Start()
    {
        if (manager == null)
        {
            Debug.LogWarning("[ChecklistToolView] No ChecklistToolManager assigned.");
            return;
        }

        manager.OnItemProgress += HandleItemProgress;
        manager.OnItemInstallComplete += HandleItemComplete;
        manager.OnAllItemsComplete += HandleAllComplete;

        Populate();

        if (allCompleteBanner != null)
            allCompleteBanner.SetActive(manager.AllItemsComplete);
    }

    private void OnDisable()
    {
        if (manager == null) return;
        manager.OnItemProgress -= HandleItemProgress;
        manager.OnItemInstallComplete -= HandleItemComplete;
        manager.OnAllItemsComplete -= HandleAllComplete;
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
