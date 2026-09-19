// PunchDownStation.cs
using UnityEngine;
using UnityEngine.Events;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The thing in the world the player walks up to and presses E on. It cuts to the stage camera;
    /// PunchStageBoard handles the termination itself.
    ///
    /// HOW THE VIEW WORKS. The punch-down stage is a small 3D set built somewhere off on its own -
    /// eight pins, eight conductors - with its own camera pointed at it. Entering switches the
    /// player camera off and the stage camera on, so the screen fills with the set and nothing else.
    /// Because the set is far from everything else, the player camera never sees it and the stage
    /// camera never sees the world; neither needs masking or hiding.
    ///
    /// DELIBERATELY KNOWS NOTHING ABOUT THE PATCH PANEL MODEL. The stage is its own diorama. Moving,
    /// rescaling or re-exporting the trainer FBX cannot break it.
    ///
    /// Setup:
    ///   1. Put this on any world object with a trigger Collider sized to the interaction range.
    ///   2. Assign Player Camera and Stage Camera. The stage camera is switched off at Awake.
    ///   3. Assign UI Root - the canvas holding the punch-down tool icon and readouts.
    ///   4. Assign Board - the PunchStageBoard on the stage set.
    ///   5. Tick Bypass Gate For Testing while you build, and press the test key to jump straight in.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PunchDownStation : Interactable
    {
        /// <summary>What a wrong termination costs the player.</summary>
        public enum FailAction
        {
            [Tooltip("Only the stage is cleared - the conductors go back to where they started and " +
                     "the player tries again without leaving the view.")]
            ResetBoardOnly,
            [Tooltip("The whole COC2 procedure restarts from stripping, the same as failing the " +
                     "cable test. Harsh; for a graded assessment run.")]
            RestartWholeProcedure
        }

        public static PunchDownStation Instance { get; private set; }

        [Header("Gate - which stages must be finished first")]
        [Tooltip("TESTING ONLY. Opens the station for anyone, whatever they have or haven't " +
                 "finished. It warns in the Console on every Play and every entry, so it cannot be " +
                 "shipped by accident. UNTICK before a graded build.")]
        public bool bypassGateForTesting = false;

        [Tooltip("Task ids that must all be complete before the station opens. The default gates on " +
                 "the cable test, which covers the whole chain: the tester only reports a PASS, and " +
                 "the player cannot reach it without stripping, wiring and crimping first.\n\n" +
                 "Every id here must be registered by COC2TaskSetup or it can never complete and " +
                 "the station never opens. Empty = no gate.")]
        public string[] requiredTaskIds = new string[] { "test_cable" };

        [Tooltip("Optional. Shown if the player tries the station too early.")]
        public DialogSequence lockedDialog;

        [Header("Testing shortcut")]
        [Tooltip("Press anywhere in the scene to open the board directly - no walking up, no gate, " +
                 "no trigger collider. Set to None to switch it off.")]
        public KeyCode testEnterKey = KeyCode.F10;

        [Tooltip("While the station is open, jumps straight to the next plugging phase without " +
                 "punching anything.\n\n" +
                 "This is the key to press when the camera does not move after the last port: it " +
                 "runs exactly the same hand-off, and prints exactly why it did or did not take " +
                 "over - so you find out in one second instead of terminating eight ports first. " +
                 "Set to None to switch it off.")]
        public KeyCode testNextPhaseKey = KeyCode.F11;

        [Tooltip("TESTING ONLY. Entering the station skips the punching entirely and opens straight " +
                 "into the first plugging phase.\n\n" +
                 "For when you are working on the cords and do not want to terminate eight ports " +
                 "before every test. The punch board is suspended rather than removed, so nothing " +
                 "about the termination is changed - it simply is not asked for. It warns in the " +
                 "Console on every Play and every entry, so it cannot ship by accident. UNTICK " +
                 "before a real run.")]
        public bool skipPunchingForTesting = false;

        [Header("Controls")]
        [Tooltip("Key that closes the board.\n\n" +
                 "Defaulted to Q rather than Escape on purpose: this project also uses Escape to " +
                 "unlock the cursor and pause, so one press would close the board AND pause the " +
                 "game. The board unlocks the cursor for you on entry, so Escape is not needed here.")]
        public KeyCode exitKey = KeyCode.Q;

        [Tooltip("Let the player close the board once they've started. Untick for an assessment run " +
                 "where a termination has to be finished once begun.")]
        public bool allowExitMidTermination = true;

        [Header("The view")]
        [Tooltip("The player's normal 3D camera - the same reference the other stages use. Switched " +
                 "off while the stage is open.")]
        public Camera playerCamera;

        [Tooltip("The camera pointed at the punch-down stage set. Switched on when the player " +
                 "enters. Leave its GameObject however you like in the editor - this switches it " +
                 "off at Awake.")]
        public Camera stageCamera;

        [Tooltip("The canvas holding the punch-down tool icon and the readouts. Shown only while " +
                 "the stage is open.")]
        public GameObject uiRoot;

        [Tooltip("Optional. The root of the 3D stage set. Only worth assigning if you want the set " +
                 "switched off when unused - the stage camera is the only thing that can see it " +
                 "either way, so leaving this empty is fine.")]
        public GameObject stageRoot;

        [Tooltip("The PunchStageBoard on the stage set.")]
        public PunchStageBoard board;

        [Tooltip("Clear the stage on every entry, so a player who backed out mid-termination starts " +
                 "clean. Untick to let them resume where they left off.")]
        public bool resetOnEnter = true;

        [Tooltip("Optional. Assign a PunchStageSequence to terminate all eight ports in one visit " +
                 "instead of one.\n\n" +
                 "With this set, a passed port moves the markers to the next block and the view " +
                 "STAYS OPEN; the player only comes back out once every port is done. Leave it " +
                 "empty and one termination ends the stage, which is what you want while testing.")]
        public PunchStageSequence sequence;

        [Tooltip("Optional. The PLUGGING phases that follow the punching, IN ORDER.\n\n" +
                 "Element 0 runs when the last port is terminated, element 1 when element 0 passes, " +
                 "and so on - so you can chain 'panel front to switch', then 'cable plug to the " +
                 "outlet', then 'plug into the tester', each with its own camera framing and its own " +
                 "set of cords. Leave it empty and the station closes after the punching, exactly as " +
                 "it did before.\n\n" +
                 "A phase whose Begin() finds nothing to plug in is skipped rather than stalling the " +
                 "run, so a half-built phase never traps the player in the station.")]
        public PatchCordBoard[] patchPhases;

        [Header("Briefing")]
        [Tooltip("Optional. Shown the first time the player enters the stage - what an IDC block " +
                 "is, what the tool does, why the conductor is trimmed as it seats.")]
        public DialogSequence introDialog;
        [Tooltip("Show the briefing only once per session. Untick to show it every entry.")]
        public bool introOnlyOnce = true;

        [Header("Player control")]
        [Tooltip("Movement and look scripts, disabled while the board is open. Same array you " +
                 "filled on the other view controllers.")]
        public MonoBehaviour[] playerControlScripts;

        [Header("Hiding the player")]
        [Tooltip("Drag your PLAYER OBJECT here. Every renderer under it is switched off while the " +
                 "stage is open, then put back exactly as it was on the way out.\n\n" +
                 "Needed because the stage camera now looks at the REAL rack out in the environment " +
                 "rather than a diorama off on its own - so the player is standing in shot, between " +
                 "the camera and the panel.\n\n" +
                 "RENDERERS, not SetActive, deliberately. Deactivating the player object would take " +
                 "its character controller, its collider and its scripts down with it, and bringing " +
                 "all that back in the right order on the way out is a whole class of bug this " +
                 "avoids. Switching renderers off also removes the player's SHADOW from the shot, " +
                 "which is the other half of the problem.")]
        public Transform playerBody;

        [Tooltip("Anything else that should disappear for the duration and is NOT under Player Body " +
                 "- first-person arms on their own rig, a held tool, a name tag, an aiming reticle.")]
        public GameObject[] alsoHideInStage;

        [Tooltip("Hides the 'Press E' prompt and the on-screen Interact / Pickup buttons while the " +
                 "stage is open. PlayerInteractor switches those on and off itself every frame, so " +
                 "they cannot be hidden by listing them in Also Hide In Stage - this asks " +
                 "PlayerInteractor to keep them off instead, and it lets go the moment the stage " +
                 "closes. Stays on unless you want the prompt visible inside the station.")]
        public bool hideInteractionPrompt = true;

        [Header("Mobile")]
        public GameObject[] mobileControlsToHide;
        public GameObject[] mobileStageControls;

        [Header("Task (optional)")]
        public string taskId = "punch_patch_panel";
        public bool reportOnlyOnPass = true;

        [Header("On failure")]
        public FailAction failAction = FailAction.ResetBoardOnly;
        [Tooltip("Only used when Fail Action is RestartWholeProcedure.")]
        public COC2ProcedureReset procedureReset;

        [Header("Events")]
        public UnityEvent OnStageOpened;
        public UnityEvent OnStageClosed;

        public bool InStageView { get; private set; }

        bool introShown;
        bool subscribed;
        int activePhase = -1;

        Renderer[] bodyRenderers;
        bool[] bodyWasEnabled;
        bool playerHidden;

        void Awake()
        {
            Instance = this;
            SetViewActive(false);
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        void Start()
        {
            ValidateSetup();
        }

        void Update()
        {
            // Asked every frame the stage is open, and PlayerInteractor forgets the request each
            // frame, so nothing needs undoing on exit. Update runs before PlayerInteractor's
            // LateUpdate, so the prompt never shows for a frame as the view opens.
            if (InStageView && hideInteractionPrompt)
                CSSSimulation.Interaction.PlayerInteractor.SuppressPromptThisFrame();

            if (!InStageView)
            {
                if (testEnterKey != KeyCode.None && Input.GetKeyDown(testEnterKey)) EnterForTesting();
                return;
            }

            if (exitKey != KeyCode.None && Input.GetKeyDown(exitKey)) RequestExit();
            if (testNextPhaseKey != KeyCode.None && Input.GetKeyDown(testNextPhaseKey))
                TestBeginNextPhase();
        }

        /// <summary>Runs the same hand-off the last punched port runs, and says what happened.</summary>
        [ContextMenu("Test: jump to the next plugging phase")]
        public void TestBeginNextPhase()
        {
            if (!InStageView)
            {
                Debug.LogWarning("[PunchDownStation] Open the station first (press " + testEnterKey +
                                 "), then press " + testNextPhaseKey + ".", this);
                return;
            }

            Debug.LogWarning("[PunchDownStation] TEST - rewinding to the first plugging phase and " +
                             "running the hand-off, as if the last port had just passed.", this);

            // Rewind first. A second press would otherwise ask for the phase AFTER the one it just
            // started - which reports that nothing took over AND leaves the run pointing past a
            // phase that has not been played, so the real hand-off later skips it too.
            ResetPhases();

            if (BeginNextPhase()) return;

            Debug.LogWarning("[PunchDownStation] ...and nothing took over. The lines above say why. " +
                             "In a real run the station would close here.", this);
        }

        // ------------------------------------------------------------------
        // Getting in
        // ------------------------------------------------------------------

        public override void Interact()
        {
            if (InStageView) return;

            if (bypassGateForTesting)
            {
                Debug.LogWarning("[PunchDownStation] Opening with the GATE BYPASSED. This is a " +
                                 "testing setting - untick 'Bypass Gate For Testing' before a " +
                                 "graded build.", this);
                SetViewActive(true);
                return;
            }

            string missing = FirstUnfinishedStage();
            if (missing != null)
            {
                if (lockedDialog != null && DialogManager.Instance != null && !DialogManager.Instance.IsDialogOpen)
                    DialogManager.Instance.ShowDialog(lockedDialog);
                else
                    // LogWarning, not Log. This project's Console has the Info filter off, so a
                    // plain Debug.Log here reads as the station doing nothing when you press E.
                    Debug.LogWarning("[PunchDownStation] Station stays shut - '" + missing + "' isn't " +
                                     "complete yet. Tick 'Bypass Gate For Testing' if you're testing " +
                                     "the board on its own.", this);
                return;
            }

            SetViewActive(true);
        }

        /// <summary>Opens the board from anywhere, ignoring the gate and the interaction range.</summary>
        [ContextMenu("Open board now (testing)")]
        public void EnterForTesting()
        {
            if (InStageView) return;
            Debug.LogWarning("[PunchDownStation] TEST ENTRY - opened the board directly, skipping " +
                             "the walk-up and the stage gate. Nothing about this path runs in a real " +
                             "playthrough.", this);
            SetViewActive(true);
        }

        /// <summary>Wire to a Back button on the board.</summary>
        public void RequestExit()
        {
            if (!InStageView) return;

            if (!allowExitMidTermination && board != null &&
                board.PunchedCount() > 0 && !board.HasFinished)
            {
                if (board != null) board.SetHint("Finish this termination before leaving.");
                return;
            }

            SetViewActive(false);
        }

        /// <summary>Null when every required stage is finished, otherwise the first id that isn't.</summary>
        string FirstUnfinishedStage()
        {
            if (requiredTaskIds == null || requiredTaskIds.Length == 0) return null;

            if (TaskManager.Instance == null)
            {
                Debug.LogWarning("[PunchDownStation] No TaskManager in the scene, so the stage gate " +
                                 "can't be checked. Letting the player in.", this);
                return null;
            }

            foreach (string id in requiredTaskIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!TaskManager.Instance.IsTaskComplete(id)) return id;
            }
            return null;
        }

        public bool IsUnlocked
        {
            get { return bypassGateForTesting || FirstUnfinishedStage() == null; }
        }

        // ------------------------------------------------------------------
        // The verdict
        // ------------------------------------------------------------------

        void Subscribe()
        {
            if (subscribed || board == null) return;
            board.OnFinished += HandleFinished;
            foreach (var phase in Phases()) phase.OnFinished += HandlePhaseFinished;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || board == null) return;
            board.OnFinished -= HandleFinished;
            foreach (var phase in Phases()) phase.OnFinished -= HandlePhaseFinished;
            subscribed = false;
        }

        void HandleFinished(bool passed, string reason)
        {
            if (!string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
            {
                if (!reportOnlyOnPass || passed)
                    TaskManager.Instance.ReportProgress(taskId);
            }

            Debug.LogWarning("[PunchDownStation] Termination " + (passed ? "PASSED" : "FAILED") +
                             ". " + reason, this);

            System.Action onClosed = passed ? (System.Action)HandlePassClosed : HandleFailClosed;

            // Reuses the patch panel pass/fail copy already written in COC2StageDialogs. If that
            // component isn't in the scene the callback still runs, so the station can never stall
            // waiting on a dialog that never appeared.
            if (COC2StageDialogs.Instance != null)
                COC2StageDialogs.Instance.ShowPanelResult(passed, passed ? null : reason, onClosed);
            else
                onClosed();
        }

        void HandlePassClosed()
        {
            // A multi-port run is not finished just because one port is. Ask the sequence whether
            // there is another block to do, and if there is, stay where we are - dropping the player
            // back into the world after every port would make eight terminations unbearable.
            if (sequence != null && sequence.AdvanceOnPass())
            {
                Debug.LogWarning("[PunchDownStation] Port passed, and the sequence has another block " +
                                 "to do - staying on the punching. Now on port " +
                                 (sequence.CurrentPort + 1) + ".", this);
                return;
            }

            Debug.LogWarning("[PunchDownStation] Punching finished" +
                             (sequence == null ? " (no sequence assigned - one port only)" : "") +
                             ". Looking for a plugging phase to hand over to.", this);

            // Every port is punched, but the job is not done: the conductors dead-end in the IDC
            // block, and nothing reaches the switch until a cord is plugged in. Hand over to the
            // first plugging phase and keep the view open. With no phases assigned - or none that
            // have anything to plug in - this falls straight through and the station closes exactly
            // as it always did.
            if (BeginNextPhase()) return;

            SetViewActive(false);
        }

        /// <summary>The end of phase 2 - and of the whole station.</summary>
        /// <summary>Every assigned plugging phase, skipping empty slots.</summary>
        System.Collections.Generic.IEnumerable<PatchCordBoard> Phases()
        {
            if (patchPhases == null) yield break;
            foreach (var p in patchPhases) { if (p != null) yield return p; }
        }

        /// <summary>The phase currently being played, or null if the punching is still going.</summary>
        PatchCordBoard CurrentPhase
        {
            get
            {
                if (patchPhases == null) return null;
                if (activePhase < 0 || activePhase >= patchPhases.Length) return null;
                return patchPhases[activePhase];
            }
        }

        /// <summary>
        /// Starts the next plugging phase that has anything to do, and reports whether one took over.
        ///
        /// The skip matters: a phase you have half-built - sockets placed but no plugs marked yet -
        /// returns false from Begin(), and the run moves on to the next one instead of handing the
        /// player a phase they cannot finish and cannot leave.
        /// </summary>
        bool BeginNextPhase()
        {
            // A phase that is ALREADY RUNNING means we are not between phases at all. Without this
            // the search starts at activePhase + 1, skips straight past the live phase, finds
            // nothing after it and reports that nothing took over - so the station closes on top of
            // a phase the player was in the middle of.
            PatchCordBoard current = CurrentPhase;
            if (current != null && current.IsRunning)
            {
                Debug.LogWarning("[PunchDownStation] Phase " + activePhase + ", '" + current.phaseName +
                                 "', is already running - staying in it.", this);
                return true;
            }

            if (patchPhases == null || patchPhases.Length == 0)
            {
                Debug.LogWarning("[PunchDownStation] No plugging phases assigned. Patch Phases on " +
                                 "this component is empty, so there is nothing to hand over to and " +
                                 "the station closes after the punching. If you built a " +
                                 "PatchCordBoard, drag it into element 0.", this);
                return false;
            }

            for (int i = activePhase + 1; i < patchPhases.Length; i++)
            {
                if (patchPhases[i] == null)
                {
                    Debug.LogWarning("[PunchDownStation] Patch Phases element " + i + " is empty - " +
                                     "skipping it.", this);
                    continue;
                }

                if (!patchPhases[i].Begin())
                {
                    Debug.LogWarning("[PunchDownStation] Phase '" + patchPhases[i].phaseName +
                                     "' declined to start - see the [PatchCordBoard] line above for " +
                                     "the reason. Trying the next one.", this);
                    continue;
                }

                activePhase = i;
                Debug.LogWarning("[PunchDownStation] Handed over to phase " + i + ", '" +
                                 patchPhases[i].phaseName + "'. The station stays open.", this);
                return true;
            }
            return false;
        }

        /// <summary>Puts the plugging run back to the start: no phase active, every phase cleared.</summary>
        void ResetPhases()
        {
            activePhase = -1;
            foreach (var phase in Phases()) phase.ResetStage();
        }

        void HandlePhaseFinished(bool passed, string reason)
        {
            PatchCordBoard phase = CurrentPhase;
            string label = phase != null ? phase.phaseName : "Plugging phase";

            Debug.LogWarning("[PunchDownStation] " + label + " " + (passed ? "PASSED" : "FAILED") +
                             ". " + reason, this);

            // A failed phase gets the same second chance as a failed termination: the cords come
            // back out and the player runs them again, without leaving the station.
            System.Action onClosed = passed ? (System.Action)ContinueAfterPhase : RetryCurrentPhase;

            if (COC2StageDialogs.Instance != null)
                COC2StageDialogs.Instance.ShowPanelResult(passed, passed ? null : reason, onClosed);
            else
                onClosed();
        }

        void ContinueAfterPhase()
        {
            if (BeginNextPhase()) return;
            CloseStage();
        }

        void RetryCurrentPhase()
        {
            if (failAction == FailAction.RestartWholeProcedure && procedureReset != null)
            {
                SetViewActive(false);
                procedureReset.RestartProcedure();
                return;
            }

            PatchCordBoard phase = CurrentPhase;
            if (phase == null) { CloseStage(); return; }

            phase.Retry();
            Debug.LogWarning("[PunchDownStation] " + phase.phaseName + " reset - every cord is back " +
                             "where it started and can be plugged in again.", this);
        }

        /// <summary>Leaves the station. Public so the patch-cord phase can end the visit, and so a
        /// Done button on the stage UI has something to call.</summary>
        public void CloseStage()
        {
            if (InStageView) SetViewActive(false);
        }

        void HandleFailClosed()
        {
            if (failAction == FailAction.RestartWholeProcedure && procedureReset != null)
            {
                SetViewActive(false);
                procedureReset.RestartProcedure();
                if (board != null) board.ResetStage();
                return;
            }

            // Stay in the view and let them go again.
            //
            // With Auto Reset On Fail ticked - the default - the board has ALREADY cleared the port
            // by the time this runs, on its own timer, whether or not this dialog was ever shown.
            // Resetting a second time here would wipe a drag the player may already have started,
            // so leave it alone.
            if (board == null) return;

            if (board.AutoResetsOnFail)
            {
                Debug.LogWarning("[PunchDownStation] Port failed. The board cleared it on its own " +
                                 "(Auto Reset On Fail) - the player is still in the station and can " +
                                 "punch this port again. Ports already terminated were untouched.",
                                 this);
                return;
            }

            // Auto Reset On Fail is off, so clearing the port is this station's job. Same effect:
            // all eight pins emptied, this port's conductors unlocked and sent back to the cable,
            // every wire straightened, the tool back on its hook.
            board.ResetStage();
            Debug.LogWarning("[PunchDownStation] Termination reset - the conductors are back on the " +
                             "cable and this port can be punched again.", this);
        }

        // ------------------------------------------------------------------

        void SetViewActive(bool active)
        {
            InStageView = active;

            // The cameras. Player camera off, stage camera on - the set is far enough away that
            // neither camera can see the other's subject, so there is no culling mask to configure.
            if (playerCamera != null) playerCamera.gameObject.SetActive(!active);
            if (stageCamera != null) stageCamera.gameObject.SetActive(active);

            if (stageRoot != null) stageRoot.SetActive(active);
            if (uiRoot != null) uiRoot.SetActive(active);

            if (active)
            {
                Subscribe();
                if (board != null && resetOnEnter && board.HasFinished) board.ResetStage();

                // The board reads this every frame to pick things up. Handing it over here means a
                // stage camera swapped at runtime can never leave the board raycasting from a camera
                // that isn't rendering - the single most common way a drag stage ends up dead.
                if (board != null && stageCamera != null) board.stageCamera = stageCamera;

                // The click or tap that opened the stage must not also grab a wire. The board is
                // told here rather than working it out for itself, because it may sit outside the
                // stage root and so never gets an OnEnable when the player arrives - and this is
                // the only place that knows for certain that they just did.
                if (board != null) board.IgnorePressUntilReleased();

                // Every plugging phase gets the same camera, for the same reason - and a clean
                // slate, so a player who backed out halfway through starts that phase again rather
                // than finding four cords already plugged in.
                foreach (var phase in Phases())
                {
                    if (stageCamera != null) phase.stageCamera = stageCamera;
                }
                if (resetOnEnter) ResetPhases();
                else activePhase = -1;
            }

            SetPlayerVisible(!active);

            foreach (var script in playerControlScripts)
            {
                if (script != null) script.enabled = !active;
            }

            foreach (var obj in mobileControlsToHide)
            {
                if (obj != null) obj.SetActive(!active);
            }
            foreach (var obj in mobileStageControls)
            {
                if (obj != null) obj.SetActive(active);
            }

            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;

            if (active)
            {
                OnStageOpened?.Invoke();
                ShowIntroIfDue();

                // Last, so the phase's camera move starts from a fully-opened station and nothing
                // that runs above can overwrite the pose it flies to.
                if (skipPunchingForTesting) SkipToPlugging();
            }
            else
            {
                OnStageClosed?.Invoke();
            }
        }

        /// <summary>
        /// Opens straight into the first plugging phase, as if the punching were already done.
        ///
        /// The punch board is SUSPENDED, not disabled or reset. Nothing about the termination is
        /// altered - it simply is not asked for - so what you are testing is the real phase, not a
        /// special case that only exists while this flag is ticked.
        /// </summary>
        void SkipToPlugging()
        {
            Debug.LogWarning("[PunchDownStation] SKIPPING THE PUNCHING - this is a testing setting. " +
                             "Untick 'Skip Punching For Testing' before a real run.", this);

            if (board != null) board.InputSuspended = true;

            if (!BeginNextPhase())
                Debug.LogWarning("[PunchDownStation] ...but there was no plugging phase to skip to. " +
                                 "The lines above say why.", this);
        }

        /// <summary>
        /// Takes the player out of the shot, and puts them back exactly as they were.
        ///
        /// The restore records what it found rather than switching everything on, because a player
        /// rig usually has renderers that are deliberately off - a third-person body hidden in first
        /// person, an LOD that is not showing, a held item that was put away. Blanket-enabling them
        /// on the way out would make the player leave the station wearing things they did not have
        /// when they walked up.
        /// </summary>
        void SetPlayerVisible(bool visible)
        {
            if (!visible)
            {
                if (playerHidden) return;               // already out of shot
                playerHidden = true;

                if (playerBody != null)
                {
                    // Collected once, then reused. Include inactive: a first-person rig routinely
                    // parks its third-person body on a deactivated object.
                    if (bodyRenderers == null)
                    {
                        bodyRenderers = playerBody.GetComponentsInChildren<Renderer>(true);
                        bodyWasEnabled = new bool[bodyRenderers.Length];
                    }

                    for (int i = 0; i < bodyRenderers.Length; i++)
                    {
                        if (bodyRenderers[i] == null) continue;
                        bodyWasEnabled[i] = bodyRenderers[i].enabled;
                        bodyRenderers[i].enabled = false;
                    }
                }

                foreach (var go in alsoHideInStage) { if (go != null) go.SetActive(false); }
                return;
            }

            // Nothing was hidden, so there is nothing to restore. This matters: Awake calls
            // SetViewActive(false), and without this guard the restore would run against an
            // all-false record and switch the player's renderers off before the game had started.
            if (!playerHidden) return;
            playerHidden = false;

            if (bodyRenderers != null)
            {
                for (int i = 0; i < bodyRenderers.Length; i++)
                {
                    if (bodyRenderers[i] == null) continue;
                    bodyRenderers[i].enabled = bodyWasEnabled[i];
                }
            }

            foreach (var go in alsoHideInStage) { if (go != null) go.SetActive(true); }
        }

        void ShowIntroIfDue()
        {
            if (introDialog == null) return;
            if (introOnlyOnce && introShown) return;
            if (DialogManager.Instance == null || DialogManager.Instance.IsDialogOpen) return;

            introShown = true;
            DialogManager.Instance.ShowDialog(introDialog);
        }

        // ------------------------------------------------------------------

        /// <summary>Names what's missing rather than letting the station quietly do nothing.</summary>
        [ContextMenu("Check my setup")]
        public void ValidateSetup()
        {
            var sb = new System.Text.StringBuilder();

            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
                sb.AppendLine("  - The Collider on this object is NOT a trigger. Interactable finds " +
                              "the player through OnTriggerEnter, so the station can never light up. " +
                              "Tick 'Is Trigger'.");

            // Include inactive objects deliberately.
            //
            // The plain FindFirstObjectByType defaults to FindObjectsInactive.Exclude, and it missed
            // a PlayerInteractor that was plainly in the scene - reporting "none found" and sending
            // you looking for a problem that did not exist. Searching everything and then checking
            // whether it is actually live tells the truth in both directions.
            var interactor = Object.FindFirstObjectByType<PlayerInteractor>(FindObjectsInactive.Include);
            if (interactor == null)
            {
                sb.AppendLine("  - No PlayerInteractor in the scene, so nothing dispatches the E key. " +
                              "Use the " + testEnterKey + " test key for now.");
            }
            else if (!interactor.isActiveAndEnabled)
            {
                sb.AppendLine("  - The PlayerInteractor on '" + interactor.name + "' is disabled, or " +
                              "its object is inactive, so E will not reach any station until it is " +
                              "switched back on.");
            }

            if (stageCamera == null)
                sb.AppendLine("  - Stage Camera is empty. Entering would switch the player camera " +
                              "off and nothing on, so the screen goes black.");
            if (playerCamera == null)
                sb.AppendLine("  - Player Camera is empty, so leaving the stage won't restore the " +
                              "player's view.");
            if (uiRoot == null)
                sb.AppendLine("  - UI Root is empty. The punch-down tool icon won't be shown, so " +
                              "nothing can be struck.");
            if (board == null)
                sb.AppendLine("  - Board is empty, so no verdict can ever be reported back here.");
            else if (board.stageCamera == null && stageCamera == null)
                sb.AppendLine("  - Neither this station nor the board has a stage camera, so no " +
                              "conductor can be picked up.");

            if (requiredTaskIds != null && requiredTaskIds.Length > 0)
            {
                var setup = Object.FindFirstObjectByType<COC2TaskSetup>();
                if (setup == null)
                {
                    sb.AppendLine("  - Required Task Ids are set but there is no COC2TaskSetup in the " +
                                  "scene to register them, so they can never complete and the station " +
                                  "would stay locked forever.");
                }
                else
                {
                    foreach (string id in requiredTaskIds)
                    {
                        if (string.IsNullOrEmpty(id)) continue;
                        if (id != setup.crimpTaskId && id != setup.testTaskId && id != setup.patchPanelTaskId)
                            sb.AppendLine("  - Required Task Id '" + id + "' matches nothing on " +
                                          "COC2TaskSetup. A typo here locks the station permanently " +
                                          "and looks exactly like a working gate.");
                    }
                }
            }

            if (playerBody == null)
                sb.AppendLine("  - Player Body is empty. If the stage camera looks at the real rack " +
                              "in the environment - which it does when the board has Search Roots " +
                              "filled - the player is standing between it and the panel, and their " +
                              "body and shadow will be in every shot. Drag the player object in.");

            // The one thing that makes this stage unplayable on a phone rather than merely awkward.
            // Exit Key needs a keyboard; a touch device has none, so without a Back button in
            // Mobile Stage Controls the player enters the station and can never leave it.
            if (mobileStageControls == null || mobileStageControls.Length == 0)
                sb.AppendLine("  - Mobile Stage Controls is empty. Exit Key is " + exitKey + ", which " +
                              "needs a keyboard - on a touch build the player would be stuck in the " +
                              "station with no way out. Make a Back button, hook its On Click to " +
                              "this component's RequestExit(), and put it in this list so it only " +
                              "appears inside the stage.");

            foreach (var phase in Phases())
            {
                if (phase.cameraPose == null)
                    sb.AppendLine("  - Plugging phase '" + phase.phaseName + "' has no Camera Pose, " +
                                  "so when it starts the camera stays wherever the previous phase " +
                                  "left it and what the player has to plug into may be off screen.");
            }

            if (failAction == FailAction.RestartWholeProcedure && procedureReset == null)
                sb.AppendLine("  - Fail Action is RestartWholeProcedure but Procedure Reset is empty, " +
                              "so a failure would reset nothing.");

            if (bypassGateForTesting)
                Debug.LogWarning("[PunchDownStation] GATE BYPASSED. Anyone can open this station. " +
                                 "Untick 'Bypass Gate For Testing' before a graded build.", this);

            if (skipPunchingForTesting)
                Debug.LogWarning("[PunchDownStation] SKIP PUNCHING is ON. Entering this station goes " +
                                 "straight to the plugging - the termination is never asked for. " +
                                 "Untick 'Skip Punching For Testing' before a real run.", this);

            if (sb.Length == 0)
            {
                Debug.LogWarning("[PunchDownStation] Setup looks complete. Walk up and press E, or " +
                                 "press " + testEnterKey + " from anywhere to open the board.", this);
                return;
            }
            Debug.LogWarning("[PunchDownStation] Setup is incomplete:\n" + sb +
                             "  (Press " + testEnterKey + " to open it anyway and test what works.)", this);
        }
    }
}