using System;
using UnityEngine;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// Real tabs on a Windows property sheet - one panel visible at a time.
    ///
    /// ATTACH THIS TO: the window root whose tabs you want to work. The desktop builder adds it
    /// for you on Users Properties and STUDENTS Properties.
    ///
    /// WHY IT EXISTS
    /// Up to now the tabs the builder drew were decoration - painted once, the selected one baked
    /// in. That was fine while every window had one job. It stops being fine at step 16 and 17,
    /// where FINDING THE TAB IS THE WHOLE LESSON: sharing lives on Sharing, permissions live on
    /// Security, and a technician who does not know which is which will hunt for either in the
    /// wrong place. A property sheet that cannot change tab cannot teach that.
    ///
    /// HOW IT WORKS
    /// Each tab is a WinToggle in one radio group, so Windows' own "clicking the selected tab does
    /// nothing, and the group is never empty" behaviour comes free. This listens for the change and
    /// switches the panels.
    ///
    /// Deliberately NOT persistent UnityEvents wired in the editor: those are invisible in a diff,
    /// break silently when an object is renamed, and are the kind of wiring that is impossible to
    /// check by reading. Subscribing in Awake is one place to look when a tab misbehaves.
    /// </summary>
    public class WinTabs : MonoBehaviour
    {
        [Serializable]
        public class Tab
        {
            [TooltipAttribute("The tab header. Give every tab in one sheet the same WinToggle Group Id.")]
            public WinToggle tab;

            [TooltipAttribute("The panel shown while that tab is selected.")]
            public GameObject panel;
        }

        public Tab[] tabs;

        [TooltipAttribute("The tab selected each time the window opens. Leave it on the FIRST tab: " +
                 "reopening a property sheet on the page the learner last used would hand them " +
                 "the answer to 'which tab was it again'.")]
        public int defaultTab = 0;

        // ------------------------------------------------------------------

        private void Awake()
        {
            if (tabs == null) return;

            for (int i = 0; i < tabs.Length; i++)
            {
                var entry = tabs[i];
                if (entry == null || entry.tab == null) continue;

                // Captured per iteration - without the local, every listener would close over the
                // final value of i and every tab would show the last panel.
                int index = i;
                entry.tab.OnChanged.AddListener(on => { if (on) Show(index); });
            }
        }

        private void OnEnable()
        {
            Select(defaultTab);
        }

        /// <summary>Also public so a step or a button can drive the sheet to a given page.</summary>
        public void Select(int index)
        {
            if (tabs == null || tabs.Length == 0) return;

            if (index < 0) index = 0;
            if (index >= tabs.Length) index = tabs.Length - 1;

            Show(index);

            for (int i = 0; i < tabs.Length; i++)
            {
                var entry = tabs[i];
                if (entry == null || entry.tab == null) continue;

                entry.tab.Set(i == index);
            }
        }

        private void Show(int index)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                var entry = tabs[i];
                if (entry == null || entry.panel == null) continue;

                entry.panel.SetActive(i == index);
            }
        }
    }
}
