using UnityEngine;
public class PlayerMovement : MonoBehaviour
{
    public CharacterController controller;
    public float speed = 12f;
    public float gravity = -9.81f;
    public Transform groundCheck;
    public float groundDistance = 0.4f;
    public LayerMask groundMask;
    bool isGrounded;
    public float jumpHeight = 2f;
    Vector3 velocity;

    [Header("Mobile Jump Button (optional)")]
    [Tooltip("Assign a UI Button's OnClick to RequestJump() for mobile, or leave unassigned to use keyboard Jump only.")]
    private bool jumpRequested;

    void Update()
    {
        // Freeze while a station panel has the keyboard. Legacy Input axes read the keyboard
        // directly and do not care that a TMP_InputField has focus, so typing "rufus.ie" into
        // the browser's address bar walked the player backwards on the 's' and strafed on
        // the 'a'.
        if (PlayerInputLock.IsLocked) return;

        // Freeze all movement/input while a tutorial dialog is open,
        // and for the one frame it just closed (so the same spacebar
        // press that closed it doesn't also trigger a jump).
        if (DialogManager.Instance != null &&
            (DialogManager.Instance.IsDialogOpen || DialogManager.Instance.WasDialogClosedThisFrame))
            return;

        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        bool jumpPressed = Input.GetButtonDown("Jump") || jumpRequested;
        jumpRequested = false;

        if (jumpPressed && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Keyboard axis (PC) - unchanged from original.
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // Virtual joystick axis (mobile) - only present if a VirtualJoystick
        // exists in the scene. Whichever input source has the larger
        // magnitude wins, so keyboard always works on PC even if a joystick
        // UI happens to be present but untouched (its Value is Vector2.zero
        // when not pressed, so this never overrides keyboard unintentionally).
        if (VirtualJoystick.Instance != null)
        {
            Vector2 joy = VirtualJoystick.Instance.Value;
            if (joy.sqrMagnitude > (x * x + z * z))
            {
                x = joy.x;
                z = joy.y;
            }
        }

        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move * speed * Time.deltaTime);
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    /// <summary>Call from a mobile on-screen Jump button's OnClick event.</summary>
    public void RequestJump()
    {
        jumpRequested = true;
    }
}