using System.Collections.Generic;

namespace CircuitCrafter.Networking
{
    /// <summary>Why a ping didn't get through. Each value maps to one real
    /// Windows message and one plain-language explanation - see PingOutcome.</summary>
    public enum PingFailure
    {
        None = 0,
        SourcePoweredOff,
        SourceLinkDown,
        SourceNotConfigured,
        SourceAddressConflict,
        SourceSelfAssigned,
        DestinationMalformed,
        NoHostAtAddress,
        DestinationPoweredOff,
        DestinationLinkDown,
        NoGatewayConfigured,
        GatewayOffSubnet,
        NoRouterAtGateway,
        DestinationUnroutable
    }

    /// <summary>
    /// The result of one ping. Carries BOTH the message Windows would print and
    /// a sentence explaining it.
    ///
    /// That pairing is the whole pedagogical point. Showing only the real output
    /// teaches learners to recognise "Destination host unreachable" without
    /// knowing what it means; showing only an explanation teaches them something
    /// they'll never see on a real screen. Show the authentic line in the console
    /// window, and put Explanation in the feedback panel beside it.
    /// </summary>
    public class PingOutcome
    {
        public bool Success;
        public PingFailure Failure = PingFailure.None;

        public int PacketsSent;
        public int PacketsReceived;

        /// <summary>Verbatim what Windows would show.</summary>
        public string ConsoleMessage = "";

        /// <summary>Why, in words a learner can act on. Never shown as the
        /// command's own output.</summary>
        public string Explanation = "";

        public int PacketsLost => PacketsSent - PacketsReceived;

        public int LossPercent => PacketsSent == 0 ? 0 : 100 * PacketsLost / PacketsSent;
    }

    /// <summary>
    /// Every machine in the exercise, and the rules for whether one can reach
    /// another.
    ///
    /// THIS REPLACES A SINGLE BOOLEAN. COC3Manager currently decides whether a
    /// ping succeeds from lastNetworkConfigCorrect - one flag carried from the
    /// immediately preceding step. That means a correct configuration entered two
    /// steps earlier cannot influence anything, a genuinely broken one cannot be
    /// diagnosed, and the learner is really being told "you typed the expected
    /// characters a moment ago" dressed up as a network test.
    ///
    /// Here the answer is derived: it depends on what is actually configured on
    /// both machines, right now. Fix the mask three steps later and the ping
    /// starts working, because the ping is reading the configuration rather than
    /// a memory of whether a form was filled in correctly.
    ///
    /// Plain C#, no Unity - so it is unit-testable, and COC4 can reuse it to
    /// build network faults with no new machinery.
    /// </summary>
    public class SimulatedNetwork
    {
        private readonly List<SimulatedHost> hosts = new List<SimulatedHost>();

        public IReadOnlyList<SimulatedHost> Hosts => hosts;

        public void Add(SimulatedHost host)
        {
            if (host != null && !hosts.Contains(host)) hosts.Add(host);
        }

        public void Remove(SimulatedHost host) => hosts.Remove(host);

        public void Clear() => hosts.Clear();

        public SimulatedHost FindByName(string hostName)
        {
            foreach (var host in hosts)
            {
                if (host != null && host.HostName == hostName) return host;
            }
            return null;
        }

        /// <summary>Any powered-on host answering to this address. Ignores link
        /// state so the caller can tell "nothing there" apart from "there, but
        /// unplugged" - two different faults with two different fixes.</summary>
        public SimulatedHost FindByAddress(IPv4Address address)
        {
            foreach (var host in hosts)
            {
                if (host != null && host.Address == address && host.IsPoweredOn) return host;
            }
            return null;
        }

        /// <summary>
        /// True when more than one powered-on host claims this address. Windows
        /// reports an address conflict and the interface stops working, which is
        /// a superb COC4 fault: everything looks correctly configured and nothing
        /// works.
        /// </summary>
        public bool HasAddressConflict(IPv4Address address)
        {
            if (address.IsUnspecified) return false;

            int count = 0;
            foreach (var host in hosts)
            {
                if (host != null && host.IsPoweredOn && host.Address == address) count++;
                if (count > 1) return true;
            }
            return false;
        }

        // ==================================================================
        // Ping
        // ==================================================================

        public PingOutcome Ping(SimulatedHost source, string destinationText, int packets = 4)
        {
            if (!IPv4Address.TryParse(destinationText, out IPv4Address destination))
            {
                return Fail(PingFailure.DestinationMalformed, packets, 0,
                    $"Ping request could not find host {destinationText}. Please check the name and try again.",
                    "That isn't a valid IPv4 address. Check for a missing octet, an extra dot, or a number above 255.");
            }

            return Ping(source, destination, packets);
        }

        /// <summary>
        /// Walks the same checks in the same order a technician would, and stops
        /// at the first one that fails. The order matters: a machine that is off
        /// should report being off, not a routing problem, even though both are
        /// true at once.
        /// </summary>
        public PingOutcome Ping(SimulatedHost source, IPv4Address destination, int packets = 4)
        {
            if (source == null)
            {
                return Fail(PingFailure.SourceNotConfigured, packets, 0,
                    "PING: transmit failed. General failure.",
                    "There is no source machine to ping from.");
            }

            // ---- the source end -------------------------------------------

            if (!source.IsPoweredOn)
            {
                return Fail(PingFailure.SourcePoweredOff, 0, 0,
                    "",
                    $"{source.HostName} is switched off, so nothing can be sent from it.");
            }

            if (!source.IsLinkUp)
            {
                return Fail(PingFailure.SourceLinkDown, packets, 0,
                    "PING: transmit failed. General failure.",
                    "The network cable on this machine is unplugged or faulty - ipconfig would show \"Media disconnected\". Check the cable and the link light before anything else.");
            }

            if (HasAddressConflict(source.Address))
            {
                return Fail(PingFailure.SourceAddressConflict, packets, 0,
                    "PING: transmit failed. General failure.",
                    $"Another machine on this network is already using {source.Address}. Windows disables the interface when it detects a duplicate address - give one of the two machines a different address.");
            }

            if (source.IsUsingDhcp && source.Address.IsLinkLocal)
            {
                return Fail(PingFailure.SourceSelfAssigned, packets, 0,
                    "PING: transmit failed. General failure.",
                    $"This machine is set to obtain an address automatically and no DHCP server answered, so Windows assigned itself {source.Address}. Set a static address, or fix the DHCP server.");
            }

            if (!source.HasUsableAddress)
            {
                string why = DescribeAddressProblem(source);
                return Fail(PingFailure.SourceNotConfigured, packets, 0,
                    "PING: transmit failed. General failure.",
                    $"This machine has no usable IP configuration. {why}");
            }

            // ---- same subnet, or off to the gateway? ----------------------

            bool local = source.IsOnSameSubnetAs(destination);

            if (!local)
            {
                if (source.DefaultGateway.IsUnspecified)
                {
                    return Fail(PingFailure.NoGatewayConfigured, packets, 0,
                        "Reply from " + source.Address + ": Destination host unreachable.",
                        $"{destination} is on a different subnet and this machine has no default gateway, so it has nowhere to send the packet. A host with no gateway can only reach its own subnet.");
                }

                if (!source.IsOnSameSubnetAs(source.DefaultGateway))
                {
                    return Fail(PingFailure.GatewayOffSubnet, packets, 0,
                        "Reply from " + source.Address + ": Destination host unreachable.",
                        $"The default gateway {source.DefaultGateway} is not on this machine's own subnet ({source.Address.NetworkAddress(source.SubnetMask)}/{source.SubnetMask.PrefixLength()}), so it can never be reached. A gateway must be an address this machine could ping directly.");
                }

                SimulatedHost router = FindByAddress(source.DefaultGateway);
                if (router == null || !router.IsLinkUp || !router.IsRouter)
                {
                    return Fail(PingFailure.NoRouterAtGateway, packets, 0,
                        "Request timed out.",
                        $"Nothing is answering at the gateway address {source.DefaultGateway}. Either that address is wrong, or the router there is off or not forwarding.");
                }
            }

            // ---- the destination end --------------------------------------

            SimulatedHost target = FindByAddress(destination);

            if (target == null)
            {
                // On the local subnet this is an ARP failure and the machine says
                // so itself; through a router it's silence, and you get a timeout.
                return local
                    ? Fail(PingFailure.NoHostAtAddress, packets, 0,
                        "Reply from " + source.Address + ": Destination host unreachable.",
                        $"Nothing on this subnet is using {destination}. Check the address you typed, and check the target machine's own configuration.")
                    : Fail(PingFailure.DestinationUnroutable, packets, 0,
                        "Request timed out.",
                        $"No reply from {destination}. The packet left this machine, so look at the far end or the route between.");
            }

            if (!target.IsLinkUp)
            {
                return Fail(PingFailure.DestinationLinkDown, packets, 0,
                    "Request timed out.",
                    $"{target.HostName} has an unplugged or faulty network cable, so it never receives the request.");
            }

            if (!target.HasUsableAddress)
            {
                // Reported as unroutable rather than getting its own value: from
                // the source's point of view a silent far end is a silent far end,
                // which is exactly why this one has to be diagnosed at the other
                // machine rather than from here.
                return Fail(PingFailure.DestinationUnroutable, packets, 0,
                    "Request timed out.",
                    $"{target.HostName} does not have a usable IP configuration, so it cannot reply.");
            }

            // A reply also needs the target to be able to send one BACK. This is
            // the asymmetry learners find genuinely surprising and worth teaching:
            // a wrong mask at the far end breaks the return path while the
            // outbound path is perfect.
            if (!target.IsOnSameSubnetAs(source.Address)
                && !target.HasReachableGateway)
            {
                return Fail(PingFailure.DestinationUnroutable, packets, 0,
                    "Request timed out.",
                    $"{target.HostName} receives the request but cannot reply: {source.Address} is not on its subnet and it has no usable default gateway. Check the mask and gateway at BOTH ends, not just this one.");
            }

            return new PingOutcome
            {
                Success = true,
                Failure = PingFailure.None,
                PacketsSent = packets,
                PacketsReceived = packets,
                ConsoleMessage = $"Reply from {destination}: bytes=32 time<1ms TTL=128",
                Explanation = $"{source.HostName} reached {target.HostName} successfully."
            };
        }

        // ==================================================================

        /// <summary>Names the specific thing wrong with a host's own address, so
        /// feedback can say which rule was broken rather than "invalid".</summary>
        private static string DescribeAddressProblem(SimulatedHost host)
        {
            if (host.Address.IsUnspecified) return "No IP address has been set.";
            if (host.SubnetMask.IsUnspecified) return "No subnet mask has been set.";

            if (!host.SubnetMask.IsValidSubnetMask())
                return $"{host.SubnetMask} is not a valid subnet mask - a mask must be an unbroken run of 1 bits followed by 0 bits, like 255.255.255.0.";

            if (host.Address.IsNetworkAddress(host.SubnetMask))
                return $"{host.Address} is the network address for this subnet, which identifies the network itself and can't be given to a host.";

            if (host.Address.IsBroadcastAddress(host.SubnetMask))
                return $"{host.Address} is the broadcast address for this subnet and can't be given to a host.";

            return "The address and mask combination is not usable.";
        }

        private static PingOutcome Fail(PingFailure failure, int sent, int received,
                                        string console, string explanation)
        {
            return new PingOutcome
            {
                Success = false,
                Failure = failure,
                PacketsSent = sent,
                PacketsReceived = received,
                ConsoleMessage = console,
                Explanation = explanation
            };
        }
    }
}
