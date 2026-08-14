using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On-screen virtual joystick for movement on touch devices. Works alongside
/// keyboard WASD on PC without any conflict - PlayerMovement reads whichever
/// input (keyboard axis or joystick) has the larger magnitude each frame.
///
/// Setup:
///   1. Create a UI Image "JoystickBackground" (a translucent circle), and a
///      child UI Image "JoystickHandle" (a smaller circle) centered on it.
///   2. Add this component to the JoystickBackground object.
///   3. Assign background + handle RectTransforms in the Inspector.
///   4. Wrap both in a Canvas Group / panel you can show only on mobile
///      builds if desired (e.g. via Application.isMobilePlatform).
///
/// Input value is exposed as a static singleton so PlayerMovement doesn't
/// need a direct scene reference - if no joystick exists in the scene
/// (e.g. a PC-only build), Instance is simply null and PlayerMovement falls
/// back to keyboard-only input automatically.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static VirtualJoystick Instance { get; private set; }

    [Header("Refs")]
    [SerializeField] private RectTransform background;
    [SerializeField] private RectTransform handle;

    [Header("Feel")]
    [Tooltip("How far (in pixels) the handle can travel from center before clamping to max input.")]
    [SerializeField] private float handleRange = 100f;
    [Tooltip("Dead zone as a fraction of handleRange - inputs below this are treated as zero, prevents drift from an imprecise thumb.")]
    [SerializeField] private float deadZone = 0.1f;

    /// <summary>Current input, X = strafe (-1..1), Y = forward/back (-1..1). Zero when not being touched.</summary>
    public Vector2 Value { get; private set; }

    public bool IsPressed { get; private set; }

    private Vector2 inputStartPos;

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
        IsPressed = true;
        OnDrag(eventData); // handle immediate placement on first touch, not just movement after
    }

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

        Vector2 clamped = Vector2.ClampMagnitude(localPoint, handleRange);
        handle.anchoredPosition = clamped;

        Vector2 raw = clamped / handleRange; // -1..1 range
        Value = raw.magnitude < deadZone ? Vector2.zero : raw;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsPressed = false;
        Value = Vector2.zero;
        handle.anchoredPosition = Vector2.zero;
    }
}