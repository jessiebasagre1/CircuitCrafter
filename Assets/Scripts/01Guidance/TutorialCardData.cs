using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// One short tutorial: a title, two to four lines of explanation, an optional picture or flipbook
    /// that DEMONSTRATES it, and a "Now you try" line that hands control back to the learner.
    ///
    /// CREATE: Project window > Create > CircuitCrafter > Guidance > Tutorial Card. The builder's
    /// "Create Starter Tutorial Cards" makes the controls and feature cards for you.
    ///
    /// WRITING A GOOD CARD
    ///   - Explain in 2-4 lines. If it needs more, it is two cards, or it belongs in the job order.
    ///   - Say WHY once ("so static does not damage the board"), not just what to press.
    ///   - Finish with a Try It line that names one action the learner can do right now.
    ///   - Give touch players their own wording in Body (Touch) - "tap", "drag", never "press E".
    ///
    /// Shown by TutorialCardPanel, which checks GuidancePolicy for the card's Kind first - so a
    /// Procedure card never appears in an assessment, whoever asks for it.
    /// </summary>
    [CreateAssetMenu(fileName = "Tutorial_", menuName = "CircuitCrafter/Guidance/Tutorial Card")]
    public class TutorialCardData : ScriptableObject
    {
        [Tooltip("Unique id used to remember that this learner has seen it. Leave blank to use the asset name.")]
        public string cardId = "";

        [Tooltip("What kind of help this is. Decides which modes may show it (GuidanceSettings).")]
        public GuidanceKind kind = GuidanceKind.ProcedureTutorial;

        [Header("Explain")]
        public string title = "New procedure";

        [TextArea(3, 8)]
        [Tooltip("Keyboard and mouse wording. Lines starting with '- ' become bullets.")]
        public string body = "";

        [TextArea(3, 8)]
        [Tooltip("Optional touch wording. Leave blank to use Body on every device.")]
        public string bodyTouch = "";

        [Header("Demonstrate (optional)")]
        [Tooltip("A still picture - a key, a gesture, the part and where it goes.")]
        public Sprite image;

        [Tooltip("A short flipbook played on the card when the learner presses Show Me. Overrides Image while playing.")]
        public Sprite[] demoFrames = new Sprite[0];

        [Min(1f)] public float demoFramesPerSecond = 6f;

        [Header("Let the player perform")]
        [Tooltip("One action to do now, e.g. 'Now you try: open the side panel.' Shown under the body.")]
        public string tryIt = "";

        [Header("Behaviour")]
        [Tooltip("Hold the player still and free the cursor until Got It is pressed. Use for a new PROCEDURE the learner must read. Leave off for control and feature tips, which should never interrupt.")]
        public bool pauseGameplay = false;

        [Tooltip("Show only the first time for each learner.")]
        public bool showOnce = true;

        [Tooltip("Hide by itself after this many seconds. 0 = stay until dismissed or until the action is done.")]
        [Min(0f)] public float autoHideSeconds = 0f;

        public string Id => string.IsNullOrEmpty(cardId) ? name : cardId;

        public string BodyFor(bool touch) =>
            touch && !string.IsNullOrEmpty(bodyTouch) ? bodyTouch : body;

        public bool HasDemo => demoFrames != null && demoFrames.Length > 0;
    }
}
