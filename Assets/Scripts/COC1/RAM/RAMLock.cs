using UnityEngine;

/// <summary>
/// A single RAM socket's retention clip. Click to toggle open/closed. Exposes
/// Open()/Close() so other scripts can control it too — used automatically
/// by RAMPartItem to close the clip the moment a stick is reinstalled.
///
/// Hover highlight: shows only while the mouse is over this lock. Color
/// reflects state — a neutral color while closed, green once open.
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

    Vector3 closedPosition;
    Quaternion closedRotation;
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
        closedPosition = transform.position;
        closedRotation = transform.rotation;
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
        transform.position = openPosition.position;
        transform.rotation = openPosition.rotation;
        IsOpen = true;
        RefreshHighlight();
    }

    public void Close()
    {
        transform.position = closedPosition;
        transform.rotation = closedRotation;
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