using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Closes the crimping station once both cables have been built and passed, and tells the
    /// learner why if they come back to it.
    ///
    /// ATTACH THIS TO: the crimping station object - the one with CrimpingStationInteractable on it.
    /// Nothing to wire: it finds the gate and the station's own interactable by itself.
    ///
    /// ---------------------------------------------------------------------------------------------
    /// WHAT IT ACTUALLY FIXES, AND WHAT IT DOES NOT
    ///
    /// COC2BothCablesGate.Finish() already reports the task, fires OnAllCablesComplete and shows its
    /// allDoneLines - and it does NOT wind the procedure back. So the gate is not what sends the
    /// learner to the station a third time. Something else is still inviting them: a waypoint still
    /// pointing at the station, or a COC2ProcedureReset.RestartProcedure() wired into an Inspector
    /// event on the tester. This component does not care which - it takes the invitation away at the
    /// station itself, so no wiring anywhere can reopen work that is finished.
    ///
    /// Two things happen the moment the gate reports complete:
    ///   1. CrimpingStationInteractable is switched OFF. No "Press E", no touch Interact button, and
    ///      nothing happens if something else calls for the prompt. The station is scenery now.
    ///   2. If the learner walks up to it anyway, a dialog says the step is complete rather than
    ///      leaving them pressing a key that does nothing.
    ///
    /// WHY PROXIMITY IS MEASURED HERE RATHER THAN USING THE INTERACTABLE'S OWN TRIGGER. Interactable
    /// reports IsPlayerInRange from its trigger, and a switched-off component receives no trigger
    /// callbacks at all - so the very act of locking the station would blind it. A distance check
    /// costs one subtraction per frame and keeps working after the lock.
    /// ---------------------------------------------------------------------------------------------
    /// </summary>
    [DisallowMultipleComponent]
    public class COC2CrimpingStationLock : MonoBehaviour
    {
        [Header("References (leave empty - both are found automatically)")]
        [Tooltip("The both-cables gate. Empty uses COC2BothCablesGate.Instance.")]
        [SerializeField] private COC2BothCablesGate gate;

        [Tooltip("The station's interactable. Empty uses the one on this object, then the scene's.")]
        [SerializeField] private CrimpingStationInteractable stationInteractable;

        [Header("Locking")]
        [Tooltip("Switch the station's interactable off once both cables are done. Leave ON - this is " +
                 "the part that stops the learner being sent back into finished work.")]
        [SerializeField] private bool lockStationWhenDone = true;

        [Header("The notice")]
        [Tooltip("How close the learner has to come, in metres, before the station says it is finished.")]
        [SerializeField] private float noticeRadius = 3.5f;

        [Tooltip("How far they must walk away before it will say it again. Must be larger than the " +
                 "radius above, or the dialog reappears every time they shuffle on the spot.")]
        [SerializeField] private float rearmDistance = 6f;

        [Tooltip("ON: say it once for the whole session. OFF (default): say it again each time they " +
                 "come back, because a learner who returns to the station has not understood that the " +
                 "step is over, and is exactly the person who needs telling.")]
        [SerializeField] private bool sayItOnlyOnce;

        [SerializeField] private string speakerName = "Instructor";

        [Tooltip("What the learner reads at the finished station. One dialog line per entry.")]
        [TextArea(2, 6)]
        [SerializeField]
        private string[] completeLines =
        {
            "This station's work is finished. Both cables are made and both passed the tester - " +
            "there is nothing left to crimp here.",

            "Leave the bench as you found it and carry on with the next task.",
        };

        [Header("Diagnostics")]
        [SerializeField] private bool logState = true;

        private Transform player;
        private bool locked;
        private bool saidIt;

        // ==================================================================

        private void Start()
        {
            if (gate == null) gate = COC2BothCablesGate.Instance;
            if (stationInteractable == null) stationInteractable = GetComponent<CrimpingStationInteractable>();

            if (stationInteractable == null)
            {
                stationInteractable = FindAnyObjectByType<CrimpingStationInteractable>(
                    FindObjectsInactive.Include);

                if (stationInteractable != null && logState)
                    Debug.Log("[COC2CrimpingStationLock] This component is not on the crimping " +
                              "station, so it is locking '" + stationInteractable.name +
                              "' found in the scene instead. The notice's distance is still measured " +
                              "from THIS object - put it on the station, or assign Station " +
                              "Interactable and move this next to it.", this);
            }

            if (stationInteractable == null)
                Debug.LogWarning("[COC2CrimpingStationLock] No CrimpingStationInteractable in this " +
                                 "scene, so there is nothing to lock. Put this component on the " +
                                 "crimping station object.", this);

            if (rearmDistance <= noticeRadius)
                Debug.LogWarning("[COC2CrimpingStationLock] Rearm Distance (" + rearmDistance + ") is " +
                                 "not larger than Notice Radius (" + noticeRadius + "), so the notice " +
                                 "can repeat while the learner stands still. Raise it.", this);
        }

        private void Update()
        {
            // The gate may be created after this: found here rather than once in Start, for the same
            // reason the rest of this project retries its lookups.
            if (gate == null)
            {
                gate = COC2BothCablesGate.Instance;
                if (gate == null) return;
            }

            if (!gate.IsComplete) return;

            Lock();
            Notice();
        }

        // ==================================================================

        private void Lock()
        {
            if (locked || !lockStationWhenDone) return;

            locked = true;

            if (stationInteractable == null) return;

            stationInteractable.enabled = false;

            if (logState)
                Debug.Log("[COC2CrimpingStationLock] Both cables are done - '" +
                          stationInteractable.name + "' is closed. No prompt, no re-entry.", this);
        }

        private void Notice()
        {
            if (saidIt && sayItOnlyOnce) return;

            Transform who = Player();
            if (who == null) return;

            float distance = Vector3.Distance(who.position, transform.position);

            if (!saidIt)
            {
                if (distance > noticeRadius) return;

                // Not on top of another dialog: the gate's own all-done lines are still on screen in
                // the moment this becomes true, and two dialogs at once is not something
                // DialogManager is built for.
                if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

                saidIt = true;
                Say();
                return;
            }

            // Re-armed only once they have genuinely walked away, so standing at the bench does not
            // repeat it.
            if (distance > rearmDistance) saidIt = false;
        }

        private Transform Player()
        {
            if (player != null) return player;

            var interactor = FindAnyObjectByType<CSSSimulation.Interaction.PlayerInteractor>();
            if (interactor != null) player = interactor.transform;
            else if (Camera.main != null) player = Camera.main.transform;

            return player;
        }

        /// <summary>
        /// Builds the sequence the same way COC2BothCablesGate does - a throwaway asset with
        /// HideAndDontSave, so it stays out of the scene file and is not reported as a leak when Play
        /// mode ends.
        ///
        /// ignoreSuppression is deliberate, and for the same reason it is there in the gate: this is
        /// not a tutorial hint, it is the module telling the learner the step is over. An assessment
        /// switches hints off, and a learner standing at a dead station still has to be told why.
        /// </summary>
        private void Say()
        {
            if (DialogManager.Instance == null)
            {
                Debug.LogWarning("[COC2CrimpingStationLock] No DialogManager - the learner saw " +
                                 "nothing at the finished station.", this);
                return;
            }

            var sequence = ScriptableObject.CreateInstance<DialogSequence>();
            sequence.hideFlags = HideFlags.HideAndDontSave;

            if (completeLines != null)
            {
                foreach (string line in completeLines)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    sequence.lines.Add(new DialogLine { speakerName = speakerName, text = line });
                }
            }

            if (sequence.lines.Count == 0)
            {
                Debug.LogWarning("[COC2CrimpingStationLock] Complete Lines is empty, so nothing was " +
                                 "said at the finished station.", this);
                return;
            }

            DialogManager.Instance.ShowDialog(sequence, null, true);
        }

        // ==================================================================

        [ContextMenu("Report")]
        public void Report()
        {
            COC2BothCablesGate g = gate != null ? gate : COC2BothCablesGate.Instance;

            Debug.Log("[COC2CrimpingStationLock] REPORT\n" +
                      "  Gate            : " + (g != null ? g.name : "NOT FOUND") + "\n" +
                      "  Cables passed   : " + (g != null ? g.PassedCount.ToString() : "?") + "\n" +
                      "  Gate complete   : " + (g != null && g.IsComplete) + "\n" +
                      "  Station         : " + (stationInteractable != null ? stationInteractable.name : "none") + "\n" +
                      "  Station enabled : " + (stationInteractable != null && stationInteractable.enabled) + "\n" +
                      "  Locked by this  : " + locked + "\n" +
                      "  Notice said     : " + saidIt, this);
        }
    }
}
