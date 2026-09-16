using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The congratulations moment: a fanfare, two party-popper cracks, and confetti over the
/// Story Complete panel.
///
/// ATTACH THIS TO: the same GameObject as StoryCompletePanel.
///
/// WHY THE CONFETTI IS BUILT FROM UI IMAGES AND NOT A PARTICLE SYSTEM
/// A ParticleSystem renders in the world, and a Screen Space - Overlay canvas is drawn after
/// everything in the world - so confetti spawned in front of the panel is painted over by it
/// and the learner sees nothing. The usual fixes all cost something: switch the canvas to
/// Screen Space - Camera and hand-manage sorting layers, add a second camera, or bake the
/// particles into a UI mesh.
///
/// Small Images parented to the panel avoid the whole problem. They are UI, so they obey the
/// canvas like every other element, they need no camera or sorting setup, and they cost
/// almost nothing - ninety quads is less work than most of the panel's own layout.
///
/// IF YOU WOULD RATHER USE A REAL PARTICLE SYSTEM, assign it to Particle Systems below and it
/// is played as well. The two are not exclusive.
///
/// EVERYTHING RUNS ON UNSCALED TIME, because a completion panel often arrives with the game
/// paused, and confetti frozen mid-air is worse than no confetti.
/// </summary>
[DisallowMultipleComponent]
public class StoryCompleteCelebration : MonoBehaviour
{
    [Header("Panel")]
    [Tooltip("Found on this object if left empty.")]
    [SerializeField] private StoryCompletePanel panel;

    [Header("Sound")]
    [Tooltip("The fanfare. SFX_ModuleComplete works well here.\n\n" +
             "Turn ON its Ignore Pitch Jitter - this is a tune, and a few percent of random " +
             "detune each time makes it sound out of tune rather than varied.")]
    [SerializeField] private SfxEntry fanfare = new SfxEntry();

    [Tooltip("The party-popper crack, played once per burst. SFX_ConfettiPop.")]
    [SerializeField] private SfxEntry popSound = new SfxEntry();

    [Tooltip("Beat after the panel appears before the fanfare starts, so it lands ON the panel " +
             "rather than underneath it arriving.")]
    [Min(0f)]
    [SerializeField] private float fanfareDelay = 0.15f;

    [Header("Confetti")]
    [Tooltip("Where the confetti lives. Use the panel's own root, or an empty child RectTransform " +
             "stretched over it. Leave empty to use this object's RectTransform.\n\n" +
             "Whatever you pick must be the LAST sibling, or the panel's own background will be " +
             "drawn on top of the confetti.")]
    [SerializeField] private RectTransform confettiParent;

    [Tooltip("Pieces per burst. 40-60 reads as celebration; past about 150 it reads as a mess and " +
             "starts to cost something on a phone.")]
    [Range(0, 200)]
    [SerializeField] private int piecesPerBurst = 55;

    [Tooltip("How many poppers go off. 2 fires from the bottom corners, which is the classic look.")]
    [Range(1, 4)]
    [SerializeField] private int bursts = 2;

    [Tooltip("Gap between bursts. A small stagger sounds and looks better than everything at once.")]
    [Min(0f)]
    [SerializeField] private float burstInterval = 0.18f;

    [Tooltip("Confetti colours, picked at random per piece. Leave empty for a default bright set.")]
    [SerializeField] private Color[] colors = new Color[0];

    [Header("Confetti motion")]
    [Tooltip("Size of one piece, in canvas units. Slightly taller than wide looks like paper.")]
    [SerializeField] private Vector2 pieceSize = new Vector2(13f, 20f);

    [Tooltip("How hard the popper fires, in canvas units per second.")]
    [SerializeField] private Vector2 launchSpeed = new Vector2(900f, 1500f);

    [Tooltip("Spread of the launch cone, in degrees either side of straight up.")]
    [Range(0f, 80f)]
    [SerializeField] private float launchSpread = 38f;

    [Tooltip("Downward pull. Higher makes the confetti fall faster and the moment shorter.")]
    [SerializeField] private float gravity = 1600f;

    [Tooltip("Air resistance. Without it the pieces fall like stones instead of drifting.")]
    [Range(0f, 4f)]
    [SerializeField] private float drag = 1.1f;

    [Tooltip("How far each piece sways side to side as it falls. This is what sells it as paper.")]
    [SerializeField] private float flutterAmount = 55f;

    [Tooltip("Seconds a piece lives. It fades over the last quarter.")]
    [SerializeField] private Vector2 lifetime = new Vector2(1.9f, 3.2f);

    [Header("Optional particle systems")]
    [Tooltip("Played alongside the UI confetti, if you have made your own. See the note on this " +
             "class about why these need a Screen Space - Camera canvas and sorting order set to " +
             "draw over the panel.")]
    [SerializeField] private ParticleSystem[] particleSystems = new ParticleSystem[0];

    [Header("Diagnostics")]
    [SerializeField] private bool logCelebration = false;

    // ==================================================================

    /// <summary>One piece of paper in flight.</summary>
    private class Piece
    {
        public RectTransform rect;
        public Image image;
        public Vector2 velocity;
        public float age;
        public float life;
        public float spin;
        public float flutterPhase;
        public float flutterSpeed;
        public Color color;
        public bool active;
    }

    private static readonly Color[] DefaultColors =
    {
        new Color(0.98f, 0.31f, 0.36f),   // red
        new Color(0.99f, 0.76f, 0.22f),   // amber
        new Color(0.34f, 0.84f, 0.53f),   // green - the project's accent
        new Color(0.30f, 0.67f, 0.98f),   // blue
        new Color(0.76f, 0.47f, 0.95f),   // violet
        new Color(1.00f, 1.00f, 1.00f),   // white
    };

    private readonly List<Piece> pieces = new List<Piece>();
    private RectTransform parentRect;
    private bool wasOpen;
    private bool celebrating;

    // ==================================================================

    private void Awake()
    {
        if (panel == null) panel = GetComponent<StoryCompletePanel>();

        if (panel == null)
            Debug.LogWarning("[StoryCompleteCelebration] No StoryCompletePanel on this object, so " +
                             "nothing will ever trigger the celebration.", this);

        parentRect = confettiParent != null ? confettiParent : transform as RectTransform;

        if (parentRect == null)
            Debug.LogWarning("[StoryCompleteCelebration] No RectTransform to put confetti in. " +
                             "Assign Confetti Parent, or put this component on a UI object.", this);
    }

    private void Update()
    {
        // Polled rather than hooked to onShown, so it works however the panel is opened -
        // StoryCompleteTrigger, a UnityEvent, ShowNow(), or a test button.
        if (panel != null)
        {
            bool open = panel.IsOpen;
            if (open != wasOpen)
            {
                wasOpen = open;
                if (open) Celebrate();
                else StopEverything();
            }
        }

        StepPieces();
    }

    // ==================================================================

    /// <summary>Fires the whole celebration. Public so a button can test it.</summary>
    [ContextMenu("Celebrate now (test)")]
    public void Celebrate()
    {
        if (celebrating) return;
        celebrating = true;

        if (logCelebration)
            Debug.Log($"[StoryCompleteCelebration] Celebrating - {bursts} burst(s) of " +
                      $"{piecesPerBurst}.", this);

        StartCoroutine(Routine());
    }

    private IEnumerator Routine()
    {
        if (fanfareDelay > 0f) yield return new WaitForSecondsRealtime(fanfareDelay);

        SfxPlayer.PlayCustom(fanfare);

        foreach (var ps in particleSystems)
            if (ps != null) ps.Play();

        for (int i = 0; i < bursts; i++)
        {
            // Bottom corners, angled inward and up - a popper held at the edge of the screen.
            // One burst goes from the middle instead, which reads better than a lone corner.
            float t = bursts == 1 ? 0.5f : (i + 0.5f) / bursts;
            SpawnBurst(t);

            // The retrigger guard is bypassed: several poppers in quick succession is the
            // intended sound, not one event heard twice.
            SfxPlayer.PlayCustom(popSound, bypassRetriggerGuard: true);

            if (burstInterval > 0f && i < bursts - 1)
                yield return new WaitForSecondsRealtime(burstInterval);
        }
    }

    private void StopEverything()
    {
        celebrating = false;
        StopAllCoroutines();

        foreach (var p in pieces)
        {
            p.active = false;
            if (p.rect != null) p.rect.gameObject.SetActive(false);
        }

        foreach (var ps in particleSystems)
            if (ps != null) ps.Stop();
    }

    // ==================================================================

    private void SpawnBurst(float horizontal01)
    {
        if (parentRect == null) return;

        Rect area = parentRect.rect;

        // Just below the bottom edge, so the pieces appear to be fired in from off-panel
        // rather than materialising in mid-air.
        var origin = new Vector2(
            Mathf.Lerp(area.xMin, area.xMax, horizontal01),
            area.yMin - pieceSize.y);

        // Outer poppers aim inward, so the confetti crosses the middle of the panel instead
        // of hugging the edges.
        float aim = (horizontal01 - 0.5f) * -2f * 22f;

        Color[] palette = (colors != null && colors.Length > 0) ? colors : DefaultColors;

        for (int i = 0; i < piecesPerBurst; i++)
            LaunchPiece(origin, aim, palette);
    }

    private void LaunchPiece(Vector2 origin, float aimDegrees, Color[] palette)
    {
        Piece piece = GetPooledPiece();
        if (piece == null) return;

        piece.rect.anchoredPosition = origin;
        piece.rect.sizeDelta = new Vector2(
            pieceSize.x * Random.Range(0.75f, 1.25f),
            pieceSize.y * Random.Range(0.75f, 1.25f));
        piece.rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        float angle = (90f + aimDegrees + Random.Range(-launchSpread, launchSpread)) * Mathf.Deg2Rad;
        float speed = Random.Range(launchSpeed.x, launchSpeed.y);
        piece.velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;

        piece.age = 0f;
        piece.life = Random.Range(lifetime.x, lifetime.y);
        piece.spin = Random.Range(-420f, 420f);
        piece.flutterPhase = Random.Range(0f, Mathf.PI * 2f);
        piece.flutterSpeed = Random.Range(3.5f, 7f);
        piece.color = palette[Random.Range(0, palette.Length)];
        piece.image.color = piece.color;

        piece.active = true;
        piece.rect.gameObject.SetActive(true);
    }

    /// <summary>
    /// Reuses a finished piece where possible.
    ///
    /// Pooled rather than Instantiate/Destroy because a burst is a hundred objects at once, and
    /// creating and destroying them every time the learner finishes a module is exactly the kind
    /// of garbage that shows up later as a stutter on a phone.
    /// </summary>
    private Piece GetPooledPiece()
    {
        foreach (var p in pieces)
            if (!p.active) return p;

        if (parentRect == null) return null;

        var go = new GameObject("Confetti", typeof(RectTransform), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parentRect, worldPositionStays: false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var image = go.GetComponent<Image>();
        image.raycastTarget = false;     // confetti must never eat a click on the buttons

        var piece = new Piece { rect = rect, image = image };
        pieces.Add(piece);
        return piece;
    }

    private void StepPieces()
    {
        // Unscaled: the completion panel can arrive with the game paused, and confetti frozen
        // in mid-air looks like the game has hung.
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        for (int i = 0; i < pieces.Count; i++)
        {
            Piece p = pieces[i];
            if (!p.active) continue;

            p.age += dt;
            if (p.age >= p.life)
            {
                p.active = false;
                p.rect.gameObject.SetActive(false);
                continue;
            }

            p.velocity.y -= gravity * dt;
            p.velocity -= p.velocity * (drag * dt);

            // Sideways sway, added to position rather than velocity so it does not compound
            // into the piece drifting off in one direction.
            float flutter = Mathf.Sin(p.flutterPhase + p.age * p.flutterSpeed) * flutterAmount * dt;

            p.rect.anchoredPosition += p.velocity * dt + new Vector2(flutter, 0f);
            p.rect.Rotate(0f, 0f, p.spin * dt);

            // Fade over the last quarter of its life, so pieces leave rather than blink out.
            float fadeStart = p.life * 0.75f;
            float alpha = p.age <= fadeStart ? 1f
                        : 1f - Mathf.InverseLerp(fadeStart, p.life, p.age);

            Color c = p.color;
            c.a = alpha;
            p.image.color = c;
        }
    }
}
