using UnityEngine;

public class MouseLook : MonoBehaviour
{

    public float mouseSensitivity = 100f;

    [Tooltip("Pushing the mouse or finger forward looks DOWN instead of up. Driven by the Settings panel; the value here is only the starting point.")]
    public bool invertLookY = false;

    public Transform playerBody;
    float xRotation = 0f;

    void Start()
    {
        // Cursor locking is a PC-only concept; skip it on mobile so the
        // touch-look field can receive pointer-down events normally
        // (a locked cursor doesn't apply on touch, but this guard avoids
        // any editor/device quirks around calling it unnecessarily).
#if !UNITY_ANDROID && !UNITY_IOS
        Cursor.lockState = CursorLockMode.Locked;
#else
        // Unity's legacy Input Manager emulates a "Mouse X"/"Mouse Y" axis from
        // ANY touch drag on screen by default - including a drag happening over
        // the virtual joystick UI. That simulated axis is independent of the UI
        // event system, so even though VirtualJoystick correctly captures that
        // touch (and TouchLookField never sees it), Input.GetAxis("Mouse X"/"Mouse Y")
        // below would still pick up the joystick drag and rotate the camera along
        // with it. Turning this off means touch only drives look via
        // TouchLookField's explicit ConsumeDelta(), cleanly separated from
        // whatever's driving the joystick.
        Input.simulateMouseWithTouches = false;
#endif
    }

    void Update()
    {
        // Same lock PlayerMovement respects. Without it the camera keeps spinning to mouse
        // movement while the learner is typing into a station panel, which is disorienting
        // even though the mouse is barely moving.
        if (PlayerInputLock.IsLocked) return;

        // PC mouse axis - only read when no touch is active. On touch
        // devices, Unity can synthesize "Mouse X"/"Mouse Y" from ANY finger
        // movement on screen (via simulateMouseWithTouches and/or the input
        // system's own touch-to-mouse simulation), completely independent of
        // which UI element that touch actually landed on - so a finger
        // dragging the joystick can leak into these axes even though
        // TouchLookField correctly ignores that same touch. Skipping the
        // read entirely whenever any touch is present removes that leak at
        // the source, and also stops the joystick-finger and look-finger
        // from both feeding rotation at once when used together.
        float mouseX = 0f;
        float mouseY = 0f;
        if (Input.touchCount == 0)
        {
            mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
            mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;
        }

        // Mobile touch-drag look, additive. On a touch device with no mouse
        // connected, mouseX/mouseY above are already ~0, so this simply adds
        // the touch contribution on top without needing a platform branch.
        if (TouchLookField.Instance != null)
        {
            Vector2 touchDelta = TouchLookField.Instance.ConsumeDelta();
            mouseX += touchDelta.x;
            mouseY += touchDelta.y;
        }

        // Applied here rather than by negating the sensitivity, so that inverting
        // affects the vertical axis ONLY. A negative sensitivity would flip the
        // horizontal too, which nobody has ever wanted and which would also make
        // the settings slider read back wrong.
        if (invertLookY) mouseY = -mouseY;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseX);


    }

}