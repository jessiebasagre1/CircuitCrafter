using UnityEngine;

/// <summary>
/// Unified pointer read for mouse AND touch.
///
/// Why this exists: MouseLook.cs deliberately sets
/// Input.simulateMouseWithTouches = false on Android/iOS (so a joystick-finger
/// drag doesn't leak into the Mouse X/Y axis - see the comment there). That's
/// correct for camera look, but it's a global engine setting: once it's off,
/// Input.mousePosition and Input.GetMouseButtonDown/Up stop receiving touch
/// data everywhere in the game, not just in MouseLook.
///
/// PartClickDetector and DragController used to read those APIs directly,
/// which meant they silently went dead on mobile (parts became untappable
/// and undraggable) the moment MouseLook flipped that flag on device. Route
/// through this instead so both mouse and touch keep working everywhere.
///
/// Uses the first active touch (Input.GetTouch(0)), same convention already
/// used by CameraZoom.cs elsewhere in this project.
/// </summary>
public static class PointerInput
{
    /// <summary>Current pointer position on screen - primary touch if any, else mouse.</summary>
    public static Vector2 Position =>
        Input.touchCount > 0 ? (Vector2)Input.GetTouch(0).position : (Vector2)Input.mousePosition;

    /// <summary>True on the exact frame the pointer (touch or mouse) went down.</summary>
    public static bool Down =>
        Input.touchCount > 0
            ? Input.GetTouch(0).phase == TouchPhase.Began
            : Input.GetMouseButtonDown(0);

    /// <summary>True on the exact frame the pointer (touch or mouse) was released.</summary>
    public static bool Up
    {
        get
        {
            if (Input.touchCount > 0)
            {
                TouchPhase phase = Input.GetTouch(0).phase;
                return phase == TouchPhase.Ended || phase == TouchPhase.Canceled;
            }
            return Input.GetMouseButtonUp(0);
        }
    }

    /// <summary>True for every frame the pointer (touch or mouse) is held down.</summary>
    public static bool Held => Input.touchCount > 0 || Input.GetMouseButton(0);
}