using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// UI front-end for BootableUSBCreator. Populates two TMP_Dropdowns from
/// its availableImages/availableDrives arrays, wires the Create button, and
/// updates a progress bar + status text from its events.
///
/// Attach this to the Canvas panel that holds the USB creator UI. Assign all
/// fields in the Inspector, then just leave it running — no other wiring
/// needed beyond what's listed here.
/// </summary>
public class BootableUSBCreatorView : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private BootableUSBCreator creator;

    [Header("UI References")]
    [SerializeField] private TMP_Dropdown isoDropdown;
    [SerializeField] private TMP_Dropdown driveDropdown;
    [SerializeField] private Button createButton;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text statusText;

    private List<ISOImageData> isoOptions;
    private List<USBDriveItem> driveOptions;

    private void OnEnable()
    {
        PopulateDropdowns();

        isoDropdown.onValueChanged.AddListener(OnIsoDropdownChanged);
        driveDropdown.onValueChanged.AddListener(OnDriveDropdownChanged);
        createButton.onClick.AddListener(OnCreateClicked);

        creator.OnWriteProgress += HandleProgress;
        creator.OnWriteComplete += HandleComplete;

        progressBar.value = 0f;
        statusText.text = "";

        // Fire once so the creator actually has a selection matching dropdown index 0,
        // rather than waiting for the player to touch the dropdown first.
        if (isoOptions.Count > 0) OnIsoDropdownChanged(0);
        if (driveOptions.Count > 0) OnDriveDropdownChanged(0);
    }

    private void OnDisable()
    {
        isoDropdown.onValueChanged.RemoveListener(OnIsoDropdownChanged);
        driveDropdown.onValueChanged.RemoveListener(OnDriveDropdownChanged);
        createButton.onClick.RemoveListener(OnCreateClicked);

        creator.OnWriteProgress -= HandleProgress;
        creator.OnWriteComplete -= HandleComplete;
    }

    private void PopulateDropdowns()
    {
        isoOptions = new List<ISOImageData>(creator.AvailableImages);
        driveOptions = new List<USBDriveItem>(creator.AvailableDrives);

        isoDropdown.ClearOptions();
        var isoLabels = new List<string>();
        foreach (var iso in isoOptions)
            isoLabels.Add(iso != null ? $"{iso.displayName} ({iso.fileSizeLabel})" : "—");
        isoDropdown.AddOptions(isoLabels);

        driveDropdown.ClearOptions();
        var driveLabels = new List<string>();
        foreach (var drive in driveOptions)
            driveLabels.Add(drive != null ? drive.name : "—");
        driveDropdown.AddOptions(driveLabels);
    }

    private void OnIsoDropdownChanged(int index)
    {
        if (index < 0 || index >= isoOptions.Count) return;
        creator.SelectImage(isoOptions[index]);
    }

    private void OnDriveDropdownChanged(int index)
    {
        if (index < 0 || index >= driveOptions.Count) return;
        creator.SelectDrive(driveOptions[index]);
    }

    private void OnCreateClicked()
    {
        if (creator.IsWriting) return;

        progressBar.value = 0f;
        statusText.text = "Writing image...";
        SetControlsInteractable(false);

        creator.BeginCreate();
    }

    private void HandleProgress(float t)
    {
        progressBar.value = t;
    }

    private void HandleComplete(USBDriveItem drive)
    {
        statusText.text = $"Done — {drive.name} is now bootable.";
        SetControlsInteractable(true);
    }

    private void SetControlsInteractable(bool interactable)
    {
        isoDropdown.interactable = interactable;
        driveDropdown.interactable = interactable;
        createButton.interactable = interactable;
    }
}