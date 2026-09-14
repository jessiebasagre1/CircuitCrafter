// GeminiClient.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CSSSimulation.Tutor
{
    /// <summary>
    /// Sends a question to Google's Gemini API and hands back the answer. Transport only - it knows
    /// nothing about chat bubbles, COC modules or the player.
    ///
    /// WHY IT IS SEPARATE FROM THE CHAT UI
    ///
    /// Two things here change for completely different reasons. The panel changes when the layout
    /// is wrong; this changes when Google renames a model or alters a JSON field. Keeping them
    /// apart means a provider change never puts the UI at risk, and it means this can be swapped
    /// for a different provider - or for a proxy server later - by rewriting one file.
    ///
    /// ABOUT THE API KEY
    ///
    /// A key typed into the Inspector ships inside the build, and anyone with a file extractor can
    /// pull it out of an APK in about a minute. For a school project that is an acceptable trade:
    /// the worst case is somebody burning through a free-tier quota. It is NOT acceptable for
    /// anything with a card attached, so:
    ///
    ///   - use a key made only for this project, never a personal one
    ///   - leave it on the free tier, or set a hard spend cap
    ///   - if the project is ever published, move the key to a small proxy server and point
    ///     Endpoint at that instead
    ///
    /// Api Key File exists so the key does not have to live in the scene file: put it in a text
    /// asset you keep out of version control, and the scene can be committed without it.
    ///
    /// SETUP: add it beside AiTutorChat and paste a key from aistudio.google.com/apikey.
    /// </summary>
    public class GeminiClient : MonoBehaviour
    {
        /// <summary>One line of the conversation so far.</summary>
        public class Turn
        {
            public bool fromUser;
            public string text;
            public Turn(bool user, string body) { fromUser = user; text = body; }
        }

        [Header("Credentials")]
        [Tooltip("Your key from aistudio.google.com/apikey. Ships inside the build - read the " +
                 "note at the top of this script before using a key that can be billed.")]
        public string apiKey = "";

        [Tooltip("Optional. A text file containing nothing but the key. Used instead of the field " +
                 "above when it is filled in, so the key can be kept out of the scene file and out " +
                 "of version control.")]
        public TextAsset apiKeyFile;

        [Header("Model")]
        [Tooltip("A current free-tier flash model. Type the name BARE - 'gemini-3.6-flash', not " +
                 "'models/gemini-3.6-flash' - because Endpoint adds the prefix.\n\n" +
                 "Google retires model names, and a retired one answers 404 rather than falling " +
                 "back to something that works. When that happens the Console prints Google's own " +
                 "message, which usually names the replacement outright. The current list is at " +
                 "ai.google.dev/gemini-api/docs/models.")]
        public string model = "gemini-3.6-flash";

        [Tooltip("Leave this alone unless you move the key to a proxy server of your own, in which " +
                 "case point it at that. {model} is replaced with the Model field.")]
        public string endpoint =
            "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        [Header("Answer shape")]
        [Range(0f, 2f)]
        [Tooltip("How loose the wording is. Low for a tutor - a trainee being examined on a " +
                 "standard does not want a creative interpretation of it.")]
        public float temperature = 0.3f;

        [Tooltip("Roughly how long an answer may be. Around 4 characters per token, so 800 is " +
                 "about two short paragraphs.")]
        public int maxOutputTokens = 800;

        [Tooltip("Send the Thinking Budget below at all.\n\n" +
                 "Leave it ON. Whether a model accepts a thinking budget - and whether it accepts " +
                 "zero as one - differs between models and changes as Google releases new ones. " +
                 "If this model refuses it, the question is simply asked again without it and the " +
                 "Console says so; untick this then, to skip that one extra round trip.")]
        public bool sendThinkingBudget = true;

        [Tooltip("Tokens the model may spend thinking before it writes anything. ZERO on purpose.\n\n" +
                 "The 2.5 and later models think before answering, and that thinking comes out of " +
                 "the SAME budget as the reply. Leave it on and a short budget can be spent " +
                 "entirely on thinking, so the API returns success with an empty answer and a " +
                 "finish reason of MAX_TOKENS - which looks exactly like the chat being broken. " +
                 "Zero disables it, which is right for short tutoring answers.")]
        public int thinkingBudget = 0;

        [Tooltip("Seconds to wait before giving up on a reply.")]
        public int timeoutSeconds = 25;

        [Header("When the service is busy")]
        [Tooltip("Wait and ask again when Google answers 503 Service Unavailable, 429 Too Many " +
                 "Requests, or a 5xx.\n\n" +
                 "Leave it ON. These mean busy, not broken - nothing about the key, the request or " +
                 "the scene is wrong, and the same question a few seconds later usually just " +
                 "works. Passing that straight to the trainee teaches them the tutor is " +
                 "unreliable, which is a worse lesson than a short wait.")]
        public bool retryWhenBusy = true;

        [Tooltip("How many times to wait and ask again before giving up. Two is plenty for a " +
                 "demand spike; more just makes a real outage take longer to admit to.")]
        [Range(0, 5)]
        public int maxRetries = 2;

        [Tooltip("Seconds before the first retry. It DOUBLES each time - 1.5, then 3, then 6.\n\n" +
                 "Backing off matters: hammering an overloaded service at a fixed interval is what " +
                 "keeps it overloaded. A rate limit (429) waits longer than this regardless, " +
                 "because the free tier counts per minute and coming back in a second is pointless.")]
        public float retryDelaySeconds = 1.5f;

        [Header("Diagnostics")]
        [Tooltip("Print what was sent and what came back. Leave ON while setting this up - it is " +
                 "the only way to tell a bad key from a bad model name from a blocked answer.")]
        public bool logTraffic = true;

        /// <summary>True while a request is in flight.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>
        /// What is happening right now, for the chat's status line.
        ///
        /// A plain "Thinking..." is a lie once a retry is under way: the wait is suddenly several
        /// seconds longer than the player expects, and the honest version - that the service is
        /// busy and this is attempt two - is the difference between waiting patiently and tapping
        /// Send again.
        /// </summary>
        public string Progress { get; private set; }

        /// <summary>
        /// Sends one question, with the conversation so far for context.
        ///
        /// onReply gets the answer. onError gets a sentence fit to show the player - not a stack
        /// trace, because the person reading it is a trainee, not a programmer. The Console gets
        /// the full detail either way.
        /// </summary>
        public void Ask(string systemPrompt, List<Turn> history, string question,
                        Action<string> onReply, Action<string> onError)
        {
            if (IsBusy)
            {
                if (onError != null) onError("Still waiting on the last answer.");
                return;
            }

            string key = Key();
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[GeminiClient] No API key. Paste one into Api Key, or drop a text " +
                               "file containing the key into Api Key File. Get one free at " +
                               "https://aistudio.google.com/apikey", this);
                if (onError != null)
                    onError("The assistant is not set up yet - no API key has been entered.");
                return;
            }

            StartCoroutine(Send(key, systemPrompt, history, question, onReply, onError));
        }

        string Key()
        {
            if (apiKeyFile != null && !string.IsNullOrEmpty(apiKeyFile.text))
                return apiKeyFile.text.Trim();
            return (apiKey ?? "").Trim();
        }

        IEnumerator Send(string key, string systemPrompt, List<Turn> history, string question,
                         Action<string> onReply, Action<string> onError)
        {
            IsBusy = true;

            long code = 0;
            string raw = "";
            UnityWebRequest.Result result = UnityWebRequest.Result.InProgress;
            string transportError = "";

            int sent = 0;
            int waitedOut = 0;
            bool droppedThinking = false;

            Progress = "Thinking...";

            while (true)
            {
                Request body = new Request();

                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    body.systemInstruction = new Instruction();
                    body.systemInstruction.parts.Add(new Part(systemPrompt));
                }

                if (history != null)
                {
                    foreach (Turn t in history)
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        // Gemini calls the assistant side "model", not "assistant". Getting this
                        // wrong does not error - the API just treats the whole conversation as one
                        // voice and the answers stop making sense.
                        body.contents.Add(Line(t.fromUser ? "user" : "model", t.text));
                    }
                }

                body.contents.Add(Line("user", question));

                body.generationConfig = new Config();
                body.generationConfig.temperature = temperature;
                body.generationConfig.maxOutputTokens = Mathf.Max(64, maxOutputTokens);

                if (sendThinkingBudget)
                {
                    body.generationConfig.thinkingConfig = new Thinking();
                    body.generationConfig.thinkingConfig.thinkingBudget = Mathf.Max(0, thinkingBudget);
                }

                string json = JsonUtility.ToJson(body);
                string url = endpoint.Replace("{model}", model.Trim()) +
                             "?key=" + UnityWebRequest.EscapeURL(key);

                if (logTraffic && sent == 0)
                    Debug.Log("[GeminiClient] asking " + model + " (" + body.contents.Count +
                              " message(s) of context): " + Shorten(question), this);

                sent++;

                UnityWebRequest req = new UnityWebRequest(url, "POST");
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.Max(5, timeoutSeconds);

                yield return req.SendWebRequest();

                code = req.responseCode;
                raw = req.downloadHandler != null ? req.downloadHandler.text : "";
                result = req.result;
                transportError = req.error;
                req.Dispose();

                if (result == UnityWebRequest.Result.Success) break;

                // The one failure worth trying again for.
                //
                // Whether a model accepts a thinking budget - and whether it accepts ZERO as one -
                // varies from model to model, and changes when Google releases new ones. A budget
                // of 0 is right for short tutoring answers on the models that allow it, and a hard
                // 400 on the ones that do not. Rather than make that somebody's Inspector homework,
                // the setting is dropped and the question asked again, once. The Console says it
                // happened so the field can be unticked and the extra round trip saved.
                if (!droppedThinking && sendThinkingBudget && MentionsThinking(raw))
                {
                    droppedThinking = true;
                    sendThinkingBudget = false;
                    Debug.LogWarning("[GeminiClient] '" + model + "' refused the Thinking Budget " +
                                     "setting, so the question was asked again without it. Untick " +
                                     "Send Thinking Budget on this component to skip that retry " +
                                     "from now on.\n\nGoogle said: " + ErrorMessage(raw), this);
                    continue;
                }

                // Busy, not broken.
                //
                // A 503 means Google's own capacity is stretched for this model - nothing about
                // the key, the request or the scene is wrong, and the same question sent ten
                // seconds later usually just works. Handing that straight to the trainee as "the
                // service is having trouble" teaches them the tutor is unreliable, when all that
                // was needed was to wait a moment and ask again.
                //
                // So it waits and asks again, and only gives up after Max Retries. The delay
                // doubles each time: hammering an overloaded service at a fixed interval is what
                // keeps it overloaded, and every client doing the same thing at the same moment is
                // how a spike becomes an outage. The small random extra stops this build's copies
                // from all retrying on the same beat.
                if (retryWhenBusy && waitedOut < maxRetries && IsWorthRetrying(code))
                {
                    waitedOut++;

                    // A 429 is a rate limit rather than a capacity blip, and the free tier counts
                    // per minute - so coming back in a second and a half is pointless.
                    float baseDelay = code == 429 ? Mathf.Max(retryDelaySeconds, 8f)
                                                  : retryDelaySeconds;
                    float wait = baseDelay * Mathf.Pow(2f, waitedOut - 1) +
                                 UnityEngine.Random.Range(0f, 0.5f);

                    Progress = "The service is busy - trying again (" + waitedOut + " of " +
                               maxRetries + ")...";

                    Debug.LogWarning("[GeminiClient] HTTP " + code + " - " + ErrorMessage(raw) +
                                     "\nWaiting " + wait.ToString("0.0") + "s and asking again (" +
                                     waitedOut + " of " + maxRetries + ").", this);

                    // Unscaled, because a scene that freezes time while a panel is open would
                    // otherwise leave this waiting forever.
                    yield return new WaitForSecondsRealtime(wait);
                    continue;
                }

                break;
            }

            IsBusy = false;
            Progress = "";

            if (result != UnityWebRequest.Result.Success)
            {
                // The key is never printed, but everything else is - Google's own message is what
                // separates the four things that go wrong here.
                Debug.LogError("[GeminiClient] Request failed (HTTP " + code + "): " +
                               transportError + "\n" + raw + "\n\n" + Advice(code, raw), this);

                if (onError != null) onError(PlayerFacing(code, raw));
                yield break;
            }

            string why;
            string answer = ReadAnswer(raw, out why);

            if (string.IsNullOrEmpty(answer))
            {
                Debug.LogWarning("[GeminiClient] The API returned success but no text" +
                                 (string.IsNullOrEmpty(why) ? "" : " (finish reason: " + why + ")") +
                                 ". Full response:\n" + raw + "\n\n" +
                                 (why == "MAX_TOKENS"
                                     ? "MAX_TOKENS with no text means the whole budget went on " +
                                       "thinking before a word was written. Set Thinking Budget to " +
                                       "0, or raise Max Output Tokens."
                                     : why == "SAFETY"
                                         ? "A safety filter refused the question."
                                         : "Most often this is the answer being cut off before it " +
                                           "started - see the Thinking Budget tooltip - or a " +
                                           "safety filter refusing the question."), this);

                if (onError != null)
                    onError("I could not put an answer together for that one. Try asking it " +
                            "another way.");
                yield break;
            }

            if (logTraffic) Debug.Log("[GeminiClient] answered: " + Shorten(answer), this);
            if (onReply != null) onReply(answer.Trim());
        }

        // ---------------------------------------------------------------- reading the reply

        /// <summary>
        /// Digs the text out of the response, and returns empty rather than throwing when it is
        /// not there.
        ///
        /// A missing answer is a normal outcome, not an exception: the model can stop early, a
        /// safety filter can refuse, or thinking can eat the whole budget. All three come back as
        /// HTTP 200 with candidates present and no text in them, so the caller has to be able to
        /// ask "was there an answer" without a try/catch around it.
        /// </summary>
        static string ReadAnswer(string raw, out string finishReason)
        {
            finishReason = "";
            if (string.IsNullOrEmpty(raw)) return "";

            Response parsed;
            try { parsed = JsonUtility.FromJson<Response>(raw); }
            catch { return ""; }

            if (parsed == null || parsed.candidates == null || parsed.candidates.Count == 0)
                return "";

            Candidate first = parsed.candidates[0];
            if (first == null) return "";

            finishReason = first.finishReason ?? "";
            if (first.content == null || first.content.parts == null) return "";

            string text = "";
            foreach (Part p in first.content.parts)
                if (p != null && !string.IsNullOrEmpty(p.text)) text += p.text;

            return text;
        }

        static string ErrorMessage(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            try
            {
                Envelope e = JsonUtility.FromJson<Envelope>(raw);
                if (e != null && e.error != null && !string.IsNullOrEmpty(e.error.message))
                    return e.error.message;
            }
            catch { }
            return "";
        }

        /// <summary>
        /// What actually went wrong, worked out from the body rather than from the status code.
        ///
        /// WHY THE CODE ALONE IS NOT ENOUGH
        ///
        /// This API answers 400 - plain INVALID_ARGUMENT - for two completely unrelated faults: a
        /// key it cannot read, and a model name it does not recognise. They need opposite fixes, and
        /// a message that guesses at one sends you looking in the wrong Inspector field. The body
        /// says which it is in plain English, so that is what gets read.
        /// </summary>
        enum Fault
        {
            Unknown, BadKey, KeyNotAuthorised, NoSuchModel, OutOfQuota, BadRequest,
            NoAnswer, ServerSide
        }

        /// <summary>
        /// Whether this failure is the kind that goes away on its own.
        ///
        /// Only capacity and rate limits. A bad key, a retired model or a malformed request will
        /// fail identically forever, and retrying those just makes the player wait longer for the
        /// same error - while spending quota on it.
        /// </summary>
        static bool IsWorthRetrying(long code)
        {
            return code == 429 || code == 500 || code == 502 || code == 503 || code == 504;
        }

        /// <summary>Whether the rejection was about the thinking setting, and so worth one retry.</summary>
        static bool MentionsThinking(string raw)
        {
            string said = ErrorMessage(raw).ToLowerInvariant();
            return said.Contains("thinking") || said.Contains("thought");
        }

        static Fault Diagnose(long code, string raw)
        {
            string said = ErrorMessage(raw).ToLowerInvariant();

            // Read before the status code, because a bad key arrives as a 400 just as often as a
            // bad model does, and only the text separates them.
            if (said.Contains("api key") || said.Contains("api_key")) return Fault.BadKey;
            if (said.Contains("permission") || said.Contains("has not been used") ||
                said.Contains("is disabled")) return Fault.KeyNotAuthorised;
            if (said.Contains("not found") || said.Contains("is not supported") ||
                said.Contains("unsupported") || said.Contains("models/")) return Fault.NoSuchModel;

            switch (code)
            {
                case 400: return Fault.BadRequest;
                case 401:
                case 403: return Fault.KeyNotAuthorised;
                case 404: return Fault.NoSuchModel;
                case 429: return Fault.OutOfQuota;
                case 0: return Fault.NoAnswer;
                default: return code >= 500 ? Fault.ServerSide : Fault.Unknown;
            }
        }

        /// <summary>What to do about it, in the Console, where a developer is reading.</summary>
        string Advice(long code, string raw)
        {
            string fromGoogle = ErrorMessage(raw);
            string detail = string.IsNullOrEmpty(fromGoogle) ? "" : "Google says: " + fromGoogle + "\n\n";

            switch (Diagnose(code, raw))
            {
                case Fault.BadKey:
                    return detail + "THE KEY, not the model. Google answers 400 for this, which is " +
                           "why it looks like a request problem.\n" +
                           "  - the key is " + Key().Length + " characters long; a real one is " +
                           "about 39 and starts with AIza\n" +
                           "  - check nothing was cut off, and that no space or line break came " +
                           "along with the paste\n" +
                           "  - if you filled in Api Key File, that wins over the Api Key field - " +
                           "make sure the file holds ONLY the key\n" +
                           "  - get a fresh one at https://aistudio.google.com/apikey";

                case Fault.KeyNotAuthorised:
                    return detail + "The key is readable but not allowed to do this. Usually the " +
                           "Generative Language API is not enabled on the Google Cloud project the " +
                           "key belongs to, or the key has an application or API restriction on it. " +
                           "A key made directly in AI Studio has neither, so making a fresh one " +
                           "there is the quickest way past this.";

                case Fault.NoSuchModel:
                    return detail + "No such model: '" + model + "'. Google retires names and " +
                           "not every model is on every API version.\n" +
                           "  - open https://ai.google.dev/gemini-api/docs/models and copy a " +
                           "current one\n" +
                           "  - the name goes in bare, with no 'models/' in front - Endpoint adds " +
                           "that\n" +
                           "  - gemini-2.5-flash and the newer flash models are all free tier";

                case Fault.OutOfQuota:
                    return detail + "Out of quota for now. The free tier caps requests per minute " +
                           "and per day - wait a minute, or check usage in AI Studio.";

                case Fault.NoAnswer:
                    return "Nothing came back at all. Either the device lost its connection " +
                           "mid-request, or the reply took longer than Timeout Seconds (" +
                           timeoutSeconds + "s).";

                case Fault.ServerSide:
                    return detail + "Google's end is busy or having a problem - nothing here is " +
                           "misconfigured." +
                           (retryWhenBusy
                               ? " This was already retried " + maxRetries + " time(s) with a " +
                                 "growing wait and was still busy, so it is more than a momentary " +
                                 "spike. Try again in a few minutes, or raise Max Retries."
                               : " Retry When Busy is unticked, so it was passed straight on - " +
                                 "tick it and a spike like this rides out on its own.");

                case Fault.BadRequest:
                    return detail + "The request itself was rejected and it was not the key or the " +
                           "model. Check Thinking Budget and Max Output Tokens are sensible - some " +
                           "models refuse a thinking budget of 0.";

                default:
                    return detail;
            }
        }

        /// <summary>What to say in the chat window, where a trainee is reading.</summary>
        static string PlayerFacing(long code, string raw)
        {
            switch (Diagnose(code, raw))
            {
                case Fault.BadKey:
                case Fault.KeyNotAuthorised:
                    return "The assistant is not set up correctly - its access key was refused.";
                case Fault.NoSuchModel:
                case Fault.BadRequest:
                    return "The assistant is not set up correctly - ask whoever built this scene.";
                case Fault.OutOfQuota:
                    return "Too many questions too quickly. Give it a minute and ask again.";
                case Fault.ServerSide:
                    return "The service is busy right now. I waited and tried again, but it is " +
                           "still busy - give it a few minutes.";
                case Fault.NoAnswer:
                    return "I could not reach the internet just then. Check the connection and try " +
                           "again.";
                default:
                    return "Something went wrong asking that. Try again.";
            }
        }

        /// <summary>
        /// Asks one throwaway question and reports the verdict. Right-click the component in Play
        /// mode.
        ///
        /// Faster than replaying the scene and walking to a station: this is the same code path a
        /// real question takes, so whatever it says about the key and the model is the truth for
        /// the chat too.
        /// </summary>
        [ContextMenu("Test the connection")]
        public void TestConnection()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[GeminiClient] Press Play first - the request is a coroutine and " +
                                 "coroutines do not run in edit mode.", this);
                return;
            }

            string key = Key();
            Debug.Log("[GeminiClient] Testing. Model '" + model + "', key " + key.Length +
                      " characters" +
                      (key.Length >= 4 ? " starting '" + key.Substring(0, 4) + "'" : "") +
                      (apiKeyFile != null ? ", taken from Api Key File '" + apiKeyFile.name + "'"
                                          : ", taken from the Api Key field") + ".", this);

            Ask("Reply with exactly the word: OK", null, "Say OK.",
                answer => Debug.Log("[GeminiClient] WORKING. It replied: " + answer, this),
                problem => Debug.LogWarning("[GeminiClient] Test failed. The player would see: " +
                                            problem + "\n\nThe error just above this line says why.",
                                            this));
        }

        /// <summary>
        /// Asks Google which models THIS key is allowed to use, and prints the list.
        ///
        /// WHY ASK RATHER THAN READ THE DOCUMENTATION
        ///
        /// The published model list and the list a given key may use are not the same list. Names
        /// get retired, new ones appear, and some are closed to keys made after a certain date -
        /// which is exactly what happened here: gemini-2.5-flash is still printed as free tier on
        /// the pricing page and still answers 404 for a key made this week.
        ///
        /// So the only answer that is reliably true is the one your own key gives. This is that
        /// question, and the first name it prints can be pasted straight into the Model field.
        /// </summary>
        [ContextMenu("List models my key can use")]
        public void ListModels()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[GeminiClient] Press Play first - this is a web request, and web " +
                                 "requests do not run in edit mode.", this);
                return;
            }

            string key = Key();
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[GeminiClient] No API key to ask with. Paste one into Api Key " +
                               "first - https://aistudio.google.com/apikey", this);
                return;
            }

            StartCoroutine(FetchModels(key));
        }

        IEnumerator FetchModels(string key)
        {
            // Same host and version as the endpoint, so the answer is about the API actually in
            // use rather than a different one that might list different names.
            string listUrl = endpoint;
            int models = listUrl.IndexOf("/models/", StringComparison.Ordinal);
            if (models >= 0) listUrl = listUrl.Substring(0, models);
            listUrl += "/models?pageSize=1000&key=" + UnityWebRequest.EscapeURL(key);

            UnityWebRequest req = UnityWebRequest.Get(listUrl);
            req.timeout = Mathf.Max(5, timeoutSeconds);
            yield return req.SendWebRequest();

            bool ok = req.result == UnityWebRequest.Result.Success;
            string raw = req.downloadHandler != null ? req.downloadHandler.text : "";
            long code = req.responseCode;
            req.Dispose();

            if (!ok)
            {
                Debug.LogError("[GeminiClient] Could not list models (HTTP " + code + "):\n" + raw +
                               "\n\n" + Advice(code, raw), this);
                yield break;
            }

            ModelList list;
            try { list = JsonUtility.FromJson<ModelList>(raw); }
            catch { list = null; }

            if (list == null || list.models == null || list.models.Count == 0)
            {
                Debug.LogWarning("[GeminiClient] The list came back empty:\n" + raw, this);
                yield break;
            }

            string usable = "";
            string others = "";
            int count = 0;

            foreach (ModelInfo m in list.models)
            {
                if (m == null || string.IsNullOrEmpty(m.name)) continue;

                // The name comes back as "models/gemini-3.6-flash". The Model field wants it bare,
                // because Endpoint puts the prefix back on - so it is stripped here and the line
                // can be copied straight across.
                string bare = m.name.StartsWith("models/") ? m.name.Substring(7) : m.name;

                bool chats = false;
                if (m.supportedGenerationMethods != null)
                    foreach (string method in m.supportedGenerationMethods)
                        if (method == "generateContent") { chats = true; break; }

                if (!chats) { others += "\n    " + bare; continue; }

                count++;
                usable += "\n    " + bare +
                          (string.IsNullOrEmpty(m.displayName) ? "" : "      (" + m.displayName + ")");
            }

            Debug.LogWarning("[GeminiClient] Your key can chat with " + count + " model(s). Paste " +
                             "any of these into the Model field exactly as written:" + usable +
                             "\n\n  Currently set to: '" + model + "'" +
                             "\n\n  A 'flash' model is the right pick here - fastest, cheapest, and " +
                             "on the free tier.\n  Avoid anything with 'tts', 'embedding', " +
                             "'transcribe' or 'image' in the name; those do other jobs." +
                             (string.IsNullOrEmpty(others) ? ""
                                 : "\n\n  Listed but cannot hold a conversation:" + others), this);
        }

#pragma warning disable 0649

        [Serializable]
        class ModelInfo
        {
            public string name;
            public string displayName;
            public List<string> supportedGenerationMethods;
        }

        [Serializable]
        class ModelList
        {
            public List<ModelInfo> models;
        }

#pragma warning restore 0649

        static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ");
            return s.Length <= 120 ? s : s.Substring(0, 120) + "...";
        }

        static Content Line(string role, string text)
        {
            Content c = new Content();
            c.role = role;
            c.parts.Add(new Part(text));
            return c;
        }

        // ---------------------------------------------------------------- the JSON shapes
        //
        // JsonUtility only sees PUBLIC fields on [Serializable] types, and it writes every field
        // it sees. That is why these are separate small classes rather than one big one: a null
        // systemInstruction is simply left out, and an empty object in its place would be rejected.

        [Serializable]
        class Part
        {
            public string text;
            public Part() { }
            public Part(string t) { text = t; }
        }

        [Serializable]
        class Content
        {
            public string role;
            public List<Part> parts = new List<Part>();
        }

        [Serializable]
        class Instruction
        {
            public List<Part> parts = new List<Part>();
        }

        [Serializable]
        class Thinking
        {
            public int thinkingBudget;
        }

        [Serializable]
        class Config
        {
            public float temperature;
            public int maxOutputTokens;
            public Thinking thinkingConfig;
        }

        [Serializable]
        class Request
        {
            public Instruction systemInstruction;
            public List<Content> contents = new List<Content>();
            public Config generationConfig;
        }

        // Filled in by JsonUtility, never by our code, so the compiler is right that nothing
        // assigns them - and wrong that it matters.
#pragma warning disable 0649

        [Serializable]
        class Candidate
        {
            public Content content;
            public string finishReason;
        }

        [Serializable]
        class Response
        {
            public List<Candidate> candidates;
        }

        [Serializable]
        class ApiError
        {
            public int code;
            public string message;
            public string status;
        }

        [Serializable]
        class Envelope
        {
            public ApiError error;
        }

#pragma warning restore 0649
    }
}