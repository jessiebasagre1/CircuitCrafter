using System.Collections;
using UnityEngine;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The line that plays when the learner arrives at a repair site, built from
    /// whichever ticket sent them there.
    ///
    /// ATTACH THIS TO: the Managers object in each repair-site scene, beside
    /// RepairTicketSceneGate. One per scene.
    ///
    /// WHY THIS IS NOT IntroDialogTrigger
    /// That one plays the scene's own fixed introduction, and RepairTicketSceneGate
    /// deliberately switches it off during a ticket - a lesson about assembling a
    /// PC is the wrong thing to hear when you have been sent to fix one. But
    /// turning it off left the arrival silent, which reads as a bug rather than a
    /// choice. This is the replacement, and the two never run at once: this one
    /// disables itself outside RepairTicketMode, exactly where the other takes over.
    ///
    /// IT WRITES ITSELF FROM THE TICKET.
    /// No per-ticket authoring, no list to keep in step with the ticket library:
    /// the customer's own words are already on TicketData.description, and that is
    /// the right text for this moment. Add a ticket tomorrow and its arrival line
    /// exists the moment you write its description.
    ///
    /// WHAT IT DELIBERATELY LEAVES OUT
    /// TicketData.symptomText - the technical summary, "fans spin briefly then
    /// settle, no POST" - is NOT included, and that is the whole point. That is
    /// what the learner is meant to discover by walking up and pressing POWER. A
    /// briefing that hands it over on arrival turns the first step of the job into
    /// a formality.
    /// </summary>
    public class TicketArrivalDialog : MonoBehaviour
    {
        [Header("What to say")]
        [Tooltip("Names the place in the opening line, e.g. \"the computer laboratory\". Leave blank to skip that sentence.")]
        [SerializeField] private string siteName = "the computer laboratory";

        [Tooltip("Who the customer's report is attributed to.")]
        [SerializeField] private string reportSpeaker = "Service call";

        [Tooltip("Who the closing line is attributed to - the learner's own thought.")]
        [SerializeField] private string playerSpeaker = "You";

        [Tooltip("The closing line, which should point at the first step rather than describe the fault. Leave blank to skip it.")]
        [TextArea(2, 4)]
        [SerializeField] private string firstStepLine =
            "Right. Before I touch anything, I want to see what it actually does. Switch it on and watch.";

        [Header("Instead of the generated text (optional)")]
        [Tooltip("ON replays the ticket's own Briefing Dialog here, the one already shown at the hub. Off by default: the learner has just read it, and hearing it twice in ten seconds teaches them to skip dialogs.")]
        [SerializeField] private bool replayBriefingDialog;

        [Header("Timing")]
        [Tooltip("A beat before it plays, so the scene has finished loading in and the learner is not reading over a fade.")]
        [Min(0f)]
        [SerializeField] private float delaySeconds = 0.4f;

        [Tooltip("Frees the cursor and holds the player still while they read - the same thing IntroDialogTrigger does, and for the same reason: MouseLook would otherwise spin the camera to any mouse movement made while reading.")]
        [SerializeField] private bool lockPlayerWhileReading = true;

        [Header("After it finishes (optional)")]
        [Tooltip("The waypoint arrow points here once the dialog is dismissed. The MONITOR, not the system unit - powering on is the first step.")]
        [SerializeField] private Transform waypointTarget;

        private DialogSequence generated;
        private bool holdingLock;

        // ==================================================================

        private void Start()
        {
            if (!RepairTicketMode.IsActive)
            {
                // Normal campaign play in this scene. IntroDialogTrigger owns the
                // arrival then, and two intros talking over each other is worse
                // than either alone.
                enabled = false;
                return;
            }

            StartCoroutine(PlayAfterDelay());
        }

        private void OnDisable()
        {
            // Never leave the player frozen because this object was switched off
            // while the dialog was up.
            ReleaseLock();
        }

        private IEnumerator PlayAfterDelay()
        {
            if (delaySeconds > 0f) yield return new WaitForSeconds(delaySeconds);

            if (DialogManager.Instance == null)
            {
                Debug.LogWarning("[TicketArrivalDialog] No DialogManager in this scene - the arrival is silent.", this);
                yield break;
            }

            DialogSequence sequence = BuildSequence();
            if (sequence == null || sequence.lines.Count == 0)
            {
                Debug.LogWarning("[TicketArrivalDialog] Nothing to say - the ticket has no description and no briefing dialog.", this);
                yield break;
            }

            if (lockPlayerWhileReading)
            {
                holdingLock = true;
                PlayerInputLock.Acquire();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            Debug.Log($"[TicketArrivalDialog] Arrived on ticket '{RepairTicketMode.ActiveTicketId}'.", this);
            DialogManager.Instance.ShowDialog(sequence, OnFinished);
        }

        // ==================================================================

        private DialogSequence BuildSequence()
        {
            TicketData ticket = RepairTicketMode.ActiveTicket;
            if (ticket == null) return null;

            if (replayBriefingDialog && ticket.briefingDialog != null)
                return ticket.briefingDialog;

            // Built once and reused. A DialogSequence created per arrival would leak
            // a ScriptableObject on every visit, and a learner works several tickets
            // in a session.
            if (generated == null)
                generated = ScriptableObject.CreateInstance<DialogSequence>();

            generated.lines.Clear();

            string opening = string.IsNullOrWhiteSpace(siteName)
                ? ticket.title
                : $"You are at {siteName}.\n\n{ticket.title}";

            if (!string.IsNullOrWhiteSpace(ticket.description))
                opening += $"\n\n\"{ticket.description}\"";

            generated.lines.Add(new DialogLine
            {
                speakerName = string.IsNullOrWhiteSpace(reportSpeaker) ? "Service call" : reportSpeaker,
                text = opening
            });

            if (!string.IsNullOrWhiteSpace(firstStepLine))
            {
                generated.lines.Add(new DialogLine
                {
                    speakerName = string.IsNullOrWhiteSpace(playerSpeaker) ? "You" : playerSpeaker,
                    text = firstStepLine
                });
            }

            return generated;
        }

        private void OnFinished()
        {
            ReleaseLock();

            if (waypointTarget != null && WaypointIndicator.Instance != null)
                WaypointIndicator.Instance.SetTarget(waypointTarget);
        }

        private void ReleaseLock()
        {
            if (!holdingLock) return;
            holdingLock = false;

            PlayerInputLock.Release();

            // Only re-lock the cursor if nothing else still needs it free - a
            // station the learner walked into while reading, say.
            if (!PlayerInputLock.IsLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
