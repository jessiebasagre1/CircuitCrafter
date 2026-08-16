using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One row representing a single ChecklistItemData (one driver category, one
/// app, one patch). Put this on a prefab and assign it to
/// ChecklistToolView.rowPrefab -- same idea as PartitionRowView/rowPrefab on
/// PartitionSelectView.
///
/// Suggested row layout: [icon] [name + description] [progress bar] [status
/// text] [Install button]. All fields except nameText are optional -- leave
/// unassigned to simplify the row.
/// </summary>
public class ChecklistItemRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button installButton;
    [SerializeField] private GameObject completeCheckmark;

    public ChecklistItemData Data { get; private set; }

    /// <summary>Call once when the row is spawned. onInstallClicked is invoked
    /// with this row's data when the Install button is pressed -- the caller
    /// (ChecklistToolView) is what actually calls ChecklistToolManager.BeginInstall.</summary>
    public void Setup(ChecklistItemData data, bool isComplete, Action<ChecklistItemData> onInstallClicked)
    {
        Data = data;

        if (nameText != null) nameText.text = data.displayName;
        if (descriptionText != null) descriptionText.text = data.description;

        if (installButton != null)
        {
            installButton.onClick.RemoveAllListeners();
            installButton.onClick.AddListener(() => onInstallClicked?.Invoke(data));
        }

        if (isComplete) SetComplete();
        else SetIdle();
    }

    public void SetIdle()
    {
        if (progressBar != null) progressBar.value = 0f;
        if (statusText != null) statusText.text = "Not installed";
        if (installButton != null) installButton.interactable = true;
        if (completeCheckmark != null) completeCheckmark.SetActive(false);
    }

    public void SetProgress(float t)
    {
        if (progressBar != null) progressBar.value = t;
        if (statusText != null) statusText.text = $"Installing... {Mathf.RoundToInt(t * 100f)}%";
        if (installButton != null) installButton.interactable = false;
    }

    public void SetComplete()
    {
        if (progressBar != null) progressBar.value = 1f;
        if (statusText != null) statusText.text = "Installed";
        if (installButton != null) installButton.interactable = false;
        if (completeCheckmark != null) completeCheckmark.SetActive(true);
    }
}
