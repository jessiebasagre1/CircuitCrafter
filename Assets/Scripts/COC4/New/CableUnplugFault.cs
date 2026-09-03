using UnityEngine;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// A cable that isn't connected.
    ///
    /// REPLACES CableUnplugFaultSetup + CableFaultTicketReporter.
    ///
    /// AUTHOR THE CABLE AS NORMALLY PLUGGED IN - tick Start Plugged on the
    /// CableConnector and assign its Start Socket, exactly like a healthy PC.
    /// This disconnects it at runtime, and only for its own ticket, so every
    /// other ticket visiting the same scene still sees a tidy machine.
    /// </summary>
    public class CableUnplugFault : TicketFault
    {
        [Header("What to unplug")]
        [SerializeField] private CableConnector cable;

        [Tooltip("Where the loose connector should hang once unplugged. REQUIRED for the fault to be visible - see the note below.")]
        [SerializeField] private Transform disconnectedRestPoint;

        protected override void Inject()
        {
            if (cable == null)
            {
                Debug.LogWarning("[CableUnplugFault] No CableConnector assigned.", this);
                return;
            }

            if (!cable.isPlugged)
            {
                Debug.LogWarning("[CableUnplugFault] The cable was already unplugged at scene start. Author it as normally PLUGGED (tick Start Plugged, assign Start Socket) and let this component disconnect it - otherwise every other ticket sharing this scene sees the fault too.", this);
                return;
            }

            cable.Unplug();

            // Unplug() only clears the logical state and detaches the parent - it
            // does not move the transform. That is fine when a player drags a
            // cable out, because the drag repositions it as a side effect. Called
            // from code with no drag, it would leave the connector sitting exactly
            // on the socket: logically unplugged, visually still connected, and a
            // learner staring at a machine with no visible fault.
            if (disconnectedRestPoint != null)
            {
                cable.transform.SetPositionAndRotation(disconnectedRestPoint.position, disconnectedRestPoint.rotation);

                // The rope re-pins its end anchor every physics step anyway, so
                // this is only about the segment count: rebuilding now recalculates
                // it for the much longer span immediately, instead of the old count
                // stretching oddly for a frame or two.
                if (cable.rope != null) cable.rope.ForceRebuild();
            }
            else
            {
                Debug.LogWarning("[CableUnplugFault] No Disconnected Rest Point assigned - the cable is logically unplugged but hasn't moved, so it still LOOKS plugged in. Assign a Transform where it should hang.", this);
            }
        }

        public override bool IsRepaired()
        {
            return cable != null && cable.isPlugged;
        }
    }
}
