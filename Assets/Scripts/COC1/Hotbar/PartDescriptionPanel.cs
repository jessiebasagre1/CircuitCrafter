using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows a centered panel (icon on top, description below) whenever a part
/// is pressed in the hotbar. Call Show() to display it, Hide() to dismiss it.
/// Uses TextMeshProUGUI for the description text.
/// </summary>
public class PartDescriptionPanel : MonoBehaviour
{
    public static PartDescriptionPanel Instance { get; private set; }

    [Header("References")]
    [Tooltip("The whole panel root, toggled active/inactive.")]
    public GameObject panelRoot;
    public Image iconImage;
    public TextMeshProUGUI descriptionText;

    void Awake()
    {
        Instance = this;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Show(Sprite icon, string description)
    {
        if (panelRoot == null) return;

        // GUIDANCE: a part's description often says what it is and where it goes - identification
        // is part of what an assessment marks. Story Mode reference only (GuidanceKind.ReferenceGuides).
        if (!CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.ReferenceGuides)) return;

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }
        if (descriptionText != null)
            descriptionText.text = description;

        panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }
}