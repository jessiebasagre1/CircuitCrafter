using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using CSSSimulation.Networking;
using CSSSimulation.Tutor;

namespace CircuitCrafter.Guidance.EditorTools
{
    /// <summary>
    /// Creates the guidance ASSETS and adds the per-scene components, so almost nothing has to be
    /// typed in by hand. Every menu item is safe to run twice - existing assets are kept, never
    /// overwritten.
    ///
    ///   CircuitCrafter > Guidance > Create Guidance Settings          (once per project)
    ///   CircuitCrafter > Guidance > Create Starter Content            (once per project)
    ///        Review Library, tutorial cards, one job order per COC, the ghost demo material
    ///   CircuitCrafter > Guidance > Add Story Guide To Scene          (each Story scene)
    ///   CircuitCrafter > Guidance > Add Controls Tutorial To Scene    (the first Story scene - COC1)
    ///   CircuitCrafter > Guidance > Preview > Story / Assessment / Auto
    ///   CircuitCrafter > Guidance > Reset Seen Tutorials
    ///
    /// All the wording is a starting point written against the CSS NC II procedures. Edit the assets;
    /// nothing here needs re-running afterwards.
    /// </summary>
    public static class GuidanceContentFactory
    {
        private const string Root = "Assets/CircuitCrafter/Guidance";
        private const string TutorialFolder = Root + "/Tutorials";
        private const string JobOrderFolder = Root + "/JobOrders";
        private const string SettingsPath = "Assets/Resources/GuidanceSettings.asset";
        private const string LibraryPath = Root + "/ReviewLibrary.asset";
        private const string GhostMaterialPath = Root + "/GuideGhost.mat";

        // ==================================================================
        // Settings
        // ==================================================================

        [MenuItem("CircuitCrafter/Guidance/Create Guidance Settings", false, 1)]
        public static GuidanceSettings CreateSettings()
        {
            GuidanceSettings existing = Resources.Load<GuidanceSettings>(GuidanceSettings.ResourcesName);
            if (existing != null)
            {
                Selection.activeObject = existing;
                Debug.Log("[Guidance] GuidanceSettings already exists - selected it.", existing);
                return existing;
            }

            EnsureFolder("Assets/Resources");
            var settings = ScriptableObject.CreateInstance<GuidanceSettings>();
            settings.reviewLibrary = AssetDatabase.LoadAssetAtPath<ReviewLibrary>(LibraryPath);
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            GuidancePolicy.ReloadSettings();

            Selection.activeObject = settings;
            Debug.Log($"[Guidance] Created {SettingsPath}. Story allows every kind of help; Assessment allows none. " +
                      "It MUST stay in a Resources folder with this name.", settings);
            return settings;
        }

        // ==================================================================
        // Starter content
        // ==================================================================

        [MenuItem("CircuitCrafter/Guidance/Create Starter Content", false, 2)]
        public static void CreateStarterContent()
        {
            EnsureFolder(Root);
            EnsureFolder(TutorialFolder);
            EnsureFolder(JobOrderFolder);

            ReviewLibrary library = CreateReviewLibrary();
            CreateTutorialCards();
            CreateJobOrders();
            CreateGhostMaterial();

            GuidanceSettings settings = CreateSettings();
            if (settings != null && settings.reviewLibrary == null)
            {
                settings.reviewLibrary = library;
                EditorUtility.SetDirty(settings);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            GuidancePolicy.ReloadSettings();

            Debug.Log($"[Guidance] Starter content is in {Root}. Review the job orders first - especially COC3's " +
                      "specifications, which were read from your ScreenshotStepData assets.", library);
            Selection.activeObject = library;
        }

        // ------------------------------------------------------------------

        private static ReviewLibrary CreateReviewLibrary()
        {
            ReviewLibrary lib = AssetDatabase.LoadAssetAtPath<ReviewLibrary>(LibraryPath);
            if (lib != null) return lib;

            lib = ScriptableObject.CreateInstance<ReviewLibrary>();

            // ---- hardware handling (COC1, and COC4's repair sites) --------------
            // Specific wording first - the first matching rule wins. No module on these: the same
            // parts are handled in COC4's repair sites, and the same procedure applies there.
            Rule(lib, "Cooler still mounted", "", "", new[] { "heatsink is still mounted" },
                "The CPU cooler is still mounted on top.",
                "Remove the heatsink and fan first.",
                "Unplug the CPU fan cable, release the cooler's clips or screws evenly, and lift the cooler off. Only then " +
                "open the socket lever and load plate and lift the CPU straight out by its edges.",
                "CPU handling", "coc1-disassembly");

            Rule(lib, "CPU not secured for cooler", "", "", new[] { "load plate is still open", "retention lever is still open", "cpu is not installed" },
                "The CPU isn't secured yet.",
                "Seat the CPU, close the load plate and lower the lever - then fit the cooler.",
                "Seat the CPU (gold triangle to the socket mark), close the load plate, lower and latch the retention lever, " +
                "apply thermal paste if the cooler has none, then fit the cooler and plug its fan into CPU_FAN.",
                "CPU handling", "coc1-assembly");

            Rule(lib, "CPU socket locked", "", "", new[] { "cpu lock is still closed", "case lock is still closed", "open the main cpu lock" },
                "The CPU socket is still locked.",
                "Lift the socket lever and open the load plate first.",
                "To remove: lift the retention lever, open the load plate, and lift the CPU straight up by its edges. " +
                "To install: line up the gold triangle with the socket mark, lower the CPU in without pressing, close the load plate and lever, " +
                "then fit the cooler with fresh thermal paste.",
                "CPU handling", "coc1-assembly");

            Rule(lib, "Removal out of order", "", "", new[] { "must come off first" },
                "That part can't come off yet - something is still in the way.",
                "Remove the parts on top of it first.",
                "Disassemble from the outside in: 1) shut down, unplug and wear the ESD strap; 2) remove the side panel; " +
                "3) disconnect power and data cables; 4) remove expansion cards and RAM; 5) remove the CPU cooler, then the CPU; " +
                "6) remove the drives and the power supply; 7) remove the motherboard last. Reassemble in the reverse order.",
                "Component removal sequence", "coc1-disassembly");

            Rule(lib, "Cable still connected", "", "", new[] { "plugged in" },
                "A cable is still connected to that part.",
                "Unplug its cable first - hold the connector, not the wire.",
                "Before removing a component, disconnect every power and data cable attached to it. Grip the connector body, " +
                "press any locking tab, and pull straight out. Never pull a cable by its wires.",
                "Cable handling", "coc1-disassembly");

            Rule(lib, "RAM clips closed", "", "", new[] { "still closed", "clip" },
                "The RAM retention clips are still closed.",
                "Push both clips outward, then lift the module straight up.",
                "Open both retention clips and lift the module straight out, holding it by the edges (never the gold contacts). " +
                "To install: line up the notch with the key in the slot and press down evenly on both ends until the clips snap shut.",
                "Memory installation", "coc1-assembly");

            Rule(lib, "Socket not ready", "", "", new[] { "tried to install '" },
                "That socket isn't ready for the part.",
                "Open the socket's clips or lever and check which way round it goes.",
                "Prepare the slot or socket first (open clips or lever), match the part's notch or key to the socket, " +
                "then seat it without forcing. A part that needs force is in the wrong way or the wrong socket.",
                "Component installation", "coc1-assembly");

            Rule(lib, "Partition not prepared", "COC1", "", new[] { "partition", "unallocated" },
                "Windows can't be installed to that space yet.",
                "Select the unallocated space, click New, then Apply.",
                "In Windows Setup choose Custom: Install Windows only. Select the unallocated space and click New > Apply - Setup " +
                "creates its system partitions. Select the Primary partition (Format it if it was used before) and click Next.",
                "OS installation - disk partitioning", "coc1-os-install");

            Rule(lib, "Wrong installer image", "COC1", "", new[] { "bootable usb", ".iso", "installer image" },
                "That isn't the installer image the job needs.",
                "Use the image named on the job order.",
                "Check the job order for the operating system and edition required, then select exactly that ISO before writing the " +
                "flash drive. Writing the wrong image wastes the drive and the customer's time.",
                "Preparing installation media", "coc1-usb");

            Rule(lib, "Rufus step", "COC1", "", new[] { "rufus" },
                "That Rufus step isn't right.",
                "Official site first, then Device, Boot selection, START.",
                "Download Rufus only from its official site (rufus.ie). In Rufus: Device = the USB flash drive; Boot selection = the " +
                "Windows ISO from the job order; Partition scheme = GPT for UEFI (MBR for a legacy BIOS); keep the default file " +
                "system; press START and confirm the warning that the drive will be erased.",
                "Preparing installation media", "coc1-usb");

            Rule(lib, "Windows Setup choice", "COC1", "", new[] { "os setup" },
                "That isn't the option this job needs.",
                "Re-read the job order and choose the matching option.",
                "Read each Windows Setup screen: language and region, Install now, the edition on the job order, accept the licence, " +
                "Custom: Install Windows only, then the partition you prepared.",
                "OS installation choices", "coc1-os-install");

            Rule(lib, "Driver order", "COC1", "", new[] { "installed out of order", "must be installed first" },
                "Drivers were installed in the wrong order.",
                "Install the chipset (motherboard) driver first.",
                "Install the chipset driver first so Windows identifies the other devices correctly, then the network, audio and " +
                "display drivers. Restart when asked, then install the application software and Windows updates.",
                "Driver and software installation", "coc1-system-config");

            // ---- COC2 ------------------------------------------------------
            Rule(lib, "Patch panel termination", "COC2", "", new[] { "termination", "punch" },
                "The patch panel termination isn't right.",
                "Follow the panel's colour code and seat each wire fully.",
                "Keep the jacket close to the panel and each pair twisted right up to the terminal. Lay each conductor in the slot " +
                "shown by the panel's colour code (T568A or T568B as specified), then punch down with the tool's cutting blade " +
                "facing the waste side. Test the port afterwards.",
                "Structured cabling (punch-down)", "coc2-punchdown");

            Rule(lib, "IP settings rejected", "COC2", "", new[] { "configuration was rejected" },
                "Those network settings won't work together.",
                "Same network, same subnet mask, no duplicate addresses.",
                "Give each device an address in the same network with the same subnet mask and no duplicates - for example " +
                "192.168.1.10 and 192.168.1.11 with 255.255.255.0.",
                "IP addressing", "coc2-connectivity");

            Rule(lib, "Peer unreachable", "COC2", "", new[] { "could not reach", "connectivity" },
                "The devices can't reach each other.",
                "Check the cable and link lights, then each device's IP settings.",
                "Check the physical link first (correct cable type, fully seated, link lights on), then the IP address and subnet " +
                "mask on both ends, then test again with ping.",
                "Testing connectivity", "coc2-connectivity");

            Rule(lib, "Cable failed tester", "COC2", "cable", new string[0],
                "The cable failed the tester.",
                "Check the colour order at both ends against the standard.",
                "T568B pin order: 1 white-orange, 2 orange, 3 white-green, 4 blue, 5 white-blue, 6 green, 7 white-brown, 8 brown. " +
                "T568A swaps the orange and green pairs (1 white-green, 2 green, 3 white-orange, 6 orange). Straight-through = the " +
                "same standard at both ends; crossover = T568A at one end, T568B at the other. Untwist no more than 13 mm, push " +
                "every conductor to the end of the plug, crimp once firmly, then test all 8 pins.",
                "Cable termination (T568A / T568B)", "coc2-crimping");

            // ---- COC3 ------------------------------------------------------
            Rule(lib, "Server step out of sequence", "COC3", "", new[] { "refused on" },
                "That action doesn't belong at this point of the setup.",
                "Check which part of the server setup comes next.",
                "Server setup order: set the computer name and a static IP address; install the AD DS (and DNS) roles; promote the " +
                "server to a domain controller; create OUs, users and groups; create the shared folders; set share and NTFS " +
                "permissions; test from a client and complete the report.",
                "Server setup sequence", "coc3-server");

            Rule(lib, "Server value wrong", "COC3", "configuration", new string[0],
                "That value isn't what the job order specifies.",
                "Check the value against the job order's specifications.",
                "Enter each setting exactly as the job order specifies: computer name, static IP address, subnet mask, default " +
                "gateway and preferred DNS server. A server needs its name and static address BEFORE roles are installed or it is " +
                "promoted to a domain controller.",
                "Server configuration values", "coc3-server");

            Rule(lib, "Ping failed", "COC3", "connectivity", new string[0],
                "The ping test failed.",
                "Check both machines with ipconfig, fix the address or mask, and ping again.",
                "A ping fails when the hosts are on different subnets, have duplicate addresses, or have no working link. Check " +
                "the settings on both machines with ipconfig, correct the address or subnet mask, then ping again.",
                "Testing network services", "coc3-server");

            // ---- COC4 ------------------------------------------------------
            Rule(lib, "Repair before diagnosis", "", "", new[] { "before diagnosing" },
                "The repair started before the fault was diagnosed.",
                "Observe and diagnose first, then repair.",
                "Follow the troubleshooting process: 1) identify the problem; 2) establish a probable cause; 3) test it; 4) plan and " +
                "carry out the repair; 5) verify the whole system works; 6) document the findings. Diagnose before you repair.",
                "Troubleshooting process", "coc4-tickets");

            Rule(lib, "Wrong diagnosis", "", "diagnosis", new string[0],
                "That cause doesn't match the evidence.",
                "Look again at what you observed.",
                "Gather evidence before choosing a cause: power the machine on and observe (fans, beeps, lights, POST messages), " +
                "then inspect inside for loose, missing or unseated parts and cables. Choose the cause that explains ALL the symptoms.",
                "Diagnosing from evidence", "coc4-tickets");

            Rule(lib, "Repair not verified", "", "verification", new string[0],
                "The machine still fails after the repair.",
                "Go back to diagnosis.",
                "After a repair, power on and confirm the fault is gone: the machine passes POST, boots, and the reported problem no " +
                "longer occurs. If it still fails, the diagnosis was incomplete - return to it.",
                "Verifying repairs", "coc4-tickets");

            Rule(lib, "Ticket unresolved", "", "unresolved", new string[0],
                "The job was left unfinished.", "",
                "See each ticket through: observe, diagnose, repair, verify, and record the cause before leaving the site.",
                "Completing each job", "coc4-tickets");

            Rule(lib, "Cause not recorded", "", "identification", new string[0],
                "The cause was not recorded.", "",
                "Record the diagnosed cause on the ticket as part of the job - a repair with no recorded cause is incomplete work " +
                "and cannot be checked by the next technician.",
                "Recording the cause", "coc4-tickets");

            // ---- any module ------------------------------------------------
            Rule(lib, "Time", "", "time", new string[0],
                "Time ran out.", "",
                "Read the job order fully first, then work methodically. If a step takes too long, re-check the job order and the " +
                "standard procedure rather than trying things at random.",
                "Working within the time allowed", "");

            // ---- activities ----------------------------------------------------
            string coc1 = FindSceneName("COC1", "COC1-StoryMode");
            string coc2 = FindSceneName("COC2", "");
            string coc3 = FindSceneName("COC3", "");
            string coc4 = FindSceneName("COC4-Hub", "COC4-Hub");

            Activity(lib, "coc1-disassembly", "COC1 Story: Disassemble the system unit", "COC1", coc1,
                     "Practise the removal order and disconnecting cables safely.");
            Activity(lib, "coc1-assembly", "COC1 Story: Reassemble the system unit", "COC1", coc1,
                     "Seat the CPU and RAM correctly and reconnect every cable.");
            Activity(lib, "coc1-usb", "COC1 Story: Create the bootable USB", "COC1", coc1,
                     "Get Rufus from the official site and write the correct ISO.");
            Activity(lib, "coc1-os-install", "COC1 Story: Install Windows", "COC1", coc1,
                     "Prepare the partition and complete Windows Setup.");
            Activity(lib, "coc1-system-config", "COC1 Story: Drivers, software and updates", "COC1", coc1,
                     "Install drivers in order, then software and updates, then test and report.");
            Activity(lib, "coc2-crimping", "COC2 Story: Make and test UTP cables", "COC2", coc2,
                     "Build straight-through and crossover cables to T568A / T568B.");
            Activity(lib, "coc2-punchdown", "COC2 Story: Patch panel punch-down", "COC2", coc2,
                     "Terminate the patch panel to the colour code and test the port.");
            Activity(lib, "coc2-connectivity", "COC2 Story: Connect and test devices", "COC2", coc2,
                     "Set matching IP settings and prove the link with ping.");
            Activity(lib, "coc3-server", "COC3 Story: Configure the server", "COC3", coc3,
                     "Follow the setup order from static IP to shared folders and permissions.");
            Activity(lib, "coc4-tickets", "COC4 Story: Service tickets", "COC4", coc4,
                     "Observe, diagnose, repair, verify and record - one ticket at a time.");

            // ---- outcomes (codes match CompetencyDefinitionGenerator) ------------
            Link(lib, "COC1", "LO1", "Assembling and disassembling hardware", "coc1-disassembly");
            Link(lib, "COC1", "LO2", "Preparing installation media", "coc1-usb");
            Link(lib, "COC1", "LO3", "Installing the OS and drivers", "coc1-os-install");
            Link(lib, "COC1", "LO4", "Installing application software", "coc1-system-config");
            Link(lib, "COC1", "LO5", "Testing and documenting the work", "coc1-system-config");
            Link(lib, "COC2", "LO1", "Preparing and terminating cables", "coc2-crimping");
            Link(lib, "COC2", "LO2", "Testing cable terminations", "coc2-crimping");
            Link(lib, "COC2", "LO3", "Structured cabling", "coc2-punchdown");
            Link(lib, "COC3", "LO1", "Server addressing and roles", "coc3-server");
            Link(lib, "COC3", "LO2", "Users, groups and shared resources", "coc3-server");
            Link(lib, "COC3", "LO3", "Services, testing and documentation", "coc3-server");
            Link(lib, "COC4", "LO1", "Diagnosing and repairing faults", "coc4-tickets");

            AssetDatabase.CreateAsset(lib, LibraryPath);
            Debug.Log($"[Guidance] Created {LibraryPath} with {lib.rules.Count} rules and {lib.activities.Count} activities." +
                      (string.IsNullOrEmpty(coc2) || string.IsNullOrEmpty(coc3)
                          ? " COC2/COC3 Story scene names were not found in Build Settings - fill in Scene Name on those activities."
                          : ""), lib);
            return lib;
        }

        private static void Rule(ReviewLibrary lib, string name, string module, string category, string[] keywords,
                                 string what, string fix, string procedure, string area, string activity)
        {
            lib.rules.Add(new ReviewLibrary.MistakeRule
            {
                ruleName = name, moduleId = module, category = category, keywords = keywords,
                whatWentWrong = what, howToFix = fix, correctProcedure = procedure,
                areaToImprove = area, activityId = activity
            });
        }

        private static void Activity(ReviewLibrary lib, string id, string title, string module, string scene, string practise)
        {
            lib.activities.Add(new ReviewLibrary.StoryActivity
            {
                activityId = id, title = title, moduleId = module, sceneName = scene, whatToPractise = practise
            });
        }

        private static void Link(ReviewLibrary lib, string module, string code, string area, string activity)
        {
            lib.outcomeLinks.Add(new ReviewLibrary.OutcomeLink
            {
                moduleId = module, outcomeCode = code, areaToImprove = area, activityId = activity
            });
        }

        /// <summary>A Build Settings scene whose name contains the module id and does not look like an
        /// assessment/results/menu scene. Falls back to the given name.</summary>
        private static string FindSceneName(string contains, string fallback)
        {
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                string name = Path.GetFileNameWithoutExtension(s.path);
                string lower = name.ToLowerInvariant();
                if (!name.Contains(contains)) continue;
                if (lower.Contains("result") || lower.Contains("assess") || lower.Contains("menu")) continue;
                return name;
            }
            return fallback;
        }

        // ------------------------------------------------------------------

        private static void CreateTutorialCards()
        {
            // Controls - taught by doing, in ControlsTutorial.
            Card("Controls_Move", GuidanceKind.ControlsTutorial, "Move around",
                 "- W A S D to walk.\n- Space to jump.",
                 "- Drag the joystick on the left to walk.",
                 "Now you try: walk a few steps.", false, 0f);
            Card("Controls_Look", GuidanceKind.ControlsTutorial, "Look around",
                 "- Move the mouse to look around.",
                 "- Drag on the right side of the screen to look around.",
                 "Now you try: look left and right.", false, 0f);
            Card("Controls_Interact", GuidanceKind.ControlsTutorial, "Use a workstation",
                 "- Walk up to a workstation until a prompt appears.\n- Press E to use it.",
                 "- Walk up until a prompt appears, then tap the Interact button.",
                 "Now you try: walk to the PC and use it.", false, 0f);
            Card("Controls_PickUp", GuidanceKind.ControlsTutorial, "Pick up a part",
                 "- Click and hold a part, then drag it off.\n- If it won't move, something is still holding it - a cable, a clip or a lever.",
                 "- Touch and drag a part to take it off.\n- If it won't move, something is still holding it - a cable, a clip or a lever.",
                 "Now you try: take off a part that is ready.", false, 0f);
            Card("Controls_Install", GuidanceKind.ControlsTutorial, "Install a part",
                 "- Drag a part from the hotbar onto its socket.\n- It only seats in the right socket, the right way round.",
                 "- Drag a part from the hotbar onto its socket.\n- It only seats in the right socket, the right way round.",
                 "Now you try: put a part back.", false, 0f);

            // Features - shown the first time each one appears.
            Card("Feature_JobOrder", GuidanceKind.FeatureTutorial, "Your job order",
                 "- Every job starts with a job order: the customer, the problem, and what 'done' looks like.\n- Press J to read it again at any time.",
                 "- Every job starts with a job order: the customer, the problem, and what 'done' looks like.\n- Tap Job order to read it again at any time.",
                 "", false, 12f);
            Card("Feature_CurrentTask", GuidanceKind.FeatureTutorial, "Current task",
                 "- The panel at the top left shows what to do next, and how.\n- It moves on by itself as you finish each task.",
                 "", "", false, 12f);
            Card("Feature_Hotbar", GuidanceKind.FeatureTutorial, "Your hotbar",
                 "- Parts you remove go into the hotbar at the bottom of the screen.\n- Click a slot to read about that part.",
                 "- Parts you remove go into the hotbar at the bottom of the screen.\n- Tap a slot to read about that part.",
                 "", false, 12f);
            Card("Feature_AiTutor", GuidanceKind.FeatureTutorial, "AI Tutor",
                 "- Stuck, or want to know WHY a step is done? Press Tab to ask the AI Tutor.\n- It knows your job order and current task. It is switched off during assessments.",
                 "- Stuck, or want to know WHY a step is done? Tap the chat icon to ask the AI Tutor.\n- It knows your job order and current task. It is switched off during assessments.",
                 "", false, 14f);
            Card("Feature_Highlights", GuidanceKind.FeatureTutorial, "Highlights",
                 "- In Story Mode the part or button you need next glows gold, with a label beside it.\n- Glows are a learning aid - Assessment Mode has none.",
                 "", "", false, 12f);
            Card("Feature_Tools", GuidanceKind.FeatureTutorial, "Tools and equipment",
                 "- Collect the tools a job needs before you start.\n- Choosing the right tool is part of the job - read the job order first.",
                 "", "", false, 12f);

            // Procedures - shown when a StoryGuide step with this card begins.
            Card("COC1_Safety", GuidanceKind.ProcedureTutorial, "Before you open the case",
                 "- Shut down, switch off and UNPLUG the power cord.\n- Wear an anti-static wrist strap clipped to bare metal on the chassis.\n- Keep screws in a parts tray and work on a dry, clear bench.",
                 "", "Now you try: open the side panel.", true, 0f);
            Card("COC1_SeatParts", GuidanceKind.ProcedureTutorial, "Seating RAM and the CPU",
                 "- RAM: open both clips, match the notch to the slot key, press both ends down until the clips snap.\n- CPU: lift the lever, match the gold triangle to the socket mark, lower it in - never press.\n- Reconnect every cable you removed.",
                 "", "Now you try: put the parts back from your hotbar.", true, 0f);
            Card("COC1_Rufus", GuidanceKind.ProcedureTutorial, "Making a bootable USB",
                 "- Get Rufus from its official site only (rufus.ie).\n- Device: your flash drive. Boot selection: the Windows ISO on the job order.\n- START erases the drive - check you picked the right one.",
                 "", "Now you try: create the installer.", true, 0f);
            Card("COC1_WindowsSetup", GuidanceKind.ProcedureTutorial, "Windows Setup",
                 "- Boot the PC from the USB (boot menu or BIOS boot order).\n- Choose Custom: Install Windows only.\n- Select the unallocated space, click New, then install to the Primary partition.",
                 "", "Now you try: install Windows.", true, 0f);
            Card("COC1_Drivers", GuidanceKind.ProcedureTutorial, "Driver order",
                 "- Chipset (motherboard) driver FIRST - Windows needs it to identify the other devices.\n- Then network, audio and display.\n- Restart when asked, then install software and updates.",
                 "", "Now you try: install the drivers.", true, 0f);
            Card("COC2_T568", GuidanceKind.ProcedureTutorial, "T568A and T568B",
                 "- T568B: white-orange, orange, white-green, blue, white-blue, green, white-brown, brown.\n- T568A swaps the orange and green pairs.\n- Straight-through: same standard both ends. Crossover: A at one end, B at the other.\n- Untwist no more than 13 mm and push every wire to the end of the plug.",
                 "", "Now you try: arrange the wires for the first end.", true, 0f);
            Card("COC2_Tester", GuidanceKind.ProcedureTutorial, "Reading the cable tester",
                 "- Plug one end into the main unit and the other into the remote.\n- Lights 1 to 8 should come on in order on both units.\n- Out of order means crossed wires; a missing light means an open wire.",
                 "", "Now you try: test your cable.", true, 0f);
            Card("COC2_PunchDown", GuidanceKind.ProcedureTutorial, "Punching down",
                 "- Keep each pair twisted right up to the terminal.\n- Follow the colour code printed on the panel.\n- Punch with the blade facing outside so the extra wire is cut away.",
                 "", "Now you try: terminate the panel.", true, 0f);
            Card("COC3_ServerOrder", GuidanceKind.ProcedureTutorial, "Setting up a server",
                 "- Set the computer name and a static IP address first.\n- Then install the roles (AD DS, DNS) and promote the server to a domain controller.\n- Then create users, groups and shared folders, and set permissions.",
                 "", "Now you try: start with the computer name.", true, 0f);
            Card("COC4_Troubleshooting", GuidanceKind.ProcedureTutorial, "Troubleshooting a service call",
                 "- Observe first: power it on and note fans, beeps, lights and screen messages.\n- Choose the cause the evidence points to, THEN repair.\n- Power on again to verify, and record the cause on the ticket.",
                 "", "Now you try: switch the PC on and watch.", true, 0f);
        }

        private static TutorialCardData Card(string file, GuidanceKind kind, string title, string body, string bodyTouch,
                                             string tryIt, bool pause, float autoHide)
        {
            string path = $"{TutorialFolder}/Tutorial_{file}.asset";
            TutorialCardData existing = AssetDatabase.LoadAssetAtPath<TutorialCardData>(path);
            if (existing != null) return existing;

            var card = ScriptableObject.CreateInstance<TutorialCardData>();
            card.cardId = file.ToLowerInvariant();
            card.kind = kind;
            card.title = title;
            card.body = body;
            card.bodyTouch = bodyTouch;
            card.tryIt = tryIt;
            card.pauseGameplay = pause;
            card.showOnce = true;
            card.autoHideSeconds = autoHide;
            AssetDatabase.CreateAsset(card, path);
            return card;
        }

        private static TutorialCardData LoadCard(string file) =>
            AssetDatabase.LoadAssetAtPath<TutorialCardData>($"{TutorialFolder}/Tutorial_{file}.asset");

        // ------------------------------------------------------------------

        private static void CreateJobOrders()
        {
            JobOrder("COC1", jo =>
            {
                jo.jobNumber = "JO-COC1-001";
                jo.unitTitle = "Install and configure computer systems";
                jo.customerName = "Ms. Reyes";
                jo.customerDetail = "Office manager, Reyes Printing Services";
                jo.request = "Our office PC needs a full check-up and a clean install. Please take it apart, check the parts, put it back together, and set it up for work.";
                jo.problem = "The PC has become slow and unstable. We want a fresh system with the right drivers and our office software.";
                jo.expectedOutcomes = new[]
                {
                    "System unit disassembled and reassembled, every part seated and every cable connected",
                    "Bootable Windows installer created on the USB flash drive",
                    "Windows installed on a correctly prepared partition",
                    "Drivers (chipset first), application software and updates installed",
                    "System tested and the service report completed"
                };
                jo.specifications = new[] { "Operating system: the Windows installer image listed in the USB creator", "Installer media: USB flash drive" };
                jo.whatYouWillPractise = "Safe disassembly and assembly of a system unit, preparing installation media, and installing and configuring the operating system.";
                jo.toolsNeeded = new[] { "Anti-static wrist strap", "Phillips screwdriver", "Parts tray", "USB flash drive (8 GB or larger)" };
                jo.safetyReminder = "Unplug the PC before opening it, and wear an anti-static wrist strap clipped to the chassis.";
                jo.assessmentTitle = "COC 1 Assessment - Install and Configure Computer Systems";
                jo.assessmentScenario = "A client's desktop computer must be disassembled, reassembled, and made ready for use with a fresh operating system.";
                jo.assessmentOutcomes = new[]
                {
                    "Disassemble and reassemble the system unit.",
                    "Prepare the installation media.",
                    "Install and configure the operating system, drivers, application software and updates.",
                    "Test the system and complete the service report."
                };
            });

            JobOrder("COC2", jo =>
            {
                jo.jobNumber = "JO-COC2-001";
                jo.unitTitle = "Set up computer networks";
                jo.customerName = "Mr. Santos";
                jo.customerDetail = "IT coordinator, Santos Trading";
                jo.request = "We need network cables made and tested for our new workstations, and our patch panel terminated.";
                jo.problem = "One new PC connects to the switch; two others need a direct PC-to-PC link for file transfer.";
                jo.expectedOutcomes = new[]
                {
                    "A straight-through cable that passes all 8 conductors on the tester",
                    "A crossover cable that passes all 8 conductors on the tester",
                    "Patch panel port terminated and verified"
                };
                jo.specifications = new[] { "Cable: Category 5e UTP with RJ45 connectors", "Cables required: 1 straight-through, 1 crossover" };
                jo.whatYouWillPractise = "Terminating UTP cable to T568A / T568B, testing it, and punching down a patch panel.";
                jo.toolsNeeded = new[] { "Crimping tool", "Cable stripper", "RJ45 connectors", "LAN cable tester", "Punch-down tool" };
                jo.safetyReminder = "Cut away from your hand, and keep offcuts of wire off the floor and the bench.";
                jo.assessmentTitle = "COC 2 Assessment - Set Up Computer Networks";
                jo.assessmentScenario = "A small office needs its network cabling prepared and verified before its workstations are connected.";
                jo.assessmentOutcomes = new[]
                {
                    "Make the cables listed in the specifications and prove each one with the tester.",
                    "Terminate the patch panel and verify the port."
                };
            });

            JobOrder("COC3", jo =>
            {
                jo.jobNumber = "JO-COC3-001";
                jo.unitTitle = "Set up computer servers";
                jo.customerName = "Principal Dela Cruz";
                jo.customerDetail = "School computer laboratory";
                jo.request = "Set up our new server so teachers and students can sign in with their own accounts and share files.";
                jo.problem = "The server is freshly installed with no network settings, roles, accounts or shares.";
                jo.expectedOutcomes = new[]
                {
                    "Server named and given its static IP address",
                    "Server roles installed and the domain created",
                    "User accounts, groups and shared folders created with the correct permissions",
                    "Connectivity tested from a client, and the work documented"
                };
                jo.specifications = Coc3SpecsFromStepData();
                jo.whatYouWillPractise = "Configuring a Windows server from addressing through roles, accounts, shares and permissions.";
                jo.toolsNeeded = new string[0];
                jo.safetyReminder = "Record every setting you change - a server's configuration must be reproducible.";
                jo.assessmentTitle = "COC 3 Assessment - Set Up Computer Servers";
                jo.assessmentScenario = "A new server must be configured to provide accounts and shared resources to the school network.";
                jo.assessmentOutcomes = new[]
                {
                    "Configure the server to the specifications below.",
                    "Provide the required accounts, groups and shared resources.",
                    "Test the services and document the work."
                };
            });

            JobOrder("COC4", jo =>
            {
                jo.jobNumber = "JO-COC4-001";
                jo.unitTitle = "Maintain and repair computer systems and networks";
                jo.customerName = "Service desk";
                jo.customerDetail = "Tickets arrive at the hub";
                jo.request = "Work through the service tickets at the hub. Each one is a customer's report of a fault.";
                jo.problem = "The customer describes what they see - never the cause. Finding the cause is the job.";
                jo.expectedOutcomes = new[]
                {
                    "Each fault observed, diagnosed, repaired and verified",
                    "The cause of each fault recorded on its ticket"
                };
                jo.specifications = new string[0];
                jo.whatYouWillPractise = "The troubleshooting process: observe, diagnose from evidence, repair, verify, document.";
                jo.toolsNeeded = new[] { "Anti-static wrist strap", "Screwdriver", "Cleaning tools as needed" };
                jo.safetyReminder = "Power off and unplug before working inside a machine.";
                jo.assessmentTitle = "COC 4 Assessment - Maintain and Repair";
                jo.assessmentScenario = "The customer reports that the computer cannot boot.";
                jo.assessmentOutcomes = new[] { "Diagnose the problem, identify the cause, and perform the necessary corrective action." };
            });
        }

        private static void JobOrder(string module, System.Action<JobOrderData> fill)
        {
            string path = $"{JobOrderFolder}/JobOrder_{module}.asset";
            if (AssetDatabase.LoadAssetAtPath<JobOrderData>(path) != null) return;

            var jo = ScriptableObject.CreateInstance<JobOrderData>();
            jo.moduleId = module;
            fill(jo);
            AssetDatabase.CreateAsset(jo, path);
        }

        /// <summary>Reads the expected values straight from the COC3 step assets, so the job order's
        /// specifications cannot disagree with what the steps actually check.</summary>
        private static string[] Coc3SpecsFromStepData()
        {
            var specs = new List<string>();
            var seen = new HashSet<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:ScreenshotStepData"))
            {
                var step = AssetDatabase.LoadAssetAtPath<ScreenshotStepData>(AssetDatabase.GUIDToAssetPath(guid));
                if (step == null) continue;

                string line = null;
                if (step.stepType == StepType.NetworkConfigInput)
                    line = $"Server IP address {step.expectedIP}, subnet mask {step.expectedSubnetMask}, default gateway {step.expectedGateway}";
                else if (step.stepType == StepType.TextInput && !string.IsNullOrEmpty(step.textFieldExpectedValue))
                    line = $"{step.textFieldLabel}: {step.textFieldExpectedValue}";

                if (line != null && seen.Add(line)) specs.Add(line);
            }

            if (specs.Count == 0)
                specs.Add("Server addressing and names: as set by your trainer (edit this job order)");

            return specs.ToArray();
        }

        // ------------------------------------------------------------------

        private static void CreateGhostMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath) != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            bool urp = shader != null;
            if (!urp) shader = Shader.Find("Sprites/Default");
            if (shader == null) { Debug.LogWarning("[Guidance] No suitable shader for the ghost material - skipped."); return; }

            var mat = new Material(shader) { name = "GuideGhost" };
            Color c = new Color(0.37f, 0.83f, 1f, 0.35f);

            if (urp)
            {
                mat.SetFloat("_Surface", 1f);   // Transparent
                mat.SetFloat("_Blend", 0f);     // Alpha
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.SetColor("_BaseColor", c);
            }
            else
            {
                mat.color = c;
            }

            AssetDatabase.CreateAsset(mat, GhostMaterialPath);
        }

        // ==================================================================
        // Scene components
        // ==================================================================

        [MenuItem("CircuitCrafter/Guidance/Add Story Guide To Scene", false, 21)]
        public static void AddStoryGuide()
        {
            if (Object.FindAnyObjectByType<StoryGuide>(FindObjectsInactive.Include) != null)
            {
                EditorUtility.DisplayDialog("Story Guide", "This scene already has a StoryGuide.", "OK");
                return;
            }

            Transform parent = GameObject.Find("Managers") != null ? GameObject.Find("Managers").transform : null;
            var go = new GameObject("StoryGuide");
            Undo.RegisterCreatedObjectUndo(go, "Add Story Guide");
            if (parent != null) go.transform.SetParent(parent, false);

            StoryGuide guide = go.AddComponent<StoryGuide>();
            string module = null;

            if (Object.FindAnyObjectByType<PCDisassemblySetup>(FindObjectsInactive.Include) != null)
            {
                module = "COC1";
                Coc1Steps(guide);
            }
            else if (Object.FindAnyObjectByType<COC2TaskSetup>(FindObjectsInactive.Include) != null)
            {
                module = "COC2";
                Coc2Steps(guide);
            }
            else if (Object.FindAnyObjectByType<TaskWaypointArrow>(FindObjectsInactive.Include) != null)
            {
                guide.ImportFromWaypointArrow();
            }

            if (module == null && Object.FindAnyObjectByType<COC3Manager>(FindObjectsInactive.Include) != null) module = "COC3";

            if (module != null)
                guide.jobOrder = AssetDatabase.LoadAssetAtPath<JobOrderData>($"{JobOrderFolder}/JobOrder_{module}.asset");

            EditorUtility.SetDirty(guide);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            Debug.Log($"[Guidance] Added StoryGuide ({guide.steps.Count} step(s){(module != null ? ", " + module : "")}). " +
                      "Fill in each step's Highlight objects (the parts, ports or buttons it involves) - that is the one thing " +
                      "that cannot be guessed. Run Create Starter Content first if the Job Order and Tutorial fields are empty.", go);
        }

        private static void Coc1Steps(StoryGuide g)
        {
            Step(g, "disassemble", "Disassemble the system unit",
                 "Remove the side panel, unplug each cable, then take the parts out one by one. A part that won't move is still locked or cabled.",
                 "Working from the outside in protects the parts and their connectors.",
                 PCDisassemblySetup.TaskId, true, LoadCard("COC1_Safety"),
                 "System unit disassembled - every part came off in a safe order.");
            Step(g, "assemble", "Reassemble the system unit",
                 "Seat each part from the hotbar back in its socket, then reconnect every cable. Parts only fit the right way round.",
                 "Loose or missing cables are the most common reason a rebuilt PC will not start.",
                 PCDisassemblySetup.AssembleTaskId, true, LoadCard("COC1_SeatParts"),
                 "Reassembled, with every cable back where it belongs.");
            Step(g, "rufus", "Download and install Rufus",
                 "On the working PC, open the browser, go to the official Rufus site and install it.",
                 "Download tools only from their official site - copies elsewhere can carry malware.",
                 RufusSetupManager.TaskId, false, null,
                 "Rufus installed from the official source.");
            Step(g, "usb", "Create the bootable USB",
                 "Plug in the flash drive, choose it as the Device, select the Windows ISO from the job order, then press START.",
                 "The installer must match the job order, or the wrong system gets installed.",
                 BootableUSBCreator.TaskId, false, LoadCard("COC1_Rufus"),
                 "Bootable installer ready.");
            Step(g, "os", "Install Windows",
                 "Boot the rebuilt PC from the USB, choose Custom install, prepare the partition, and finish Windows Setup.",
                 "", CircuitCrafter.Assessment.OSInstallTaskBridge.TaskId, false, LoadCard("COC1_WindowsSetup"),
                 "Windows installed.");
            Step(g, "drivers", "Install the drivers",
                 "Install the chipset (motherboard) driver first, then network, audio and display.",
                 "The chipset driver lets Windows identify every other device correctly.",
                 SystemConfigTaskIds.InstallDrivers, true, LoadCard("COC1_Drivers"),
                 "Drivers installed in the right order.");
            Step(g, "apps", "Install the application software",
                 "Install the programs the job order asks for.", "",
                 SystemConfigTaskIds.InstallAppSoftware, true, null, "Software installed.");
            Step(g, "updates", "Install updates",
                 "Install the pending Windows updates.",
                 "Updates close security holes before the PC goes back to the customer.",
                 SystemConfigTaskIds.InstallUpdates, true, null, "System up to date.");
            Step(g, "test", "Test the computer",
                 "Run the diagnostic tests and check each result.", "",
                 SystemConfigTaskIds.TestComputer, false, null, "Tests passed.");
            Step(g, "report", "Complete the service report",
                 "Record what you did and the test results, then submit the report.",
                 "The service report is the customer's proof of the work done.",
                 SystemConfigTaskIds.DocumentWork, false, null,
                 "Job complete - well done.");
        }

        private static void Coc2Steps(StoryGuide g)
        {
            var setup = Object.FindAnyObjectByType<COC2TaskSetup>(FindObjectsInactive.Include);
            string crimp = setup != null ? setup.crimpTaskId : "crimp_cable";
            string test = setup != null ? setup.testTaskId : "test_cable";
            string panel = setup != null ? setup.patchPanelTaskId : "punch_patch_panel";
            string cords = setup != null ? setup.patchCordTaskId : "patch_to_switch";

            Step(g, "crimp", "Make the cable",
                 "Strip about 2-3 cm of jacket, arrange the eight wires in the standard's order, trim them even, then insert them and crimp the RJ45.",
                 "The pair order decides whether the cable works - and which devices it can connect.",
                 crimp, true, LoadCard("COC2_T568"), "Cable crimped.");
            Step(g, "test", "Test the cable",
                 "Plug both ends into the cable tester and check all eight lights.",
                 "A cable is not finished until it passes the tester.",
                 test, true, LoadCard("COC2_Tester"), "Cable passed the tester.");
            Step(g, "punch", "Terminate the patch panel",
                 "Lay each conductor in its colour-coded slot and punch it down with the blade facing out.", "",
                 panel, true, LoadCard("COC2_PunchDown"), "Patch panel terminated.");
            Step(g, "patch", "Patch to the switch",
                 "Run each patch cord from its panel port to the matching switch port.", "",
                 cords, true, null, "Every port patched.");
        }

        private static void Step(StoryGuide g, string id, string title, string instruction, string why, string taskId,
                                 bool progress, TutorialCardData card, string praise)
        {
            g.steps.Add(new StoryGuide.GuideStep
            {
                stepId = id, title = title, instruction = instruction, whyItMatters = why,
                doneWhen = StoryGuide.DoneWhen.TaskComplete, taskId = taskId, showProgress = progress,
                tutorial = card, completionFeedback = praise
            });
        }

        [MenuItem("CircuitCrafter/Guidance/Add Controls Tutorial To Scene (first Story scene)", false, 22)]
        public static void AddControlsTutorial()
        {
            if (Object.FindAnyObjectByType<ControlsTutorial>(FindObjectsInactive.Include) != null)
            {
                EditorUtility.DisplayDialog("Controls Tutorial", "This scene already has a ControlsTutorial.", "OK");
                return;
            }

            Transform parent = GameObject.Find("Managers") != null ? GameObject.Find("Managers").transform : null;
            var go = new GameObject("ControlsTutorial");
            Undo.RegisterCreatedObjectUndo(go, "Add Controls Tutorial");
            if (parent != null) go.transform.SetParent(parent, false);

            ControlsTutorial ct = go.AddComponent<ControlsTutorial>();
            ct.steps.Add(new ControlsTutorial.ControlStep { card = LoadCard("Controls_Move"), check = ControlsTutorial.Check.Move, amount = 2f, successText = "That's walking." });
            ct.steps.Add(new ControlsTutorial.ControlStep { card = LoadCard("Controls_Look"), check = ControlsTutorial.Check.Look, amount = 60f, successText = "That's looking around." });
            ct.steps.Add(new ControlsTutorial.ControlStep { card = LoadCard("Controls_Interact"), check = ControlsTutorial.Check.KeyPressed, key = KeyCode.E, successText = "That's how you use a workstation." });

            // First-time feature and control tips, each on its own trigger.
            Trigger(go.transform, "Tip_CurrentTask", LoadCard("Feature_CurrentTask"), TutorialTrigger.When.SceneStart, null);
            Trigger(go.transform, "Tip_JobOrder", LoadCard("Feature_JobOrder"), TutorialTrigger.When.SceneStart, null);
            Trigger(go.transform, "Tip_PickUp", LoadCard("Controls_PickUp"), TutorialTrigger.When.Manual, null);
            Trigger(go.transform, "Tip_Install", LoadCard("Controls_Install"), TutorialTrigger.When.Manual, null);

            if (Object.FindAnyObjectByType<HotbarTutorialTrigger>(FindObjectsInactive.Include) == null)
                Trigger(go.transform, "Tip_Hotbar", LoadCard("Feature_Hotbar"), TutorialTrigger.When.HotbarGetsItem, null);

            AiTutorChat tutor = Object.FindAnyObjectByType<AiTutorChat>(FindObjectsInactive.Include);
            if (tutor != null && tutor.openButton != null)
                Trigger(go.transform, "Tip_AiTutor", LoadCard("Feature_AiTutor"), TutorialTrigger.When.ObjectBecomesActive, tutor.openButton.gameObject);

            EditorUtility.SetDirty(ct);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            Debug.Log("[Guidance] Added ControlsTutorial (move, look, interact) and first-time tips. Tip_PickUp and Tip_Install are " +
                      "set to Manual: wire their Trigger() to the moment the learner first enters the PC station and first " +
                      "starts reassembly - or change When to Object Becomes Active with that station's camera or UI as Witness.", go);
        }

        private static void Trigger(Transform parent, string name, TutorialCardData card, TutorialTrigger.When when, GameObject witness)
        {
            if (card == null) return;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Add Tutorial Trigger");
            go.transform.SetParent(parent, false);
            TutorialTrigger t = go.AddComponent<TutorialTrigger>();
            t.card = card;
            t.when = when;
            t.witness = witness;
        }

        // ==================================================================
        // Preview and reset
        // ==================================================================

        [MenuItem("CircuitCrafter/Guidance/Preview/Story Mode", false, 40)]
        private static void PreviewStory() => SetPreview(GuidanceSettings.EditorPreview.ForceStory);

        [MenuItem("CircuitCrafter/Guidance/Preview/Assessment Mode", false, 41)]
        private static void PreviewAssessment() => SetPreview(GuidanceSettings.EditorPreview.ForceAssessment);

        [MenuItem("CircuitCrafter/Guidance/Preview/Auto (the menu decides)", false, 42)]
        private static void PreviewAuto() => SetPreview(GuidanceSettings.EditorPreview.Auto);

        private static void SetPreview(GuidanceSettings.EditorPreview value)
        {
            GuidanceSettings settings = Resources.Load<GuidanceSettings>(GuidanceSettings.ResourcesName);
            if (settings == null) settings = CreateSettings();
            settings.editorPreview = value;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            GuidancePolicy.ReloadSettings();
            Debug.Log($"[Guidance] Editor preview set to {value}. Builds ignore this setting.", settings);
        }

        [MenuItem("CircuitCrafter/Guidance/Reset Seen Tutorials", false, 60)]
        private static void ResetSeen()
        {
            TutorialProgress.ResetForActiveProfile();
            Debug.Log("[Guidance] Seen tutorials reset for " +
                      (Application.isPlaying ? "the active profile." : "the 'default' profile. Run it in Play Mode to reset the profile you are playing as."));
        }

        // ==================================================================

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
