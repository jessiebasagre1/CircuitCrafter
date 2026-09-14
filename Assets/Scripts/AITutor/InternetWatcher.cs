// InternetWatcher.cs
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace CSSSimulation.Tutor
{
    /// <summary>
    /// Says whether this device can actually reach the internet, and tells anyone who asks when
    /// that changes.
    ///
    /// WHY Application.internetReachability IS NOT THE ANSWER
    ///
    /// That property reports whether the device has a network INTERFACE up - a Wi-Fi association,
    /// a mobile data connection. It says nothing about whether anything is on the other end. All
    /// of these report ReachableViaLocalAreaNetwork while the internet is completely unavailable:
    ///
    ///   - a school Wi-Fi captive portal that has not been signed into
    ///   - a router with no upstream link
    ///   - mobile data with the balance used up
    ///   - a firewall that blocks everything except the portal
    ///
    /// A trainee on campus Wi-Fi hits at least one of those regularly. If the chat icon is on
    /// screen because the interface is up, every question they ask will time out after several
    /// seconds and tell them nothing useful. So this asks a real server a real question.
    ///
    /// HOW THE PROBE WORKS
    ///
    /// It fetches a URL that is designed for exactly this and nothing else: Google's connectivity
    /// check, which returns 204 No Content and an empty body. It is a few bytes, it costs no API
    /// quota, and it is the same endpoint Android itself uses to decide whether to show the "no
    /// internet" icon.
    ///
    /// Redirects are NOT followed, on purpose. A captive portal answers by redirecting you to its
    /// sign-in page, so a redirect is the portal telling us it is in the way - which is the one
    /// case a plain reachability check gets wrong. Anything other than a clean 204 counts as
    /// offline.
    ///
    /// The interface check still earns its place as a first gate: when the device says there is no
    /// network at all, there is no point spending battery on a request that cannot leave.
    ///
    /// SETUP: add it to the same object as AiTutorChat. Nothing to fill in.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class InternetWatcher : MonoBehaviour
    {
        /// <summary>The watcher in this scene, if there is one.</summary>
        public static InternetWatcher Instance { get; private set; }

        /// <summary>
        /// True when a probe has actually completed successfully.
        ///
        /// Starts FALSE and stays false until the first probe answers, which takes a moment. That
        /// is deliberate: showing the chat icon and then snatching it away a second later looks
        /// broken, while it appearing a second after the scene loads looks like it was waiting to
        /// be sure - which is exactly what happened.
        /// </summary>
        public static bool IsOnline { get; private set; }

        /// <summary>Raised whenever the answer changes. True means online.</summary>
        public event Action<bool> OnChanged;

        [Header("The probe")]
        [Tooltip("The host to ask. Defaults to the Gemini API's own host, because that is the " +
                 "thing that actually has to be reachable.\n\n" +
                 "With no key on the URL it answers 403 or 404 in a few bytes - which is a perfect " +
                 "answer for this purpose: any reply at all proves the round trip works, and it " +
                 "costs no quota.")]
        public string probeUrl = "https://generativelanguage.googleapis.com/";

        [Tooltip("Tried only when the first one does not answer. A second host on a different " +
                 "network keeps one blocked or slow CDN from declaring the whole internet down.")]
        public string fallbackProbeUrl = "https://www.gstatic.com/generate_204";

        [Tooltip("Seconds between probes while the answer is not changing.")]
        public float checkEvery = 15f;

        [Tooltip("Seconds between probes just after a change, until the answer settles. A network " +
                 "that has only just come back is worth re-checking sooner than one that has been " +
                 "steady for a minute.")]
        public float checkEveryAfterChange = 4f;

        [Tooltip("Seconds to wait for a probe before giving up on it.\n\n" +
                 "Eight rather than five: on a loaded campus Wi-Fi or a phone on mobile data, a " +
                 "first request can take several seconds purely to open the connection, and a " +
                 "timeout tuned for a good desk connection reports the internet as down on exactly " +
                 "the networks this most needs to work on.")]
        public int timeoutSeconds = 8;

        [Tooltip("Refuse to follow redirects, so a captive portal is caught rather than mistaken " +
                 "for a working connection.\n\n" +
                 "Untick it if a network you use answers the probe with a redirect chain that is " +
                 "legitimate - some corporate proxies do - and this keeps reporting offline while " +
                 "everything else works.")]
        public bool catchCaptivePortals = true;

        [Header("Diagnostics")]
        [Tooltip("Say in the Console each time the answer changes, with the reason. One line per " +
                 "change, and the fastest way to answer 'why is the chat icon missing'.")]
        public bool logChanges = true;

        [Tooltip("Say something on EVERY probe, not just on changes. Noisy - for when a connection " +
                 "is dropping in and out and you want to see the pattern.")]
        public bool logEveryProbe = false;

        /// <summary>Why the last probe answered the way it did. Shown in the chat's status line.</summary>
        public string LastReason { get; private set; }

        bool probing;
        float nextProbe;
        bool settled;

        void Awake()
        {
            Instance = this;
            LastReason = "not checked yet";
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (probing || Time.unscaledTime < nextProbe) return;
            StartCoroutine(Probe());
        }

        /// <summary>
        /// Checks now rather than waiting for the timer.
        ///
        /// Worth calling the moment the player opens the chat: the last scheduled probe could be
        /// fifteen seconds stale, and fifteen seconds is long enough to walk out of Wi-Fi range.
        /// </summary>
        public void CheckNow()
        {
            if (probing) return;
            nextProbe = 0f;
        }

        IEnumerator Probe()
        {
            probing = true;

            // The interface is down, so nothing can leave the device. No point spending a request
            // on it, and the answer is not in any doubt.
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Settle(false, "the device reports no network connection at all");
                probing = false;
                yield break;
            }

            bool ok = false;
            string why = "";

            // Two hosts, because one host having a bad day is not the internet being down. That is
            // not hypothetical: the first version of this asked a single CDN, that CDN timed out
            // on a connection where the Gemini API itself was answering fine, and the chat button
            // disappeared from a game that would have worked perfectly.
            string[] hosts = string.IsNullOrEmpty(fallbackProbeUrl)
                ? new string[] { probeUrl }
                : new string[] { probeUrl, fallbackProbeUrl };

            for (int i = 0; i < hosts.Length; i++)
            {
                if (string.IsNullOrEmpty(hosts[i])) continue;

                UnityWebRequest req = UnityWebRequest.Get(hosts[i]);
                req.timeout = Mathf.Max(1, timeoutSeconds);

                // A captive portal answers by redirecting to its sign-in page. Refusing to follow
                // that is what turns "the Wi-Fi is connected" into "the internet is reachable".
                if (catchCaptivePortals) req.redirectLimit = 0;

                yield return req.SendWebRequest();

                long code = req.responseCode;
                UnityWebRequest.Result result = req.result;
                string error = req.error;
                req.Dispose();

                bool redirected = code >= 300 && code < 400;

                // ANY real answer counts, not only 200 or 204.
                //
                // The point of this is to find out whether a request can leave the device and come
                // back, and a 403 or a 404 proves that just as well as a 204 does - better, in
                // fact, when it comes from the very host the chat has to talk to. Demanding a
                // specific status code was the other half of the first version's mistake: it meant
                // the probe only worked against a purpose-built endpoint, so it could not be
                // pointed at the API that actually matters.
                if (code > 0 && !redirected)
                {
                    ok = true;
                    why = "reached " + Host(hosts[i]) + " (answered " + code + ")";
                    break;
                }

                if (redirected)
                {
                    why = "a captive portal redirected the check at " + Host(hosts[i]) + " (code " +
                          code + ") - this network needs signing into first";
                    break;      // a portal will redirect the fallback too; no point asking twice
                }

                why = result == UnityWebRequest.Result.ConnectionError
                    ? "could not reach " + Host(hosts[i]) + " (" + error + ")"
                    : "the check at " + Host(hosts[i]) + " timed out after " + timeoutSeconds + "s";

                if (logEveryProbe && i < hosts.Length - 1)
                    Debug.Log("[InternetWatcher] " + why + " - trying " +
                              Host(hosts[i + 1]) + " instead.", this);
            }

            if (logEveryProbe)
                Debug.Log("[InternetWatcher] probe: " + (ok ? "ONLINE" : "OFFLINE") + " - " + why, this);

            Settle(ok, why);
            probing = false;
        }

        void Settle(bool online, string why)
        {
            LastReason = why;

            bool changed = online != IsOnline || !settled;
            IsOnline = online;

            nextProbe = Time.unscaledTime +
                        Mathf.Max(1f, changed ? checkEveryAfterChange : checkEvery);

            if (!changed) return;

            bool first = !settled;
            settled = true;

            if (logChanges)
                Debug.Log("[InternetWatcher] " + (online ? "ONLINE" : "OFFLINE") +
                          (first ? " (first check)" : "") + " - " + why, this);

            if (OnChanged != null) OnChanged(online);
        }

        static string Host(string url)
        {
            try { return new Uri(url).Host; } catch { return url; }
        }

        /// <summary>Runs a probe now and prints the verdict, whatever it is.</summary>
        [ContextMenu("Check the connection now")]
        public void LogCheck()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[InternetWatcher] Only works in Play mode - the probe is a " +
                                 "coroutine.", this);
                return;
            }
            logEveryProbe = true;
            CheckNow();
        }
    }
}