using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Puts a tick or a cross on the quiz choices once the question has been answered.
///
/// ATTACH THIS TO: the quiz panel, or the same object as QuizView. One per scene. Assign the
/// two sprites and nothing else.
///
/// IT REUSES THE TOGGLE'S OWN CHECKMARK. Every Unity Toggle already has a graphic that appears
/// when it is on - the tick inside the box. Rather than asking you to add another Image to
/// every choice row and keep four more references in step, this swaps the sprite already there
/// and, where needed, makes it visible on a toggle that is off. Nothing new to build.
///
/// IT MARKS THE RIGHT ANSWER TOO, NOT JUST THEIRS.
/// A learner who picks wrong and sees only a cross has been told they failed and not what the
/// answer was. The explanation text says it in words, but the eye goes to the choices, and on a
/// phone the explanation may be below the fold. So a wrong answer gets a cross AND the correct
/// row gets a tick - which is the difference between marking a test and teaching one. Turn
/// Also Mark The Correct Answer off if you would rather they only see their own result.
///
/// WHY SPRITES AND NOT JUST COLOUR. Roughly one boy in twelve cannot separate the green from
/// the red, this runs on projectors that wash both out, and a tick still reads as a tick in a
/// screenshot printed in black and white. The tint fields are there as reinforcement, not as
/// the message.
/// </summary>
public class QuizChoiceMarks : MonoBehaviour
{
    public static QuizChoiceMarks Instance { get; private set; }

    [Header("Sprites")]
    [Tooltip("Shown on the correct answer.")]
    [SerializeField] private Sprite correctSprite;

    [Tooltip("Shown on a wrong answer the player picked.")]
    [SerializeField] private Sprite wrongSprite;

    [Header("Behaviour")]
    [Tooltip("When the player answers wrongly, also tick the answer that WAS right.\n\n" +
             "Leave ON. A cross on its own tells a learner they were wrong and not what the " +
             "answer is, and the eye goes to the choices rather than to the explanation text.")]
    [SerializeField] private bool alsoMarkTheCorrectAnswer = true;

    [Header("Tint (optional reinforcement)")]
    [Tooltip("Tints the toggle's background as well. Leave the colours at white to skip this - " +
             "the sprites carry the meaning on their own.")]
    [SerializeField] private Color correctTint = Color.white;
    [SerializeField] private Color wrongTint = Color.white;
    [SerializeField] private Color untouchedTint = Color.white;

    [Header("Diagnostics")]
    [SerializeField] private bool verbose;

    /// <summary>Each toggle's checkmark sprite and colour as the scene author left them, taken
    /// the first time that toggle is touched. Restoring from here is what keeps question two
    /// from opening with question one's ticks still on it.</summary>
    private readonly Dictionary<Toggle, Sprite> originalSprites = new Dictionary<Toggle, Sprite>();
    private readonly Dictionary<Toggle, Color> originalTints = new Dictionary<Toggle, Color>();

    private bool warnedNoGraphic;
    private bool warnedNotAnImage;

    // ==================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // FULL PATHS, not names. Both copies are usually on objects called the same thing -
            // a duplicated canvas, a prefab dropped in twice - so a message naming them by name
            // reads as "duplicate on QuizCanvas, one already exists on QuizCanvas", which is
            // true and completely useless for finding either of them.
            Debug.LogWarning("[QuizChoiceMarks] TWO of these in the scene. Only one can work, and " +
                             "the marks will look dead if the surviving one is not the copy you " +
                             "assigned the sprites to.\n" +
                             $"    keeping  : {PathOf(Instance.transform)}" +
                             $"  (sprites: {Instance.SpriteSummary()})\n" +
                             $"    destroyed: {PathOf(transform)}" +
                             $"  (sprites: {SpriteSummary()})\n\n" +
                             "Find them with the Hierarchy search box: t:QuizChoiceMarks  " +
                             "(set the search to include inactive objects). Delete the one you " +
                             "did not set up.", this);
            Destroy(this);
            return;
        }

        Instance = this;

        if (correctSprite == null || wrongSprite == null)
            Debug.LogWarning($"[QuizChoiceMarks] on '{name}': " +
                             (correctSprite == null ? "Correct Sprite" : "Wrong Sprite") +
                             " is not assigned, so that mark can never appear.", this);
    }

    /// <summary>
    /// Says, once, that the hook into QuizView was never made.
    ///
    /// This component cannot mark anything on its own - QuizView is what knows an answer was
    /// given and which choice was right. Two lines have to be added there. Forget them and this
    /// component sits in the scene looking perfectly configured, sprites assigned, doing
    /// absolutely nothing, with no error anywhere. That is the single most likely reason the
    /// marks "do not work", so it is worth saying out loud rather than leaving it to be guessed.
    /// </summary>
    private void Start()
    {
        StartCoroutine(ComplainIfNeverCalled());
    }

    private System.Collections.IEnumerator ComplainIfNeverCalled()
    {
        // Long enough that a learner has plausibly answered a question by now.
        yield return new WaitForSecondsRealtime(90f);

        if (marked) yield break;

        Debug.LogWarning($"[QuizChoiceMarks] on '{name}': 90 seconds in and Mark() has never been " +
                         "called, so no answer has ever been marked.\n\n" +
                         "If a quiz has been answered in that time, the hook into QuizView is " +
                         "missing. In QuizView.HandleAnswerSelected, AFTER the loop that sets " +
                         "toggle.interactable = false, add:\n\n" +
                         "    QuizChoiceMarks.Mark(choiceToggles, choiceIndex, q.correctChoiceIndex);\n\n" +
                         "and where the next question resets the toggles, add:\n\n" +
                         "    QuizChoiceMarks.Clear(choiceToggles);", this);
    }

    private bool marked;

    /// <summary>True only while "Test marks now" is driving Apply. Without it the test looked
    /// exactly like a real answer in the console - it printed "the QuizView hook is working" and
    /// silenced the 90-second complaint - which is the opposite of what the test is for.</summary>
    private bool testing;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private string SpriteSummary()
    {
        if (correctSprite == null && wrongSprite == null) return "NEITHER assigned";
        if (correctSprite == null) return "correct MISSING";
        if (wrongSprite == null) return "wrong MISSING";
        return $"{correctSprite.name} / {wrongSprite.name}";
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    // ==================================================================
    // Called from QuizView - both are no-ops when this component is not in the scene, so the
    // two lines added there are safe in a scene that has no marks set up.
    // ==================================================================

    /// <summary>Mark the choices after an answer. Call it AFTER locking the toggles.</summary>
    public static void Mark(List<Toggle> toggles, int chosenIndex, int correctIndex)
    {
        if (Instance != null) Instance.Apply(toggles, chosenIndex, correctIndex);
    }

    /// <summary>Put every choice back to how the scene author left it. Call it when a new
    /// question is being set up.</summary>
    public static void Clear(List<Toggle> toggles)
    {
        if (Instance != null) Instance.Restore(toggles);
    }

    // ==================================================================

    private void Apply(List<Toggle> toggles, int chosenIndex, int correctIndex)
    {
        if (toggles == null) return;

        for (int i = 0; i < toggles.Count; i++)
        {
            Toggle toggle = toggles[i];
            if (toggle == null || !toggle.gameObject.activeSelf) continue;

            Remember(toggle);

            bool isChosen = i == chosenIndex;
            bool isCorrect = i == correctIndex;

            if (isCorrect && (isChosen || alsoMarkTheCorrectAnswer))
                Show(toggle, correctSprite, correctTint);
            else if (isChosen)
                Show(toggle, wrongSprite, wrongTint);
            else
                Hide(toggle);
        }

        if (testing) return;   // the test prints its own heading; it is not evidence of a hook

        if (verbose || !marked)
            Debug.Log($"[QuizChoiceMarks] Marked: chose {chosenIndex}, correct was {correctIndex}. " +
                      (marked ? "" : "(First time - QuizView is calling Mark, so the hook is in.)"), this);

        marked = true;
    }

    /// <summary>
    /// Shows a mark on a toggle whether or not it is on.
    ///
    /// Toggle drives graphic.enabled from its own value, so the correct answer - which the
    /// player did NOT pick, and whose toggle is therefore off - would have its tick hidden the
    /// moment it was set. Forcing enabled afterwards is what makes the right answer visible
    /// without turning the toggle on, which would fight the ToggleGroup and register as a
    /// second answer.
    ///
    /// It holds because the toggles are locked before this runs: nothing changes their value
    /// again, so nothing calls PlayEffect to undo it.
    /// </summary>
    private void Show(Toggle toggle, Sprite sprite, Color tint)
    {
        Graphic mark = toggle.graphic;

        if (mark == null)
        {
            if (!warnedNoGraphic)
            {
                warnedNoGraphic = true;
                Debug.LogWarning($"[QuizChoiceMarks] The toggle '{toggle.name}' has no Graphic " +
                                 "assigned (the checkmark Image inside it), so there is nothing to " +
                                 "put a tick or a cross on. Assign the Checkmark image to the " +
                                 "Toggle's Graphic field.", toggle);
            }
            return;
        }

        if (mark is Image image)
        {
            if (sprite != null)
            {
                image.sprite = sprite;

                // Read it straight back. An assignment that does not stick - because the Image
                // is a different one from the Graphic you are looking at, or something rewrites
                // it a moment later - is otherwise indistinguishable from never having run.
                if (verbose)
                    Debug.Log($"[QuizChoiceMarks]   {toggle.name}: set '{sprite.name}', " +
                              $"Image now holds '{(image.sprite != null ? image.sprite.name : "null")}' " +
                              $"on object '{PathOf(image.transform)}'", image);
            }
        }
        else
        {
            // The Graphic is something that cannot hold a sprite - nearly always a
            // TextMeshProUGUI used as the tick, which is a Graphic and so is perfectly legal
            // in the Toggle's Graphic field.
            //
            // This is the quietest failure in the whole component: the mark appears, in the
            // right place, at the right moment, and is simply the wrong mark - whatever
            // character the text already held. Nothing errors, because setting enabled on a
            // Graphic is valid whatever kind it is.
            if (!warnedNotAnImage)
            {
                warnedNotAnImage = true;
                Debug.LogWarning($"[QuizChoiceMarks] The Graphic on '{toggle.name}' is a " +
                                 $"{mark.GetType().Name}, not an Image, so a sprite cannot be put " +
                                 "on it. The tick and cross will never appear - you will see " +
                                 "whatever text that object already contains.\n\n" +
                                 "FIX: replace the Checkmark text object with a UI > Image, and " +
                                 "drag that Image into the Toggle's Graphic field. Then assign " +
                                 "Correct Sprite and Wrong Sprite here.\n\n" +
                                 "A text tick has a second problem anyway: Unity's default " +
                                 "LiberationSans has no check character, so it renders as an " +
                                 "empty box.", toggle);
            }
        }

        SetMarkVisible(mark, true);

        if (toggle.targetGraphic != null) toggle.targetGraphic.color = tint;
    }

    private void Hide(Toggle toggle)
    {
        if (toggle.graphic != null) SetMarkVisible(toggle.graphic, false);
        if (toggle.targetGraphic != null) toggle.targetGraphic.color = untouchedTint;
    }

    /// <summary>
    /// Shows or hides the checkmark the way TOGGLE does, which is not the way it looks.
    ///
    /// Unity's Toggle does not switch its graphic on and off. PlayEffect calls
    /// CrossFadeAlpha on the CanvasRenderer, so an unchecked toggle's checkmark is a fully
    /// ENABLED Image sitting at alpha 0. Setting Graphic.enabled therefore achieves nothing in
    /// either direction: enabled was already true, and the thing making it invisible is an
    /// alpha this component never touched.
    ///
    /// That is why marking looked completely dead. The sprite was being swapped correctly on
    /// every row; every one of them was transparent.
    ///
    /// canvasRenderer.SetAlpha is the same channel Toggle uses, applied instantly rather than
    /// faded. It holds because the toggles are locked before this runs - nothing changes their
    /// value again, so PlayEffect is never called to fade it back.
    /// </summary>
    private static void SetMarkVisible(Graphic mark, bool visible)
    {
        if (mark == null) return;

        mark.enabled = true;                                    // in case anything disabled it
        mark.canvasRenderer.SetAlpha(visible ? 1f : 0f);        // what actually decides it
    }

    private void Remember(Toggle toggle)
    {
        if (toggle.graphic is Image image && !originalSprites.ContainsKey(toggle))
            originalSprites[toggle] = image.sprite;

        if (toggle.targetGraphic != null && !originalTints.ContainsKey(toggle))
            originalTints[toggle] = toggle.targetGraphic.color;
    }

    private void Restore(List<Toggle> toggles)
    {
        if (toggles == null) return;

        foreach (Toggle toggle in toggles)
        {
            if (toggle == null) continue;

            if (toggle.graphic is Image image && originalSprites.TryGetValue(toggle, out Sprite s))
                image.sprite = s;

            if (toggle.targetGraphic != null && originalTints.TryGetValue(toggle, out Color c))
                toggle.targetGraphic.color = c;

            // Back under the Toggle's own control - ALPHA, not enabled, for the reason in
            // SetMarkVisible. A question that opened with a tick left at alpha 1 from the last
            // one would show an answer nobody had given yet.
            if (toggle.graphic != null)
            {
                toggle.graphic.enabled = true;
                toggle.graphic.canvasRenderer.SetAlpha(toggle.isOn ? 1f : 0f);
            }
        }
    }

    // ==================================================================

    /// <summary>
    /// Marks the choices right now, without QuizView, to prove whether the sprite swap works.
    ///
    /// This exists because "the marks do not work" has two completely separate causes that look
    /// identical on screen: the swap is broken, or nothing ever asks for it. Every check that
    /// depends on playing a quiz confuses the two. This one does not - it finds the toggles
    /// itself and marks the first as wrong, the second as correct.
    ///
    /// Run it in PLAY MODE with the quiz on screen:
    ///   sprites change  -> the swap works, and the missing piece is the QuizView hook
    ///   nothing changes -> the swap is the problem, and the log below says which part
    /// </summary>
    [ContextMenu("Test marks now (play mode)")]
    private void TestMarksNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[QuizChoiceMarks] Enter Play mode with a quiz question on screen, " +
                             "then run this again.", this);
            return;
        }

        var found = new List<Toggle>(GetComponentsInChildren<Toggle>(true));

        if (found.Count < 2)
        {
            Debug.LogWarning($"[QuizChoiceMarks] Found {found.Count} Toggle(s) under " +
                             $"'{PathOf(transform)}'. This component has to sit on or above the " +
                             "choice toggles for the test to reach them - move it onto the quiz " +
                             "panel.", this);
            return;
        }

        bool wasVerbose = verbose;
        verbose = true;
        testing = true;

        Debug.Log($"[QuizChoiceMarks] TEST: marking {found.Count} toggle(s) - " +
                  $"index 0 as WRONG, index 1 as CORRECT.", this);

        Apply(found, 0, 1);

        testing = false;
        verbose = wasVerbose;

        Debug.Log("[QuizChoiceMarks] TEST done. If the first two choices now show your Wrong and " +
                  "Check sprites, the swap works and the missing piece is the QuizView hook - " +
                  "QuizChoiceMarks.Mark(...) is not being called. If they did not change, read " +
                  "the per-toggle lines above.", this);
    }

    [ContextMenu("Report mark setup")]
    private void ReportSetup()
    {
        Debug.Log($"[QuizChoiceMarks] on '{name}':" +
                  $"\n    Correct Sprite : {(correctSprite != null ? correctSprite.name : "NOT ASSIGNED - the correct answer gets no tick")}" +
                  $"\n    Wrong Sprite   : {(wrongSprite != null ? wrongSprite.name : "NOT ASSIGNED - a wrong answer gets no cross")}" +
                  $"\n    Also mark the correct answer when they get it wrong: {alsoMarkTheCorrectAnswer}" +
                  $"\n    Tints          : correct {correctTint}, wrong {wrongTint}, untouched {untouchedTint}", this);
    }
}