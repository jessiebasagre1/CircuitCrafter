using UnityEngine;

namespace CircuitCrafter.Settings
{
    /// <summary>
    /// Marks one canvas as "leave my size alone" for the Interface Size setting.
    ///
    /// ATTACH THIS TO: the Canvas object itself - QuizCanvas, in the COC2 scene. Nothing to fill
    /// in; the component having been added IS the instruction.
    ///
    /// WHY THIS EXISTS RATHER THAN THE EXISTING LIST
    /// SettingsApplier already has an Excluded Canvases list, and it works - but only for canvases
    /// in the SAME SCENE as the applier. SettingsApplier lives in the Main Menu; QuizCanvas lives
    /// in COC2. Unity cannot hold a reference from one scene to an object in another, so that list
    /// can never name it: the field simply refuses the drag.
    ///
    /// A marker component has no such problem. It travels with the canvas, it works in any scene,
    /// it needs no names to be kept in step, and the reason a canvas is being skipped is visible on
    /// the canvas itself rather than in a list three scenes away.
    ///
    /// WHEN TO USE IT
    /// Sparingly. Interface Size is an accessibility setting and a learner who turns it up expects
    /// everything to follow - a quiz they cannot read is exactly what the setting is for. Use this
    /// only for a canvas whose layout is positioned by hand against stretched artwork and would
    /// come apart at another scale, and treat it as a note to fix that layout later rather than as
    /// the finished answer.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [DisallowMultipleComponent]
    public class IgnoreInterfaceScale : MonoBehaviour
    {
        [Tooltip("For your own reference only - why this canvas opts out. It is printed in the " +
                 "Console when SettingsApplier skips it with Verbose ticked, so a canvas that is " +
                 "mysteriously not scaling can explain itself.")]
        [TextArea(2, 4)]
        [SerializeField]
        private string reason = "Laid out by hand at 1920x1080; the rows come apart at other scales.";

        public string Reason
        {
            get { return string.IsNullOrWhiteSpace(reason) ? "no reason given" : reason.Trim(); }
        }
    }
}
