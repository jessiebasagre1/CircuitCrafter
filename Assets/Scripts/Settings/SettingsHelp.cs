using System.Collections.Generic;

namespace CircuitCrafter.Settings
{
    /// <summary>
    /// What each setting actually does, in plain words, for the question-mark
    /// buttons on the settings screen.
    ///
    /// WHY THE TEXT LIVES IN CODE RATHER THAN IN THE INSPECTOR
    /// Every one of these has to match the control it sits beside, and a string
    /// typed into a TMP_Text on a row is invisible from anywhere else - so the day
    /// a setting's range changes, nobody finds the sentence that still describes
    /// the old one. Keeping them in one table means the whole set can be read,
    /// checked for tone, and revised in a single pass. The builder wires each
    /// question mark to a key here; nothing is retyped.
    ///
    /// WRITTEN FOR A GRADE 12 STUDENT, NOT A DEVELOPER. "How fast the view turns
    /// when you move the mouse" is useful; "multiplier applied to Input.GetAxis"
    /// is not. Where a setting has a real reason to exist, the text says what that
    /// reason is - a learner who understands why a control is there makes a better
    /// choice than one who just drags it.
    /// </summary>
    public static class SettingsHelp
    {
        public struct Entry
        {
            public string title;
            public string body;

            public Entry(string title, string body)
            {
                this.title = title;
                this.body = body;
            }
        }

        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>
        {
            ["mouseLook"] = new Entry(
                "Mouse look speed",
                "How far the view turns when you move the mouse. Higher means you can look around quickly with a small movement; " +
                "lower gives you finer control when you are lining something up. If the camera feels like it slides past what you " +
                "are aiming at, turn this down."),

            ["touchLook"] = new Entry(
                "Touch look speed",
                "How far the view turns when you drag a finger across the screen. A big tablet usually wants this lower than a " +
                "small phone, because the same turn takes a longer drag. Set it so a comfortable thumb movement turns you about " +
                "a quarter of the way round."),

            ["invertLook"] = new Entry(
                "Invert vertical look",
                "Normally, pushing the mouse or your finger forward looks UP. Turn this on and it looks DOWN instead, the way " +
                "aircraft controls work. Some people find this far more natural and cannot play comfortably without it. It " +
                "changes the up-and-down direction only - left and right stay the same."),

            ["stationPan"] = new Entry(
                "Drag speed",
                "How fast the close-up view slides when you drag it - at the assembly bench, and inside the opened system unit. " +
                "This is separate from look speed because you are moving a fixed view of a machine here, not turning your head."),

            ["stationZoom"] = new Entry(
                "Zoom speed",
                "How quickly the close-up view moves in and out - the scroll wheel on a computer, a two-finger pinch on a phone " +
                "or tablet. Turn it down if you keep overshooting the part you are trying to look at."),

            ["invertDrag"] = new Entry(
                "Invert drag direction",
                "Decides what dragging means in a close-up view. Off: you are moving the camera, so dragging left moves your view " +
                "left. On: you are moving the machine, so dragging left pushes the machine left and the view appears to go right. " +
                "Neither is correct - pick whichever matches how you expect it to behave."),

            ["uiScale"] = new Entry(
                "Interface size",
                "Makes every menu, button and piece of text bigger or smaller. Turn it up when you are working on a phone or when " +
                "the game is being shown on a projector and people at the back need to read it. Turn it down to fit more on screen " +
                "at once. It does not change the 3D view, only the interface on top of it."),

            ["reduceMotion"] = new Entry(
                "Reduce motion",
                "Skips the animations between views - the system unit tipping onto its side, mainly. The change still happens, it " +
                "just happens instantly. Useful once you have seen it a few times and are working through several repair tickets, " +
                "and important for anyone who finds moving views uncomfortable."),

            ["performance"] = new Entry(
                "Performance mode",
                "Lowers the graphics quality and limits the game to 30 frames per second. On an older or mid-range phone or tablet " +
                "this usually makes the game smoother and steadier, not slower, and it keeps the device from getting hot and " +
                "draining the battery. Turn it off on a computer or a newer device for a sharper picture."),

            ["fullscreen"] = new Entry(
                "Fullscreen",
                "Fills the whole screen instead of running in a window. Turn it off if you need to keep notes or instructions open " +
                "beside the game."),

            ["volume"] = new Entry(
                "Master volume",
                "The overall loudness of the game. It controls every sound at once, and the two sliders " +
                "below adjust music and effects within it."),

            ["music"] = new Entry(
                "Music volume",
                "Background music only. Turn it right down to work in silence without losing the sounds " +
                "that tell you a part has seated or a step is finished."),

            ["sfx"] = new Entry(
                "Sound effects volume",
                "The sounds the equipment makes - parts coming loose and clicking into place, the hotbar, " +
                "and the warning when a step is refused. These are feedback, not decoration, so keep them " +
                "audible: they confirm an action actually took."),
        };

        /// <summary>The tip for a setting, or a clearly-marked placeholder rather
        /// than an empty box - a help button that opens nothing reads as broken.</summary>
        public static Entry Get(string settingId)
        {
            if (!string.IsNullOrEmpty(settingId) && entries.TryGetValue(settingId, out Entry found))
                return found;

            return new Entry("No description yet",
                $"Nothing has been written for '{settingId}'. Add it to SettingsHelp.cs.");
        }

        public static bool Has(string settingId)
        {
            return !string.IsNullOrEmpty(settingId) && entries.ContainsKey(settingId);
        }
    }
}