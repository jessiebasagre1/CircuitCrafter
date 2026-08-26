using UnityEngine;

/// <summary>
/// One place that decides whether the mouse cursor is captured for look-around.
///
/// WHY THIS EXISTS
/// Several scripts across COC 1, 2 and 3 set Cursor.lockState directly, and some do it from
/// Awake(). A captured cursor makes Input.mousePosition stop tracking and report the screen
/// centre, and the EventSystem hands that position to the raycaster - so EVERY UI click lands
/// dead centre of the screen. An on-screen joystick in the corner can never be reached, and it
/// reads as "the joystick is broken" rather than "the pointer is pinned".
///
/// WHAT CHANGED FROM THE FIRST VERSION
/// This used to refuse to capture whenever UNITY_ANDROID was defined. That was too blunt: with
/// the Android build target selected, the cursor then stayed visible in the Editor even during
/// normal play, which is not what anyone wants while building the game.
///
/// The decision is now made at RUNTIME, from two questions:
///
///   1. Is this an actual touch device?  Application.isMobilePlatform - true only on a real
///      phone or tablet, false in the Editor whatever build target is selected. There is no
///      cursor to capture there, so it never captures.
///
///   2. Are the on-screen touch controls currently VISIBLE?  If a VirtualJoystick is active in
///      the scene, something is expecting screen taps to land where the finger (or mouse) is,
///      and capturing the cursor would break exactly that. This is the case that matters in the
///      Editor when you untick MobileControlsVisibility to test the touch controls with a mouse.
///
/// Neither applies - ordinary Editor play, ordinary desktop build - and it captures normally.
/// Press Start on an assessment and the cursor disappears, the way it should.
/// </summary>
public static class CursorControl
{
    /// <summary>True when the cursor is currently captured for look-around.</summary>
    public static bool Captured { get; private set; }

    /// <summary>
    /// Manual override. Set true and nothing will ever capture the cursor until it is set back.
    /// An escape hatch for a case the two automatic checks below do not cover - not needed in
    /// normal use.
    /// </summary>
    public static bool SuspendCapture;

    /// <summary>
    /// True when capturing the cursor would break on-screen controls.
    ///
    /// VirtualJoystick.Instance is assigned in Awake and only cleared in OnDestroy, so it stays
    /// non-null after MobileControlsVisibility switches the canvas off. isActiveAndEnabled is the
    /// question actually worth asking: is the joystick on screen right now.
    /// </summary>
    public static bool TouchControlsVisible
    {
        get
        {
            return VirtualJoystick.Instance != null && VirtualJoystick.Instance.isActiveAndEnabled;
        }
    }

    /// <summary>True when the cursor must be left alone, whatever is asked for.</summary>
    public static bool CaptureBlocked
    {
        get
        {
            return SuspendCapture || Application.isMobilePlatform || TouchControlsVisible;
        }
    }

    public static void SetCaptured(bool captured)
    {
        if (captured && CaptureBlocked)
        {
            Free();
            return;
        }

        Captured = captured;
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    /// <summary>Capture the cursor for mouse-look. Ignored while CaptureBlocked is true.</summary>
    public static void Capture()
    {
        SetCaptured(true);
    }

    /// <summary>Release the cursor so UI can be clicked. Always honoured.</summary>
    public static void Free()
    {
        Captured = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}