using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Tiny "Progress Saved" toast. Subscribes to SaveManager.OnProgressSaved
/// (only fired for real milestones — see SaveCheckpoint's showProgressToast
/// param) and fades a small label in/out.
///
/// Setup:
///   1. Add a small TMP_Text (e.g. "Progress Saved") to your gameplay
///      Canvas, low-key corner placement works well (bottom-left/right).
///   2. Put this script on that same object (or a CanvasGroup wrapping it).
///   3. Assign the CanvasGroup + TMP_Text fields below.
///   4. Leave it inactive/alpha-0 by default — this script handles show/hide.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class SaveToast : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text label;
    [SerializeField] private string message = "Progress Saved";
    [SerializeField] private float visibleDuration = 1.5f;
    [SerializeField] private float fadeDuration = 0.25f;

    private Coroutine activeRoutine;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        if (label != null) label.text = message;
    }

    private void Start()
    {
        // Scene-local, same as the rest of the gameplay UI — re-subscribes
        // fresh every scene load, same pattern as TaskCheckpointAutosaver.
        if (SaveManager.Instance != null)
            SaveManager.Instance.OnProgressSaved += HandleProgressSaved;
    }

    private void OnDisable()
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.OnProgressSaved -= HandleProgressSaved;
    }

    private void HandleProgressSaved()
    {
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(ShowRoutine());
    }

    private IEnumerator ShowRoutine()
    {
        yield return Fade(0f, 1f);
        yield return new WaitForSeconds(visibleDuration);
        yield return Fade(1f, 0f);
        activeRoutine = null;
    }

    private IEnumerator Fade(float from, float to)
    {
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime; // unscaled — still shows if timeScale is 0 (paused)
            canvasGroup.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = to;
    }
}