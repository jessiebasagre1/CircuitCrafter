// AiTutorChat.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CSSSimulation.Tutor
{
    /// <summary>
    /// The message icon and the chat window. Drop one in each COC scene, set which module it is,
    /// and the trainee can ask an AI tutor about what they are doing.
    ///
    /// THE ICON IS ONLY THERE WHEN IT WORKS
    ///
    /// This needs the internet, and a lot of the machines it will run on - a campus laptop behind
    /// a portal, a phone with no data left - do not have it. An icon that is present but dead is
    /// worse than no icon: the trainee taps it, waits, gets an error, and learns the whole feature
    /// is unreliable. So the icon is only on screen while InternetWatcher has actually reached a
    /// server, and it disappears the moment that stops being true. Nothing to explain, nothing to
    /// apologise for.
    ///
    /// IT KNOWS WHERE THE PLAYER IS UP TO
    ///
    /// "What do I do next" is the question a trainee actually asks, and it is unanswerable without
    /// context. TaskWaypointArrow already holds the job order for the scene and which step is
    /// current, so this reads that and puts it in the system prompt. The tutor can then say "you
    /// have crimped one end, now strip the other" instead of a paragraph of general theory.
    ///
    /// That costs nothing to wire: if the scene has a waypoint arrow, this finds it. If it has
    /// not, the tutor still works and simply answers as a module tutor without step awareness.
    ///
    /// SETUP: run Tools > CircuitCrafter > AI Tutor Builder in each scene. It creates the canvas
    /// and fills every field below. Then set Coc Number, and paste a key into GeminiClient.
    /// </summary>
    [DefaultExecutionOrder(260)]
    public class AiTutorChat : MonoBehaviour
    {
        [Header("Which module this is")]
        [Range(1, 4)]
        [Tooltip("1 = Install and Configure Computer Systems, 2 = Set-up Computer Networks, " +
                 "3 = Set-up Computer Servers, 4 = Maintain and Repair. This is the only field " +
                 "that differs between the four scenes.")]
        public int cocNumber = 2;

        [Header("UI - the builder fills these in")]
        public Button openButton;
        public GameObject panel;
        public Button closeButton;
        public Button sendButton;
        public TMP_InputField inputField;

        [Tooltip("The scroll view's Content object. Message rows are added under it.")]
        public RectTransform messageRoot;

        [Tooltip("A message row kept INACTIVE under Content. Cloned for every message, so " +
                 "restyling one row restyles the whole conversation.")]
        public GameObject messageTemplate;

        [Tooltip("Scrolled to the bottom whenever a message arrives.")]
        public ScrollRect scrollRect;

        [Tooltip("The small line under the input - 'Thinking...', 'Offline', and errors.")]
        public TMP_Text statusLabel;

        [Header("While the chat is open")]
        [Tooltip("Switched off while the window is up, and returned to EXACTLY how they were when " +
                 "it closes. Put the player's movement and mouse-look scripts here.\n\n" +
                 "Left empty, this finds the components that read the keyboard - the interactor " +
                 "and the station Back button - and freezes those, so typing the letter e into " +
                 "the chat cannot open a PC behind the window. Movement scripts it cannot guess " +
                 "at; drag those in yourself.")]
        public MonoBehaviour[] freezeWhileOpen = new MonoBehaviour[0];

        [Tooltip("Hidden while the window is up - the crosshair, the touch controls, the interact " +
                 "prompt.")]
        public GameObject[] hideWhileOpen = new GameObject[0];

        [Tooltip("Free the mouse so the window can be clicked. Untick if your own pause handling " +
                 "already does it.")]
        public bool releaseCursor = true;

        [Header("What it says")]
        [TextArea(2, 4)]
        [Tooltip("Shown once when the window is first opened.")]
        public string greeting = "Ask me anything about this task - what a setting means, why a " +
                                 "step is done in that order, or what to do next.";

        [TextArea(6, 20)]
        [Tooltip("The instructions the AI is given before every question. This is where the tutor " +
                 "gets its manners, its scope and its length limit.\n\n" +
                 "{module} is replaced with this scene's module name and {context} with the job " +
                 "order and the current step. Edit the wording freely, but keep those two markers " +
                 "or the tutor loses track of where the player is.")]
        public string systemPrompt =
            "You are a patient workshop instructor helping a Philippine TESDA Computer Systems " +
            "Servicing NC II trainee inside a 3D training simulator. The trainee is working " +
            "through {module}.\n\n" +
            "{context}\n\n" +
            "How to answer:\n" +
            "- Keep it to 2-4 short sentences unless they ask for detail. They are reading this " +
            "on a phone, standing in a simulated workshop.\n" +
            "- Use the vocabulary of the competency standard: straight-through and crossover, " +
            "T568A and T568B, punch-down, patch panel, subnet mask, default gateway.\n" +
            "- When they ask what to do next, answer from the job order above, naming the step.\n" +
            "- Explain WHY a step is done, not only what to press. Understanding the reason is " +
            "what the assessment is actually testing.\n" +
            "- If they ask something outside computer systems servicing, say briefly that you can " +
            "only help with the module, and offer something you can help with.\n" +
            "- If they ask you to simply give them the answers to a graded quiz, do not. Explain " +
            "the underlying idea instead, so they can work it out.\n" +
            "- Never invent a control or a screen that a simulator might not have. If you are not " +
            "sure something exists here, describe the real-world procedure instead.";

        [Tooltip("Shown in the chat if the connection drops while the window is open.")]
        public string offlineMessage = "I have lost the internet connection, so I cannot answer " +
                                       "right now. The rest of the simulation carries on working.";

        [Header("Limits")]
        [Tooltip("How many past messages travel with each question. More context costs more " +
                 "tokens per question and gets you nearer the free tier's daily limit; less makes " +
                 "the tutor forget what was just said. Twelve is about six exchanges.")]
        public int rememberMessages = 12;

        [Tooltip("Seconds a trainee has to wait between questions. Stops a held Enter key from " +
                 "spending a day's quota in a minute.")]
        public float cooldownSeconds = 1.5f;

        [Tooltip("Longest question accepted, in characters.")]
        public int maxQuestionLength = 500;

        [Header("Input")]
        [Tooltip("Opens the chat, and closes it again. Tab by default - it is not bound to " +
                 "anything else in this project, it is nowhere near WASD, and no station uses it.")]
        public KeyCode openKey = KeyCode.Tab;

        [Tooltip("Also closes it, for players who reach for Escape. Leave it as Escape.")]
        public KeyCode closeKey = KeyCode.Escape;

        [Tooltip("Ignore the open key on touch devices.\n\n" +
                 "Leave it ON. A phone has no Tab key, but a Bluetooth keyboard paired to a tablet " +
                 "does, and there is no harm in it working there - this only stops the key being " +
                 "read where it cannot be pressed.")]
        public bool openKeyOnPCOnly = true;

        [Tooltip("Show the AI button ONLY on touch devices, hiding it on a keyboard.\n\n" +
                 "Left OFF, so the button is on screen everywhere. A control nobody knows about " +
                 "does not exist: a trainee who is never told about the tutor will never press Tab " +
                 "to find it, and the whole feature goes unused. The button is what advertises it.\n\n" +
                 "Tick it only if you have told the player some other way - a line in the briefing " +
                 "dialog, say - and want the screen back.")]
        public bool iconOnTouchDevicesOnly = false;

        [Tooltip("Put the key on the button when there is a keyboard - 'AI  Tab' rather than " +
                 "just 'AI'.\n\n" +
                 "This is what turns the button into a lesson. The player taps it the first time, " +
                 "reads the key while doing so, and after that reaches for Tab without thinking. " +
                 "On a phone the key is dropped, because a key nobody can press is noise.")]
        public bool showKeyOnButton = true;

        [Tooltip("The text on the AI button. Left empty, whatever TMP text sits under the button " +
                 "is used - which is what the builder makes.")]
        public TMP_Text openButtonLabel;

        [Tooltip("What the button says. {key} becomes the open key, and is dropped on touch.")]
        public string openButtonText = "AI  {key}";

        [Header("Diagnostics")]
        public bool logSetup = true;

        [Tooltip("Say when the AI button appears or disappears, and name the reason when it is " +
                 "hidden. Leave ON until the button behaves - a missing button gives you nothing " +
                 "to search for otherwise.")]
        public bool logButtonVisibility = true;

        /// <summary>True while the window is up. Read by anything that should stand aside.</summary>
        public static bool IsOpen { get; private set; }

        readonly List<GeminiClient.Turn> history = new List<GeminiClient.Turn>();
        readonly List<MonoBehaviour> frozen = new List<MonoBehaviour>();
        readonly List<bool> frozenWere = new List<bool>();

        GeminiClient client;
        InternetWatcher watcher;
        CursorLockMode savedLock;
        bool savedCursorVisible;
        bool everShown;
        bool saidWhyOnce;
        bool greeted;
        bool waiting;
        float readyAt;
        int openedOnFrame = -10;

        void Awake()
        {
            client = GetComponent<GeminiClient>();
            if (client == null) client = Object.FindFirstObjectByType<GeminiClient>();

            watcher = GetComponent<InternetWatcher>();
            if (watcher == null) watcher = Object.FindFirstObjectByType<InternetWatcher>();

            if (openButton != null) openButton.onClick.AddListener(Open);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (sendButton != null) sendButton.onClick.AddListener(SendTyped);

            if (inputField != null)
            {
                inputField.characterLimit = Mathf.Max(40, maxQuestionLength);
                inputField.onSubmit.AddListener(OnSubmitted);
            }

            if (panel != null) panel.SetActive(false);
            if (messageTemplate != null) messageTemplate.SetActive(false);

            SetStatus("");
        }

        void Start() { ValidateSetup(); }

        void OnDestroy() { if (IsOpen) IsOpen = false; }

        void Update()
        {
            // Read before the IsOpen guard, so the same key works from both sides. Tab is the
            // toggle: it is what opens the window and what puts it away again, which is one thing
            // to remember instead of two.
            //
            // Held off on the frame it opened, because the tap or press that opened it is still
            // being read this frame and would close it again immediately.
            if (Time.frameCount - openedOnFrame > 1 && KeyWantedHere && Input.GetKeyDown(openKey))
            {
                Toggle();
                return;
            }

            if (!IsOpen) return;

            // The world prompt has no business sitting behind a chat window, and the COC2
            // interactor takes this as a per-frame request rather than a switch.
            CSSSimulation.Interaction.PlayerInteractor.SuppressPromptThisFrame();

            if (Time.frameCount - openedOnFrame > 1 && Input.GetKeyDown(closeKey)) { Close(); return; }

            // Let the client say what it is doing rather than leaving "Thinking..." up for what
            // could be fifteen seconds. A retry against a busy service takes far longer than the
            // player expects, and silence during it reads as the tutor having died - which is when
            // they tap Send again and make it worse.
            if (waiting && client != null && !string.IsNullOrEmpty(client.Progress))
                SetStatus(client.Progress);

            if (watcher != null && !InternetWatcher.IsOnline && !waiting)
                SetStatus("Offline - " + watcher.LastReason);
        }

        void LateUpdate()
        {
            if (openButton == null) return;

            string blocking = WhyTheButtonIsHidden();
            bool wanted = blocking == null;

            if (openButton.gameObject.activeSelf != wanted)
            {
                openButton.gameObject.SetActive(wanted);

                if (logButtonVisibility)
                    Debug.Log("[AiTutorChat] AI button " + (wanted ? "SHOWN" : "HIDDEN - " + blocking),
                              this);
            }

            // Said once, a second or so in, when it has never been on screen. A button that is
            // simply absent gives the player nothing to ask about and gives you nothing to search
            // for - and four of the five reasons it can be absent are invisible in the Inspector.
            if (!wanted && !everShown && logButtonVisibility && Time.timeSinceLevelLoad > 3f &&
                !saidWhyOnce)
            {
                saidWhyOnce = true;
                Debug.LogWarning("[AiTutorChat] The AI button has not appeared at all. " + blocking +
                                 "\n\nRight-click this component > Why is the AI button hidden? for " +
                                 "the full picture.", this);
            }

            if (wanted)
            {
                everShown = true;
                LabelTheButton();
            }
        }

        /// <summary>
        /// The one condition keeping the button off screen, or null when it should be showing.
        ///
        /// Written as a reason rather than a bool because four of the five ways this button can be
        /// missing are invisible in the Inspector: a scene value that overrides the script default,
        /// a connectivity probe that has not answered, a dialog nobody noticed is still open, and a
        /// touch-only setting on a machine with a keyboard. From the outside they produce one
        /// identical symptom - nothing in the corner of the screen - and no error anywhere.
        /// </summary>
        string WhyTheButtonIsHidden()
        {
            if (openButton == null) return "Open Button is empty on this component.";

            if (!InternetWatcher.IsOnline)
                return "there is no internet connection yet" +
                       (watcher != null ? " (" + watcher.LastReason + ")"
                                        : " - and there is no InternetWatcher in this scene, so " +
                                          "the answer is stuck at 'not checked'");

            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
                return "a dialog is open, and the button stands aside for those.";

            if (IsOpen) return "the chat window is already open.";

            if (iconOnTouchDevicesOnly && !TouchSession)
                return "Icon On Touch Devices Only is TICKED and this is not a touch session, so " +
                       "the button is hidden on purpose. Untick it on this component - the script " +
                       "default is off, but a value already saved in the scene wins over the " +
                       "default, so changing the script did not reach this copy.";

            Transform parent = openButton.transform.parent;
            while (parent != null)
            {
                if (!parent.gameObject.activeSelf)
                    return "an ancestor of the button, '" + parent.name + "', is switched off, so " +
                           "the button cannot render however many times it is set active.";
                parent = parent.parent;
            }

            return null;
        }

        /// <summary>
        /// Writes the key onto the button, and takes it off again on a touch device.
        ///
        /// Recomputed rather than set once at Start, because whether there is a keyboard is not
        /// settled at Start: MobileControlsVisibility can be switched at runtime, and the Device
        /// Simulator flips it while the game is playing. Writing it every frame would be wasteful
        /// and would fight anyone editing the text in the Inspector, so it only writes when the
        /// answer has actually changed.
        /// </summary>
        void LabelTheButton()
        {
            if (openButtonLabel == null && openButton != null)
                openButtonLabel = openButton.GetComponentInChildren<TMP_Text>(true);

            if (openButtonLabel == null || string.IsNullOrEmpty(openButtonText)) return;

            bool withKey = showKeyOnButton && !TouchSession;

            string text = withKey
                ? openButtonText.Replace("{key}", openKey.ToString())
                // Not just a blank substitution: "AI  " with the spaces left behind sits
                // off-centre, and on a round button that reads as a layout fault rather than a
                // deliberately shorter label.
                : openButtonText.Replace("{key}", "").Trim();

            if (openButtonLabel.text != text) openButtonLabel.text = text;
        }

        /// <summary>Whether the open key should be read at all right now.</summary>
        bool KeyWantedHere
        {
            get
            {
                if (openKeyOnPCOnly && TouchSession) return false;

                // A dialog swallows every other press in this project, and a chat window opening
                // over a tutorial popup the player has not read yet is the wrong order of
                // business. Closing is still allowed, so nobody can be trapped.
                if (!IsOpen && DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
                    return false;

                return true;
            }
        }

        /// <summary>
        /// Whether this is a touch session - asked of the touch controls, not of the platform.
        ///
        /// Application.isMobilePlatform is FALSE in the Editor, with the Android build target
        /// selected, and in the Device Simulator. Reading MobileControlsVisibility instead means
        /// this cannot disagree with the rest of the HUD: if the joystick is on screen, so is the
        /// AI button, and the Tab key stands aside.
        /// </summary>
        bool TouchSession
        {
            get
            {
                if (touchControls == null)
                    touchControls = Object.FindFirstObjectByType<MobileControlsVisibility>();

                if (touchControls != null) return touchControls.Wanted;
                return Application.isMobilePlatform;
            }
        }

        MobileControlsVisibility touchControls;

        // ---------------------------------------------------------------- open / close

        public void Open()
        {
            if (IsOpen || panel == null) return;

            IsOpen = true;
            openedOnFrame = Time.frameCount;
            panel.SetActive(true);

            // The last scheduled probe could be fifteen seconds old, and fifteen seconds is long
            // enough to have walked out of Wi-Fi range.
            if (watcher != null) watcher.CheckNow();

            HoldPlayer(true);

            if (!greeted && !string.IsNullOrEmpty(greeting))
            {
                greeted = true;
                AddMessage(false, greeting);
            }

            SetStatus(InternetWatcher.IsOnline ? "" : "Offline - " +
                      (watcher != null ? watcher.LastReason : "no connection"));

            if (inputField != null)
            {
                inputField.text = "";
                inputField.ActivateInputField();
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            if (inputField != null) inputField.DeactivateInputField();
            if (panel != null) panel.SetActive(false);

            HoldPlayer(false);
        }

        /// <summary>Wire to a button that should toggle rather than only open.</summary>
        public void Toggle() { if (IsOpen) Close(); else Open(); }

        /// <summary>
        /// Freezes and unfreezes the player around the window.
        ///
        /// The restore is the part worth being careful about. Blanket-enabling everything on close
        /// is the obvious version and it is wrong: a trainee who opens the chat while sitting at a
        /// PC desktop already has their movement script switched off BY the station, and switching
        /// it back on hands them a player who can walk away while the desktop is still on screen.
        /// So each component's state is recorded on the way in and put back exactly as it was.
        /// </summary>
        void HoldPlayer(bool held)
        {
            if (held)
            {
                frozen.Clear();
                frozenWere.Clear();

                foreach (MonoBehaviour m in freezeWhileOpen)
                {
                    if (m == null) continue;
                    frozen.Add(m);
                    frozenWere.Add(m.enabled);
                    m.enabled = false;
                }

                foreach (GameObject go in hideWhileOpen)
                    if (go != null) go.SetActive(false);

                if (releaseCursor)
                {
                    savedLock = Cursor.lockState;
                    savedCursorVisible = Cursor.visible;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;

                    // CursorKeeper re-locks the cursor every frame the learner is in first-person
                    // control and nothing has claimed it. Without this Claim it would undo the two
                    // lines above on the next frame, and the tutor's text box could not be clicked
                    // into. Paired with the Release below; counted, so opening the tutor on top of a
                    // station panel cannot hand the cursor back while that panel still wants it.
                    CursorKeeper.Claim();
                }
                return;
            }

            for (int i = 0; i < frozen.Count; i++)
                if (frozen[i] != null) frozen[i].enabled = frozenWere[i];

            frozen.Clear();
            frozenWere.Clear();

            foreach (GameObject go in hideWhileOpen)
                if (go != null) go.SetActive(true);

            if (releaseCursor)
            {
                CursorKeeper.Release();      // paired with the Claim above
                Cursor.lockState = savedLock;
                Cursor.visible = savedCursorVisible;
            }
        }

        // ---------------------------------------------------------------- asking

        void OnSubmitted(string _) { SendTyped(); }

        /// <summary>Sends whatever is in the input box. Public so a hardware key can call it.</summary>
        public void SendTyped()
        {
            if (inputField == null) return;

            string question = (inputField.text ?? "").Trim();
            if (question.Length == 0) return;

            if (waiting) { SetStatus("Still thinking about the last one..."); return; }

            if (Time.unscaledTime < readyAt)
            {
                SetStatus("One moment...");
                return;
            }

            if (!InternetWatcher.IsOnline)
            {
                AddMessage(false, offlineMessage);
                SetStatus("Offline - " + (watcher != null ? watcher.LastReason : "no connection"));
                return;
            }

            if (client == null)
            {
                AddMessage(false, "The assistant is not set up in this scene.");
                Debug.LogError("[AiTutorChat] No GeminiClient found, so nothing can be asked. Add " +
                               "one to this object.", this);
                return;
            }

            inputField.text = "";
            AddMessage(true, question);

            waiting = true;
            SetStatus("Thinking...");

            client.Ask(BuildSystemPrompt(), Recent(), question,
                answer =>
                {
                    waiting = false;
                    readyAt = Time.unscaledTime + Mathf.Max(0f, cooldownSeconds);

                    history.Add(new GeminiClient.Turn(true, question));
                    history.Add(new GeminiClient.Turn(false, answer));
                    Trim();

                    AddMessage(false, answer);
                    SetStatus("");
                    if (inputField != null) inputField.ActivateInputField();
                },
                problem =>
                {
                    waiting = false;
                    readyAt = Time.unscaledTime + Mathf.Max(0f, cooldownSeconds);

                    AddMessage(false, problem);
                    SetStatus("");
                });
        }

        List<GeminiClient.Turn> Recent()
        {
            int keep = Mathf.Max(0, rememberMessages);
            if (history.Count <= keep) return history;
            return history.GetRange(history.Count - keep, keep);
        }

        void Trim()
        {
            int keep = Mathf.Max(0, rememberMessages);
            while (history.Count > keep) history.RemoveAt(0);
        }

        // ---------------------------------------------------------------- context

        string BuildSystemPrompt()
        {
            string text = systemPrompt ?? "";
            text = text.Replace("{module}", ModuleName());
            text = text.Replace("{context}", JobOrder());
            return text;
        }

        string ModuleName()
        {
            switch (cocNumber)
            {
                case 1: return "COC 1 - Install and Configure Computer Systems";
                case 2: return "COC 2 - Set-up Computer Networks";
                case 3: return "COC 3 - Set-up Computer Servers";
                case 4: return "COC 4 - Maintain and Repair Computer Systems and Networks";
                default: return "a Computer Systems Servicing NC II module";
            }
        }

        /// <summary>
        /// The scene's job order and where the player is in it, in plain sentences.
        ///
        /// Read from the waypoint arrow rather than kept as a second list here, because a second
        /// list would need updating every time the scene's steps changed and would silently go
        /// stale when someone forgot. The arrow is already the thing the player is following, so
        /// the tutor and the arrow cannot disagree about what comes next.
        /// </summary>
        string JobOrder()
        {
            CSSSimulation.Networking.TaskWaypointArrow arrow =
                CSSSimulation.Networking.TaskWaypointArrow.Instance;

            if (arrow == null || arrow.steps == null || arrow.steps.Count == 0)
                return "You do not have visibility of which step they are on, so answer as a " +
                       "module tutor and ask them what they are working on if it matters.";

            int active = arrow.ActiveIndex;

            string s;
            if (active >= 0 && active < arrow.steps.Count)
                s = "The step they are on right now is: \"" + arrow.steps[active].label + "\".";
            else
                s = "They have finished every step in this module.";

            s += "\nThe job order for this module, in order";
            if (active >= 0) s += " (> marks the current step, earlier ones are done)";
            s += ":";

            for (int i = 0; i < arrow.steps.Count; i++)
            {
                CSSSimulation.Networking.TaskWaypointArrow.Step step = arrow.steps[i];
                if (step == null) continue;
                s += "\n  " + (i == active ? "> " : "  ") + step.label;
            }

            return s;
        }

        // ---------------------------------------------------------------- messages

        void AddMessage(bool fromPlayer, string text)
        {
            if (messageTemplate == null || messageRoot == null)
            {
                Debug.LogWarning("[AiTutorChat] Message Template or Message Root is empty, so '" +
                                 text + "' could not be shown.", this);
                return;
            }

            GameObject row = Instantiate(messageTemplate, messageRoot);
            row.SetActive(true);
            row.name = fromPlayer ? "You" : "Tutor";

            TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = (fromPlayer ? "You:  " : "Tutor:  ") + text;
                label.alignment = fromPlayer ? TextAlignmentOptions.TopRight
                                             : TextAlignmentOptions.TopLeft;
            }

            ScrollToBottom();
        }

        void ScrollToBottom()
        {
            if (scrollRect == null) return;

            // Layout has not been rebuilt yet on the frame a row is added, so a scroll now would
            // be measured against the old content height and land in the wrong place.
            Canvas.ForceUpdateCanvases();
            if (messageRoot != null) LayoutRebuilder.ForceRebuildLayoutImmediate(messageRoot);
            scrollRect.verticalNormalizedPosition = 0f;
        }

        void SetStatus(string text)
        {
            if (statusLabel != null) statusLabel.text = text ?? "";
        }

        /// <summary>Empties the conversation. Wire to a procedure restart.</summary>
        public void ClearConversation()
        {
            history.Clear();
            greeted = false;

            if (messageRoot == null) return;
            for (int i = messageRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = messageRoot.GetChild(i);
                if (messageTemplate != null && child.gameObject == messageTemplate) continue;
                Destroy(child.gameObject);
            }
        }

        // ---------------------------------------------------------------- self-reporting

        void ValidateSetup()
        {
            AdoptKeyboardEatersIfEmpty();

            List<string> problems = new List<string>();

            if (client == null)
                problems.Add("No GeminiClient in the scene, so nothing can be asked. Add one to " +
                             "this object.");
            else if (string.IsNullOrEmpty((client.apiKey ?? "").Trim()) && client.apiKeyFile == null)
                problems.Add("GeminiClient has no API key, so every question will fail. Get one " +
                             "free at aistudio.google.com/apikey.");

            if (watcher == null)
                problems.Add("No InternetWatcher in the scene. The icon cannot hide itself when " +
                             "the connection is down, so it will be tappable and dead. Add one to " +
                             "this object.");

            if (openButton == null) problems.Add("Open Button is empty - nothing opens the chat.");
            if (panel == null) problems.Add("Panel is empty - there is no window to open.");
            else if (panel == gameObject)
                problems.Add("Panel points at THIS object. Opening the chat would switch off the " +
                             "component doing the opening. Point it at the window.");

            if (inputField == null) problems.Add("Input Field is empty - nothing can be typed.");
            if (messageRoot == null) problems.Add("Message Root is empty - replies have nowhere to go.");
            if (messageTemplate == null)
                problems.Add("Message Template is empty - there is no row to clone for each message.");
            else if (messageTemplate.activeSelf)
                problems.Add("Message Template '" + messageTemplate.name + "' is ACTIVE. It is the " +
                             "thing that gets copied, not a real message - leave it switched off " +
                             "or an empty row sits at the top of every conversation.");

            if (systemPrompt != null && !systemPrompt.Contains("{context}"))
                problems.Add("System Prompt has no {context} marker, so the tutor is never told " +
                             "which step the player is on and 'what do I do next' cannot be answered.");

            if (problems.Count == 0)
            {
                if (logSetup)
                    Debug.Log("[AiTutorChat] Ready for " + ModuleName() + ".\n    open with: " +
                              openKey + (openKeyOnPCOnly ? " (keyboard only)" : "") +
                              ", or the AI button" +
                              (iconOnTouchDevicesOnly
                                  ? " which is set to TOUCH DEVICES ONLY - on a keyboard it stays " +
                                    "hidden and " + openKey + " is the only way in, so nothing on " +
                                    "screen tells the player the tutor exists"
                                  : ", which is on screen everywhere") +
                              "\n    freezing " + freezeWhileOpen.Length +
                              " component(s) while open.", this);
                return;
            }

            string msg = "[AiTutorChat] Setup is incomplete:";
            foreach (string p in problems) msg += "\n  - " + p;
            Debug.LogWarning(msg, this);
        }

        /// <summary>
        /// Freezes the things that read the keyboard, when nothing has been listed by hand.
        ///
        /// Typing "where does the blue wire go" into the chat sends w, e, r and e through
        /// Input.GetKeyDown as well, because a TMP input field does not consume keys - it reads
        /// the same stream everything else does. E opens whatever station the player is standing
        /// at, and the station's Back button reads E too. So a sentence typed into the chat can
        /// open a PC desktop, leave it again, and open it once more, all behind the window.
        ///
        /// These two are findable by type, so they are handled without anyone having to know this.
        /// Movement and look scripts are project-specific and get dragged in by hand.
        /// </summary>
        void AdoptKeyboardEatersIfEmpty()
        {
            if (freezeWhileOpen != null && freezeWhileOpen.Length > 0) return;

            List<MonoBehaviour> found = new List<MonoBehaviour>();

            CSSSimulation.Interaction.PlayerInteractor[] interactors =
                Object.FindObjectsByType<CSSSimulation.Interaction.PlayerInteractor>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (CSSSimulation.Interaction.PlayerInteractor p in interactors) found.Add(p);

            CSSSimulation.Networking.StationExitButton[] backButtons =
                Object.FindObjectsByType<CSSSimulation.Networking.StationExitButton>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (CSSSimulation.Networking.StationExitButton b in backButtons) found.Add(b);

            // COC1 has its own interactor in the global namespace, and it reads the keyboard in
            // exactly the same way. Naming it explicitly rather than relying on the COC2 type is
            // what makes this one component correct in all four scenes.
            global::PlayerInteractor[] coc1 = Object.FindObjectsByType<global::PlayerInteractor>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (global::PlayerInteractor p in coc1) found.Add(p);

            freezeWhileOpen = found.ToArray();

            if (found.Count == 0 || !logSetup) return;

            string names = "";
            foreach (MonoBehaviour m in found) names += "\n    " + m.GetType().Name + " on '" + m.name + "'";

            Debug.Log("[AiTutorChat] Freeze While Open was empty, so it has been filled with the " +
                      found.Count + " component(s) that read the keyboard:" + names + "\n\n" +
                      "Without this, typing a letter e into the chat also opens whatever station " +
                      "the player is standing at. Drag your movement and mouse-look scripts in as " +
                      "well - those cannot be found by type.", this);
        }

        /// <summary>Prints every condition the button depends on, and which one is blocking it.</summary>
        [ContextMenu("Why is the AI button hidden?")]
        public void LogButtonState()
        {
            string blocking = WhyTheButtonIsHidden();

            Debug.LogWarning("[AiTutorChat] AI button: " +
                (blocking == null ? "SHOWING - nothing is blocking it." : "HIDDEN - " + blocking) +
                "\n    Open Button              : " + (openButton != null
                    ? "'" + openButton.name + "', active=" + openButton.gameObject.activeInHierarchy
                    : "NOT ASSIGNED") +
                "\n    internet reached         : " + InternetWatcher.IsOnline +
                (watcher != null ? "   (" + watcher.LastReason + ")"
                                 : "   NO InternetWatcher in this scene") +
                "\n    dialog open              : " + (DialogManager.Instance != null &&
                                                       DialogManager.Instance.IsDialogOpen) +
                "\n    chat already open        : " + IsOpen +
                "\n    Icon On Touch Only       : " + iconOnTouchDevicesOnly +
                "\n    is a touch session       : " + TouchSession +
                "\n    MobileControlsVisibility : " + (touchControls != null
                    ? "'" + touchControls.name + "', Wanted=" + touchControls.Wanted
                    : "none found - falling back to Application.isMobilePlatform (" +
                      Application.isMobilePlatform + "), which is FALSE in the Editor whatever the " +
                      "build target") +
                "\n    open key                 : " + openKey +
                (openKeyOnPCOnly ? " (ignored on touch)" : ""), this);
        }

        /// <summary>Prints what the AI would be told if a question were asked right now.</summary>
        [ContextMenu("Report what the tutor knows")]
        public void LogContext()
        {
            Debug.LogWarning("[AiTutorChat] The tutor would be sent:\n\n" + BuildSystemPrompt() +
                             "\n\n---- " + history.Count + " message(s) of conversation remembered.",
                             this);
        }
    }
}