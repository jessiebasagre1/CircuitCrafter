// CrimpedCablePickup.cs
using UnityEngine;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The finished UTP cable lying on the workbench. Same behaviour as CrimpingToolPickup -
    /// walk up, press E, it goes in your pocket - with one addition: it isn't there until the
    /// player has actually crimped both ends.
    ///
    /// "Spawning" here is just revealing an object that's already in the scene. No prefab, no
    /// Instantiate, nothing to wire up at runtime - the cable sits on the table from the start
    /// with its visual switched off, and switches on when crimping finishes.
    ///
    /// Note the split: THIS script's GameObject stays active the whole time (it has to, or it
    /// couldn't listen for the crimp finishing). Only `cableVisual` and the trigger collider get
    /// toggled. That's the same rule CableTypeSelectionUI follows.
    ///
    /// Setup:
    ///   1. Put your finished-cable model on the workbench where it should appear.
    ///   2. Create an empty GameObject next to it named "CrimpedCablePickup", add this script,
    ///      and add a trigger Collider sized to the interaction range (Interactable makes it a
    ///      trigger for you when you add the component).
    ///   3. Drag the cable model into `cableVisual`.
    ///   4. Set promptText to something like "Press E to pick up cable".
    ///   5. Leave BOTH objects active in the scene - this script hides the visual itself on Awake.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CrimpedCablePickup : Interactable
    {
        [Header("Cable")]
        [Tooltip("The cable model on the table. Hidden until both ends are crimped, then revealed.")]
        public GameObject cableVisual;

        [Tooltip("Optional. Switched OFF when the finished cable appears on the table - put the crimping " +
                 "station's own connector objects here so the player doesn't see the same cable in two " +
                 "places at once. Switched back ON by ResetForReplay() after a failed test.")]
        public GameObject[] hideWhenAvailable;

        [Header("Availability")]
        [Tooltip("Untick to have the cable sit on the table from the start, regardless of crimping - " +
                 "useful while you're testing this stage on its own.")]
        public bool requireCrimpingFinished = true;

        /// <summary>True once the cable is on the table and can be picked up.</summary>
        public bool IsAvailable { get; private set; }

        Collider trigger;
        bool subscribed;

        void Awake()
        {
            trigger = GetComponent<Collider>();
            SetAvailable(false);
        }

        void Start()
        {
            if (!requireCrimpingFinished)
            {
                SetAvailable(true);
                return;
            }

            var station = CrimpingStationController.Instance;
            if (station == null)
            {
                Debug.LogWarning("[CrimpedCablePickup] No CrimpingStationController in the scene, so this " +
                                 "cable can never appear. Untick Require Crimping Finished to place it anyway.", this);
                return;
            }

            // Already finished (e.g. the player crimped, wandered off, and came back).
            if (station.IsCrimped) SetAvailable(true);
            else Subscribe(station);
        }

        void OnDestroy()
        {
            if (CrimpingStationController.Instance != null && subscribed)
                CrimpingStationController.Instance.OnBothEndsCrimped -= HandleCrimpingFinished;
        }

        void Subscribe(CrimpingStationController station)
        {
            if (subscribed || station == null) return;
            station.OnBothEndsCrimped += HandleCrimpingFinished;
            subscribed = true;
        }

        void HandleCrimpingFinished()
        {
            SetAvailable(true);
            Debug.Log("[CrimpedCablePickup] Crimped cable is now on the table.", this);
        }

        void SetAvailable(bool available)
        {
            IsAvailable = available;
            if (cableVisual != null) cableVisual.SetActive(available);

            // The cable the player built is still sitting at the crimping station. Switch it off as
            // the finished one appears here, or the same cable exists twice in the world.
            if (available && hideWhenAvailable != null)
            {
                foreach (var obj in hideWhenAvailable)
                {
                    if (obj != null) obj.SetActive(false);
                }
            }

            // Turning the trigger off keeps the "Press E" prompt from appearing over an invisible
            // cable. Switching it back on re-fires OnTriggerEnter if the player is already standing
            // there, so they get the prompt without having to step out and back in.
            if (trigger != null) trigger.enabled = available;
        }

        public override void Interact()
        {
            if (!IsAvailable) return;

            if (CableInventory.Instance == null)
            {
                Debug.LogWarning("[CrimpedCablePickup] No CableInventory found in the scene.", this);
                return;
            }
            if (CableInventory.Instance.HasCable) return;

            CableInventory.Instance.PickUp();

            // Hide the model explicitly rather than relying on it being a child of this object -
            // the recommended layout makes them siblings, and deactivating this object alone would
            // leave the cable lying on the table after the player pocketed it.
            if (cableVisual != null) cableVisual.SetActive(false);

            gameObject.SetActive(false); // "goes into the pocket", same as the crimping tool
        }

        /// <summary>
        /// Puts this pickup back to how it was before the player ever crimped anything, so the
        /// whole procedure can be run again. Called by COC2ProcedureReset after a failed test.
        ///
        /// Three things have to be undone, and it's the third one that's easy to miss: Interact()
        /// deactivates this GameObject, and an inactive object's trigger never fires, so without
        /// re-activating it the "Press E" prompt would never come back even once the cable had
        /// been re-crimped.
        ///
        /// Safe to call on an inactive GameObject - a plain public method still runs, it's only
        /// coroutines and Invoke() that need an active object.
        /// </summary>
        public void ResetForReplay()
        {
            SetAvailable(false);

            // The station's own connector meshes went off when the finished cable appeared. The
            // player is about to build that cable again, so they have to come back.
            if (hideWhenAvailable != null)
            {
                foreach (var obj in hideWhenAvailable)
                {
                    if (obj != null) obj.SetActive(true);
                }
            }

            gameObject.SetActive(true);

            // Start() only subscribed if the station wasn't already crimped when the scene loaded.
            // Re-check here so a pickup that started life on an already-finished station still
            // hears about the NEXT crimp.
            if (requireCrimpingFinished)
                Subscribe(CrimpingStationController.Instance);
        }
    }
}