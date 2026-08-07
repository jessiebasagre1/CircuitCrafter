using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// One row in the partition table — a radio-style Toggle (assign it to the
/// panel's ToggleGroup for exclusive selection) plus labels for name, size,
/// free space, and type. Put this on a prefab and assign it to
/// PartitionSelectView.rowPrefab.
/// </summary>
public class PartitionRowView : MonoBehaviour
{
    [SerializeField] private Toggle toggle;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text totalSizeText;
    [SerializeField] private TMP_Text freeSpaceText;
    [SerializeField] private TMP_Text typeText;

    public void Setup(PartitionOption data, ToggleGroup group, bool startSelected, Action onSelected)
    {
        nameText.text = data.partitionName;
        totalSizeText.text = $"{data.totalSizeGB:0.0} GB";
        freeSpaceText.text = $"{data.freeSpaceGB:0.0} GB";
        typeText.text = BuildTypeLabel(data);

        toggle.group = group;
        toggle.SetIsOnWithoutNotify(startSelected);

        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(isOn =>
        {
            if (isOn) onSelected?.Invoke();
        });
    }

    private string BuildTypeLabel(PartitionOption data)
    {
        switch (data.kind)
        {
            case PartitionKind.Unallocated: return "—";
            case PartitionKind.Extended: return "Extended";
            default: return data.isFormatted ? "Primary (formatted)" : "Primary (not formatted)";
        }
    }
}