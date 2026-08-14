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
        gameObject.SetActive(Application.isMobilePlatform);
    }
}