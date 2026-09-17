using UnityEngine;
using TMPro;

/// <summary>
/// Swaps a prompt's wording for the device it is actually being read on - "Press Space to
/// continue" on a keyboard, "Tap to continue" on a phone.
///
/// ATTACH THIS TO: the TMP_Text showing the prompt. It finds the text on its own object, so
/// there is usually nothing to assign but the two strings.
///
/// WHY THIS IS NOT COSMETIC. A prompt that names a control the device does not have is worse
/// than no prompt at all: the learner reads "Press Space", finds no Space bar, and concludes
/// the dialog is stuck - when tapping the screen would have worked the whole time. They are
/// being told, in plain words, that the thing they need to do is impossible. On a CSS
/// assessment that is a learner sitting in front of a frozen screen losing marked time.
///
/// THE WORDING IS NOT THE ONLY THING THAT CHANGES. Touch prompts should describe a gesture,
/// not a key with a different name - "Tap to continue", not "Press the screen". Say what the
/// hand does.
///
/// IT RE-CHECKS EVERY TIME THE OBJECT IS SWITCHED ON, not once at startup. Dialog panels are
/// switched on and off constantly, and the editor preview toggle below can be flipped during
/// play - a value read once in Awake would be stale for the rest of the session.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class PlatformPromptText : MonoBehaviour
{
    [Header("Wording")]
    [Tooltip("Shown on a keyboard build. Name the actual key.")]
    [TextArea(1, 3)]
    [SerializeField] private string desktopText = "Press Space to continue";

    [Tooltip("Shown on a touch build. Describe the GESTURE, not a key - \"Tap to continue\", " +
             "not \"Press the screen to continue\".")]
    [TextArea(1, 3)]
    [SerializeField] private string touchText = "Tap to continue";

    [Header("Diagnostics")]
    [Tooltip("Say which wording was chosen and why, once per switch-on. Worth leaving on until " +
             "you have seen it pick the touch line on a real device.")]
    [SerializeField] private bool logChoice;

    private TMP_Text label;
    private MobileControlsVisibility touchSource;

    // ==================================================================

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        Apply();
    }

    /// <summary>Also safe to call by hand - from a settings change, say.</summary>
    public void Apply()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (label == null) return;

        bool touch = TouchWanted();
        label.text = touch ? touchText : desktopText;

        if (logChoice)
            Debug.Log($"[PlatformPromptText] '{name}' -> \"{label.text}\" " +
                      $"(touch={touch}).", this);
    }

    /// <summary>
    /// Asks MobileControlsVisibility first, and only falls back to the raw platform check.
    ///
    /// WHY NOT JUST Application.isMobilePlatform. Because that is FALSE in the Editor no matter
    /// what the build target is, so every prompt would read the desktop wording while you are
    /// previewing the touch layout - and the one place you would notice the mistake is the one
    /// place it cannot show. MobileControlsVisibility already owns this decision for the touch
    /// canvas, editor preview switch included, so asking it keeps the prompt and the controls
    /// telling the same story. A third copy of the platform test is a third thing to get out of
    /// step.
    /// </summary>
    private bool TouchWanted()
    {
        if (touchSource == null)
            touchSource = FindAnyObjectByType<MobileControlsVisibility>(FindObjectsInactive.Include);

        if (touchSource != null) return touchSource.Wanted;

        return Application.isMobilePlatform;
    }

    // ==================================================================

#if UNITY_EDITOR
    /// <summary>Keeps the Scene view honest while you type the strings.</summary>
    private void OnValidate()
    {
        if (!Application.isPlaying && isActiveAndEnabled) Apply();
    }
#endif

    [ContextMenu("Report prompt")]
    private void ReportPrompt()
    {
        bool touch = TouchWanted();

        Debug.Log($"[PlatformPromptText] on '{name}':" +
                  $"\n    touch wanted : {touch}" +
                  $"\n    source       : {(touchSource != null ? "MobileControlsVisibility on '" + touchSource.name + "'" : "Application.isMobilePlatform (no MobileControlsVisibility in this scene)")}" +
                  $"\n    desktop      : \"{desktopText}\"" +
                  $"\n    touch        : \"{touchText}\"" +
                  $"\n    showing      : \"{(label != null ? label.text : "no TMP_Text")}\"", this);
    }
}
