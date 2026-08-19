#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.Events;
using TMPro;

/// <summary>
/// EDITOR-ONLY TOOL. Auto-builds the entire COC3 hierarchy (managers, Canvas, TopBar,
/// FeedbackPanel, 30 step panels, CompletionScreen) plus 30 pre-filled ScreenshotStepData
/// assets, and wires every Inspector reference — following the "COC3 Complete Build Guide".
///
/// HOW TO USE:
/// 1. Put this script in a folder literally named "Editor" anywhere under Assets
///    (e.g. Assets/Editor/COC3SceneBuilder.cs). Unity excludes "Editor" folders from
///    builds automatically, so this tool never ships in your game.
/// 2. Make sure your runtime scripts (COC3Manager, ScreenshotStep, ScreenshotStepData,
///    etc.) are already in the project and compiling without errors.
/// 3. Open (or create) the scene you want the activity built into.
/// 4. Unity menu bar -> COC3 -> Build Full Scene.
/// 5. Read the summary dialog at the end - it lists exactly what still needs manual
///    work (screenshots, and hitbox buttons for ButtonInteraction steps).
///
/// WHAT THIS DOES NOT DO (must be done by hand afterward - this is normal, not a bug):
/// - Assign actual screenshot Sprites to each ScreenshotImage (you'll drag these in).
/// - Place transparent hitbox buttons over ButtonInteraction screenshots (Step 8 in the
///   guide) - the script deliberately leaves these panels without a generic Submit
///   button, because a generic button would let learners skip the actual challenge.
/// - Fine-tune colors/fonts/layout - this builds a fully FUNCTIONAL but plain-looking
///   UI. Reskin however you like afterward; none of the script references will break
///   as long as you don't rename/delete the GameObjects this tool created.
///
/// NOTE ON EVENT SYSTEM: this uses the legacy UI input module (StandaloneInputModule).
/// If your project uses ONLY the new Input System package, replace that line with
/// AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() instead.
/// </summary>
public static class COC3SceneBuilder
{
    private const string StepDataFolder = "Assets/COC3/StepData";

    // ---------------------------------------------------------------
    // Step content, in the exact order from the "Checklist -> Panels" table.
    // Edit expectedValue/instructionText here before running if you want the
    // generated assets to already match your specific screenshots/scenario.
    // ---------------------------------------------------------------
    private class StepDef
    {
        public string id;
        public string instruction;
        public StepType type;
        public string textLabel;      // only used when type == TextInput
        public string expectedValue;  // only used when type == TextInput
    }

    private static readonly List<StepDef> Steps = new List<StepDef>
    {
        new StepDef { id = "Step_01_ComputerName", type = StepType.TextInput,
            instruction = "Set the server's computer name as shown in the screenshot.",
            textLabel = "Computer Name", expectedValue = "SVR-FILE01" },

        new StepDef { id = "Step_02_StaticIPConfig", type = StepType.NetworkConfigInput,
            instruction = "Configure the static IP address, subnet mask, and default gateway shown." },

        new StepDef { id = "Step_03_DNSConfig", type = StepType.TextInput,
            instruction = "Set the preferred DNS server address.",
            textLabel = "DNS Server", expectedValue = "192.168.1.1" },

        new StepDef { id = "Step_04_InstallRoles", type = StepType.ButtonInteraction,
            instruction = "Select the correct server roles (DHCP, DNS, File and Storage Services)." },

        new StepDef { id = "Step_05_DHCPConfig", type = StepType.ButtonInteraction,
            instruction = "Activate the DHCP scope you just configured." },

        new StepDef { id = "Step_06_DNSServerConfig", type = StepType.ButtonInteraction,
            instruction = "Create the forward lookup zone/record shown." },

        new StepDef { id = "Step_07_FileServicesConfig", type = StepType.ButtonInteraction,
            instruction = "Confirm File Services is installed and running." },

        new StepDef { id = "Step_08_OtherServices", type = StepType.Informational,
            instruction = "Review the additional required network service shown." },

        new StepDef { id = "Step_09_CreateUserFolders", type = StepType.TextInput,
            instruction = "Create a folder named after the user account.",
            textLabel = "Folder Name", expectedValue = "jdelacruz" },

        new StepDef { id = "Step_10_CreateUserAccounts", type = StepType.TextInput,
            instruction = "Create a user account with the username shown.",
            textLabel = "Username", expectedValue = "jdelacruz" },

        new StepDef { id = "Step_11_AccessLevels", type = StepType.ButtonInteraction,
            instruction = "Assign this user to the correct access-level group." },

        new StepDef { id = "Step_12_AccessPolicies", type = StepType.ButtonInteraction,
            instruction = "Apply the appropriate access policy." },

        new StepDef { id = "Step_13_CreateSharedFolder", type = StepType.TextInput,
            instruction = "Share the folder with the name shown.",
            textLabel = "Share Name", expectedValue = "PublicDocs" },

        new StepDef { id = "Step_14_FolderPermissions", type = StepType.ButtonInteraction,
            instruction = "Set the correct permission level for this group." },

        new StepDef { id = "Step_15_TestClientAccess", type = StepType.Informational,
            instruction = "Review the client accessing the shared folder." },

        new StepDef { id = "Step_16_InstallSharedPrinter", type = StepType.ButtonInteraction,
            instruction = "Install and share the printer shown." },

        new StepDef { id = "Step_17_PrinterPermissions", type = StepType.ButtonInteraction,
            instruction = "Set the correct print permission for this group." },

        new StepDef { id = "Step_18_TestPrintFromClient", type = StepType.Informational,
            instruction = "Review the test page printing successfully from the client." },

        new StepDef { id = "Step_19_CreateRedirectionFolder", type = StepType.TextInput,
            instruction = "Create the server folder for redirected user data.",
            textLabel = "Folder Path/Name", expectedValue = "RedirectedData" },

        new StepDef { id = "Step_20_RedirectionPolicy", type = StepType.ButtonInteraction,
            instruction = "Configure the folder redirection policy correctly." },

        new StepDef { id = "Step_21_RedirectFoldersTarget", type = StepType.ButtonInteraction,
            instruction = "Set the redirection target for Documents and Desktop." },

        new StepDef { id = "Step_22_TestRedirectionClient", type = StepType.Informational,
            instruction = "Review folder redirection working from the client." },

        new StepDef { id = "Step_23_EnableRDP", type = StepType.ButtonInteraction,
            instruction = "Enable Remote Desktop on this server." },

        new StepDef { id = "Step_24_AllowedRDPUsers", type = StepType.TextInput,
            instruction = "Add the specified user to the allowed Remote Desktop users list.",
            textLabel = "Username", expectedValue = "jdelacruz" },

        new StepDef { id = "Step_25_ConnectFromClient", type = StepType.Informational,
            instruction = "This is what connecting from a client computer looks like." },

        new StepDef { id = "Step_26_VerifyRemoteAccess", type = StepType.Informational,
            instruction = "Review the remote session connecting successfully." },

        new StepDef { id = "Step_27_FirewallCheck", type = StepType.ButtonInteraction,
            instruction = "Identify the correct firewall rule status." },

        new StepDef { id = "Step_28_UserPermissionsCheck", type = StepType.ButtonInteraction,
            instruction = "Confirm this user's permission level is correct." },

        new StepDef { id = "Step_29_SharedFolderSecurityCheck", type = StepType.ButtonInteraction,
            instruction = "Identify any incorrect/overly-permissive entry." },

        new StepDef { id = "Step_30_UnauthorizedAccessCheck", type = StepType.ButtonInteraction,
            instruction = "Confirm that an unauthorized user is denied access." },
    };

    // ---------------------------------------------------------------
    // MAIN BUILD
    // ---------------------------------------------------------------
    [MenuItem("COC3/Build Full Scene")]
    public static void BuildScene()
    {
        bool proceed = EditorUtility.DisplayDialog(
            "COC3 Scene Builder",
            "This will build a new COC3_Root hierarchy in the currently open scene, " +
            "and create 30 ScreenshotStepData assets under " + StepDataFolder + ".\n\n" +
            "Run this in an empty/dedicated scene to avoid clutter. Continue?",
            "Build", "Cancel");

        if (!proceed) return;

        EnsureFolder(StepDataFolder);

        // ---------- Root ----------
        GameObject root = new GameObject("COC3_Root");

        // ---------- Managers ----------
        COC3Manager manager = CreateEmpty(root, "COC3Manager").AddComponent<COC3Manager>();
        COC3ProgressManager progressManager = CreateEmpty(root, "COC3ProgressManager").AddComponent<COC3ProgressManager>();
        COC3ScoreManager scoreManager = CreateEmpty(root, "COC3ScoreManager").AddComponent<COC3ScoreManager>();
        FeedbackManager feedbackManager = CreateEmpty(root, "FeedbackManager").AddComponent<FeedbackManager>();
        PingSimulation pingSimulation = CreateEmpty(root, "PingSimulation").AddComponent<PingSimulation>();

        // ---------- Canvas ----------
        GameObject canvasGO = CreateEmpty(root, "Canvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        // ---------- TopBar ----------
        GameObject topBar = CreatePanel(canvasGO, "TopBar", new Color(0f, 0f, 0f, 150f / 255f));
        RectTransform topBarRT = topBar.GetComponent<RectTransform>();
        SetAnchorTopStretch(topBarRT, 80f);

        Slider progressBar = CreateSlider(topBar, "ProgressBar");
        AnchorLeft(progressBar.GetComponent<RectTransform>(), new Vector2(20, 0), new Vector2(320, 24));

        TMP_Text stepIndicatorText = CreateTMPText(topBar, "StepIndicatorText", "Step 1 of 1");
        AnchorLeft(stepIndicatorText.GetComponent<RectTransform>(), new Vector2(360, 0), new Vector2(220, 40));

        TMP_Text scoreText = CreateTMPText(topBar, "ScoreText", "Score: 0");
        AnchorRight(scoreText.GetComponent<RectTransform>(), new Vector2(-260, 0), new Vector2(220, 40));

        TMP_Text mistakeText = CreateTMPText(topBar, "MistakeText", "Mistakes: 0");
        AnchorRight(mistakeText.GetComponent<RectTransform>(), new Vector2(-20, 0), new Vector2(220, 40));

        // ---------- Feedback Popup ----------
        GameObject feedbackPanel = CreatePanel(canvasGO, "FeedbackPanel", new Color(0f, 0f, 0f, 0.85f));
        RectTransform feedbackRT = feedbackPanel.GetComponent<RectTransform>();
        feedbackRT.anchorMin = new Vector2(0.5f, 0.85f);
        feedbackRT.anchorMax = new Vector2(0.5f, 0.85f);
        feedbackRT.pivot = new Vector2(0.5f, 0.5f);
        feedbackRT.sizeDelta = new Vector2(600, 80);
        feedbackRT.anchoredPosition = Vector2.zero;

        TMP_Text feedbackText = CreateTMPText(feedbackPanel, "FeedbackText", "");
        StretchFull(feedbackText.GetComponent<RectTransform>());
        feedbackText.alignment = TextAlignmentOptions.Center;
        feedbackText.fontSize = 28;
        feedbackPanel.SetActive(false);

        // ---------- Steps Container ----------
        GameObject stepsContainer = CreateEmpty(canvasGO, "StepsContainer");
        RectTransform stepsRT = stepsContainer.AddComponent<RectTransform>();
        StretchFull(stepsRT);

        // ---------- Build all step panels + data assets ----------
        List<ScreenshotStep> allSteps = new List<ScreenshotStep>();
        List<string> hitboxTodo = new List<string>();

        foreach (StepDef def in Steps)
        {
            GameObject panel = CreatePanel(stepsContainer, def.id, new Color(0f, 0f, 0f, 0f));
            StretchFull(panel.GetComponent<RectTransform>());

            ScreenshotStep step = panel.AddComponent<ScreenshotStep>(); // auto-adds CanvasGroup

            GameObject screenshotImgGO = CreateEmpty(panel, "ScreenshotImage");
            Image screenshotImg = screenshotImgGO.AddComponent<Image>();
            StretchFull(screenshotImgGO.GetComponent<RectTransform>());
            screenshotImg.preserveAspect = true;

            TMP_Text instructionLabel = CreateTMPText(panel, "InstructionLabel", def.instruction);
            RectTransform instrRT = instructionLabel.GetComponent<RectTransform>();
            instrRT.anchorMin = new Vector2(0f, 1f);
            instrRT.anchorMax = new Vector2(1f, 1f);
            instrRT.pivot = new Vector2(0.5f, 1f);
            instrRT.sizeDelta = new Vector2(-40, 80);
            instrRT.anchoredPosition = new Vector2(0, -90); // below TopBar
            instructionLabel.alignment = TextAlignmentOptions.Center;
            instructionLabel.fontSize = 22;

            step.screenshotDisplay = screenshotImg;
            step.instructionLabel = instructionLabel;

            // ---- Data asset ----
            ScreenshotStepData data = ScriptableObject.CreateInstance<ScreenshotStepData>();
            data.stepId = def.id;
            data.instructionText = def.instruction;
            data.stepType = def.type;

            if (def.type == StepType.NetworkConfigInput)
            {
                data.expectedIP = "192.168.1.10";
                data.expectedSubnetMask = "255.255.255.0";
                data.expectedGateway = "192.168.1.1";
            }
            else if (def.type == StepType.TextInput)
            {
                data.textFieldLabel = def.textLabel;
                data.textFieldExpectedValue = def.expectedValue;
            }

            AssetDatabase.CreateAsset(data, StepDataFolder + "/" + def.id + ".asset");
            step.stepData = data;

            // ---- Type-specific fields + confirm button ----
            if (def.type == StepType.NetworkConfigInput)
            {
                step.ipInputField = CreateInputField(panel, "IPInputField", "0.0.0.0");
                step.subnetInputField = CreateInputField(panel, "SubnetInputField", "255.255.255.0");
                step.gatewayInputField = CreateInputField(panel, "GatewayInputField", "0.0.0.1");
                LayoutThreeFieldsRow(step.ipInputField.GetComponent<RectTransform>(),
                                      step.subnetInputField.GetComponent<RectTransform>(),
                                      step.gatewayInputField.GetComponent<RectTransform>());

                Button submit = CreateButton(panel, "SubmitButton", "Submit");
                AnchorBottomCenter(submit.GetComponent<RectTransform>(), new Vector2(0, 40), new Vector2(180, 55));
                UnityEventTools.AddPersistentListener(submit.onClick, manager.SubmitCurrentStep);
            }
            else if (def.type == StepType.TextInput)
            {
                step.textInputField = CreateInputField(panel, "TextValueInputField", "");
                RectTransform tfRT = step.textInputField.GetComponent<RectTransform>();
                tfRT.anchorMin = new Vector2(0.5f, 0.5f);
                tfRT.anchorMax = new Vector2(0.5f, 0.5f);
                tfRT.pivot = new Vector2(0.5f, 0.5f);
                tfRT.sizeDelta = new Vector2(360, 50);
                tfRT.anchoredPosition = new Vector2(0, -40);

                Button submit = CreateButton(panel, "SubmitButton", "Submit");
                AnchorBottomCenter(submit.GetComponent<RectTransform>(), new Vector2(0, 40), new Vector2(180, 55));
                UnityEventTools.AddPersistentListener(submit.onClick, manager.SubmitCurrentStep);
            }
            else if (def.type == StepType.Informational)
            {
                Button next = CreateButton(panel, "NextButton", "Next");
                AnchorBottomCenter(next.GetComponent<RectTransform>(), new Vector2(0, 40), new Vector2(180, 55));
                UnityEventTools.AddPersistentListener(next.onClick, manager.SkipToNextStep);
            }
            else if (def.type == StepType.ButtonInteraction)
            {
                // Deliberately no generic Submit button here - see class-level comment.
                hitboxTodo.Add(def.id);
            }

            panel.SetActive(false);
            allSteps.Add(step);
        }

        manager.steps = allSteps;
        manager.progressManager = progressManager;
        manager.scoreManager = scoreManager;
        manager.feedbackManager = feedbackManager;
        manager.pingSimulation = pingSimulation;

        progressManager.progressBar = progressBar;
        progressManager.stepIndicatorText = stepIndicatorText;
        scoreManager.scoreText = scoreText;
        scoreManager.mistakeText = mistakeText;
        feedbackManager.feedbackPanel = feedbackPanel;
        feedbackManager.feedbackText = feedbackText;

        // ---------- Completion Screen ----------
        GameObject completionScreen = CreatePanel(canvasGO, "CompletionScreen", new Color(0f, 0f, 0f, 0.92f));
        StretchFull(completionScreen.GetComponent<RectTransform>());

        TMP_Text reportText = CreateTMPText(completionScreen, "ReportText", "");
        RectTransform reportRT = reportText.GetComponent<RectTransform>();
        reportRT.anchorMin = new Vector2(0.5f, 0.5f);
        reportRT.anchorMax = new Vector2(0.5f, 0.5f);
        reportRT.pivot = new Vector2(0.5f, 0.5f);
        reportRT.sizeDelta = new Vector2(900, 600);
        reportRT.anchoredPosition = new Vector2(0, 40);
        reportText.alignment = TextAlignmentOptions.TopLeft;
        reportText.fontSize = 22;

        Button restartButton = CreateButton(completionScreen, "RestartButton", "Try Again");
        AnchorBottomCenter(restartButton.GetComponent<RectTransform>(), new Vector2(0, 60), new Vector2(200, 55));
        UnityEventTools.AddPersistentListener(restartButton.onClick, manager.RestartActivity);

        completionScreen.SetActive(false);
        manager.completionScreen = completionScreen;
        manager.reportText = reportText;

        // ---------- Wrap up ----------
        Selection.activeGameObject = root;
        EditorSceneManager_MarkDirty();

        StringBuilder summary = new StringBuilder();
        summary.AppendLine("Scene built: " + allSteps.Count + " panels, all references wired.");
        summary.AppendLine();
        summary.AppendLine("Still needed by hand:");
        summary.AppendLine("1) Drag each step's screenshot Sprite onto its ScreenshotImage.");
        summary.AppendLine("2) Add transparent hitbox buttons (Step 8 in the guide) to these " +
                            hitboxTodo.Count + " ButtonInteraction panels:");
        foreach (string id in hitboxTodo)
            summary.AppendLine("   - " + id);
        summary.AppendLine();
        summary.AppendLine("StepData assets were created in: " + StepDataFolder);
        summary.AppendLine("(Edit expectedIP/expectedSubnetMask/expectedGateway and textFieldExpectedValue " +
                            "on those assets to match your real screenshots.)");

        Debug.Log(summary.ToString());
        EditorUtility.DisplayDialog("COC3 Scene Builder - Done", summary.ToString(), "OK");
    }

    // ---------------------------------------------------------------
    // Small helpers
    // ---------------------------------------------------------------
    private static GameObject CreateEmpty(GameObject parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static GameObject CreatePanel(GameObject parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    private static TMP_Text CreateTMPText(GameObject parent, string name, string content)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = content;
        txt.fontSize = 24;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(280, 40);
        return txt;
    }

    private static Button CreateButton(GameObject parent, string name, string label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.5f, 0.9f, 1f);
        Button btn = go.AddComponent<Button>();

        GameObject textGO = new GameObject("Text (TMP)", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        TextMeshProUGUI txt = textGO.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = Color.white;
        txt.fontSize = 22;
        StretchFull(textGO.GetComponent<RectTransform>());

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(180, 55);
        return btn;
    }

    private static TMP_InputField CreateInputField(GameObject parent, string name, string placeholderText)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        Image bg = go.AddComponent<Image>();
        bg.color = Color.white;
        TMP_InputField inputField = go.AddComponent<TMP_InputField>();

        GameObject textArea = new GameObject("Text Area", typeof(RectTransform));
        textArea.transform.SetParent(go.transform, false);
        RectTransform textAreaRT = textArea.GetComponent<RectTransform>();
        StretchFull(textAreaRT);
        textAreaRT.offsetMin = new Vector2(10, 6);
        textAreaRT.offsetMax = new Vector2(-10, -7);
        textArea.AddComponent<RectMask2D>();

        GameObject placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGO.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
        placeholder.text = placeholderText;
        placeholder.fontSize = 22;
        placeholder.color = new Color(0f, 0f, 0f, 0.4f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFull(placeholderGO.GetComponent<RectTransform>());

        GameObject textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI text = textGO.AddComponent<TextMeshProUGUI>();
        text.fontSize = 22;
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFull(textGO.GetComponent<RectTransform>());

        inputField.textViewport = textAreaRT;
        inputField.textComponent = text;
        inputField.placeholder = placeholder;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300, 45);
        return inputField;
    }

    private static Slider CreateSlider(GameObject parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        Image bg = go.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.15f);
        Slider slider = go.AddComponent<Slider>();

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        StretchFull(fillArea.GetComponent<RectTransform>());

        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.75f, 0.3f, 1f);
        RectTransform fillRT = fill.GetComponent<RectTransform>();
        StretchFull(fillRT);

        slider.fillRect = fillRT;
        slider.targetGraphic = fillImg;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;
        return slider;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetAnchorTopStretch(RectTransform rt, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = Vector2.zero;
    }

    private static void AnchorLeft(RectTransform rt, Vector2 offsetFromLeft, Vector2 size)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = offsetFromLeft;
    }

    private static void AnchorRight(RectTransform rt, Vector2 offsetFromRight, Vector2 size)
    {
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = offsetFromRight;
    }

    private static void AnchorBottomCenter(RectTransform rt, Vector2 offsetFromBottom, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = size;
        rt.anchoredPosition = offsetFromBottom;
    }

    private static void LayoutThreeFieldsRow(RectTransform a, RectTransform b, RectTransform c)
    {
        float width = 260f;
        float gap = 30f;
        float totalWidth = width * 3 + gap * 2;
        float startX = -totalWidth / 2f + width / 2f;

        RectTransform[] fields = { a, b, c };
        for (int i = 0; i < fields.Length; i++)
        {
            RectTransform rt = fields[i];
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, 50);
            rt.anchoredPosition = new Vector2(startX + i * (width + gap), -20);
        }
    }

    private static void EnsureEventSystem()
    {
        // FindAnyObjectByType is used here instead of FindFirstObjectByType because we only
        // need to know whether AN EventSystem exists somewhere in the scene, not which one -
        // that makes it the faster of the two non-obsolete options for this check.
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
        // If your project uses ONLY the new Input System package, replace the line above with:
        // es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static void EditorSceneManager_MarkDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}
#endif