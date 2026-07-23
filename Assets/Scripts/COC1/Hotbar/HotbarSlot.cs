using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A single hotbar slot. Purely a UI-side reference holder — it never touches
/// the part's 3D transform directly, it just shows/hides the icon and starts
/// a drag when clicked. Needs an Image with Raycast Target ON so it can
/// receive both pointer-down and drop raycasts.
/// </summary>
[RequireComponent(typeof(Image))]
public class HotbarSlot : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    [Tooltip("Child Image used to display the stored part's icon.")]
    public Image iconImage;

    public PCPartItem StoredItem { get; private set; }
    public bool IsEmpty => StoredItem == null;

    // Captured at press time, since BeginDragFromHotbar clears StoredItem
    // immediately — OnPointerClick (which fires later, on release) needs its
    // own reference rather than re-reading StoredItem at that point.
    PCPartItem pressedItem;

    void Awake()
    {
        if (iconImage == null)
            Debug.LogError($"{name}: Icon Image is not assigned on this HotbarSlot.", this);
        else
            iconImage.enabled = false;
    }

    public void SetItem(PCPartItem item)
    {
        StoredItem = item;
        if (iconImage == null) return; // already logged in Awake; don't corrupt state further
        iconImage.sprite = item.icon;
        iconImage.enabled = true;
    }

    public void Clear()
    {
        StoredItem = null;
        if (iconImage == null) return;
        iconImage.enabled = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsEmpty) return;

        pressedItem = StoredItem;

        DragController.Instance.BeginDragFromHotbar(this);
    }

    /// <summary>
    /// Fires only if release happens back over this same slot — i.e. a real
    /// click, not a drag that ended somewhere else. Shows the description
    /// panel; the panel now stays open until the player closes it manually,
    /// it's no longer tied to the drag's hold duration.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (pressedItem == null) return;
        PartDescriptionPanel.Instance?.Show(pressedItem.icon, pressedItem.description);
    }
}