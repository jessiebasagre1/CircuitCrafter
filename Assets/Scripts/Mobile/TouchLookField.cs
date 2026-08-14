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
}