using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using CircuitCrafter.Repair;   // ServiceCallState. AssessmentMode, RepairTicketMode,
                               // SaveManager and PlayerInputLock are all global.

namespace CircuitCrafter.Settings
{
    /// <summary>
    /// Starts the module the learner is currently in over again, from inside the game.
    ///
    /// ATTACH THIS TO: the pause menu panel - the object that is switched on when the player
    /// pauses. Assign the Retry button and, if you want the safety step, a small confirm panel.
    ///
    /// IT NEVER WRITES ANY TEXT. The Retry button says "Retry" and the confirm panel says
    /// whatever you typed into it. All this component decides is whether Retry is on screen and
    /// what pressing it restarts.
    ///
    /// WHY THIS IS NOT JUST "RELOAD THE SCENE"
    /// Three different things can be running in a scene that looks the same from the outside,
    /// and each needs restarting differently:
    ///
    ///   ASSESSMENT       - a timed, marked run. The mode must SURVIVE the reload, or the scene
    ///                      comes back as story mode: no HUD, no scoring, no report, and a
    ///                      learner who thinks they are being assessed and is not.
    ///   COC 4 TICKET     - a repair job running inside a COC 1/2/3 scene. The ticket must
    ///                      survive too, and the service call - observations, time spent, wrong
    ///                      diagnoses - must NOT: that is the whole thing being retried.
    ///   STORY            - ordinary campaign play. Goes through SaveManager, which drops the
    ///                      half-finished attempt while keeping the COCs already earned.
    ///
    /// Reloading the scene by hand gets the first two wrong in ways that are quiet at the time
    /// and only show up at the end, in the marks.
    ///
    /// IT ASKS FIRST, BY DEFAULT. Retry throws away everything done since the run started, and
    /// in an assessment that includes a clock the learner cannot get back. A control that
    /// destroys marked work on one tap, sitting on a menu opened by mistake, will eventually be
    /// tapped by mistake.
    /// </summary>
    public class RestartRunButton : MonoBehaviour
    {
        [Header("Buttons")]
        [Tooltip("The Retry button. Its OnClick is wired automatically - leave the list empty.")]
        [SerializeField] private Button retryButton;

        [Header("Confirm (strongly recommended)")]
        [Tooltip("A small panel with a warning, a Yes and a No. Leave empty to restart on the " +
                 "first tap - only sensible while testing.")]
        [SerializeField] private GameObject confirmPanel;

        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;

        [Header("Behaviour")]
        [Tooltip("Hide the Retry button when there is nothing sensible to restart - no game mode " +
                 "known, no assessment and no ticket. Leave ON: a Retry that does nothing is worse " +
                 "than no Retry.")]
        [SerializeField] private bool hideWhenNothingToRestart = true;

        [Tooltip("Scene to reload. Leave EMPTY to use whichever scene is active, which is almost " +
                 "always right - fill it in only for a scene that must restart somewhere else.")]
        [SerializeField] private string sceneNameOverride = "";

        private bool warnedNothingRunning;

        // ==================================================================

        private void Awake()
        {
            Wire(retryButton, OnRetryPressed);
            Wire(confirmYesButton, OnConfirmYes);
            Wire(confirmNoButton, HideConfirm);

            HideConfirm();
        }

        private void OnEnable()
        {
            // Refreshed every time the menu opens, not once. The learner can open this menu in
            // story mode, finish the module, start an assessment and open it again in the same
            // session - and whether Retry belongs on screen has to be right both times.
            HideConfirm();
            Refresh();
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        // ==================================================================

        /// <summary>What is actually running right now.</summary>
        private enum RunKind { Nothing, Story, Assessment, Ticket }

        private static RunKind CurrentRun()
        {
            // Assessment first. A COC 4 ticket can be running INSIDE an assessment, and the
            // assessment is the thing being marked - so it is the one whose rules must survive.
            if (AssessmentMode.IsActive) return RunKind.Assessment;
            if (RepairTicketMode.IsActive) return RunKind.Ticket;

            bool hasMode = SaveManager.Instance != null &&
                           !string.IsNullOrEmpty(SaveManager.Instance.CurrentGameMode);

            return hasMode ? RunKind.Story : RunKind.Nothing;
        }

        private static string RunName()
        {
            switch (CurrentRun())
            {
                case RunKind.Assessment: return AssessmentMode.ModeId + " Assessment";
                case RunKind.Ticket: return "this repair job";
                case RunKind.Story: return SaveManager.Instance.CurrentGameMode;
                default: return null;
            }
        }

        /// <summary>
        /// Re-reads what is running and decides whether the Retry button belongs on screen.
        ///
        /// Called automatically in OnEnable, which covers the normal case: this component sits
        /// on the pause panel, and the panel being switched on IS the moment to check. Public
        /// because it does not cover the other case - a component parked on an always-active
        /// object never gets a second OnEnable, so the button's visibility would be stuck at
        /// whatever was true when the scene loaded. Wire the pause button's OnClick to this as
        /// well when that is how your menu is built.
        /// </summary>
        public void Refresh()
        {
            RunKind kind = CurrentRun();

            if (retryButton != null && hideWhenNothingToRestart)
                retryButton.gameObject.SetActive(kind != RunKind.Nothing);

            if (kind == RunKind.Nothing)
            {
                // Said out loud, once. Hiding a Retry that cannot work is right, but doing it
                // silently means the button vanishes and the only explanation is in this
                // method - so the obvious conclusion is that the component is broken, when in
                // fact it is the run that is missing.
                if (!warnedNothingRunning)
                {
                    warnedNothingRunning = true;
                    Debug.LogWarning("[RestartRunButton] Retry is HIDDEN because nothing is running " +
                                     "that could be restarted:\n" +
                                     "    AssessmentMode.IsActive  : False\n" +
                                     "    RepairTicketMode.IsActive: False\n" +
                                     $"    SaveManager              : {(SaveManager.Instance == null ? "no instance in this scene" : "CurrentGameMode is empty")}\n\n" +
                                     "Almost always this means the scene was entered by pressing Play " +
                                     "directly rather than through the menu, so nothing ever told " +
                                     "SaveManager which module this is. Start from the main menu and " +
                                     "the button appears.\n\n" +
                                     "If it is hidden during a REAL run, untick Hide When Nothing To " +
                                     "Restart and right-click this component > Report retry setup.", this);
                }
                return;
            }

            warnedNothingRunning = false;

            // No text is written here, deliberately. The Retry button says "Retry" and the
            // confirm panel says whatever you typed into it - both are yours to word, and this
            // component never overwrites them. It decides only whether Retry is on screen.
        }

        // ==================================================================

        public void OnRetryPressed()
        {
            if (CurrentRun() == RunKind.Nothing)
            {
                Debug.LogWarning("[RestartRunButton] Nothing to restart - no assessment, no ticket, " +
                                 "and SaveManager has no current game mode. This usually means the " +
                                 "scene was entered by pressing Play directly rather than through " +
                                 "the menu.", this);
                return;
            }

            if (confirmPanel != null)
            {
                Refresh();
                confirmPanel.SetActive(true);
                return;
            }

            Restart();
        }

        public void HideConfirm()
        {
            if (confirmPanel != null) confirmPanel.SetActive(false);
        }

        private void OnConfirmYes()
        {
            HideConfirm();
            Restart();
        }

        // ==================================================================

        /// <summary>Restarts whatever is running. Public so a key binding or another menu can
        /// call it directly, confirm step included or not.</summary>
        public void Restart()
        {
            RunKind kind = CurrentRun();
            string scene = string.IsNullOrWhiteSpace(sceneNameOverride)
                ? SceneManager.GetActiveScene().name
                : sceneNameOverride.Trim();

            // BEFORE the load, always. This menu may have paused the game, and a scene loaded at
            // timeScale 0 comes up frozen - every coroutine that waits on scaled time never
            // resumes, and the learner is looking at a still picture with no way to tell why.
            Time.timeScale = 1f;

            // The input lock is a counter held by whatever opened this menu. A scene load destroys
            // the holder without it ever releasing, so the count would stay up for the rest of the
            // session and the player would be unable to move in the scene that just loaded.
            PlayerInputLock.ReleaseAll();

            switch (kind)
            {
                case RunKind.Assessment:
                    // AssessmentMode is a plain static, so it survives LoadScene untouched - which
                    // is exactly what is wanted. Do NOT call End() and Begin() around this: End()
                    // clears ModeId, and anything reading it during the teardown frame would see a
                    // story run.
                    Debug.Log($"[RestartRunButton] Restarting the {AssessmentMode.ModeId} assessment " +
                              $"- reloading '{scene}' with assessment mode still active.");
                    ServiceCallState.Clear();
                    SceneManager.LoadScene(scene);
                    return;

                case RunKind.Ticket:
                    // The ticket survives; the service call does not. Clearing it is the point of a
                    // retry - observations, time spent and wrong diagnoses all go, so the job is
                    // graded on this attempt rather than on the sum of both.
                    Debug.Log($"[RestartRunButton] Restarting ticket '{RepairTicketMode.ActiveTicketId}' " +
                              $"- reloading '{scene}'.");
                    ServiceCallState.Clear();
                    SceneManager.LoadScene(scene);
                    return;

                default:
                    // Story. SelectMode does the whole job: drops the half-finished attempt, keeps
                    // the COCs already completed, and loads the scene. Reloading by hand here would
                    // bring the old attempt's saved state straight back with it.
                    string mode = SaveManager.Instance.CurrentGameMode;
                    Debug.Log($"[RestartRunButton] Restarting story mode '{mode}' in '{scene}'.");
                    SaveManager.Instance.SelectMode(mode, scene);
                    return;
            }
        }

        // ==================================================================

        [ContextMenu("Report retry setup")]
        private void ReportSetup()
        {
            RunKind kind = CurrentRun();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[RestartRunButton] on '{name}':");
            sb.AppendLine($"  Running now : {kind}" + (kind == RunKind.Nothing ? "" : $" ({RunName()})"));
            sb.AppendLine($"  Would reload: '{(string.IsNullOrWhiteSpace(sceneNameOverride) ? SceneManager.GetActiveScene().name : sceneNameOverride)}'");
            sb.AppendLine($"  Retry Button: {(retryButton == null ? "NOT ASSIGNED - nothing to press" : "'" + retryButton.name + "'")}");
            sb.AppendLine(confirmPanel == null
                ? "  Confirm Panel: not assigned - Retry restarts on the FIRST tap. Fine while " +
                  "testing; on a real build this destroys a marked run with one mis-tap."
                : $"  Confirm Panel: '{confirmPanel.name}'" +
                  (confirmYesButton == null ? "   <-- but Confirm Yes Button is NOT assigned, so the panel opens and cannot be accepted" : ""));

            if (kind == RunKind.Nothing)
                sb.AppendLine("  Nothing is running. Entering the scene through the menu rather than " +
                              "pressing Play directly is what sets this.");

            Debug.Log(sb.ToString(), this);
        }
    }
}