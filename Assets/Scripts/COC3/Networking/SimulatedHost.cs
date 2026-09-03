using System.Collections.Generic;

namespace CircuitCrafter.Networking
{
    /// <summary>
    /// One machine on the simulated network - the server, the client, a router,
    /// a printer.
    ///
    /// A plain C# class, not a MonoBehaviour, so the network rules can be tested
    /// without Unity and so one scene object can own several of these if it
    /// needs to. A thin MonoBehaviour wrapper can hold one and expose it to the
    /// Inspector when the COC3 scene needs that.
    ///
    /// The fields mirror what the learner actually fills in on the Windows IPv4
    /// properties dialog, one for one, because that is the screen this is
    /// standing in for.
    /// </summary>
    public class SimulatedHost
    {
        public string HostName = "PC";

        /// <summary>What the learner typed, or what DHCP handed out.</summary>
        public IPv4Address Address = IPv4Address.None;

        public IPv4Address SubnetMask = IPv4Address.None;

        /// <summary>Blank is legal and common - a host with no gateway simply
        /// can't reach anything off its own subnet, which is a real and
        /// diagnosable state rather than an error.</summary>
        public IPv4Address DefaultGateway = IPv4Address.None;

        public readonly List<IPv4Address> DnsServers = new List<IPv4Address>();

        /// <summary>False when the machine is off. Its own class of ping failure,
        /// and the first thing a technician should check.</summary>
        public bool IsPoweredOn = true;

        /// <summary>False for an unplugged or faulted cable. This is the hook
        /// COC4 needs: a cable fault in COC2's world becomes "Media disconnected"
        /// in ipconfig here, with no special-case code.</summary>
        public bool IsLinkUp = true;

        /// <summary>True while set to "Obtain an IP address automatically" with
        /// no DHCP server answering - which is what makes Windows self-assign a
        /// 169.254 address. Set the address to a link-local one alongside this.</summary>
        public bool IsUsingDhcp;

        /// <summary>True if this host forwards traffic between subnets. The
        /// simulation needs at least one for any cross-subnet exercise to
        /// succeed.</summary>
        public bool IsRouter;

        // ------------------------------------------------------------------

        /// <summary>Configured well enough to put a packet on the wire: a real
        /// address and a mask that is actually a mask. Not a judgement about
        /// whether the values are the RIGHT ones for the exercise - that is the
        /// validator's job, and the two questions want different answers.</summary>
        public bool HasUsableAddress
        {
            get
            {
                if (Address.IsUnspecified || SubnetMask.IsUnspecified) return false;
                if (!SubnetMask.IsValidSubnetMask()) return false;
                if (Address.IsNetworkAddress(SubnetMask)) return false;
                if (Address.IsBroadcastAddress(SubnetMask)) return false;
                return true;
            }
        }

        /// <summary>Online enough to send or answer anything.</summary>
        public bool IsReachableCandidate => IsPoweredOn && IsLinkUp && HasUsableAddress;

        public bool IsOnSameSubnetAs(IPv4Address other)
        {
            return IPv4Address.SameSubnet(Address, other, SubnetMask);
        }

        /// <summary>True when a gateway is set AND sits on this host's own subnet.
        /// A gateway outside the local subnet is unreachable by definition, and it
        /// is one of the most common configuration mistakes there is - worth
        /// asking about separately from "is a gateway set at all".</summary>
        public bool HasReachableGateway
        {
            get
            {
                if (DefaultGateway.IsUnspecified) return false;
                if (!HasUsableAddress) return false;
                return IsOnSameSubnetAs(DefaultGateway);
            }
        }

        /// <summary>What ipconfig would print. Handy for a console-style step and
        /// for debugging the simulation itself.</summary>
        public string ToIpConfigString()
        {
            if (!IsPoweredOn) return $"{HostName}: powered off";
            if (!IsLinkUp) return $"{HostName}: Media State . . . : Media disconnected";

            var lines = new List<string>
            {
                $"{HostName}",
                $"   IPv4 Address. . . . . . . . . . . : {Address}",
                $"   Subnet Mask . . . . . . . . . . . : {SubnetMask}",
                $"   Default Gateway . . . . . . . . . : {(DefaultGateway.IsUnspecified ? "" : DefaultGateway.ToString())}"
            };

            for (int i = 0; i < DnsServers.Count; i++)
            {
                lines.Add(i == 0
                    ? $"   DNS Servers . . . . . . . . . . . : {DnsServers[i]}"
                    : $"                                       {DnsServers[i]}");
            }

            return string.Join("\n", lines);
        }
    }
}
