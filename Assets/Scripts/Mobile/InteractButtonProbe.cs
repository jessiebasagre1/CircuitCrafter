using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.Interaction
{
    /// <summary>
    /// Temporary diagnostic. Reports why the mobile Interact button will not appear.
    ///
    /// ATTACH THIS TO: any always-active object - your Managers object is ideal. It announces
    /// itself in Start(), so if you do not see that line the component is not running and
    /// nothing else it says means anything.
    ///
    /// EVERYTHING IS LOGGED AS A WARNING, ON PURPOSE. The Console's info filter is off often
    /// enough that a silent probe reads as a dead script.
    ///
    /// WHAT IT ANSWERS
    /// A button fails to appear for exactly one of four reasons, and this separates them:
    ///
    ///   1. Nothing ever calls SetActive(true)  - the interactor is not firing
    ///   2. Something calls SetActive(false) again immediately - a second component is fighting
    ///   3. It IS active, but an ancestor is not - so it renders nothing
    ///   4. The reference points at a different object than the one you are watching
    ///
    /// Reason 2 is invisible in the Inspector, because the Inspector shows you the result of the
    /// last writer to run. The FLIPS counter below is what exposes it: a button being toggled
    /// every frame is not a wiring problem, it is two owners.
    ///
    /// Press the report key at any time. Delete the component once it has told you the answer.
    /// </summary>
    public class InteractButtonProbe : MonoBehaviour
    {
        [Tooltip("The button that will not appear. Leave empty to use whatever " +
                 "PlayerInteractor.Instance has in its Interact Button field.")]
        [SerializeField] private Button interactButton;

        [Tooltip("Press any time to dump a report.")]
        [SerializeField] private KeyCode reportKey = KeyCode.I;

        [Tooltip("Log every change of the button's active state as it happens. This is what " +
                 "catches a second component switching it back off.")]
        [SerializeField] private bool logFlips = true;

        private bool lastActive;
        private bool haveLast;
        private int flips;
        private float flipWindowStart;
        private int flipsThisSecond;

        // ------------------------------------------------------------------

        private void Start()
        {
            Debug.LogWarning($"[InteractButtonProbe] ACTIVE on '{Path(transform)}'. " +
                             $"Press {reportKey} for a report.");
            Report();
        }

        private void Update()
        {
            Button target = Target();

            if (target != null)
            {
                bool active = target.gameObject.activeInHierarchy;

                if (haveLast && active != lastActive)
                {
                    flips++;
                    flipsThisSecond++;

                    if (logFlips)
                        Debug.LogWarning($"[InteractButtonProbe] '{target.name}' -> " +
                                         (active ? "SHOWN" : "HIDDEN") + $"  (flip #{flips})");
                }

                lastActive = active;
                haveLast = true;
            }

            if (Time.unscaledTime - flipWindowStart >= 1f)
            {
                if (flipsThisSecond > 4)
                    Debug.LogWarning($"[InteractButtonProbe] {flipsThisSecond} active-state changes in " +
                                     "one second. TWO components are fighting over this button - see the " +
                                     "SUSPECTS section of the report.");

                flipsThisSecond = 0;
                flipWindowStart = Time.unscaledTime;
            }

            if (Input.GetKeyDown(reportKey)) Report();
        }

        // ------------------------------------------------------------------

        private Button Target()
        {
            if (interactButton != null) return interactButton;
            return PlayerInteractor.Instance != null ? PlayerInteractor.Instance.interactButton : null;
        }

        private void Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== InteractButtonProbe ===");

            DescribeInteractors(sb);
            DescribeButton(sb);
            DescribeSuspects(sb);
            DescribeInteractables(sb);
            DescribePlayerTag(sb);

            Debug.LogWarning(sb.ToString());
        }

        /// <summary>
        /// The COC2 interactor, and how many of it exist. Instance is a plain static assigned in
        /// Awake, so with two in the scene the LAST one to wake wins and the other is inert - and
        /// nothing warns you.
        /// </summary>
        private void DescribeInteractors(StringBuilder sb)
        {
            var mine = Object.FindObjectsByType<PlayerInteractor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            sb.AppendLine($"--- CSSSimulation.Interaction.PlayerInteractor: {mine.Length} in scene ---");

            foreach (var p in mine)
            {
                sb.AppendLine($"  on '{Path(p.transform)}'"
                              + (PlayerInteractor.Instance == p ? "   <-- this one is Instance" : "")
                              + (p.gameObject.activeInHierarchy ? "" : "   [INACTIVE - its Awake never ran]"));
                sb.AppendLine($"     Interact Button : {(p.interactButton != null ? Path(p.interactButton.transform) : "NOT ASSIGNED")}");
                sb.AppendLine($"     Prompt UI       : {(p.promptUI != null ? p.promptUI.name : "not assigned")}");
            }

            if (mine.Length == 0)
                sb.AppendLine("  NONE. Nothing can ever show the button. Put one on the player.");

            if (PlayerInteractor.Instance == null)
                sb.AppendLine("  Instance is NULL - no PlayerInteractor has run Awake(). If the only one " +
                              "is on an object that starts inactive, that is the whole problem.");
        }

        /// <summary>
        /// activeSelf true with activeInHierarchy false means a PARENT is off - almost always
        /// MobileControlsCanvas, which MobileControlsVisibility disables whenever
        /// Application.isMobilePlatform is false. That is false in the Editor, always, whatever
        /// build target is selected.
        /// </summary>
        private void DescribeButton(StringBuilder sb)
        {
            Button target = Target();

            if (target == null)
            {
                sb.AppendLine("--- Button: NONE ---");
                sb.AppendLine("  No button assigned here and none on PlayerInteractor.Instance.");
                return;
            }

            var go = target.gameObject;
            sb.AppendLine($"--- Button '{Path(target.transform)}' ---");
            sb.AppendLine($"  activeSelf        : {go.activeSelf}");
            sb.AppendLine($"  activeInHierarchy : {go.activeInHierarchy}");
            sb.AppendLine($"  interactable      : {target.interactable}");
            sb.AppendLine($"  active-state flips: {flips} since start");

            if (go.activeSelf && !go.activeInHierarchy)
            {
                Transform t = go.transform.parent;
                while (t != null)
                {
                    if (!t.gameObject.activeSelf)
                    {
                        sb.AppendLine($"  BLOCKED BY PARENT : '{Path(t)}' is switched off, so this button " +
                                      "cannot render however many times it is set active.");
                        break;
                    }
                    t = t.parent;
                }
            }

            var image = go.GetComponent<Graphic>();
            if (image == null)
                sb.AppendLine("  NO Graphic on the button - nothing to draw and nothing to raycast.");
            else
                sb.AppendLine($"  graphic alpha     : {image.color.a}  raycastTarget: {image.raycastTarget}");
        }

        /// <summary>
        /// The two COC1 components that drive a mobile button from Update(), every frame, and so
        /// beat anything that pushes once on a trigger enter.
        /// </summary>
        private void DescribeSuspects(StringBuilder sb)
        {
            sb.AppendLine("--- SUSPECTS (COC1 components that write button visibility every frame) ---");

            var coc1 = Object.FindObjectsByType<global::PlayerInteractor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var p in coc1)
            {
                sb.AppendLine($"  COC1 PlayerInteractor on '{Path(p.transform)}'"
                              + (p.isActiveAndEnabled ? "  [RUNNING]" : "  [not running]"));
                sb.AppendLine("     Its Update() ends with UpdateMobileButtons(), which does");
                sb.AppendLine("     interactButton.SetActive(nearbyDrive != null). COC2 has no USB drives,");
                sb.AppendLine("     so if its Interact Button field points at the same button it hides it");
                sb.AppendLine("     EVERY FRAME. That field is private - check it in the Inspector.");
            }

            var relays = Object.FindObjectsByType<global::StationInteractionRelay>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var r in relays)
            {
                sb.AppendLine($"  StationInteractionRelay on '{Path(r.transform)}'"
                              + (r.isActiveAndEnabled ? "  [RUNNING]" : "  [not running]"));
                sb.AppendLine("     COC2 has no StationCameraSwitch, so NearbyOrActive is always null and");
                sb.AppendLine("     this switches its Button Root off every frame. Remove it from COC2.");
            }

            if (coc1.Length == 0 && relays.Length == 0)
                sb.AppendLine("  None found. If the flip counter above is still climbing, something else " +
                              "in your own scripts is toggling this object.");
        }

        private void DescribeInteractables(StringBuilder sb)
        {
            var all = Object.FindObjectsByType<Interactable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            sb.AppendLine($"--- Interactables: {all.Length} in scene ---");

            foreach (var it in all)
            {
                var col = it.GetComponent<Collider>();
                sb.AppendLine($"  '{Path(it.transform)}' ({it.GetType().Name})");
                sb.AppendLine($"     inRange={it.IsPlayerInRange}"
                              + $"  active={it.gameObject.activeInHierarchy}"
                              + (col == null
                                  ? "  NO COLLIDER - its trigger can never fire"
                                  : $"  collider={col.GetType().Name} isTrigger={col.isTrigger}"
                                    + (col.isTrigger ? "" : "  <-- NOT A TRIGGER, OnTriggerEnter never runs")));
            }

            if (all.Length == 0)
                sb.AppendLine("  NONE. Nothing in this scene can put an interactable in range.");
        }

        /// <summary>
        /// Interactable.OnTriggerEnter does other.CompareTag("Player") against the COLLIDER's own
        /// GameObject. Tagging a parent rig and leaving the CharacterController object untagged is
        /// the usual miss, and it fails silently.
        /// </summary>
        private void DescribePlayerTag(StringBuilder sb)
        {
            GameObject[] tagged;
            try
            {
                tagged = GameObject.FindGameObjectsWithTag("Player");
            }
            catch
            {
                sb.AppendLine("--- Player tag: the tag 'Player' is not defined in this project ---");
                return;
            }

            sb.AppendLine($"--- Objects tagged 'Player': {tagged.Length} ---");

            foreach (var go in tagged)
            {
                var col = go.GetComponent<Collider>();
                sb.AppendLine($"  '{Path(go.transform)}'"
                              + (col == null
                                  ? "  NO COLLIDER ON THIS OBJECT - it will never enter a trigger"
                                  : $"  collider={col.GetType().Name}"));
            }

            if (tagged.Length == 0)
                sb.AppendLine("  NONE. Interactable.OnTriggerEnter checks CompareTag(\"Player\") on the " +
                              "collider's own object, so nothing will ever register as in range.");
        }

        // ------------------------------------------------------------------

        private static string Path(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
