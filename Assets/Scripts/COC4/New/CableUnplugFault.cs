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
    ///
    /// THE REST POINT HAS TO BE A CHILD OF THE MACHINE.
    /// CableConnector.Unplug() calls SetParent(null), so a loose connector is a
    /// root object with no attachment to anything. Injection happens at Start,
    /// while the tower is still standing - so the connector is placed at the
    /// standing machine's back panel, and then the system unit station lays the
    /// tower over and the connector stays exactly where it was, hanging in the
    /// air above it. Nothing is broken; the cable simply was not told it belongs
    /// to the machine.
    ///
    /// So Attach To Rest Point re-parents it, and the rest point itself should be
    /// a child of the system unit root. Then it rides the tower down and arrives
    /// where the learner can reach it.
    /// </summary>
    public class CableUnplugFault : TicketFault
    {
        [Header("What to unplug")]
        [SerializeField] private CableConnector cable;

        [Tooltip("Where the loose connector should hang once unplugged. REQUIRED for the fault to be visible.\n\nMake this a CHILD of the system unit root, and position it for how the machine looks LAID DOWN - that is the pose the learner actually works in. A rest point parented to the room instead of the machine leaves the cable floating in mid-air the moment the tower tips over.")]
        [SerializeField] private Transform disconnectedRestPoint;

        [Tooltip("Leave ON. Parents the loose connector to the rest point so it travels with whatever that point is attached to - which is what stops it being left behind when the system unit is laid down.\n\nTurn it OFF only for a cable that genuinely belongs to the room rather than the machine, such as one lying on the floor by the wall socket.")]
        [SerializeField] private bool attachToRestPoint = true;

        /// <summary>Deliberately describes the machine, not the cable. "Nothing at
        /// all happens" is the honest symptom of a power cable that is out, and it
        /// is also the symptom of a dead PSU and of a wall socket that is switched
        /// off - which is exactly the ambiguity the learner is meant to resolve by
        /// looking, rather than being told.</summary>
        protected override string DefaultBootSymptom =>
            "Nothing happens at all. No lights, no fan noise, nothing on the screen.";

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

                // Parented AFTER the world pose is set, with worldPositionStays
                // true, so Unity works out the local values instead of the
                // connector jumping if the rest point is scaled or rotated.
                //
                // Safe to parent a loose connector: Plug() re-parents it to the
                // socket's snap point and Unplug() clears the parent again, so
                // nothing downstream reads this. Dragging sets world position
                // directly and does not care either.
                if (attachToRestPoint)
                    cable.transform.SetParent(disconnectedRestPoint, true);

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