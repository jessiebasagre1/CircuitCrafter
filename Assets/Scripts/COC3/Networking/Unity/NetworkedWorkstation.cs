using UnityEngine;

namespace CircuitCrafter.Networking.Unity
{
    /// <summary>
    /// Gives one machine in the scene an identity on the simulated network.
    ///
    /// ATTACH THIS TO: the Server PC object and the Client PC object in COC3 -
    /// the same objects that already carry COC3Workstation. Add one to a router
    /// object too if the exercise crosses subnets, and tick Is Router on it.
    ///
    /// The Inspector fields are the STARTING state of the machine, written the
    /// way a person types them. That's what lets you author a fault by simply
    /// leaving a field wrong: an exercise where the client arrives with the wrong
    /// mask needs no code at all, just 255.255.0.0 typed in the box.
    ///
    /// The live configuration lives on Host, and the config panel writes to it as
    /// the learner works. Nothing here is saved - a machine's network settings
    /// belong to the exercise, and restarting it should hand the learner the same
    /// starting fault every time.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkedWorkstation : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Shown in ping output and feedback, e.g. \"SERVER01\".")]
        [SerializeField] private string hostName = "PC";

        [Header("Starting configuration")]
        [Tooltip("Leave blank for a machine that starts unconfigured - which is the normal state at the beginning of the exercise.")]
        [SerializeField] private string startingAddress = "";

        [SerializeField] private string startingSubnetMask = "";
        [SerializeField] private string startingGateway = "";

        [Tooltip("Optional DNS servers, in order.")]
        [SerializeField] private string[] startingDnsServers = new string[0];

        [Header("Hardware state")]
        [Tooltip("Untick to model a machine that is switched off.")]
        [SerializeField] private bool isPoweredOn = true;

        [Tooltip("Untick to model an unplugged or faulty network cable - ipconfig would say \"Media disconnected\". This is the hook a COC4 cable fault uses.")]
        [SerializeField] private bool isLinkUp = true;

        [Tooltip("Tick for a machine set to obtain an address automatically. With no DHCP server answering, also set the starting address to a 169.254.x.x value to model what Windows really does.")]
        [SerializeField] private bool isUsingDhcp;

        [Tooltip("Tick on a router. Cross-subnet traffic only works if a powered, linked router is sitting at the source's default gateway address.")]
        [SerializeField] private bool isRouter;

        // ------------------------------------------------------------------

        private SimulatedHost host;

        /// <summary>The live machine. Built on first access rather than in Awake,
        /// so a panel that reads it early still gets a correctly configured host
        /// instead of a null - there is no ordering guarantee between components
        /// on different objects.</summary>
        public SimulatedHost Host
        {
            get
            {
                if (host == null) Build();
                return host;
            }
        }

        private void Awake()
        {
            if (host == null) Build();
        }

        private void Build()
        {
            host = new SimulatedHost
            {
                HostName = string.IsNullOrWhiteSpace(hostName) ? name : hostName,
                IsPoweredOn = isPoweredOn,
                IsLinkUp = isLinkUp,
                IsUsingDhcp = isUsingDhcp,
                IsRouter = isRouter
            };

            host.Address = Parse(startingAddress, "IP address");
            host.SubnetMask = Parse(startingSubnetMask, "subnet mask");
            host.DefaultGateway = Parse(startingGateway, "default gateway");

            foreach (string dns in startingDnsServers)
            {
                IPv4Address parsed = Parse(dns, "DNS server");
                if (!parsed.IsUnspecified) host.DnsServers.Add(parsed);
            }
        }

        /// <summary>
        /// Blank means "not set", which is a legitimate starting state and must
        /// stay silent. A non-blank value that won't parse is a typo in the
        /// Inspector, which must not: it would otherwise become a machine that is
        /// mysteriously unconfigured with no clue why.
        /// </summary>
        private IPv4Address Parse(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text)) return IPv4Address.None;

            if (IPv4Address.TryParse(text, out IPv4Address parsed)) return parsed;

            Debug.LogWarning($"[NetworkedWorkstation] '{name}' has \"{text}\" as its starting {label}, which isn't a valid IPv4 address. Treating it as unset.", this);
            return IPv4Address.None;
        }

        // ==================================================================
        // Runtime changes - what the config panel calls
        // ==================================================================

        public void SetAddress(IPv4Address address) => Host.Address = address;
        public void SetSubnetMask(IPv4Address mask) => Host.SubnetMask = mask;
        public void SetGateway(IPv4Address gateway) => Host.DefaultGateway = gateway;

        /// <summary>Switches the machine's static/automatic setting, mirroring the
        /// radio buttons on the real IPv4 properties dialog.</summary>
        public void SetUsingDhcp(bool value) => Host.IsUsingDhcp = value;

        /// <summary>Pull or restore the network cable. Wire this to a COC4 fault,
        /// or to a cable object in the scene.</summary>
        public void SetLinkUp(bool value) => Host.IsLinkUp = value;

        public void SetPoweredOn(bool value) => Host.IsPoweredOn = value;

        /// <summary>Puts the machine back to the state the Inspector describes.
        /// Wire this to a "Reset" button so a learner can start the exercise
        /// again without reloading the scene.</summary>
        public void ResetToStartingConfiguration()
        {
            host = null;
            Build();

            if (NetworkSimulationManager.Instance != null)
                NetworkSimulationManager.Instance.Rebuild();
        }

        [ContextMenu("Log ipconfig")]
        private void LogIpConfig()
        {
            Debug.Log(Host.ToIpConfigString(), this);
        }
    }
}
