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

    [Tooltip("Hide this icon completely until the gate unlocks, instead of showing it greyed out. " +
             "Right for a program that is not installed yet - an uninstalled app has no desktop " +
             "shortcut at all. REQUIRES a DesktopIconVisibility component on the desktop panel " +
             "above this icon; see the note on IsUnlocked below for why this component cannot do " +
             "it alone.")]
    [SerializeField] private bool hideWhenLocked;

    private Button _button;

    /// <summary>
    /// Whether this icon should vanish rather than grey out while locked. Read by
    /// DesktopIconVisibility on the desktop panel.
    /// </summary>
    public bool HideWhenLocked => hideWhenLocked;

    /// <summary>
    /// This icon's gate condition, exposed so the desktop panel can decide whether to show it.
    ///
    /// WHY THE PANEL HAS TO DO IT AND NOT THIS COMPONENT.
    /// A GameObject that switches ITSELF off can never switch itself back on: nothing on an
    /// inactive object runs, so its OnEnable never fires again and Refresh() is never called.
    /// Something that stays active has to own the decision, and the desktop panel is the
    /// natural owner - it is enabled every time the learner comes back to the desktop.
    /// </summary>
    public bool IsUnlocked => gate.IsUnlocked();

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

        // Deliberately does NOT touch this object's own active state, even when hideWhenLocked
        // is set. Hiding itself would work exactly once and then strand the icon off forever.
        // Warn loudly instead if the component that CAN do it is missing, because a shortcut
        // that silently never comes back is a miserable thing to chase.
        if (hideWhenLocked && GetComponentInParent<DesktopIconVisibility>() == null)
        {
            Debug.LogWarning($"[DesktopIconButton] '{name}' has Hide When Locked ticked, but there " +
                             "is no DesktopIconVisibility on any parent. Add one to the desktop " +
                             "panel, or this icon will stay visible while locked.", this);
        }
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