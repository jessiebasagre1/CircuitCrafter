using UnityEngine;

/// <summary>
/// Scroll to zoom, drag to pan, on a station close-up camera.
///
/// ATTACH THIS TO: the camera (or the rig object the camera is a child of) at each station.
///
/// IT WORKS IN THE CAMERA'S OWN AXES, and that is the whole design.
///
/// The first version zoomed along the parent's -Y and panned in the parent's XZ plane. That is
/// correct for exactly one mounting - a camera pointing straight down at something flat, which is
/// how COC1's motherboard station sits - and silently wrong for every other. Point the rig
/// horizontally, at a tower lying on its side, and parent-Y becomes a sideways axis: the scroll
/// wheel slides the camera across the scene instead of moving it closer, and the pan throws away
/// the component that would have moved across the screen and keeps the one along the view, so
/// dragging zooms. The controls appear to have swapped places. Nothing errors, because nothing is
/// wrong with the maths - it is being applied to the wrong axes.
///
/// So the view axis is read ONCE from where the camera is actually looking, and zoom and pan are
/// tracked as a distance along it and an offset across it. A rig can then be mounted at any angle
/// - overhead, horizontal, tilted over a bench - and scroll always means closer, drag always means
/// sideways. A camera pointing straight down behaves exactly as it did before, because for that
/// mounting the old axes and the new ones are the same ones.
///
/// ZOOM AND PAN ARE STORED SEPARATELY rather than both being read back out of localPosition. Two
/// clamps sharing one position is how a pan can quietly eat into the zoom range and leave the
/// camera unable to come all the way back in.
/// </summary>
public class CameraZoom : MonoBehaviour
{
    [Header("Zoom")]
    public float zoomSpeed = 2f;        // scroll wheel (PC)
    public float pinchZoomSpeed = 0.01f; // two-finger pinch (mobile)

    [Tooltip("Closest the camera may come to its starting position, along the direction it looks.")]
    public float minDistance = 0.2f;

    [Tooltip("Furthest the camera may travel toward the subject from its starting position.")]
    public float maxDistance = 2f;

    public float minZoomSpeed = 0.5f;   // sensitivity slider range
    public float maxZoomSpeed = 5f;

    [Header("Pan")]
    public float panSpeed = 0.002f;     // tuned for raw pixel deltas, works for mouse and touch alike

    [Tooltip("Max shift left/right from the starting view, in metres.")]
    public float panLimitX = 0.3f;

    [Tooltip("Max shift up/down from the starting view, in metres.\n\nStill called Z so your existing " +
             "Inspector values survive. It used to mean the parent's Z axis; it now means the " +
             "camera's own up/down, which is what it was always being used for on an overhead rig.")]
    public float panLimitZ = 0.3f;

    public float minPanSpeed = 0.0005f; // sensitivity slider range
    public float maxPanSpeed = 0.01f;
    public bool invertPanX = false;
    public bool invertPanY = false;
    public Camera cam;                  // camera used for the raycast; defaults to Camera.main
    public LayerMask partLayerMask;     // set this to whatever layer your draggable parts are on

    [Header("Reset")]
    public KeyCode resetPanKey = KeyCode.R; // PC only - call ResetPan() from a UI button for mobile

    private Vector3 startPos;

    // The rig's three axes, in PARENT space, captured at Start before anything has moved it.
    // Parent space rather than world space because localPosition is what gets written, and rebuilding
    // it from world vectors every frame would drift on a rig whose parent is itself animated.
    private Vector3 axisForward = Vector3.down;
    private Vector3 axisRight = Vector3.right;
    private Vector3 axisUp = Vector3.forward;

    // Where the camera is, expressed the way the controls think about it.
    private float zoomDistance;         // along axisForward, 0 = the starting position
    private Vector2 panOffset;          // across it, in (right, up)

    // Pan state
    private bool trackingPointer;
    private bool isPanning;
    private Vector2 lastPointerPos;

    // Pinch state
    private bool pinchActive;
    private float lastPinchDistance;

    void Start()
    {
        LoadSettings();
        startPos = transform.localPosition;
        if (cam == null) cam = Camera.main;

        CaptureAxes();
    }

    /// <summary>
    /// Reads the rig's own axes into parent space, once.
    ///
    /// Once, and not per frame, because zooming MOVES the camera - and on a rig that also rotates to
    /// follow its subject, re-reading the axis mid-gesture would make each frame's movement relative
    /// to the pose the previous frame produced. That compounds into a slow curve away from the
    /// subject that feels like drift and is very hard to see in the Inspector.
    /// </summary>
    private void CaptureAxes()
    {
        Transform parent = transform.parent;

        if (parent != null)
        {
            axisForward = parent.InverseTransformDirection(transform.forward);
            axisRight = parent.InverseTransformDirection(transform.right);
            axisUp = parent.InverseTransformDirection(transform.up);
        }
        else
        {
            axisForward = transform.forward;
            axisRight = transform.right;
            axisUp = transform.up;
        }

        axisForward = axisForward.sqrMagnitude > 0.0001f ? axisForward.normalized : Vector3.down;
        axisRight = axisRight.sqrMagnitude > 0.0001f ? axisRight.normalized : Vector3.right;
        axisUp = axisUp.sqrMagnitude > 0.0001f ? axisUp.normalized : Vector3.forward;
    }

    void Update()
    {
        HandleZoom();
        HandlePan();

        if (Input.GetKeyDown(resetPanKey))
        {
            ResetPan();
        }
    }

    /// <summary>Rebuilds localPosition from the two things the controls actually track. One place
    /// writes the transform, so zoom and pan can never disagree about where the camera is.</summary>
    private void Apply()
    {
        transform.localPosition = startPos
                                + axisForward * zoomDistance
                                + axisRight * panOffset.x
                                + axisUp * panOffset.y;
    }

    void HandleZoom()
    {
        float zoomDelta = 0f;

        if (Input.touchCount == 2)
        {
            // Two fingers on screen = pinch to zoom
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);
            float currentDistance = Vector2.Distance(t0.position, t1.position);

            if (!pinchActive)
            {
                pinchActive = true;
                lastPinchDistance = currentDistance;
            }
            else
            {
                // Fingers spreading apart (distance growing) zooms in, same direction as scrolling up
                zoomDelta = (currentDistance - lastPinchDistance) * pinchZoomSpeed;
                lastPinchDistance = currentDistance;
            }
        }
        else
        {
            pinchActive = false;
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            zoomDelta = scroll * zoomSpeed;
        }

        if (zoomDelta == 0f) return;

        // Positive delta - scroll up, or fingers spreading - means CLOSER, and closer is along the
        // direction the camera is pointing. That sentence is the entire fix.
        zoomDistance = Mathf.Clamp(zoomDistance + zoomDelta, minDistance, maxDistance);
        Apply();
    }

    void HandlePan()
    {
        // A part drag (world or hotbar) owns input right now - never pan while it's active.
        // Two separate systems can own a drag: a PC part (CPU, RAM, the PSU body) goes through
        // DragController, but a cable's plug drags itself via CableConnector - see the note on
        // CableConnector.IsAnyDragging. Missing the second one is exactly why dragging a cable
        // used to pan the camera at the same time: nothing here knew the cable was moving.
        if ((DragController.Instance != null && DragController.Instance.IsDragging) ||
            CableConnector.IsAnyDragging)
        {
            trackingPointer = false;
            isPanning = false;
            return;
        }

        if (Input.touchCount >= 2)
        {
            // Two+ fingers belongs to pinch zoom, not pan
            trackingPointer = false;
            isPanning = false;
            return;
        }

        bool usingTouch = Input.touchCount == 1;
        Vector2 pointerPos = usingTouch ? (Vector2)Input.GetTouch(0).position : (Vector2)Input.mousePosition;
        bool pointerDown = usingTouch ? true : Input.GetMouseButton(0);
        bool justReleased = usingTouch
            ? (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled)
            : Input.GetMouseButtonUp(0);

        if (justReleased || !pointerDown)
        {
            trackingPointer = false;
            isPanning = false;
            return;
        }

        if (!trackingPointer)
        {
            // Fresh press (including recovering from a pinch) - start a new gesture
            trackingPointer = true;
            lastPointerPos = pointerPos;

            isPanning = true;

            // Whether this press landed on something grabbable decides the whole gesture, so it
            // needs to agree with what actually grabs things - PartClickDetector, which tries a
            // pixel-precise raycast and then falls back to a forgiving 0.15m SphereCast so a part
            // that is small on screen (zoomed OUT - the far end of the zoom range, not the close-up
            // end) can still be clicked precisely. This check used to run its own bare
            // Physics.Raycast with no forgiveness at all - so at the same zoom level and the same
            // click, PartClickDetector could grab the part while this one missed it and decided the
            // gesture was a pan instead. Reusing PartClickDetector's own test removes that gap
            // outright, and it doubles as covering RAM/CPU locks and cable plugs too, not just
            // PCPartItem, with no separate layer mask to keep in sync.
            if (PartClickDetector.Instance != null)
            {
                if (PartClickDetector.Instance.IsOverInteractable(pointerPos))
                    isPanning = false; // a part, lock, or cable owns this gesture instead
            }
            else if (cam != null)
            {
                // Fallback for a station rig with no PartClickDetector in the scene (nothing to
                // click on this camera at all, or it lives on a different object here).
                Ray ray = cam.ScreenPointToRay(pointerPos);
                if (Physics.Raycast(ray, out _, 500f, partLayerMask))
                {
                    isPanning = false;
                }
            }
            return;
        }

        if (!isPanning) return;

        Vector2 delta = pointerPos - lastPointerPos;
        lastPointerPos = pointerPos;
        if (delta == Vector2.zero) return;

        if (invertPanX) delta.x = -delta.x;
        if (invertPanY) delta.y = -delta.y;

        // Negated so the subject follows the finger: dragging right sends the camera left, which
        // reads as pushing the machine to the right. Screen X and Y map straight onto the camera's
        // own right and up, so this holds at any mounting angle without a special case.
        panOffset.x = Mathf.Clamp(panOffset.x - delta.x * panSpeed, -panLimitX, panLimitX);
        panOffset.y = Mathf.Clamp(panOffset.y - delta.y * panSpeed, -panLimitZ, panLimitZ);

        Apply();
    }

    public void ResetPan()
    {
        panOffset = Vector2.zero;
        Apply();
    }

    /// <summary>Puts the camera back exactly where it was mounted - pan AND zoom. Worth wiring to a
    /// station's On Enter, so a learner who left the last station zoomed into a corner does not
    /// arrive at the next one already lost.</summary>
    public void ResetView()
    {
        panOffset = Vector2.zero;
        zoomDistance = 0f;
        Apply();
    }

    /// <summary>
    /// Prints which way this rig thinks it is pointing. The fastest way to answer "why does scroll
    /// move sideways here" - the Forward line is the direction the scroll wheel pushes the camera,
    /// and it should be the direction the camera visibly looks.
    /// </summary>
    [ContextMenu("Report camera axes")]
    public void LogAxes()
    {
        if (!Application.isPlaying) CaptureAxes();

        Debug.LogWarning(
            "[CameraZoom] '" + name + "' axes, in " +
            (transform.parent != null ? "'" + transform.parent.name + "' space" : "world space") + ":" +
            "\n    Forward (scroll moves the camera this way) : " + axisForward +
            "\n    Right   (drag moves the camera this way)   : " + axisRight +
            "\n    Up                                          : " + axisUp +
            "\n    Zoom " + zoomDistance.ToString("F3") + " of [" + minDistance + ", " + maxDistance + "]" +
            ", pan " + panOffset + " of [" + panLimitX + ", " + panLimitZ + "]", this);
    }

    // ---- Settings UI hooks ----
    // Sliders drive these as plain 0-1 values instead of the raw speed numbers above.

    public float GetPanSensitivity01() => Mathf.InverseLerp(minPanSpeed, maxPanSpeed, panSpeed);
    public void SetPanSensitivity01(float t) => panSpeed = Mathf.Lerp(minPanSpeed, maxPanSpeed, t);

    public float GetZoomSensitivity01() => Mathf.InverseLerp(minZoomSpeed, maxZoomSpeed, zoomSpeed);
    public void SetZoomSensitivity01(float t) => zoomSpeed = Mathf.Lerp(minZoomSpeed, maxZoomSpeed, t);

    private const string PrefPanSpeed = "CameraZoom.PanSpeed";
    private const string PrefZoomSpeed = "CameraZoom.ZoomSpeed";
    private const string PrefInvertX = "CameraZoom.InvertX";
    private const string PrefInvertY = "CameraZoom.InvertY";

    private void LoadSettings()
    {
        panSpeed = PlayerPrefs.GetFloat(PrefPanSpeed, panSpeed);
        zoomSpeed = PlayerPrefs.GetFloat(PrefZoomSpeed, zoomSpeed);
        invertPanX = PlayerPrefs.GetInt(PrefInvertX, invertPanX ? 1 : 0) == 1;
        invertPanY = PlayerPrefs.GetInt(PrefInvertY, invertPanY ? 1 : 0) == 1;
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat(PrefPanSpeed, panSpeed);
        PlayerPrefs.SetFloat(PrefZoomSpeed, zoomSpeed);
        PlayerPrefs.SetInt(PrefInvertX, invertPanX ? 1 : 0);
        PlayerPrefs.SetInt(PrefInvertY, invertPanY ? 1 : 0);
        PlayerPrefs.Save();
    }
}