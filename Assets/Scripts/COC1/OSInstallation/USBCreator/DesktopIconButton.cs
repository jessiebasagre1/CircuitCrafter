using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to a desktop icon (an Image/Button representing e.g. the Rufus shortcut).
/// Clicking it opens the target panel and, optionally, hides the desktop itself so
/// the two don't overlap. Reusable for any future desktop icon (other tools, files, etc),
/// not just Rufus -- just point it at a different targetPanel.
/// </summary>
[RequireComponent(typeof(Button))]
public class DesktopIconButton : MonoBehaviour
{
    [Header("What this icon opens")]
    [Tooltip("The panel to activate when this icon is clicked, e.g. USBCreatorPanel.")]
    [SerializeField] private GameObject targetPanel;

    [Header("Desktop reference")]
    [Tooltip("The desktop panel this icon lives on. Hidden while targetPanel is open so they don't overlap. Leave empty if you don't want the desktop to hide.")]
    [SerializeField] private GameObject desktopPanel;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OpenTarget);
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(OpenTarget);
    }

    public void OpenTarget()
    {
        if (targetPanel != null) targetPanel.SetActive(true);
        if (desktopPanel != null) desktopPanel.SetActive(false);
    }
}