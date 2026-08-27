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
            if (!InStageView)
            {
                if (testEnterKey != KeyCode.None && Input.GetKeyDown(testEnterKey)) EnterForTesting();
                return;
            }

            if (exitKey != KeyCode.None && Input.GetKeyDown(exitKey)) RequestExit();
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
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || board == null) return;
            board.OnFinished -= HandleFinished;
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
            if (sequence != null && sequence.AdvanceOnPass()) return;

            SetViewActive(false);
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

            // Stay in the view and let them go again - the conductors are back in the tray.
            if (board != null) board.ResetStage();
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
            }

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
            }
            else
            {
                OnStageClosed?.Invoke();
            }
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

            if (failAction == FailAction.RestartWholeProcedure && procedureReset == null)
                sb.AppendLine("  - Fail Action is RestartWholeProcedure but Procedure Reset is empty, " +
                              "so a failure would reset nothing.");

            if (bypassGateForTesting)
                Debug.LogWarning("[PunchDownStation] GATE BYPASSED. Anyone can open this station. " +
                                 "Untick 'Bypass Gate For Testing' before a graded build.", this);

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