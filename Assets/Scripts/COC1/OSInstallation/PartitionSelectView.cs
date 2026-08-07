using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Interactive partition table for the PartitionSelect stage: radio-button
/// rows, New/Format/Delete acting on whichever row is selected, and a Next
/// button that only proceeds once a formatted primary partition is chosen —
/// teaches that you can't install onto raw unallocated space or an
/// unformatted partition.
///
/// Works off a runtime copy of OSInstallStageData.partitionOptions — New/
/// Format/Delete during play never touch the asset itself, so replaying the
/// stage (or a different playthrough) always starts from the same layout.
///
/// OSInstallView shows/hides this panel and calls Populate() when the
/// current stage is PartitionSelect; this view calls
/// OSInstallManager.Instance.SubmitUserChoice(partitionName) itself once
/// Next succeeds, so OSInstallView's generic continueButton/choiceDropdown
/// stay hidden for this stage.
/// </summary>
public class PartitionSelectView : MonoBehaviour
{
    [Header("List")]
    [SerializeField] private Transform rowsContent;
    [SerializeField] private PartitionRowView rowPrefab;
    [SerializeField] private ToggleGroup toggleGroup;

    [Header("Actions")]
    [SerializeField] private Button newButton;
    [SerializeField] private Button formatButton;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button nextButton;

    [Header("Feedback (optional)")]
    [SerializeField] private TMP_Text errorText;

    private readonly List<PartitionOption> partitions = new List<PartitionOption>();
    private readonly List<PartitionRowView> spawnedRows = new List<PartitionRowView>();
    private int selectedIndex = -1;

    private void Awake()
    {
        if (newButton != null) newButton.onClick.AddListener(OnNewClicked);
        if (formatButton != null) formatButton.onClick.AddListener(OnFormatClicked);
        if (deleteButton != null) deleteButton.onClick.AddListener(OnDeleteClicked);
        if (nextButton != null) nextButton.onClick.AddListener(OnNextClicked);
    }

    /// <summary>Call when entering the PartitionSelect stage — resets the
    /// table to the stage asset's starting layout.</summary>
    public void Populate(OSInstallStageData stage)
    {
        partitions.Clear();
        if (stage.partitionOptions != null)
        {
            foreach (var option in stage.partitionOptions)
            {
                partitions.Add(new PartitionOption
                {
                    partitionName = option.partitionName,
                    totalSizeGB = option.totalSizeGB,
                    freeSpaceGB = option.freeSpaceGB,
                    kind = option.kind,
                    isFormatted = false
                });
            }
        }

        selectedIndex = partitions.Count > 0 ? 0 : -1;
        SetError(null);
        RebuildRows();
    }

    private void RebuildRows()
    {
        foreach (var row in spawnedRows)
            if (row != null) Destroy(row.gameObject);
        spawnedRows.Clear();

        for (int i = 0; i < partitions.Count; i++)
        {
            int index = i; // capture for closure
            var row = Instantiate(rowPrefab, rowsContent);
            row.Setup(partitions[i], toggleGroup, startSelected: index == selectedIndex, onSelected: () => OnRowSelected(index));
            spawnedRows.Add(row);
        }

        RefreshButtonStates();
    }

    private void OnRowSelected(int index)
    {
        selectedIndex = index;
        SetError(null);
        RefreshButtonStates();
    }

    private void RefreshButtonStates()
    {
        bool hasSelection = selectedIndex >= 0 && selectedIndex < partitions.Count;
        var selected = hasSelection ? partitions[selectedIndex] : null;

        if (newButton != null)
            newButton.interactable = hasSelection && selected.kind == PartitionKind.Unallocated;

        if (formatButton != null)
            formatButton.interactable = hasSelection && selected.kind == PartitionKind.Primary && !selected.isFormatted;

        if (deleteButton != null)
            deleteButton.interactable = hasSelection && selected.kind == PartitionKind.Primary;
    }

    // ---------------------------------------------------------------
    // Actions
    // ---------------------------------------------------------------

    private void OnNewClicked()
    {
        if (selectedIndex < 0) return;
        var p = partitions[selectedIndex];
        if (p.kind != PartitionKind.Unallocated) return;

        p.kind = PartitionKind.Primary;
        p.partitionName = $"Drive 0 Partition {selectedIndex + 1}: New Volume";
        p.freeSpaceGB = p.totalSizeGB; // brand new partition, nothing written yet
        p.isFormatted = false;

        RebuildRows();
    }

    private void OnFormatClicked()
    {
        if (selectedIndex < 0) return;
        var p = partitions[selectedIndex];
        if (p.kind != PartitionKind.Primary) return;

        p.isFormatted = true;
        p.freeSpaceGB = p.totalSizeGB; // formatting clears it

        RebuildRows();
    }

    private void OnDeleteClicked()
    {
        if (selectedIndex < 0) return;
        var p = partitions[selectedIndex];
        if (p.kind != PartitionKind.Primary) return;

        p.kind = PartitionKind.Unallocated;
        p.partitionName = "Drive 0 Unallocated Space";
        p.freeSpaceGB = p.totalSizeGB;
        p.isFormatted = false;

        RebuildRows();
    }

    private void OnNextClicked()
    {
        if (selectedIndex < 0)
        {
            SetError("Select a partition to continue.");
            return;
        }

        var p = partitions[selectedIndex];
        if (p.kind != PartitionKind.Primary)
        {
            SetError("Create a partition here before continuing.");
            return;
        }
        if (!p.isFormatted)
        {
            SetError("Format this partition before continuing.");
            return;
        }

        SetError(null);
        OSInstallManager.Instance.SubmitUserChoice(p.partitionName);
    }

    private void SetError(string message)
    {
        if (errorText == null) return;
        errorText.text = message ?? "";
        errorText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }
}