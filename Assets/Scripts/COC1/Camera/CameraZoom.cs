using UnityEngine;

public class CameraZoom : MonoBehaviour
{
    [Header("Zoom")]
    public float zoomSpeed = 2f;        // scroll wheel (PC)
    public float pinchZoomSpeed = 0.01f; // two-finger pinch (mobile)
    public float minDistance = 0.2f;
    public float maxDistance = 2f;
    public float minZoomSpeed = 0.5f;   // sensitivity slider range
    public float maxZoomSpeed = 5f;

    [Header("Pan")]
    public float panSpeed = 0.002f;     // tuned for raw pixel deltas, works for mouse and touch alike
    public float panLimitX = 0.3f;      // max horizontal shift from the starting view
    public float panLimitZ = 0.3f;      // max forward/back shift from the starting view
    public float minPanSpeed = 0.0005f; // sensitivity slider range
    public float maxPanSpeed = 0.01f;
    public bool invertPanX = false;
    public bool invertPanY = false;
    public Camera cam;                  // camera used for the raycast; defaults to Camera.main
    public LayerMask partLayerMask;     // set this to whatever layer your draggable parts are on

    [Header("Reset")]
    public KeyCode resetPanKey = KeyCode.R; // PC only - call ResetPan() from a UI button for mobile

    private Vector3 startPos;

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

        if (zoomDelta != 0f)
        {
            Vector3 pos = transform.localPosition;
            pos += Vector3.down * zoomDelta;
            pos.y = Mathf.Clamp(pos.y, startPos.y - maxDistance, startPos.y - minDistance);
            transform.localPosition = pos;
        }
    }

    void HandlePan()
    {
        // A part drag (world or hotbar) owns input right now — never pan while it's active.
        if (DragController.Instance != null && DragController.Instance.IsDragging)
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
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(pointerPos);
                if (Physics.Raycast(ray, out _, 500f, partLayerMask))
                {
                    isPanning = false; // a part owns this gesture instead
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

        // Move opposite the drag using the camera's own right/up, so left/right and
        // up/down always match the screen no matter how this rig is rotated.
        Vector3 worldMove = (-transform.right * delta.x) + (-transform.up * delta.y);

        Vector3 localMove = transform.parent != null
            ? transform.parent.InverseTransformVector(worldMove)
            : worldMove;
        localMove.y = 0f; // never fight with the zoom clamp above

        Vector3 pos = transform.localPosition + localMove * panSpeed;

        pos.x = Mathf.Clamp(pos.x, startPos.x - panLimitX, startPos.x + panLimitX);
        pos.z = Mathf.Clamp(pos.z, startPos.z - panLimitZ, startPos.z + panLimitZ);

        transform.localPosition = pos;
    }

    public void ResetPan()
    {
        Vector3 pos = transform.localPosition;
        pos.x = startPos.x;
        pos.z = startPos.z;
        transform.localPosition = pos;
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