using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Makes "tap/click anywhere to advance" reliable on both mouse AND touch.
///
/// Attach to a full-screen, invisible UI Image (Raycast Target ON) that is a
/// CHILD of dialogPanel and the LAST sibling in it (so it renders on top of
/// the text box and actually receives the tap).
///
/// Why this instead of polling Input.GetMouseButtonDown / touches directly:
/// this project has repeatedly hit bugs where raw Input polling silently
/// breaks on mobile (MouseLook.cs disables Input.simulateMouseWithTouches;
/// see PointerInput.cs for the full story), and even PointerInput-based
/// polling can race with other systems (DragController, TouchLookField)
/// reading the same physical touch in the same frame. Going through the UI
/// EventSystem instead - the same approach VirtualJoystick and TouchLookField
/// already use successfully - sidesteps all of that: this Image sits on top
/// of everything while the dialog is open, so it unambiguously owns the tap.
/// </summary>
public class DialogTapAdvance : MonoBehaviour, IPointerDownHandler
{
    public void OnPointerDown(PointerEventData eventData)
    {
        if (DialogManager.Instance != null)
            DialogManager.Instance.RequestAdvance();
    }
}