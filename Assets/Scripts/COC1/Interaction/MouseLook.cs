using UnityEngine;

public class MouseLook : MonoBehaviour
{

    public float mouseSensitivity = 100f;
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
#endif
    }

    void Update()
    {
        // PC mouse axis - unchanged from original.
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // Mobile touch-drag look, additive. On a touch device with no mouse
        // connected, mouseX/mouseY above are already ~0, so this simply adds
        // the touch contribution on top without needing a platform branch.
        if (TouchLookField.Instance != null)
        {
            Vector2 touchDelta = TouchLookField.Instance.ConsumeDelta();
            mouseX += touchDelta.x;
            mouseY += touchDelta.y;
        }

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseX);


    }

}