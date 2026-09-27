using UnityEngine;
using UnityEngine.Events;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// Shows one tutorial card the FIRST time something happens - the first time the hotbar gets a
    /// part, the first time the AI Tutor button appears, the first time the learner walks up to the
    /// crimping bench. "Introduce tutorials only when a feature is encountered for the first time."
    ///
    /// ATTACH THIS TO: any object in the scene. One per card. For "Player Enters Trigger" put it on
    /// an object with a trigger Collider where the feature is.
    ///
    /// It fires once and switches itself off. Whether the card actually appears is then up to
    /// TutorialCardPanel: not in an assessment, and not if this learner has already seen it.
    ///
    /// DEMONSTRATION: wire On Show Me to whatever demonstrates the idea - GuideGhostDemo.Play() to
    /// float a see-through part into its slot, an Animator trigger, GuideHighlight.Pulse(). The card
    /// then gets a Show Me button.
    /// </summary>
    public class TutorialTrigger : MonoBehaviour
    {
        public enum When
        {
            SceneStart = 0,
            PlayerEntersTrigger = 1,
            ObjectBecomesActive = 2,
            TaskComplete = 3,
            HotbarGetsItem = 4,
            DragStarts = 5,
            Manual = 6
        }

        [Tooltip("The card to show.")]
        public TutorialCardData card;

        [Tooltip("What makes it appear.")]
        public When when = When.SceneStart;

        [Tooltip("Seconds to wait after the condition is met, so it does not land on the same frame as a dialog or a camera cut.")]
        [Min(0f)] public float delay = 0.5f;

        [Header("The one field your 'When' needs")]
        [Tooltip("Object Becomes Active: the object to watch - e.g. the AI Tutor button, the tool belt panel.")]
        public GameObject witness;

        [Tooltip("Task Complete: the TaskManager task id.")]
        public string taskId = "";

        [Tooltip("Player Enters Trigger: which collider counts as the player. Leave blank to accept an object tagged Player or one with a CharacterController.")]
        public string playerTag = "Player";

        [Header("Demonstrate")]
        [Tooltip("Wire a demonstration here and the card gets a Show Me button.")]
        public UnityEvent onShowMe = new UnityEvent();

        private bool fired;
        private float fireAt = -1f;

        private void Update()
        {
            if (fired) return;

            if (fireAt >= 0f)
            {
                if (Time.time >= fireAt) Fire();
                return;
            }

            if (ConditionMet()) Arm();
        }

        private bool ConditionMet()
        {
            switch (when)
            {
                case When.SceneStart: return true;
                case When.ObjectBecomesActive: return witness != null && witness.activeInHierarchy;
                case When.TaskComplete:
                    return !string.IsNullOrEmpty(taskId) && TaskManager.Instance != null &&
                           TaskManager.Instance.IsTaskComplete(taskId) &&
                           (SaveManager.Instance == null || !SaveManager.Instance.IsRestoringSave);
                case When.HotbarGetsItem: return HotbarHasItem();
                case When.DragStarts: return DragController.Instance != null && DragController.Instance.IsDragging;
                default: return false;   // PlayerEntersTrigger and Manual arm themselves
            }
        }

        private static bool HotbarHasItem()
        {
            if (HotbarManager.Instance == null || HotbarManager.Instance.slots == null) return false;
            foreach (HotbarSlot slot in HotbarManager.Instance.slots)
                if (slot != null && !slot.IsEmpty) return true;
            return false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (fired || when != When.PlayerEntersTrigger) return;
            bool isPlayer = (!string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag)) ||
                            other.GetComponent<CharacterController>() != null;
            if (isPlayer) Arm();
        }

        /// <summary>For When = Manual - wire it to any UnityEvent.</summary>
        public void Trigger() => Arm();

        private void Arm()
        {
            if (fired || fireAt >= 0f) return;
            fireAt = Time.time + delay;
        }

        private void Fire()
        {
            fired = true;
            enabled = false;

            System.Action demo = null;
            if (onShowMe != null && onShowMe.GetPersistentEventCount() > 0) demo = onShowMe.Invoke;

            TutorialCardPanel.TryShow(card, demo);
        }
    }
}
