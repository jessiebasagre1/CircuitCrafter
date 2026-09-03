using UnityEngine;

namespace CircuitCrafter.Networking.Unity
{
    /// <summary>
    /// The scene's one SimulatedNetwork, and the thing panels ask to run a ping.
    ///
    /// ATTACH THIS TO: the Managers object in the COC3 scene. One per scene, same
    /// idea as TaskManager - it is scene-local on purpose, because the machines
    /// it knows about are the ones in this scene.
    ///
    /// IT BUILDS ITSELF LAZILY, NOT IN Start().
    /// Every public method calls EnsureBuilt() first. That matters because a UI
    /// panel can easily run its own Start() before this one - Unity gives no
    /// ordering guarantee between components on different objects - and a ping
    /// issued against a network that hadn't collected its hosts yet would report
    /// a perfectly confident "no host at that address". Same trap the lazy
    /// CanvasGroup in ScreenshotStep exists to avoid.
    /// </summary>
    public class NetworkSimulationManager : MonoBehaviour
    {
        public static NetworkSimulationManager Instance { get; private set; }

        [Tooltip("Log every ping and its verdict to the Console. Useful while authoring an exercise; noisy in a build.")]
        [SerializeField] private bool logPings = true;

        private readonly SimulatedNetwork network = new SimulatedNetwork();
        private bool built;

        public SimulatedNetwork Network
        {
            get { EnsureBuilt(); return network; }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy(this), not the GameObject - the Managers object it sits on
                // carries other components that must survive. Same reasoning as
                // ProfileManager's guard.
                Debug.Log($"[NetworkSimulationManager] A second manager on '{name}' stood down.", this);
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Collects every NetworkedWorkstation in the scene, once.</summary>
        public void EnsureBuilt()
        {
            if (built) return;
            built = true;

            network.Clear();

            // Include inactive: a workstation whose object is switched off at scene
            // start is still part of the exercise, and a machine that is "off" in
            // the fiction is modelled by IsPoweredOn, not by deactivating it.
            var stations = FindObjectsByType<NetworkedWorkstation>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var station in stations)
            {
                if (station != null) network.Add(station.Host);
            }

            Debug.Log($"[NetworkSimulationManager] Network built with {stations.Length} machine(s).", this);
        }

        /// <summary>Forces a rebuild - call it if you spawn or destroy a
        /// workstation while the scene is running.</summary>
        public void Rebuild()
        {
            built = false;
            EnsureBuilt();
        }

        // ==================================================================

        public PingOutcome Ping(NetworkedWorkstation source, string destination, int packets = 4)
        {
            EnsureBuilt();

            if (source == null)
            {
                Debug.LogWarning("[NetworkSimulationManager] Ping called with no source workstation.", this);
                return new PingOutcome
                {
                    Success = false,
                    Failure = PingFailure.SourceNotConfigured,
                    ConsoleMessage = "PING: transmit failed. General failure.",
                    Explanation = "No source machine was assigned on the panel that ran this ping."
                };
            }

            PingOutcome outcome = network.Ping(source.Host, destination, packets);

            if (logPings)
            {
                Debug.Log($"[NetworkSimulationManager] {source.Host.HostName} -> {destination}: " +
                          $"{(outcome.Success ? "reply" : outcome.Failure.ToString())}. {outcome.Explanation}", this);
            }

            return outcome;
        }

        /// <summary>True when every machine listed can reach every other one -
        /// a convenient end-of-exercise check for "the network is correctly set
        /// up", rather than testing one direction and hoping.</summary>
        public bool AllReachable(params NetworkedWorkstation[] stations)
        {
            EnsureBuilt();
            if (stations == null || stations.Length < 2) return false;

            foreach (var from in stations)
            {
                foreach (var to in stations)
                {
                    if (from == null || to == null || from == to) continue;
                    if (!network.Ping(from.Host, to.Host.Address, 1).Success) return false;
                }
            }
            return true;
        }
    }
}
