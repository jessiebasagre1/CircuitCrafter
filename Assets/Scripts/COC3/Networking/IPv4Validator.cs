using System.Collections.Generic;

namespace CircuitCrafter.Networking
{
    /// <summary>What a field was checked against, so feedback can name the rule
    /// rather than saying "incorrect".</summary>
    public enum AddressRule
    {
        Ok = 0,
        Empty,
        Malformed,
        NotAValidMask,
        WrongMask,
        WrongSubnet,
        IsNetworkAddress,
        IsBroadcastAddress,
        IsGatewayAddress,
        IsReservedAddress,
        AlreadyInUse,
        OutsideAllowedRange,
        GatewayOffSubnet
    }

    /// <summary>The verdict on one typed value.</summary>
    public class AddressCheck
    {
        public bool IsAcceptable;
        public AddressRule Rule = AddressRule.Ok;

        /// <summary>Shown to the learner. Names the rule that was broken and what
        /// would satisfy it - never just "wrong".</summary>
        public string Message = "";

        public IPv4Address Parsed = IPv4Address.None;

        public static AddressCheck Pass(IPv4Address value, string message = "")
        {
            return new AddressCheck { IsAcceptable = true, Rule = AddressRule.Ok, Parsed = value, Message = message };
        }

        public static AddressCheck Fail(AddressRule rule, string message, IPv4Address parsed)
        {
            return new AddressCheck { IsAcceptable = false, Rule = rule, Message = message, Parsed = parsed };
        }
    }

    /// <summary>
    /// What a step requires of an address, expressed as rules rather than as one
    /// expected string.
    ///
    /// THIS IS THE REPLACEMENT FOR ValidateExactMatch. The existing check asks
    /// "did you type 192.168.1.10", so a learner who types 192.168.1.11 - equally
    /// correct, and arguably showing better understanding for having chosen it -
    /// is marked wrong. Worse, the learner who passes has demonstrated only that
    /// they can copy an answer key.
    ///
    /// Expressed this way the step asks the real question: is this a usable host
    /// address on 192.168.1.0/24 that isn't the network address, the broadcast
    /// address, the gateway, or already taken. Many answers satisfy it, which is
    /// exactly right, and every rejection can say which rule was broken.
    ///
    /// Set RequireExactAddress only where the exercise genuinely does need one
    /// specific value - a server whose address the rest of the lab is configured
    /// to expect, say.
    /// </summary>
    public class AddressRequirement
    {
        /// <summary>The subnet the answer must sit in, e.g. 192.168.1.0.</summary>
        public IPv4Address NetworkAddress = IPv4Address.None;

        /// <summary>The mask defining that subnet.</summary>
        public IPv4Address SubnetMask = IPv4Address.None;

        /// <summary>Set only when one specific address is genuinely required.
        /// Leave unset for "any usable host address on this subnet".</summary>
        public IPv4Address RequiredAddress = IPv4Address.None;

        /// <summary>Addresses already taken - the gateway, the server, other
        /// machines in the lab. A learner picking one gets told what it clashes
        /// with, by name where one is supplied.</summary>
        public readonly List<IPv4Address> Reserved = new List<IPv4Address>();

        /// <summary>Optional labels for Reserved, same order. "the default
        /// gateway", "the server". Used to make the message specific.</summary>
        public readonly List<string> ReservedLabels = new List<string>();

        /// <summary>Reject 127.x, multicast and link-local. On by default -
        /// they're never right for a lab host address.</summary>
        public bool RejectReservedRanges = true;

        // ------------------------------------------------------------------

        /// <summary>Checks a typed host address against every rule, in the order
        /// that produces the most useful message: form first, then placement,
        /// then conflicts.</summary>
        public AddressCheck CheckHostAddress(string typed)
        {
            if (string.IsNullOrWhiteSpace(typed))
                return AddressCheck.Fail(AddressRule.Empty, "Enter an IP address.", IPv4Address.None);

            if (!IPv4Address.TryParse(typed, out IPv4Address value))
            {
                return AddressCheck.Fail(AddressRule.Malformed,
                    "That isn't a valid IPv4 address. It needs four numbers from 0 to 255 separated by dots, like 192.168.1.10.",
                    IPv4Address.None);
            }

            if (!RequiredAddress.IsUnspecified && value != RequiredAddress)
            {
                return AddressCheck.Fail(AddressRule.OutsideAllowedRange,
                    $"This step needs one specific address: {RequiredAddress}. The rest of the lab is configured to expect it there.",
                    value);
            }

            if (RejectReservedRanges && (value.IsLoopback || value.IsMulticastOrReserved || value.IsLinkLocal))
            {
                string which = value.IsLoopback ? "the loopback range (127.x.x.x), which always means \"this machine\""
                             : value.IsLinkLocal ? "the automatic private range (169.254.x.x), which Windows only assigns itself when DHCP fails"
                             : "a reserved multicast range";

                return AddressCheck.Fail(AddressRule.IsReservedAddress,
                    $"{value} is in {which}. Pick an address from the subnet you've been given.", value);
            }

            if (!SubnetMask.IsUnspecified && !NetworkAddress.IsUnspecified)
            {
                if (!IPv4Address.SameSubnet(value, NetworkAddress, SubnetMask))
                {
                    return AddressCheck.Fail(AddressRule.WrongSubnet,
                        $"{value} is not on the {NetworkAddress}/{SubnetMask.PrefixLength()} subnet. With a mask of {SubnetMask}, the address has to start with the same network part as {NetworkAddress}.",
                        value);
                }

                if (value.IsNetworkAddress(SubnetMask))
                {
                    return AddressCheck.Fail(AddressRule.IsNetworkAddress,
                        $"{value} is the network address for this subnet. It names the network itself, so no host can use it. The first usable address here is {FirstUsable()}.",
                        value);
                }

                if (value.IsBroadcastAddress(SubnetMask))
                {
                    return AddressCheck.Fail(AddressRule.IsBroadcastAddress,
                        $"{value} is the broadcast address for this subnet - anything sent there goes to every host at once, so no single machine can use it. The last usable address here is {LastUsable()}.",
                        value);
                }
            }

            for (int i = 0; i < Reserved.Count; i++)
            {
                if (Reserved[i] != value) continue;

                string label = i < ReservedLabels.Count && !string.IsNullOrEmpty(ReservedLabels[i])
                    ? ReservedLabels[i]
                    : "another machine";

                return AddressCheck.Fail(AddressRule.AlreadyInUse,
                    $"{value} is already used by {label}. Two devices with the same address knock each other off the network - pick a different one.",
                    value);
            }

            return AddressCheck.Pass(value);
        }

        /// <summary>Checks a typed subnet mask. Separate from the host address
        /// because the failures are completely different and deserve their own
        /// messages.</summary>
        public AddressCheck CheckSubnetMask(string typed)
        {
            if (string.IsNullOrWhiteSpace(typed))
                return AddressCheck.Fail(AddressRule.Empty, "Enter a subnet mask.", IPv4Address.None);

            if (!IPv4Address.TryParse(typed, out IPv4Address value))
            {
                return AddressCheck.Fail(AddressRule.Malformed,
                    "That isn't a valid subnet mask. It needs four numbers from 0 to 255 separated by dots, like 255.255.255.0.",
                    IPv4Address.None);
            }

            if (!value.IsValidSubnetMask())
            {
                return AddressCheck.Fail(AddressRule.NotAValidMask,
                    $"{value} isn't a usable subnet mask. A mask has to be an unbroken run of 1 bits followed by 0 bits - so 255.255.255.0 and 255.255.240.0 are fine, but anything with a gap in the middle isn't.",
                    value);
            }

            if (!SubnetMask.IsUnspecified && value != SubnetMask)
            {
                return AddressCheck.Fail(AddressRule.WrongMask,
                    $"{value} is a valid mask, but this network uses {SubnetMask} (/{SubnetMask.PrefixLength()}). With {value} this machine would disagree with the others about which addresses are local.",
                    value);
            }

            return AddressCheck.Pass(value);
        }

        /// <summary>Checks a typed default gateway. The rule people break here is
        /// almost always the same one: putting the gateway outside the subnet it
        /// is supposed to serve.</summary>
        public AddressCheck CheckGateway(string typed, IPv4Address hostAddress)
        {
            if (string.IsNullOrWhiteSpace(typed))
                return AddressCheck.Fail(AddressRule.Empty, "Enter a default gateway.", IPv4Address.None);

            if (!IPv4Address.TryParse(typed, out IPv4Address value))
            {
                return AddressCheck.Fail(AddressRule.Malformed,
                    "That isn't a valid IPv4 address.", IPv4Address.None);
            }

            if (!RequiredAddress.IsUnspecified && value != RequiredAddress)
            {
                return AddressCheck.Fail(AddressRule.OutsideAllowedRange,
                    $"The gateway for this network is {RequiredAddress}.", value);
            }

            if (!SubnetMask.IsUnspecified && !NetworkAddress.IsUnspecified)
            {
                if (!IPv4Address.SameSubnet(value, NetworkAddress, SubnetMask))
                {
                    return AddressCheck.Fail(AddressRule.GatewayOffSubnet,
                        $"{value} is not on the {NetworkAddress}/{SubnetMask.PrefixLength()} subnet, so this machine could never reach it. A default gateway has to be an address the machine can talk to directly.",
                        value);
                }

                if (value.IsNetworkAddress(SubnetMask) || value.IsBroadcastAddress(SubnetMask))
                {
                    return AddressCheck.Fail(AddressRule.IsNetworkAddress,
                        $"{value} is the network or broadcast address for this subnet, so no router can be sitting on it.", value);
                }
            }

            if (!hostAddress.IsUnspecified && value == hostAddress)
            {
                return AddressCheck.Fail(AddressRule.IsGatewayAddress,
                    "The gateway can't be this machine's own address - a host can't be its own router.", value);
            }

            return AddressCheck.Pass(value);
        }

        // ------------------------------------------------------------------

        public IPv4Address FirstUsable()
        {
            if (SubnetMask.IsUnspecified) return IPv4Address.None;
            return new IPv4Address(NetworkAddress.NetworkAddress(SubnetMask).Value + 1);
        }

        public IPv4Address LastUsable()
        {
            if (SubnetMask.IsUnspecified) return IPv4Address.None;
            return new IPv4Address(NetworkAddress.BroadcastAddress(SubnetMask).Value - 1);
        }

        /// <summary>Convenience for building one from the strings a designer would
        /// type in the Inspector.</summary>
        public static AddressRequirement For(string network, string mask)
        {
            var requirement = new AddressRequirement();
            IPv4Address.TryParse(network, out IPv4Address n);
            IPv4Address.TryParse(mask, out IPv4Address m);
            requirement.NetworkAddress = n;
            requirement.SubnetMask = m;
            return requirement;
        }

        public AddressRequirement Reserve(string address, string label)
        {
            if (IPv4Address.TryParse(address, out IPv4Address parsed))
            {
                Reserved.Add(parsed);
                ReservedLabels.Add(label);
            }
            return this;
        }
    }
}
