using UnityEngine;

/// <summary>
/// Attach this to MobileControlsCanvas itself. Hides the whole mobile
/// control UI (joystick, look field, buttons) when running on PC, so it
/// doesn't sit uselessly on top of a desktop build.
/// </summary>
public class MobileControlsVisibility : MonoBehaviour
{
    void Awake()
    {
        ApplyPlatformVisibility();
    }

    /// <summary>
    /// Re-checks every time this canvas gets re-enabled, not just once at
    /// Awake. StationCameraSwitch's enableOnStationView/disableOnStationView
    /// arrays (and anything else that toggles this canvas, like the quiz
    /// camera hand-off) call SetActive(true) on it with no idea what platform
    /// it's running on -- Awake alone can't protect against that once the
    /// object gets flipped back on later. OnEnable catches it immediately and
    /// re-hides it on PC, without needing every other script that might touch
    /// this canvas to remember a platform check.
    /// </summary>
    void OnEnable()
    {
        ApplyPlatformVisibility();
    }

    private void ApplyPlatformVisibility()
    {
        if (!Application.isMobilePlatform)
            gameObject.SetActive(false);
    }
}