using UnityEngine;
using CircuitCrafter.Networking;
using CircuitCrafter.Networking.Unity;

namespace CircuitCrafter.Repair
{
    /// <summary>What is wrong with the machine's network configuration.</summary>
    public enum NetworkFaultKind
    {
        WrongAddress,
        WrongSubnetMask,
        WrongGateway,
        NoGateway,
        DuplicateAddress,
        CableUnplugged,
        DhcpWithNoServer
    }

    /// <summary>
    /// A machine that can't talk to the network, and the reason why.
    ///
    /// THIS IS THE ONE THAT WAS EXPENSIVE AND IS NOW FREE. Every kind below is
    /// already modelled by SimulatedNetwork, with its own real Windows message
    /// and its own explanation, so a network ticket needs no new machinery at all
    /// - just this component and a NetworkedWorkstation to point it at.
    ///
    /// It is also the most honest troubleshooting content in the game. A wrong
    /// subnet mask and an unplugged cable produce visibly different ping output,
    /// and the learner has to read that output to tell them apart. There is no
    /// answer key to memorise, because the symptom is derived from the fault.
    ///
    /// REPAIR IS DEFINED BY CONNECTIVITY, NOT BY MATCHING A VALUE.
    /// The fault counts as fixed when the machine can actually reach Ping Target
    /// - so a learner who picks a different valid address than the one that was
    /// there before has still fixed it, which is correct. Fixing the symptom by
    /// accident is impossible: the ping only succeeds when the configuration
    /// genuinely works at both ends.
    /// </summary>
    public class NetworkFault : TicketFault
    {
        [Header("What is broken")]
        [Tooltip("The machine to misconfigure.")]
        [SerializeField] private NetworkedWorkstation station;

        [SerializeField] private NetworkFaultKind kind = NetworkFaultKind.WrongSubnetMask;

        [Tooltip("The bad value to write, for the Wrong Address / Wrong Subnet Mask / Wrong Gateway kinds. Leave blank for a sensible default. Ignored by the other kinds.")]
        [SerializeField] private string badValue = "";

        [Header("What counts as fixed")]
        [Tooltip("The machine this one must be able to reach again. Usually the server.")]
        [SerializeField] private NetworkedWorkstation pingTarget;

        [Tooltip("Also require the machine's own address to be a usable one. Leave on - it stops a learner 'fixing' connectivity by giving the machine an address that happens to work but breaks the rules.")]
        [SerializeField] private bool requireUsableAddress = true;

        /// <summary>A misconfigured network card does not stop a computer starting,
        /// and telling the learner it does would teach a symptom that does not
        /// exist. The machine reaches the desktop; the trouble is waiting there.</summary>
        protected override BootImpact DefaultBootImpact => BootImpact.BootsButFaulty;

        protected override string DefaultBootSymptom =>
            "Windows starts normally, but nothing on the network can be reached.";

        protected override void Inject()
        {
            if (station == null)
            {
                Debug.LogWarning("[NetworkFault] No workstation assigned.", this);
                return;
            }

            switch (kind)
            {
                case NetworkFaultKind.WrongAddress:
                    station.SetAddress(ParseOr(badValue, "192.168.9.50"));
                    break;

                case NetworkFaultKind.WrongSubnetMask:
                    // The classic. Valid as a mask, wrong for this network, and it
                    // makes the target look like it is on another subnet - so the
                    // machine tries to route to a gateway instead of speaking
                    // directly, and the ping fails in a way that reads as a routing
                    // problem rather than an addressing one.
                    station.SetSubnetMask(ParseOr(badValue, "255.255.255.240"));
                    break;

                case NetworkFaultKind.WrongGateway:
                    station.SetGateway(ParseOr(badValue, "192.168.9.1"));
                    break;

                case NetworkFaultKind.NoGateway:
                    station.SetGateway(IPv4Address.None);
                    break;

                case NetworkFaultKind.DuplicateAddress:
                    // Give this machine the target's address. Everything on both
                    // machines looks correctly filled in and nothing works, which
                    // is exactly why it is a good ticket.
                    if (pingTarget != null) station.SetAddress(pingTarget.Host.Address);
                    else Debug.LogWarning("[NetworkFault] Duplicate Address needs a Ping Target to copy an address from.", this);
                    break;

                case NetworkFaultKind.CableUnplugged:
                    station.SetLinkUp(false);
                    break;

                case NetworkFaultKind.DhcpWithNoServer:
                    // What Windows really does when it asks for an address and
                    // nothing answers: assigns itself a 169.254 one.
                    station.SetUsingDhcp(true);
                    station.SetAddress(ParseOr(badValue, "169.254.14.62"));
                    break;
            }

            // The network caches its host list; a changed address has to be seen.
            if (NetworkSimulationManager.Instance != null)
                NetworkSimulationManager.Instance.Rebuild();
        }

        public override bool IsRepaired()
        {
            if (station == null || pingTarget == null) return false;
            if (NetworkSimulationManager.Instance == null) return false;

            if (requireUsableAddress && !station.Host.HasUsableAddress) return false;

            return NetworkSimulationManager.Instance
                       .Ping(station, pingTarget.Host.Address.ToString(), 1).Success;
        }

        private static IPv4Address ParseOr(string text, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(text) && IPv4Address.TryParse(text, out IPv4Address parsed))
                return parsed;

            IPv4Address.TryParse(fallback, out IPv4Address defaulted);
            return defaulted;
        }
    }
}