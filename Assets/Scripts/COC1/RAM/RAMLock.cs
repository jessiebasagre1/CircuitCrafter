using UnityEngine;

/// <summary>
/// A single RAM socket's retention clip. Click to toggle open/closed. Exposes
/// Open()/Close() so other scripts can control it too � used automatically
/// by RAMPartItem to close the clip the moment a stick is reinstalled.
///
/// Hover highlight: shows only while the mouse is over this lock. Color
/// reflects state � a neutral color while closed, green once open.
/// </summary>
public class RAMLock : MonoBehaviour
{
    [Header("References")]
    public Transform openPosition;

    [Header("Hover Highlight")]
    [Tooltip("Visual shown only while the mouse is hovering this lock.")]
    public GameObject highlightVisual;
    [Tooltip("Renderer on the highlight visual whose color reflects lock state. Usually the same object as Highlight Visual above.")]
    public Renderer highlightRenderer;
    public Color closedHighlightColor = Color.white;
    public Color openHighlightColor = Color.green;


    // ---- POSES ARE STORED RELATIVE TO THE PARENT, NOT IN WORLD SPACE ----------------
    //
    // THE BUG THIS FIXES: the system unit lies down in COC 4, and the locks stayed behind at the
    // world spot the unit used to stand in. Both poses were world-space snapshots - the closed one
    // captured from transform.position, the open one read live from openPosition, which is a loose
    // scene object that does not move with the unit either. Neither knew the unit had moved.
    //
    // Converting both into the PARENT's local space once, at wake-up, makes them ride along with
    // whatever the lock is attached to - lying down, standing up, rotated, carried. Exactly the
    // same fix as the inspector camera.
    //
    // Nothing changes for a unit that never moves: local and world agree when the parent has not
    // been touched.
    Vector3 closedLocalPosition;
    Quaternion closedLocalRotation;
    Vector3 openLocalPosition;
    Quaternion openLocalRotation;
    bool openPoseCaptured;

    /// <summary>Records both poses in parent space. Safe to call more than once; only the first
    /// call counts, so a restore that moves this transform cannot overwrite the real pose.</summary>
    void CapturePoses()
    {
        if (openPoseCaptured) return;
        openPoseCaptured = true;

        closedLocalPosition = transform.localPosition;
        closedLocalRotation = transform.localRotation;

        if (openPosition == null) return;

        Transform parent = transform.parent;
        if (parent != null)
        {
            // World -> parent space, once. After this the open pose is a local offset like any
            // other, so it follows the unit without openPosition having to be parented under it.
            openLocalPosition = parent.InverseTransformPoint(openPosition.position);
            openLocalRotation = Quaternion.Inverse(parent.rotation) * openPosition.rotation;
        }
        else
        {
            // No parent: local IS world, so this behaves exactly as the old code did.
            openLocalPosition = openPosition.position;
            openLocalRotation = openPosition.rotation;
        }
    }

    void ApplyOpenPose()
    {
        CapturePoses();
        if (openPosition == null) return;
        transform.localPosition = openLocalPosition;
        transform.localRotation = openLocalRotation;
    }

    void ApplyClosedPose()
    {
        CapturePoses();
        transform.localPosition = closedLocalPosition;
        transform.localRotation = closedLocalRotation;
    }

    bool isHovering;

    public bool IsOpen { get; private set; }

    void Awake()
    {
        // IMPORTANT: this must be captured in Awake, not Start.
        // SaveManager restores latch state (Open()/Close()) from
        // SceneManager.sceneLoaded, which fires after Awake/OnEnable but
        // BEFORE Start for objects in the newly loaded scene. If this were
        // captured in Start(), Continue would call Close()/Open() first
        // (moving this transform), and Start() would then cache that
        // corrupted position as "closed" forever for this session.
        CapturePoses();
    }

    void Start()
    {
        if (highlightVisual != null)
            highlightVisual.SetActive(false); // always start hidden, regardless of Inspector state
    }



    public void SetHovering(bool hovering)
    {
        isHovering = hovering;
        RefreshHighlight();
    }



    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        ApplyOpenPose();
        IsOpen = true;
        RefreshHighlight();
    }

    public void Close()
    {
        ApplyClosedPose();
        IsOpen = false;
        RefreshHighlight();
    }

    void RefreshHighlight()
    {
        if (highlightVisual == null) return;

        highlightVisual.SetActive(isHovering);

        if (isHovering && highlightRenderer != null)
        {
            // Uses _BaseColor directly since this targets URP Unlit shaders specifically
            // (same material setup used for your other highlights). If you're using a
            // different shader, swap this for highlightRenderer.material.color instead.
            highlightRenderer.material.SetColor("_BaseColor", IsOpen ? openHighlightColor : closedHighlightColor);
        }
    }
}