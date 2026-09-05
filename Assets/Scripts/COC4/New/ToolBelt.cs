using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The learner's hands. Owns the grab key, remembers what is being carried, and
    /// hands it to the hotbar on arrival at the system unit.
    ///
    /// ATTACH THIS TO: the Managers object in each repair-site scene. One per scene.
    /// Assign Deposit Station (the SystemUnitStation) and, if you want a walk-up
    /// prompt, a TMP_Text. Everything else is optional.
    ///
    /// WHY THE KEY LIVES HERE AND NOT ON THE TOOL
    /// Two tools within range of each other would both hear G and both be picked up
    /// on one press, and every tool in the scene would be polling input every frame
    /// to answer a question only one of them can answer. One listener that picks the
    /// NEAREST candidate is the whole difference, and it also gives the prompt a
    /// single place to live instead of one label per tool.
    ///
    /// WHY IT WATCHES RepairStation.Current INSTEAD OF BEING WIRED TO onEnter
    /// A UnityEvent has to be wired in the Inspector to an object that is alive when
    /// the event fires, and this project has already been bitten twice by events
    /// aimed at components that switch their own GameObject off. Polling the static
    /// costs nothing, cannot be mis-wired, and works the same whether the station
    /// was entered with E or from the mobile button. Deposit() is still public, so
    /// you can wire onEnter as well if you prefer to see it in the Inspector.
    ///
    /// GRABBING ONLY WORKS OUTSIDE A STATION, on purpose. Inside a station the
    /// camera is inches from a motherboard and the player object is not moving, so
    /// "walk over and pick it up" has no meaning there - and a G press that quietly
    /// teleported a brush across the room would be a mechanic nobody could see.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class ToolBelt : MonoBehaviour
    {
        public static ToolBelt Instance { get; private set; }

        [Header("Input")]
        [Tooltip("The key that picks up a tool the learner is standing next to. Keep it off E - E enters and leaves stations - and off whatever RepairWorkGate uses for the diagnosis.")]
        [SerializeField] private KeyCode grabKey = KeyCode.G;

        [Tooltip("Found automatically by the Player tag if left empty.")]
        [SerializeField] private Transform player;

        [Header("UI")]
        [Tooltip("Optional. The walk-up prompt, e.g. \"Press G to pick up the brush\". Can be the same label the stations use - only one of them is ever in range at a time.")]
        [SerializeField] private TMP_Text promptText;

        [Tooltip("Optional, for mobile. Shown only while a tool is within reach. Wire its OnClick to RequestGrab().")]
        [SerializeField] private Button grabButton;

        [Header("Where it goes")]
        [Tooltip("The station that puts carried tools into the hotbar - the SYSTEM UNIT station, where the work happens. Leave empty to deposit at any station, which is rarely what you want: the monitor station has no hotbar on screen.")]
        [SerializeField] private RepairStation depositStation;

        [Tooltip("Leave on. Off means tools are carried but never reach the hotbar, and you would have to call Deposit() yourself from somewhere.")]
        [SerializeField] private bool depositOnEnter = true;

        [Header("Feedback")]
        [Tooltip("Shown the first time the learner picks a tool up. Leave empty and a line is written for you - it is worth having, because it is the only place the game explains that the tool appears once the case is open.")]
        [SerializeField] private DialogSequence pickedUpDialog;

        [Tooltip("Shown the first time a tool lands in the hotbar. Leave empty and a line is written for you.")]
        [SerializeField] private DialogSequence readyToUseDialog;

        [Tooltip("Turn both dialogs off if you would rather the prompts carried the whole explanation.")]
        [SerializeField] private bool showDialogs = true;

        // ==================================================================

        private readonly List<ToolPickup> carried = new List<ToolPickup>();

        private RepairStation lastStation;
        private bool shownPickedUpDialog;
        private bool shownReadyDialog;
        private DialogSequence generatedPickedUp;
        private DialogSequence generatedReady;

        /// <summary>How many tools the learner is holding but has not yet used.</summary>
        public int CarriedCount => carried.Count;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy(this), not Destroy(gameObject): Managers carries half the
                // scene's systems and removing the whole object to resolve a
                // duplicate component would take all of them with it.
                Debug.LogWarning($"[ToolBelt] Duplicate on '{name}' - one already exists on '{Instance.name}'. Removing this component only.", this);
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (player == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }

            if (player == null)
                Debug.LogWarning("[ToolBelt] No player found. Assign Player, or tag your player object \"Player\" - without it nothing can be picked up.", this);

            if (depositStation == null)
                Debug.LogWarning("[ToolBelt] No Deposit Station assigned, so tools will be handed to the hotbar at whichever station is entered first. Assign the system unit station.", this);

            if (grabButton != null)
            {
                grabButton.onClick.AddListener(RequestGrab);
                grabButton.gameObject.SetActive(false);
            }

            if (promptText != null) promptText.text = "";
        }

        private void Update()
        {
            WatchStations();
            WatchForPickups();
        }

        // ==================================================================
        // Arriving at the station
        // ==================================================================

        /// <summary>
        /// Notices the frame the learner arrives somewhere. Edge-triggered, not
        /// level-triggered: comparing against the last station means the deposit
        /// happens once on entry rather than being attempted every frame the learner
        /// stands there.
        /// </summary>
        private void WatchStations()
        {
            RepairStation current = RepairStation.Current;
            if (current == lastStation) return;

            lastStation = current;

            if (!depositOnEnter) return;
            if (current == null) return;
            if (depositStation != null && current != depositStation) return;

            Deposit();
        }

        /// <summary>
        /// Puts everything carried into the hotbar. Public so it can also be wired
        /// to RepairStation.onEnter if you would rather see the connection in the
        /// Inspector than trust the polling above.
        /// </summary>
        public void Deposit()
        {
            if (carried.Count == 0) return;
            if (HotbarManager.Instance == null)
            {
                Debug.LogWarning("[ToolBelt] No HotbarManager in this scene - the carried tools have nowhere to go.", this);
                return;
            }

            // Iterated backwards because successful deposits are removed from the
            // list as we go, and walking forwards over a list you are removing from
            // skips every second entry.
            for (int i = carried.Count - 1; i >= 0; i--)
            {
                ToolPickup tool = carried[i];
                if (tool == null) { carried.RemoveAt(i); continue; }
                if (tool.IsInHotbar) { carried.RemoveAt(i); continue; }

                HotbarSlot slot = HotbarManager.Instance.GetFirstEmptySlot();
                if (slot == null)
                {
                    Debug.LogWarning($"[ToolBelt] The hotbar is full, so '{tool.name}' stayed in hand. It will go in as soon as a slot frees up.", this);
                    continue;
                }

                slot.SetItem(tool.Item);
                tool.IsInHotbar = true;
                carried.RemoveAt(i);

                Debug.Log($"[ToolBelt] '{tool.name}' is in the hotbar.", this);

                ShowReadyDialog(tool);
            }
        }

        // ==================================================================
        // Picking things up
        // ==================================================================

        private void WatchForPickups()
        {
            ToolPickup nearest = NearestInReach();

            if (promptText != null)
                promptText.text = nearest != null ? $"Press {grabKey} to pick up {nearest.DisplayName}" : "";

            if (grabButton != null)
                grabButton.gameObject.SetActive(nearest != null);

            if (nearest == null) return;
            if (!Input.GetKeyDown(grabKey)) return;

            Grab(nearest);
        }

        /// <summary>Wire the mobile button here.</summary>
        public void RequestGrab()
        {
            ToolPickup nearest = NearestInReach();
            if (nearest != null) Grab(nearest);
        }

        private void Grab(ToolPickup tool)
        {
            if (tool == null || !tool.Grab()) return;

            carried.Add(tool);

            if (promptText != null) promptText.text = "";
            if (grabButton != null) grabButton.gameObject.SetActive(false);

            ShowPickedUpDialog(tool);
        }

        /// <summary>
        /// The nearest tool the learner could take right now, or null.
        ///
        /// Everything that could stop a pickup is checked here rather than at the
        /// key press, so the prompt and the key can never disagree - a prompt that
        /// says "press G" while G does nothing is worse than no prompt at all.
        /// </summary>
        private ToolPickup NearestInReach()
        {
            if (player == null) return null;

            // Not while a dialog owns the keyboard, and not from inside a station -
            // the learner is not walking anywhere from in there.
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return null;
            if (RepairStation.Current != null) return null;
            if (TicketDiagnosisPanel.BlocksStationInput) return null;

            Vector3 here = player.position;

            ToolPickup best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < ToolPickup.All.Count; i++)
            {
                ToolPickup tool = ToolPickup.All[i];
                if (tool == null) continue;
                if (!tool.IsInRange(here)) continue;

                float d = tool.DistanceTo(here);
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = tool;
            }

            return best;
        }

        // ==================================================================
        // Dialogs
        // ==================================================================

        private void ShowPickedUpDialog(ToolPickup tool)
        {
            if (!showDialogs || shownPickedUpDialog) return;
            if (DialogManager.Instance == null) return;

            shownPickedUpDialog = true;

            if (pickedUpDialog != null)
            {
                DialogManager.Instance.ShowDialog(pickedUpDialog);
                return;
            }

            // Built once and reused. A DialogSequence created per pickup would leak
            // a ScriptableObject every time the learner collected a tool.
            if (generatedPickedUp == null)
                generatedPickedUp = ScriptableObject.CreateInstance<DialogSequence>();

            generatedPickedUp.lines.Clear();
            generatedPickedUp.lines.Add(new DialogLine
            {
                speakerName = "You",
                text = $"Got {tool.DisplayName}. I will have it in reach once I have the machine open - " +
                       "it goes straight into my kit when I get to the system unit."
            });

            DialogManager.Instance.ShowDialog(generatedPickedUp);
        }

        private void ShowReadyDialog(ToolPickup tool)
        {
            if (!showDialogs || shownReadyDialog) return;
            if (DialogManager.Instance == null) return;

            shownReadyDialog = true;

            if (readyToUseDialog != null)
            {
                DialogManager.Instance.ShowDialog(readyToUseDialog);
                return;
            }

            if (generatedReady == null)
                generatedReady = ScriptableObject.CreateInstance<DialogSequence>();

            generatedReady.lines.Clear();
            generatedReady.lines.Add(new DialogLine
            {
                speakerName = "You",
                text = $"{Capitalise(tool.DisplayName)} is in my kit. Drag it out and work it across the dirt - " +
                       "short strokes, and keep going until the surface is clear."
            });

            DialogManager.Instance.ShowDialog(generatedReady);
        }

        private static string Capitalise(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}
