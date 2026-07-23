using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Handles switching between normal 3D player movement and the close-up crimping
    /// view. Swaps active cameras, disables player movement/look while in the crimping
    /// view, and hands the active camera over to WireDragController so dragging lines up
    /// with whatever's actually on screen.
    /// </summary>
    public class CrimpingViewController : MonoBehaviour
    {
        public static CrimpingViewController Instance { get; private set; }

        [Header("Cameras")]
        [Tooltip("The player's normal 3D camera (first/third person).")]
        public Camera playerCamera;
        [Tooltip("A separate camera framed close-up on the connector. Set it to Orthographic " +
                  "for a flatter, more 2D feel — Perspective also works, just frame it tight and locked.")]
        public Camera crimpingCamera;

        [Header("Player control")]
        [Tooltip("Whatever script(s) handle player movement/look — assign so they get disabled while crimping.")]
        public MonoBehaviour[] playerControlScripts;

        [Header("UI")]
        [Tooltip("e.g. an 'ESC to exit' prompt, progress panel — anything specific to the crimping view.")]
        public GameObject crimpingUI;

        public bool InCrimpingView { get; private set; }

        void Awake()
        {
            Instance = this;
            SetCrimpingViewActive(false);
        }

        public void EnterCrimpingView()
        {
            if (InCrimpingView) return;
            SetCrimpingViewActive(true);
        }

        public void ExitCrimpingView()
        {
            if (!InCrimpingView) return;
            SetCrimpingViewActive(false);
        }

        void Update()
        {
            if (InCrimpingView && Input.GetKeyDown(KeyCode.Escape))
                ExitCrimpingView();
        }

        void SetCrimpingViewActive(bool active)
        {
            InCrimpingView = active;

            if (playerCamera != null) playerCamera.gameObject.SetActive(!active);
            if (crimpingCamera != null) crimpingCamera.gameObject.SetActive(active);

            foreach (var script in playerControlScripts)
            {
                if (script != null) script.enabled = !active;
            }

            if (crimpingUI != null) crimpingUI.SetActive(active);

            if (WireDragController.Instance != null)
            {
                WireDragController.Instance.worldCamera = active ? crimpingCamera : playerCamera;
                WireDragController.Instance.enabled = active;
            }

            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;
        }
    }
}