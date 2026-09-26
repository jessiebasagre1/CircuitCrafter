using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// Makes the simulated desktop show the machine the learner is actually sitting at.
    ///
    /// ATTACH THIS TO: the COC3Desktop root. The builder adds and wires it.
    ///
    /// It reads COC3StationController.CurrentStation - the machine being USED, not the machine the
    /// step wants. Sitting down at the wrong computer shows you that computer's desktop, and the
    /// step gate refuses the action. Showing the right machine's windows at the wrong machine
    /// would be a lie, and a comfortable one.
    ///
    /// WINDOWS ARE NOT SWITCHED ON OR OFF BY MACHINE. A window's activeSelf means "open" - forcing
    /// it on because it belongs here would open every window on the desktop at once. Instead the
    /// wrong machine's windows simply cannot be OPENED (WinDesktop refuses), and any that are open
    /// when the learner changes machine are closed.
    ///
    /// Icons and Start entries are plain UI, so those really are shown and hidden.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class WinMachineFilter : MonoBehaviour
    {
        public static WinMachineFilter Instance { get; private set; }

        [HeaderAttribute("Which machine")]
        [TooltipAttribute("Left empty, the scene's COC3StationController is found automatically.")]
        public COC3StationController station;

        [TooltipAttribute("Used before the learner has sat down anywhere, and if there is no station " +
                 "controller in the scene at all.")]
        public COC3MachineType fallback = COC3MachineType.Server;

        [HeaderAttribute("Two Start menus")]
        [TooltipAttribute("Shown while at the server. Assigned to WinDesktop.startMenu when the " +
                 "learner is at that machine.")]
        public GameObject serverStartMenu;

        public GameObject clientStartMenu;

        [HeaderAttribute("Telling them where they are")]
        [TooltipAttribute("The wallpaper. Tinted differently per machine - the fastest possible " +
                 "answer to 'which computer am I looking at'.")]
        public Image wallpaper;

        public Color serverWallpaper = new Color(0.118f, 0.227f, 0.373f, 1f);
        public Color clientWallpaper = new Color(0.180f, 0.290f, 0.216f, 1f);

        [TooltipAttribute("The taskbar clock label, which also carries the machine name.")]
        public TMP_Text taskbarName;

        public string serverName = "SERVER01";
        public string clientName = "CLIENT01";

        [HeaderAttribute("Diagnostics")]
        public bool logChanges = true;

        /// <summary>The desktop currently on screen.</summary>
        public COC3MachineType Current { get; private set; }

        private bool applied;

        // ------------------------------------------------------------------

        private void Awake()
        {
            Instance = this;
            if (station == null) station = FindAnyObjectByType<COC3StationController>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            applied = false;      // the desktop was just shown - re-read where we are
        }

        private void Update()
        {
            COC3MachineType wanted = WantedMachine();

            if (applied && wanted == Current) return;

            Current = wanted;
            applied = true;
            Apply();

            if (logChanges) Debug.Log("[WinMachineFilter] Desktop is now " + Current + ".", this);
        }

        private COC3MachineType WantedMachine()
        {
            if (station == null) return fallback;

            // The machine being SAT AT. Null between stations, when the desktop is hidden anyway.
            var current = station.CurrentStation;
            if (current != null) return current.machine;

            return station.RequiredMachine;
        }

        /// <summary>Also public so a tool or a test can force a machine.</summary>
        public void Apply()
        {
            // EVERY open window closes, not only the other machine's.
            //
            // Walking to the other computer is not alt-tabbing. A property sheet left open on the
            // server has no business being on screen at the client, even one tagged Both - the
            // printer sheet is the same DIALOG on each machine, not the same open window. Leaving
            // it up also strands its step-scoped gate on a machine that step is not performed at.
            if (WinDesktop.Instance != null) WinDesktop.Instance.CloseAll();

            foreach (var tag in GetComponentsInChildren<WinMachine>(true))
            {
                if (tag == null) continue;

                bool belongs = tag.AppliesTo(Current);

                // activeSelf on a WINDOW means open, so it is never switched on here - CloseAll
                // above has already dealt with those. Icons and Start entries are plain UI.
                if (tag.GetComponent<WinWindow>() != null) continue;

                if (tag.gameObject.activeSelf != belongs) tag.gameObject.SetActive(belongs);
            }

            bool atServer = Current == COC3MachineType.Server;

            if (serverStartMenu != null && clientStartMenu != null)
            {
                serverStartMenu.SetActive(false);
                clientStartMenu.SetActive(false);

                if (WinDesktop.Instance != null)
                    WinDesktop.Instance.startMenu = atServer ? serverStartMenu : clientStartMenu;
            }

            if (wallpaper != null)
                wallpaper.color = atServer ? serverWallpaper : clientWallpaper;

            if (taskbarName != null)
                taskbarName.text = "10:42\n" + (atServer ? serverName : clientName);
        }

        /// <summary>
        /// True when that window may be opened on the desktop currently on screen. Called by
        /// WinDesktop.OpenWindow, so a launch button for the other machine refuses out loud
        /// instead of doing nothing.
        /// </summary>
        public bool Allows(WinWindow window)
        {
            if (window == null) return false;

            var tag = window.GetComponent<WinMachine>();
            return tag == null || tag.AppliesTo(Current);
        }
    }
}