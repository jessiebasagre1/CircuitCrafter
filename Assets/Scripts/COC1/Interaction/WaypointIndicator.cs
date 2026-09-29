using UnityEngine;

/// <summary>
/// World-space waypoint arrow. Attach this to a GameObject that has a
/// SpriteRenderer (using your arrow sprite) placed in the scene -- NOT a UI
/// Canvas Image. It floats above a target transform, billboards to always
/// face the camera, and rotates within that billboard plane so it visually
/// points straight down at the target.
///
/// Because it lives in 3D world space instead of on a Canvas, there's no
/// screen-pixel math at all: it moves and orients itself using the target's
/// and camera's real transforms, so it can never "drift" the way a
/// mismatched UI/Canvas conversion could. It also naturally goes off-screen
/// along with the target -- no edge-clamping / compass behavior.
///
/// Setup:
/// 1. Create an empty GameObject in the scene (e.g. "WaypointArrow").
/// 2. Add a SpriteRenderer component to it and assign your arrow sprite.
/// 3. Attach this script to the same GameObject.
/// 4. Leave References empty in the Inspector -- they auto-fill in Awake()
///    (Arrow Transform = this object, World Camera = Camera.main). Only
///    assign them manually if you need something different.
/// </summary>
public class WaypointIndicator : MonoBehaviour
{
    public static WaypointIndicator Instance { get; private set; }

    [Header("References")]
    [Tooltip("Usually just leave empty -- defaults to this GameObject's own transform.")]
    [SerializeField] private Transform arrowTransform;
    [Tooltip("Usually leave empty -- defaults to Camera.main.")]
    [SerializeField] private Camera worldCamera;

    [Header("Floating Settings")]
    [Tooltip("How far above the target the arrow floats, in world units.")]
    [SerializeField] private float worldHeightOffset = 1.5f;
    [SerializeField] private bool bobUpDown = true;
    [Tooltip("Bob amount in world units (not pixels -- this is a 3D object now).")]
    [SerializeField] private float bobAmount = 0.15f;
    [SerializeField] private float bobSpeed = 2f;
    [Tooltip("Shifts the arrow left/right relative to the camera's current view, in world units. " +
             "Positive = right (from the camera's perspective, changes as the camera rotates). Usually leave at 0.")]
    [SerializeField] private float cameraRelativeXOffset = 0f;
    [Tooltip("Shifts the arrow along the world's true X axis, in world units. Positive = world +X, " +
             "regardless of camera angle. Use this for a simple, fixed left/right nudge.")]
    [SerializeField] private float worldXOffset = 0f;
    [Tooltip("Shifts the arrow along the world's true Z axis, in world units. Positive = world +Z, " +
             "regardless of camera angle. Use this for a simple, fixed forward/back nudge.")]
    [SerializeField] private float worldZOffset = 0f;

    [Header("Rotation Fix")]
    [Tooltip("Direction your arrow sprite points at rest, in degrees counter-clockwise from 'pointing right' (0). " +
             "For Arrow.png (points right by default), leave this at 0.")]
    [SerializeField] private float spriteForwardAngle = 0f;

    private Transform target;
    private bool isActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (arrowTransform == null) arrowTransform = transform;
        if (worldCamera == null) worldCamera = Camera.main;

        SetVisible(false);
    }

    /// <summary>Call this to start pointing at a target (e.g. the PC).
    ///
    /// Story Mode help. In an assessment this clears the arrow instead - which silences every story
    /// caller (IntroDialogTrigger, PCDisassemblySetup, TicketArrivalDialog, COC3StationController)
    /// with no edits to any of them. The assessment managers use SetStationTarget below.</summary>
    public void SetTarget(Transform newTarget)
    {
        if (newTarget != null && AssessmentMode.IsActive)
        {
            ClearTarget();
            return;
        }

        target = newTarget;
        isActive = target != null;
        SetVisible(isActive);
    }

    /// <summary>
    /// For the COC1/COC2 assessment managers: point at the NEXT STATION after a stage. That is part
    /// of their assessment design - it names a place, never a part or an action - so it is allowed in
    /// an assessment. In Story Mode it behaves exactly like SetTarget.
    /// </summary>
    public void SetStationTarget(Transform station)
    {
        if (station != null && AssessmentMode.IsActive)
        {
            target = station;
            isActive = true;
            SetVisible(true);
            return;
        }

        SetTarget(station);
    }

    /// <summary>Call this to hide the arrow (e.g. task complete).</summary>
    public void ClearTarget()
    {
        target = null;
        isActive = false;
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (arrowTransform != null)
            arrowTransform.gameObject.SetActive(visible);
    }

    private void LateUpdate()
    {
        if (!isActive || target == null || worldCamera == null)
            return;

        Vector3 pos = target.position + Vector3.up * worldHeightOffset;

        if (bobUpDown)
            pos += Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobAmount);

        if (cameraRelativeXOffset != 0f)
            pos += worldCamera.transform.right * cameraRelativeXOffset;

        if (worldXOffset != 0f)
            pos += Vector3.right * worldXOffset;

        if (worldZOffset != 0f)
            pos += Vector3.forward * worldZOffset;

        arrowTransform.position = pos;

        // Billboard: face the camera dead-on (sprite plane parallel to the
        // camera's near plane), then rotate within that plane so the sprite
        // visually points straight down at the target below it.
        Quaternion billboard = worldCamera.transform.rotation;
        arrowTransform.rotation = billboard * Quaternion.Euler(0f, 0f, -90f - spriteForwardAngle);
    }
}