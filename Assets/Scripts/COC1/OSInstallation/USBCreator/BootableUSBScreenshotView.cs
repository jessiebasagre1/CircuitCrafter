using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Drop-in replacement for BootableUSBCreatorView. Same three interactions
/// (ISO dropdown, drive dropdown, Create button), same events, same
/// BootableUSBCreator underneath -- the only thing that changes is that a
/// Rufus screenshot sits behind everything and the dropdowns/button/progress
/// bar are made visually transparent so they read as "part of the screenshot"
/// while remaining fully functional Unity UI.
///
/// Everything else visible in the screenshot (partition scheme, target
/// system, volume label, file system, etc.) is NOT wired to anything --
/// your BootableUSBCreator doesn't model those, so they're just static
/// decoration that makes the screen look authentic. Don't add hotspots for
/// them; there is nothing for them to do.
/// </summary>
public class BootableUSBScreenshotView : MonoBehaviour
{
    [Header("Target (unchanged from BootableUSBCreatorView)")]
    [SerializeField] private BootableUSBCreator creator;

    [Header("Reference screenshot")]
    [SerializeField] private Image rufusScreenshot;
    [SerializeField] private RectTransform screenshotRoot;

    [Header("Functional controls, skinned transparent, placed via ScreenshotUIPlacement")]
    [Tooltip("Sits over Rufus's 'Boot selection' dropdown area.")]
    [SerializeField] private TMP_Dropdown isoDropdown;
    [Tooltip("Sits over Rufus's 'Device' dropdown area.")]
    [SerializeField] private TMP_Dropdown driveDropdown;
    [Tooltip("Sits over Rufus's START button.")]
    [SerializeField] private Button createButton;

    [Header("Feedback (progress bar stays visible -- screenshot only shows the idle state)")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text statusText;

    [Header("Optional: highlight ring for the currently-actionable control")]
    [SerializeField] private GameObject highlightRingPrefab;

    [Header("Visual styling")]
    [Tooltip("Background color for dropdowns/button so they opaquely cover the screenshot's static drawn text underneath, while still showing the live selected value on top. Match this to Rufus's actual field color (near-white) for the best blend.")]
    [SerializeField] private Color controlBackgroundColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("When ON, dropdowns/button get the cover-style background above. Turn OFF to use Unity's default control styling instead (useful for quick testing).")]
    [SerializeField] private bool coverScreenshotText = true;

    [Header("Placeholder text shown before a real choice is made")]
    [SerializeField] private string isoPlaceholderLabel = "SELECT AN ISO IMAGE";
    [SerializeField] private string drivePlaceholderLabel = "SELECT A FLASHDRIVE";

    private List<ISOImageData> isoOptions;
    private List<USBDriveItem> driveOptions;
    private GameObject _activeHighlight;

    private void OnEnable()
    {
        PopulateDropdowns();
        if (coverScreenshotText) StyleControlsToCoverScreenshot();

        isoDropdown.onValueChanged.AddListener(OnIsoDropdownChanged);
        driveDropdown.onValueChanged.AddListener(OnDriveDropdownChanged);
        createButton.onClick.AddListener(OnCreateClicked);

        creator.OnWriteProgress += HandleProgress;
        creator.OnWriteComplete += HandleComplete;

        progressBar.gameObject.SetActive(false); // hidden until Create is clicked, matches Rufus's idle screenshot
        statusText.text = "Select a device and boot image.";

        ShowHighlightOn(driveDropdown.GetComponent<RectTransform>());
    }

    private void OnDisable()
    {
        isoDropdown.onValueChanged.RemoveListener(OnIsoDropdownChanged);
        driveDropdown.onValueChanged.RemoveListener(OnDriveDropdownChanged);
        createButton.onClick.RemoveListener(OnCreateClicked);

        creator.OnWriteProgress -= HandleProgress;
        creator.OnWriteComplete -= HandleComplete;

        ClearHighlight();
    }

    private void PopulateDropdowns()
    {
        // Index 0 in each dropdown is a placeholder (no real ISO/drive behind it), so
        // that picking an actual option later always counts as a genuine change --
        // otherwise, with only one real option, re-selecting it never fires
        // onValueChanged and the highlight ring has nothing to react to.
        isoOptions = new List<ISOImageData> { null };
        isoOptions.AddRange(creator.AvailableImages);

        driveOptions = new List<USBDriveItem> { null };
        driveOptions.AddRange(creator.AvailableDrives);

        isoDropdown.ClearOptions();
        var isoLabels = new List<string> { isoPlaceholderLabel };
        foreach (var iso in creator.AvailableImages)
            isoLabels.Add(iso != null ? $"{iso.displayName} ({iso.fileSizeLabel})" : "-");
        isoDropdown.AddOptions(isoLabels);
        isoDropdown.SetValueWithoutNotify(0);
        isoDropdown.RefreshShownValue();

        driveDropdown.ClearOptions();
        var driveLabels = new List<string> { drivePlaceholderLabel };
        foreach (var drive in creator.AvailableDrives)
            driveLabels.Add(drive != null ? drive.name : "-");
        driveDropdown.AddOptions(driveLabels);
        driveDropdown.SetValueWithoutNotify(0);
        driveDropdown.RefreshShownValue();
    }

    /// <summary>
    /// Gives each control an opaque background matching Rufus's field color, so it fully
    /// covers the static drawn box/text underneath it in the screenshot. The caption text
    /// (the live selected value) stays fully visible on top -- this is the opposite of
    /// making things invisible: it's making them opaque so only the REAL, current value
    /// is what the learner ever sees, never the screenshot's frozen placeholder text.
    /// </summary>
    private void StyleControlsToCoverScreenshot()
    {
        SetGraphicOpaqueCover(isoDropdown.GetComponent<Image>());
        SetGraphicOpaqueCover(driveDropdown.GetComponent<Image>());
        SetGraphicOpaqueCover(createButton.GetComponent<Image>());

        // Caption text stays visible and fully opaque -- this is the live, real value.
        if (isoDropdown.captionText != null) isoDropdown.captionText.color = Color.black;
        if (driveDropdown.captionText != null) driveDropdown.captionText.color = Color.black;
    }

    private void SetGraphicOpaqueCover(Graphic graphic)
    {
        if (graphic == null) return;
        graphic.color = controlBackgroundColor;
        graphic.raycastTarget = true;
    }

    private void OnIsoDropdownChanged(int index)
    {
        if (index <= 0 || index >= isoOptions.Count) return; // 0 = placeholder, ignore
        creator.SelectImage(isoOptions[index]);
        ShowHighlightOn(createButton.GetComponent<RectTransform>());
    }

    private void OnDriveDropdownChanged(int index)
    {
        if (index <= 0 || index >= driveOptions.Count) return; // 0 = placeholder, ignore
        creator.SelectDrive(driveOptions[index]);
        ShowHighlightOn(isoDropdown.GetComponent<RectTransform>());
    }

    private void OnCreateClicked()
    {
        if (creator.IsWriting) return;

        bool started = creator.BeginCreate();
        if (!started) return; // blocked, creator already showed its warning dialog

        progressBar.gameObject.SetActive(true);
        progressBar.value = 0f;
        statusText.text = "Writing image...";
        SetControlsInteractable(false);
        ClearHighlight();
    }

    private void HandleProgress(float t)
    {
        progressBar.value = t;
        statusText.text = $"Writing image... {(t * 100f):0}%";
    }

    private void HandleComplete(USBDriveItem drive)
    {
        statusText.text = $"Done - {drive.name} is now bootable.";
        SetControlsInteractable(true);
    }

    private void SetControlsInteractable(bool interactable)
    {
        isoDropdown.interactable = interactable;
        driveDropdown.interactable = interactable;
        createButton.interactable = interactable;
    }

    private void ShowHighlightOn(RectTransform target)
    {
        ClearHighlight();
        if (highlightRingPrefab == null || target == null) return;

        _activeHighlight = Instantiate(highlightRingPrefab, target);
        var rt = _activeHighlight.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    private void ClearHighlight()
    {
        if (_activeHighlight != null) Destroy(_activeHighlight);
        _activeHighlight = null;
    }
}