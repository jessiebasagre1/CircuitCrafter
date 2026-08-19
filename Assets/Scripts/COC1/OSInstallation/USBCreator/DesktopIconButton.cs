using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to a desktop icon (an Image/Button representing e.g. the Rufus shortcut,
/// or one of the post-install desktop apps). Clicking it opens the target panel and,
/// optionally, hides the desktop itself so the two don't overlap. Reusable for any
/// desktop icon -- just point it at a different targetPanel.
///
/// Optionally gated: assign `gate` to require prior tasks (and/or OS install) to be
/// complete before this icon can be clicked. Ungated icons (gate left at its default,
/// empty state) behave exactly as before -- always unlocked.
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

    [Header("Locking (optional)")]
    [Tooltip("Leave every sub-field empty/unchecked to keep this icon always unlocked.")]
    [SerializeField] private TaskGate gate = new TaskGate();

    [Tooltip("Optional -- e.g. a padlock icon shown over this button while gate is locked.")]
    [SerializeField] private GameObject lockedOverlay;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OpenTarget);
    }

    private void OnEnable()
    {
        // Re-checks every time this icon (or its parent desktop) becomes
        // active again -- covers both re-opening the desktop and coming
        // back from another station, with no per-frame polling needed.
        Refresh();
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(OpenTarget);
    }

    /// <summary>Re-checks the gate and updates interactable/lockedOverlay.
    /// Safe to call any time.</summary>
    public void Refresh()
    {
        bool unlocked = gate.IsUnlocked();
        if (_button != null) _button.interactable = unlocked;
        if (lockedOverlay != null) lockedOverlay.SetActive(!unlocked);
    }

    public void OpenTarget()
    {
        if (!gate.IsUnlocked())
        {
            gate.ShowLockedMessage();
            return; // safety check -- the button should already be non-interactable
        }

        if (targetPanel != null) targetPanel.SetActive(true);
        if (desktopPanel != null) desktopPanel.SetActive(false);
    }
}