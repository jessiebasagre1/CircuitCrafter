using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Screen switching for the Main Menu: title screen -> main menu -> story / assessment / settings.
///
/// ATTACH THIS TO: the main menu CANVAS, not to any of the panels it controls.
/// A panel that is switched off cannot switch itself back on, and this component is what does
/// the switching. On the Canvas it is always active and always available.
///
/// WHAT IT REPLACES
/// Nothing. Your MainMenuController still owns the COC1-COC4 campaign buttons and their unlock
/// logic; AssessmentMenuController still owns the assessment ones. This only decides which
/// screen is visible, which neither of them was written to do.
///
/// THE ONE RULE
/// Every screen listed below must be a SIBLING of the others - all direct children of the
/// Canvas. If one screen is nested inside another, switching the outer one off takes the inner
/// one with it and you get a menu that goes blank with no error. Start() checks for this and
/// warns rather than letting you find out during a demo.
///
/// WIRING A BUTTON
/// On the button's OnClick, drag in the Canvas, then choose MenuNavigator > Show (GameObject)
/// from the TOP section of the dropdown - the static-parameter section. A GameObject slot
/// appears under it; drag the screen you want in there. The lower "Dynamic GameObject" section
/// is the wrong one: it has no slot and passes nothing.
/// </summary>
public class MenuNavigator : MonoBehaviour
{
    [Header("Screens")]
    [Tooltip("Every screen this navigator owns. Showing one switches all the others off.")]
    [SerializeField] private GameObject[] screens;

    [Tooltip("The screen shown when the scene opens - your title screen with the Start button.")]
    [SerializeField] private GameObject firstScreen;

    [Header("Housekeeping")]
    [Tooltip("Free and show the mouse cursor when the menu opens. Leave this ON: MouseLook locks " +
             "the cursor during gameplay, and coming back to the menu with it still locked makes " +
             "every button in the scene unclickable.")]
    [SerializeField] private bool freeCursorOnStart = true;

    [Tooltip("Reset Time.timeScale to 1. A pause menu or a dialog that paused the game and then " +
             "loaded this scene would otherwise leave the menu frozen.")]
    [SerializeField] private bool resetTimeScaleOnStart = true;

    [Tooltip("Clear any assessment run still marked as active. Reaching the menu means the run " +
             "is over - without this, a Story Mode launch afterwards would start in assessment.")]
    [SerializeField] private bool endAssessmentOnStart = true;

    /// <summary>The screen currently on show. Read-only, for anything that wants to ask.</summary>
    public GameObject Current { get; private set; }

    private readonly List<GameObject> history = new List<GameObject>();

    // ==================================================================

    private void Start()
    {
        if (freeCursorOnStart)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (resetTimeScaleOnStart) Time.timeScale = 1f;

        // Returning here from a gameplay scene ends whatever was running. AssessmentMode is a
        // static that survives LoadScene, which is exactly what makes it useful and also what
        // makes a stale one dangerous: it would silently turn the next campaign run into a
        // graded one.
        if (endAssessmentOnStart) AssessmentMode.End();

        WarnAboutNesting();

        history.Clear();
        Current = null;
        Apply(firstScreen);
    }

    // ==================================================================
    // Public - wire these to buttons
    // ==================================================================

    /// <summary>Show one screen and switch every other one off.</summary>
    public void Show(GameObject screen)
    {
        if (screen == null)
        {
            Debug.LogWarning("[MenuNavigator] A button called Show with nothing assigned. " +
                             "Check the GameObject slot under the function name in its OnClick.", this);
            return;
        }

        if (Current != null && Current != screen) history.Add(Current);
        Apply(screen);
    }

    /// <summary>
    /// Return to the previous screen. Optional - wiring each Back button to Show with an explicit
    /// target is easier to follow in the Inspector, and does the right thing even if a screen was
    /// opened by something other than this component.
    /// </summary>
    public void Back()
    {
        if (history.Count == 0)
        {
            Debug.Log("[MenuNavigator] Back pressed with nothing to go back to.", this);
            return;
        }

        int last = history.Count - 1;
        var previous = history[last];
        history.RemoveAt(last);

        Apply(previous);
    }

    /// <summary>Wire the Exit button here.</summary>
    public void Quit()
    {
        Debug.Log("[MenuNavigator] Quit requested.");

#if UNITY_EDITOR
        // Application.Quit does nothing in the Editor, which makes an Exit button look broken
        // every time you test it. This stops Play mode instead, so the button can be verified
        // without building.
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ==================================================================

    private void Apply(GameObject screen)
    {
        if (screen == null) return;

        bool listed = false;

        foreach (var s in screens)
        {
            if (s == null) continue;
            s.SetActive(s == screen);
            if (s == screen) listed = true;
        }

        if (!listed)
        {
            // Shown anyway - refusing would leave a blank menu, which is worse than a warning.
            screen.SetActive(true);
            Debug.LogWarning($"[MenuNavigator] '{screen.name}' is not in the Screens list, so " +
                             "showing it did not switch the others off. Add it to the list.", this);
        }

        Current = screen;
    }

    /// <summary>
    /// The failure this catches is silent and looks like a dead button: a screen parented inside
    /// another screen goes inactive along with its parent, and no amount of clicking brings it
    /// back. Worth one loop at startup.
    /// </summary>
    private void WarnAboutNesting()
    {
        if (screens == null) return;

        foreach (var a in screens)
        {
            if (a == null) continue;

            foreach (var b in screens)
            {
                if (b == null || b == a) continue;

                if (a.transform.IsChildOf(b.transform))
                    Debug.LogError($"[MenuNavigator] '{a.name}' is inside '{b.name}'. Screens must " +
                                   "be siblings - as it stands, hiding the outer one hides both and " +
                                   $"'{a.name}' can never be shown again.", this);
            }
        }

        if (firstScreen == null)
            Debug.LogError("[MenuNavigator] No First Screen assigned - the menu will open on " +
                           "whatever happened to be left active in the scene.", this);
    }
}
