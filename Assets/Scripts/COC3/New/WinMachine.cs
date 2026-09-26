using UnityEngine;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// Says which computer a window, icon or Start-menu entry belongs to.
    ///
    /// ATTACH THIS TO: a WinWindow root, a desktop icon, or a Start menu entry. The desktop
    /// builder adds it for you.
    ///
    /// WHY IT EXISTS
    /// COC 3 is done on two machines. Until now one desktop stood in for both, and the client's
    /// windows were reached from the server's Start menu - so the learner could run the "test it
    /// from the client" steps without ever walking to the client. That is the one place the
    /// module stopped being a simulation and became a list of dialogs.
    ///
    /// COC3StationController already knows which machine the learner is sitting at. This is the
    /// other half: the desktop's contents following it, so walking to the client PC and sitting
    /// down IS how you reach the client's desktop.
    /// </summary>
    public class WinMachine : MonoBehaviour
    {
        public enum Scope
        {
            Server,
            Client,

            /// <summary>On both desktops - a tool that genuinely exists on each machine.</summary>
            Both
        }

        [TooltipAttribute("Which desktop this belongs on.\n\n" +
                 "Both is for a window a technician really would open on either machine - Devices " +
                 "and Printers is used on the server to publish the printer and on the client to " +
                 "test it.")]
        public Scope scope = Scope.Server;

        public bool AppliesTo(COC3MachineType machine)
        {
            if (scope == Scope.Both) return true;

            return scope == Scope.Server
                ? machine == COC3MachineType.Server
                : machine == COC3MachineType.Client;
        }
    }
}
