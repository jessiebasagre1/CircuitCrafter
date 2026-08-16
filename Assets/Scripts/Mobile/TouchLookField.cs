using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Invisible full-screen UI panel that turns a finger drag anywhere on
/// screen into a look-delta, for camera look on touch devices. Coexists
/// with PC mouse look - MouseLook reads mouse axis AND this delta each
/// frame, and only one will ever be non-zero at a time in practice (mouse
/// delta is ~0 on a touch device with no mouse connected, and vice versa).
///
/// Setup:
///   1. Create a full-screen UI Image, set Color alpha to 0 (invisible),
///      Raycast Target ON.
///   2. IMPORTANT: place it BEHIND (earlier sibling than) the virtual
///      joystick and any interact/plug buttons in the hierarchy, or set a
///      transparent "hole" for the joystick area, so dragging the joystick
///      doesn't also rotate the camera. Simplest fix: put the joystick and
///      buttons in a separate Canvas with a higher sort order.
///   3. Add this component to that Image.
///
/// Sensitivity is separate from mouseSensitivity in MouseLook since touch
/// drag pixel deltas and raw mouse-axis deltas are different units.
/// </summary>
public class TouchLookField : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static TouchLookField Instance { get; private set; }

    [Tooltip("Degrees of rotation per pixel of drag movement.")]
    public float touchLookSensitivity = 0.15f;

    /// <summary>Look delta accumulated this frame (X = yaw, Y = pitch), consumed and reset by MouseLook.</summary>
    public Vector2 ConsumeDelta()
    {
        Vector2 result = pendingDelta;
        pendingDelta = Vector2.zero;
        return result;
    }

    private Vector2 pendingDelta;
    private Vector2 lastPointerPos;
    private bool dragging;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // Don't start a look-drag while a tutorial dialog is open - the same
        // finger tap used to skip the dialog shouldn't also rotate the camera
        // underneath it.
        if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
        {
            dragging = false;
            return;
        }

        // Safety net independent of Canvas sibling/raycast order: if this touch
        // started over the joystick's own area, it belongs to the joystick, not
        // look. Without this check, if TouchLookField ever ends up as a later
        // sibling (rendered on top) of the joystick - even temporarily, or after
        // some other UI change - it would swallow the touch before VirtualJoystick
        // ever sees it, making the joystick rotate the camera instead of moving
        // the player.
        if (IsOverJoystick(eventData))
        {
            dragging = false;
            return;
        }

        // Same idea, but for PC parts / RAM locks / cables: if this touch landed on
        // something PartClickDetector considers interactable, let that own the touch
        // instead (drag-to-place a part shouldn't also spin the camera underneath it).
        if (PartClickDetector.Instance != null && PartClickDetector.Instance.IsOverInteractable(eventData.position))
        {
            dragging = false;
            return;
        }

        dragging = true;
        lastPointerPos = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        Vector2 delta = eventData.position - lastPointerPos;
        lastPointerPos = eventData.position;
        pendingDelta += delta * touchLookSensitivity;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        dragging = false;
    }

    private static bool IsOverJoystick(PointerEventData eventData)
    {
        RectTransform joystickBg = VirtualJoystick.Instance != null ? VirtualJoystick.Instance.Background : null;
        if (joystickBg == null) return false;

        return RectTransformUtility.RectangleContainsScreenPoint(
            joystickBg, eventData.position, eventData.pressEventCamera);
    }
}