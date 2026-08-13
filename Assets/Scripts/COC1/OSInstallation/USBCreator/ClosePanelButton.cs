using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to any panel's CLOSE button (starting with USBCreatorPanel's). Hides the
/// panel it's told to close and, optionally, re-shows the desktop underneath.
/// Reusable for other panels later -- just drop it on their CLOSE button too.
/// </summary>
[RequireComponent(typeof(Button))]
public class ClosePanelButton : MonoBehaviour
{
    [Header("What CLOSE hides")]
    [Tooltip("The panel this CLOSE button belongs to, e.g. USBCreatorPanel.")]
    [SerializeField] private GameObject panelToClose;

    [Header("Desktop reference")]
    [Tooltip("Re-activated when the panel closes. Leave empty if there's nothing to return to.")]
    [SerializeField] private GameObject desktopPanel;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(Close);
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(Close);
    }

    public void Close()
    {
        if (panelToClose != null) panelToClose.SetActive(false);
        if (desktopPanel != null) desktopPanel.SetActive(true);
    }
}