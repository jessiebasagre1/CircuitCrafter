// COC2BothCablesGate.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Makes COC 2 require BOTH cable types. Passing the tester with a straight-through is now
    /// half the job: the learner is told to build a crossover as well, the whole procedure winds
    /// back to the cable choice, and only when every listed type has passed does the testing task
    /// actually complete.
    ///
    /// WHY A SEPARATE COMPONENT
    /// Everything it needs is already public. It is add-and-assign: nothing here edits an existing
    /// script, it just listens for a pass, remembers which cable type it was, and drives the
    /// COC2ProcedureReset you already have.
    ///
    /// ONE EDIT IS REQUIRED, and only one - a public method on COC2StageDialogs so this can clear
    /// its remembered verdict. See the guide; without it the SECOND cable's pass/fail dialog
    /// never appears, because that component still thinks it has already reported a result.
    ///
    /// SETUP
    ///   1. Put this on the same always-active Managers object as COC2StageDialogs.
    ///   2. Fill Required Cable Types with your straight-through and crossover CableTypeSO assets
    ///      - the same two the CableTypeSelectionUI uses.
    ///   3. CLEAR the Task Id field on NetworkTestStation. This component reports that task
    ///      instead, once, when everything is done. Leave it filled and COC 2 completes after the
    ///      first cable.
    ///   4. On COC2StageDialogs, add an OnTestPassed entry pointing at this component's
    ///      NotifyTestPassed().
    ///   5. On the cable-choice panel, repoint the two buttons from CableTypeSelectionUI to THIS
    ///      component's ChooseStraightThrough() / ChooseCrossover(), so a type that has already
    ///      been built can be refused.
    ///
    /// ORDER IS NOT FORCED. The learner may build either type first; they are then required to
    /// build the other. Add a third CableTypeSO and it will require three.
    ///
    /// WHEN BOTH ARE DONE, cable-making is closed for good: the "step complete" dialog plays and the
    /// learner is taken out of the tester close-up back to walking. From then on the crimping station
    /// and the cable tester answer E with a short "this stage is complete" dialog instead of opening
    /// (they call RefuseBecauseComplete() at the top of their Interact), and the finished-cable
    /// pickup is switched off. Nothing sends them back to a station for a job that is finished.
    /// Both behaviours can be switched off below.
    /// </summary>
    public class COC2BothCablesGate : MonoBehaviour
    {
        public static COC2BothCablesGate Instance { get; private set; }

        // ------------------------------------------------------------------

        [Header("What must be built")]
        [Tooltip("Every cable type the learner has to build and pass. Normally two: straight-through " +
                 "and crossover. Must be the SAME asset references the CableTypeSelectionUI uses - " +
                 "two assets with the same name are two different cables as far as this is concerned.")]
        public CableTypeSO[] requiredCableTypes = new CableTypeSO[2];

        [Header("References")]
        [Tooltip("The cable-choice panel. Left empty, it is taken from COC2ProcedureReset.")]
        public CableTypeSelectionUI cableTypeSelection;

        [Tooltip("Winds the procedure back for the next cable. Left empty, the scene's instance is used.")]
        public COC2ProcedureReset procedureReset;

        [Tooltip("Left empty, the scene's instance is used.")]
        public COC2StageDialogs stageDialogs;

        [Header("Completion")]
        [Tooltip("Reported to TaskManager ONCE, when every required cable type has passed. Must match " +
                 "COC2TaskSetup.testTaskId. Clear NetworkTestStation.taskId so this is the only reporter.")]
        public string taskId = "test_cable";

        [Tooltip("Brief the learner through every stage again on the second cable. Off by default - " +
                 "they have just been through all four.")]
        public bool replayTutorialsForNextCable = false;

        [Header("Dialog")]
        public string speakerName = "Instructor";

        [Tooltip("Shown after a pass when another cable type is still outstanding. " +
                 "{done} and {next} are replaced with the cable display names.")]
        [TextArea(2, 6)]
        public string[] nextCableLines =
        {
            "That's one working {done}. Half the job.",

            "A technician is expected to build both of the cables in this competency, because they " +
            "do different work. A straight-through joins unlike devices - a PC to a switch. A " +
            "crossover joins like to like - PC to PC, or switch to switch - by swapping the transmit " +
            "and receive pairs at one end.",

            "So: same procedure again, this time for a {next}. Strip, arrange, crimp, test. " +
            "Take a fresh length of cable and choose it from the panel."
        };

        [Tooltip("Shown when every required type has passed - in Story Mode AND in an assessment, so " +
                 "keep it to the fact that the step is complete. {count} is the number of cables built.")]
        [TextArea(2, 6)]
        public string[] allDoneLines =
        {
            "Both cables passed the tester. Your straight-through and crossover cables are complete.",

            "The cable-making step is finished, so the crimping station and the cable tester are now closed."
        };

        [Tooltip("Story Mode only - added after All Done Lines. A short recap of what was learned. " +
                 "Left out in an assessment.")]
        [TextArea(2, 6)]
        public string[] storyRecapLines =
        {
            "Remember: a straight-through cable connects unlike devices, like a PC to a switch. " +
            "A crossover cable connects like devices, like PC to PC or switch to switch."
        };

        [Tooltip("Story Mode only - added last, while the task in Next Step Task Id is still to do. " +
                 "Tells the learner where to go next. Left out in an assessment, and left out once that " +
                 "task is already done (the patch panel can be finished before the cables).")]
        [TextArea(2, 6)]
        public string[] nextStepLines =
        {
            "Next, go to the patch panel and terminate the cable run."
        };

        [Tooltip("The task Next Step Lines point to. Must match COC2TaskSetup's patch panel task id. " +
                 "Leave empty to always add the Next Step Lines.")]
        public string nextStepTaskId = "punch_patch_panel";

        [Tooltip("Shown when the learner picks a cable type they have already passed. " +
                 "{done} is that cable's display name.")]
        [TextArea(2, 6)]
        public string[] alreadyBuiltLines =
        {
            "You've already built and passed a {done}. Pick the other one."
        };

        [Header("When both cables are done")]
        [Tooltip("Take the learner out of the tester close-up (and any other cable-making view) as " +
                 "soon as the 'step complete' dialog closes, back to walking.")]
        public bool exitStationWhenDone = true;

        [Tooltip("Close cable-making: the crimping station and the cable tester answer E with the " +
                 "Stage Complete Lines instead of opening, and the finished-cable pickup is switched off.")]
        public bool lockStationsWhenDone = true;

        [Tooltip("Shown when the learner tries to use the crimping station or the tester again after both " +
                 "cables have passed. In Story Mode the Next Step Lines are added while that task is still to do.")]
        [TextArea(2, 6)]
        public string[] stageCompleteLines =
        {
            "This stage is already complete. Both of your cables passed the tester, so there is nothing " +
            "more to do at this station."
        };

        [Tooltip("Any other pickup or station that belongs to cable-making and should close as well. " +
                 "These are switched off (no prompt at all). The crimping station, tester and cable " +
                 "pickup are handled automatically - leave them out of this list.")]
        public Interactable[] alsoLock = new Interactable[0];

        [Header("Events")]
        [Tooltip("Fires once, when the last required cable type passes. Wire scene completion here.")]
        public UnityEvent OnAllCablesComplete;

        [Tooltip("Fires after each pass that still leaves work to do.")]
        public UnityEvent OnNextCableRequired;

        // ------------------------------------------------------------------

        private readonly List<CableTypeSO> passedTypes = new List<CableTypeSO>();
        private bool completed;
        private bool handling;

        /// <summary>How many required types have been built and passed.</summary>
        public int PassedCount => passedTypes.Count;

        /// <summary>True once every required cable type has passed.</summary>
        public bool IsComplete => completed;

        /// <summary>True if this exact CableTypeSO has already been built and passed.</summary>
        public bool HasPassed(CableTypeSO type)
        {
            return type != null && passedTypes.Contains(type);
        }

        /// <summary>The first required type not yet passed, or null when they are all done.</summary>
        public CableTypeSO NextRequired()
        {
            foreach (var t in requiredCableTypes)
            {
                if (t == null) continue;
                if (!passedTypes.Contains(t)) return t;
            }
            return null;
        }

        // ------------------------------------------------------------------

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (procedureReset == null) procedureReset = COC2ProcedureReset.Instance;
            if (stageDialogs == null) stageDialogs = COC2StageDialogs.Instance;

            if (cableTypeSelection == null && procedureReset != null)
                cableTypeSelection = procedureReset.cableTypeSelection;

            Warn();
        }

        /// <summary>
        /// Everything here is a silent failure at runtime hours later, so it is all checked once at
        /// startup instead.
        /// </summary>
        private void Warn()
        {
            int valid = 0;
            foreach (var t in requiredCableTypes) if (t != null) valid++;

            if (valid < 2)
                Debug.LogWarning("[COC2BothCablesGate] Fewer than two Required Cable Types are " +
                                 "assigned, so nothing is being gated. Assign your straight-through " +
                                 "and crossover CableTypeSO assets.", this);

            if (procedureReset == null)
                Debug.LogError("[COC2BothCablesGate] No COC2ProcedureReset found. The procedure can " +
                               "never wind back for the second cable.", this);

            if (stageDialogs == null)
                Debug.LogWarning("[COC2BothCablesGate] No COC2StageDialogs found. The second cable's " +
                                 "pass/fail dialog will not fire - see ResetForNextAttempt in the guide.", this);

            var tester = NetworkTestStation.Instance;
            if (tester != null && !string.IsNullOrEmpty(tester.taskId) && tester.taskId == taskId)
            {
                Debug.LogError("[COC2BothCablesGate] NetworkTestStation.taskId is still set to '" +
                               taskId + "'. It will report that task on the FIRST pass and COC 2 will " +
                               "complete after one cable. Clear that field - this component reports it.", this);
            }
        }

        // ------------------------------------------------------------------
        // The pass hook
        // ------------------------------------------------------------------

        /// <summary>
        /// Wire this to COC2StageDialogs.OnTestPassed.
        ///
        /// Not subscribed in code on purpose: that event fires immediately BEFORE the pass dialog
        /// is shown, and doing the work from an Inspector-visible slot keeps the whole chain
        /// readable in one place rather than hidden in an AddListener call.
        /// </summary>
        public void NotifyTestPassed()
        {
            if (completed || handling) return;

            var station = CrimpingStationController.Instance;
            if (station == null || station.targetCableType == null)
            {
                Debug.LogWarning("[COC2BothCablesGate] A cable passed but there is no target cable " +
                                 "type to credit it to, so it has been ignored.", this);
                return;
            }

            CableTypeSO built = station.targetCableType;

            if (!passedTypes.Contains(built)) passedTypes.Add(built);

            Debug.Log("[COC2BothCablesGate] " + Name(built) + " passed. " +
                      PassedCount + " of " + RequiredCount() + " done.", this);

            handling = true;
            StartCoroutine(AfterPass());
        }

        /// <summary>
        /// Waits for the pass dialog to close before doing anything.
        ///
        /// COC2StageDialogs shows that dialog in the same frame this runs. Winding the procedure
        /// back underneath it would move the world while the learner is still reading about it,
        /// and stacking a second dialog on top of the first is not something DialogManager is
        /// built for. Frame-based waits only - a dialog that sets Time.timeScale to 0 would stall
        /// anything time-based forever.
        /// </summary>
        private IEnumerator AfterPass()
        {
            yield return null;

            while (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
                yield return null;

            CableTypeSO next = NextRequired();

            if (next == null)
            {
                Finish();
                handling = false;
                yield break;
            }

            OnNextCableRequired?.Invoke();

            CableTypeSO justBuilt = passedTypes.Count > 0 ? passedTypes[passedTypes.Count - 1] : null;
            Show(Build(nextCableLines, justBuilt, next), StartNextCable);
        }

        /// <summary>Winds the procedure back and reopens the cable choice.</summary>
        private void StartNextCable()
        {
            handling = false;

            if (procedureReset == null)
            {
                Debug.LogError("[COC2BothCablesGate] No COC2ProcedureReset - the learner has been " +
                               "told to build another cable and nothing has been reset.", this);
                return;
            }

            // The learner has to CHOOSE the second type, so this restart must open the choice panel
            // whatever the reset is normally set to. A failed test still uses that normal setting,
            // which is usually EnterStrippingView - going straight back to work with the same cable
            // type is right there, and wrong here.
            var previousEntry = procedureReset.restartEntry;
            procedureReset.restartEntry = COC2ProcedureReset.RestartEntry.OpenCableChoice;

            procedureReset.RestartProcedure();

            procedureReset.restartEntry = previousEntry;

            // AFTER the restart, not before. The restart clears NetworkTestStation.HasTested, and
            // COC2StageDialogs only re-arms once it sees that flag drop. Clearing its verdict while
            // the flag was still true would let it report the same pass a second time.
            if (stageDialogs != null)
                stageDialogs.ResetForNextAttempt(replayTutorialsForNextCable);
        }

        private void Finish()
        {
            completed = true;

            if (!string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                TaskManager.Instance.ReportProgress(taskId);

            Debug.Log("[COC2BothCablesGate] All " + PassedCount + " cable types passed. " +
                      "Reported '" + taskId + "'.", this);

            OnAllCablesComplete?.Invoke();

            // Closed straight away, so the finished-cable pickup never offers itself again.
            if (lockStationsWhenDone) LockCableStations();

            Show(Build(AllDoneLines(), null, null), null);

            // Not the dialog's callback: if anything else takes the dialog box over (a quiz, a task
            // dialog fired by the same task completing), that callback would never run and the learner
            // would be left in the tester. Waiting for the box to be clear works either way.
            if (exitStationWhenDone) StartCoroutine(LeaveStationsWhenDialogsClose());
        }

        /// <summary>All Done Lines, plus - in Story Mode - the recap and, while it is still to do,
        /// where to go next.</summary>
        private string[] AllDoneLines()
        {
            var lines = new List<string>();
            if (allDoneLines != null) lines.AddRange(allDoneLines);

            if (!AssessmentMode.IsActive)
            {
                if (storyRecapLines != null) lines.AddRange(storyRecapLines);
                if (NextStepStillToDo() && nextStepLines != null) lines.AddRange(nextStepLines);
            }

            return lines.ToArray();
        }

        /// <summary>True while the task Next Step Lines point to has not been done yet.</summary>
        private bool NextStepStillToDo()
        {
            if (string.IsNullOrEmpty(nextStepTaskId) || TaskManager.Instance == null) return true;
            return !TaskManager.Instance.IsTaskComplete(nextStepTaskId);
        }

        // ------------------------------------------------------------------
        // A closed station being used again
        // ------------------------------------------------------------------

        /// <summary>
        /// Call at the top of a cable-making station's Interact(). Returns true - after showing the
        /// "this stage is complete" dialog - when both cables have passed and the station must not
        /// open; the station then simply returns. False at every other time, so it changes nothing
        /// before cable-making is finished, and nothing in a scene without this gate.
        /// </summary>
        public static bool RefuseBecauseComplete()
        {
            COC2BothCablesGate gate = Instance;
            if (gate == null || !gate.completed || !gate.lockStationsWhenDone) return false;

            gate.ShowStageComplete();
            return true;
        }

        private void ShowStageComplete()
        {
            // One message at a time - a second press while it is up does nothing.
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            var lines = new List<string>();
            if (stageCompleteLines != null) lines.AddRange(stageCompleteLines);
            if (!AssessmentMode.IsActive && NextStepStillToDo() && nextStepLines != null)
                lines.AddRange(nextStepLines);

            Show(Build(lines.ToArray(), null, null), null);
        }

        private IEnumerator LeaveStationsWhenDialogsClose()
        {
            yield return null;

            while (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
                yield return null;

            var tester = NetworkTestStation.Instance;
            if (tester != null && tester.InTesterView)
            {
                // The test itself has finished by now; RequestExit refuses only while one is running.
                while (tester.IsTesting) yield return null;
                tester.RequestExit();
            }

            if (cableTypeSelection != null && cableTypeSelection.IsOpen)
                cableTypeSelection.Cancel();

            if (CrimpingViewController.Instance != null)
                CrimpingViewController.Instance.ExitCrimpingView();

            if (StrippingViewController.Instance != null)
                StrippingViewController.Instance.ExitStrippingView();

            // Again after leaving, in case a view switched anything back on on the way out.
            if (lockStationsWhenDone) LockCableStations();

            Debug.Log("[COC2BothCablesGate] Cable-making finished - the learner is back to walking.", this);
        }

        /// <summary>
        /// Switches off the finished-cable pickup (and anything in Also Lock). PlayerInteractor treats
        /// an interactable with no live trigger as out of reach - no prompt, no key, no button.
        ///
        /// The crimping station and the tester are NOT switched off: they stay usable so that pressing
        /// E at them can say the stage is complete (RefuseBecauseComplete) rather than doing nothing.
        /// </summary>
        private void LockCableStations()
        {
            var targets = new List<Interactable>();
            targets.AddRange(FindObjectsByType<CrimpedCablePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            if (alsoLock != null) targets.AddRange(alsoLock);

            int closed = 0;
            foreach (Interactable station in targets)
            {
                if (station == null || station.Trigger == null || !station.Trigger.enabled) continue;
                station.Trigger.enabled = false;
                closed++;
            }

            if (closed > 0)
                Debug.Log("[COC2BothCablesGate] Switched off " + closed + " cable-making pickup(s).", this);
        }

        // ------------------------------------------------------------------
        // Choice interception
        // ------------------------------------------------------------------

        /// <summary>Repoint the panel's Straight-Through button here instead of CableTypeSelectionUI.</summary>
        public void ChooseStraightThrough()
        {
            if (cableTypeSelection == null) return;
            Choose(cableTypeSelection.straightThroughCableType, cableTypeSelection.ChooseStraightThrough);
        }

        /// <summary>Repoint the panel's Crossover button here instead of CableTypeSelectionUI.</summary>
        public void ChooseCrossover()
        {
            if (cableTypeSelection == null) return;
            Choose(cableTypeSelection.crossoverCableType, cableTypeSelection.ChooseCrossover);
        }

        /// <summary>
        /// Refuses a type that has already been passed, and forwards anything else straight through.
        ///
        /// Refusing rather than silently re-running it is the teaching point: the learner has to
        /// notice that the two cables are different jobs, not repeat the one they are comfortable
        /// with. The panel is left open, so the correction costs one tap.
        /// </summary>
        private void Choose(CableTypeSO type, System.Action forward)
        {
            if (type == null || forward == null) return;

            // Cable-making is finished - a third cable is not part of the job.
            if (completed)
            {
                if (cableTypeSelection != null && cableTypeSelection.IsOpen) cableTypeSelection.Cancel();
                return;
            }

            if (HasPassed(type))
            {
                Show(Build(alreadyBuiltLines, type, NextRequired()), null);
                return;
            }

            forward();
        }

        // ------------------------------------------------------------------
        // Dialog helpers
        // ------------------------------------------------------------------

        private int RequiredCount()
        {
            int n = 0;
            foreach (var t in requiredCableTypes) if (t != null) n++;
            return n;
        }

        private static string Name(CableTypeSO type)
        {
            if (type == null) return "cable";
            return string.IsNullOrEmpty(type.displayName) ? type.name : type.displayName;
        }

        /// <summary>
        /// Builds a throwaway DialogSequence. HideAndDontSave keeps it out of the scene file and
        /// stops it being counted as a leaked asset when Play mode ends.
        /// </summary>
        private DialogSequence Build(string[] lines, CableTypeSO done, CableTypeSO next)
        {
            var sequence = ScriptableObject.CreateInstance<DialogSequence>();
            sequence.hideFlags = HideFlags.HideAndDontSave;

            if (lines == null) return sequence;

            foreach (string raw in lines)
            {
                if (string.IsNullOrEmpty(raw)) continue;

                string text = raw
                    .Replace("{done}", Name(done))
                    .Replace("{next}", Name(next))
                    .Replace("{count}", PassedCount.ToString());

                sequence.lines.Add(new DialogLine { speakerName = speakerName, text = text });
            }

            return sequence;
        }

        /// <summary>
        /// ignoreSuppression is deliberate. These are not tutorial hints - they are the module
        /// telling the learner what the task is. Assessment Mode switches tutorials off, and this
        /// still has to be said or the second half of the exercise is invisible.
        /// </summary>
        private void Show(DialogSequence sequence, System.Action onComplete)
        {
            if (sequence == null || sequence.lines.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            if (DialogManager.Instance == null)
            {
                Debug.LogWarning("[COC2BothCablesGate] No DialogManager - the message was skipped.", this);
                onComplete?.Invoke();
                return;
            }

            DialogManager.Instance.ShowDialog(sequence, onComplete, true);
        }
    }
}