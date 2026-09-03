using System;
using UnityEngine;

namespace CircuitCrafter.Networking.Unity
{
    /// <summary>
    /// What a configuration step requires, authored in the Inspector.
    ///
    /// Create with: Create > CircuitCrafter > Network Requirement
    ///
    /// This is the asset that replaces ScreenshotStepData's expectedIP /
    /// expectedSubnetMask / expectedGateway. Those three fields can only express
    /// one right answer; these express the RULES, so any address that genuinely
    /// works is accepted and every rejection can name what it broke.
    ///
    /// The normal setup is: fill in Network Address and Subnet Mask, list the
    /// addresses already taken, and leave Required Address blank. That gives you
    /// "any usable host address on this subnet that isn't already spoken for",
    /// which is what a real technician is actually deciding.
    /// </summary>
    [CreateAssetMenu(fileName = "NetworkRequirement", menuName = "CircuitCrafter/Network Requirement")]
    public class NetworkRequirementData : ScriptableObject
    {
        [Serializable]
        public class ReservedAddress
        {
            [Tooltip("An address a learner must not choose, e.g. \"192.168.1.1\".")]
            public string address = "";

            [Tooltip("What it belongs to, e.g. \"the default gateway\". Used in the feedback so the learner is told what they'd have clashed with.")]
            public string label = "";
        }

        [Header("The subnet")]
        [Tooltip("The network address, e.g. \"192.168.1.0\".")]
        public string networkAddress = "192.168.1.0";

        [Tooltip("The mask that defines it, e.g. \"255.255.255.0\".")]
        public string subnetMask = "255.255.255.0";

        [Header("Exact answer (usually leave blank)")]
        [Tooltip("Fill in ONLY where one specific address is genuinely required - a server the rest of the lab is configured to expect, for instance. Blank means any usable host address on the subnet is accepted, which is the better default.")]
        public string requiredAddress = "";

        [Header("Already taken")]
        [Tooltip("Addresses in use by other equipment. A learner who picks one is told what it clashes with.")]
        public ReservedAddress[] reserved = new ReservedAddress[0];

        [Header("Rules")]
        [Tooltip("Reject loopback (127.x), automatic private (169.254.x) and multicast addresses. Leave on - none of them are ever a correct answer for a lab host.")]
        public bool rejectReservedRanges = true;

        /// <summary>Turns the authored strings into the runtime rule object.</summary>
        public AddressRequirement ToRequirement()
        {
            var requirement = AddressRequirement.For(networkAddress, subnetMask);
            requirement.RejectReservedRanges = rejectReservedRanges;

            if (!string.IsNullOrWhiteSpace(requiredAddress)
                && IPv4Address.TryParse(requiredAddress, out IPv4Address exact))
            {
                requirement.RequiredAddress = exact;
            }

            foreach (var entry in reserved)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.address)) continue;
                requirement.Reserve(entry.address, entry.label);
            }

            return requirement;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Catches the authoring mistakes that would otherwise show up as a step
        /// no learner can ever pass. Worth shouting about while you're filling the
        /// asset in rather than during a demo.
        /// </summary>
        private void OnValidate()
        {
            if (!string.IsNullOrWhiteSpace(subnetMask))
            {
                if (!IPv4Address.TryParse(subnetMask, out IPv4Address mask))
                    Debug.LogWarning($"[NetworkRequirementData] '{name}': \"{subnetMask}\" isn't a valid address.", this);
                else if (!mask.IsValidSubnetMask())
                    Debug.LogWarning($"[NetworkRequirementData] '{name}': {subnetMask} isn't a valid subnet mask - it needs an unbroken run of 1 bits followed by 0 bits.", this);
                else if (IPv4Address.TryParse(networkAddress, out IPv4Address net) && !net.IsNetworkAddress(mask))
                    Debug.LogWarning($"[NetworkRequirementData] '{name}': {networkAddress} is not the network address for a {subnetMask} subnet - did you mean {net.NetworkAddress(mask)}?", this);
            }

            foreach (var entry in reserved)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.address)) continue;

                if (!IPv4Address.TryParse(entry.address, out _))
                    Debug.LogWarning($"[NetworkRequirementData] '{name}': reserved entry \"{entry.address}\" isn't a valid address, so it will never be matched.", this);
            }
        }
#endif
    }
}
