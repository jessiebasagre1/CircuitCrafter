using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// THE CONTENT TABLE FOR COC 1's software half - bootable USB, OS installation,
/// drivers/applications/updates, and testing.
///
/// This file holds no Unity plumbing at all: it is the wording, the options, the correct
/// answers and the prerequisites, in one place you can read top to bottom and hand to an
/// assessor. COC1ContentBuilder turns it into ScriptableObject assets and wires them into
/// the scene. Same split as COC3StepBlueprint / COC3SceneBuilder.
///
/// TO CHANGE THE COURSE CONTENT, EDIT THIS FILE AND PRESS BUILD AGAIN.
/// Rebuilding overwrites the fields of existing assets rather than recreating them, so
/// everything you have dragged into an Inspector stays connected.
///
/// -------------------------------------------------------------------------------------
/// WHY THE ORDER HERE IS NOT THE ORDER OF THE InstallStage ENUM
/// InstallStage values are serialised by ORDINAL on every OSInstallStageData asset, so the
/// seven new stages had to be APPENDED to that enum, not slotted into the middle. The order
/// the install actually runs in comes from the stages LIST on OSInstallManager - which is
/// exactly the list Stages below produces, in exactly this order.
/// -------------------------------------------------------------------------------------
/// </summary>
public static class COC1ContentBlueprint
{
    // =====================================================================
    // Lab constants - change these once, everything below follows.
    // =====================================================================

    public const string RootFolder = "Assets/COC1/Content";

    public const string TargetImageName = "Windows 10 Pro 64-bit (22H2)";
    public const string TargetImageSize = "5.4 GB";
    public const string DecoyImageName = "Ubuntu 22.04.4 LTS Desktop (64-bit)";
    public const string DecoyImageSize = "4.7 GB";

    public const string BootDevice = "USB Flash Drive (UEFI: SanDisk Cruzer 16GB)";
    public const string TargetEdition = "Windows 10 Pro";
    public const string InstallTypeCustom = "Custom: Install Windows only (advanced)";
    public const string InstallTypeUpgrade = "Upgrade: Install Windows and keep files, settings, and applications";
    public const string AcceptTerms = "I accept the license terms";
    public const string NoProductKey = "I don't have a product key";
    public const string RegionAndKeyboard = "Philippines - US keyboard layout";
    public const string LocalAccount = "Create a local account";

    public const float DiskSizeGB = 240f;

    // =====================================================================
    // Records
    // =====================================================================

    public class StageDef
    {
        public string assetName;
        public OSInstallManager.InstallStage stage;
        public string displayText;
        public float duration = 2f;
        public bool reboot;
        public bool userInput;
        public string[] choices;

        /// <summary>Leave null/empty for a genuinely free choice. Anything else is graded:
        /// a wrong pick shows wrongText, counts as a mistake, and the learner picks again.</summary>
        public string correct;

        public string wrongText;
        public string teachText;
        public PartitionOption[] partitions;
    }

    public class ImageDef
    {
        public string assetName;
        public string displayName;
        public string sizeLabel;

        /// <summary>True for the image the job order calls for. The builder points
        /// BootableUSBCreator.requiredImage at it and gives it the install sequence.</summary>
        public bool isRequired;
    }

    public class ItemDef
    {
        public string assetName;
        public string itemTaskId;
        public string displayName;
        public string description;
        public float duration = 3f;
        public string[] prerequisites;
        public string blockedHint;
        public string teachText;
    }

    public class TestDef
    {
        public string assetName;
        public string testName;
        public string description;
        public float duration = 2f;
        public string[] prerequisites;
        public string failureHint;
    }

    // =====================================================================
    // 1. THE OS INSTALLATION SEQUENCE - 17 stages, in run order
    // =====================================================================
    //
    // Mapped to what a learner actually sees installing Windows 10 from a USB stick:
    // power on -> set boot order -> POST -> boot the installer -> language -> product key ->
    // edition -> licence -> installation type -> partition -> copy -> install -> restart ->
    // first boot -> region and keyboard -> account -> desktop.
    //
    // Five of those are graded decisions (boot device, product key, edition, licence,
    // installation type, region, account). Language is deliberately left free, because it
    // genuinely is. The partition stage MUST stay ungraded here - see its comment.

    public static readonly List<StageDef> Stages = new List<StageDef>
    {
        new StageDef
        {
            assetName = "OS_01_PowerOn",
            stage = OSInstallManager.InstallStage.PowerOn,
            duration = 1.5f,
            displayText =
                "American Megatrends Inc.\n" +
                "BIOS Date: 09/14/22  Ver: F.42\n\n" +
                "Press DEL to enter SETUP",
            teachText =
                "Before switching on: case closed, power cable seated, no tools left inside, hands dry. " +
                "A unit that is powered on with the side panel off and a screwdriver on the board is an " +
                "OHS finding, not just an untidy bench."
        },

        new StageDef
        {
            assetName = "OS_02_BiosSetup",
            stage = OSInstallManager.InstallStage.BiosSetup,
            userInput = true,
            displayText =
                "BIOS SETUP UTILITY  -  Boot\n\n" +
                "Boot Option Priorities\n" +
                "  Boot Option #1   [ ? ]\n\n" +
                "Set the device the PC should try FIRST. The Windows installer is on the flash drive " +
                "you prepared at the reference PC.\n\n" +
                "F10 = Save & Exit",
            choices = new[]
            {
                BootDevice,
                "Windows Boot Manager (240GB SSD)",
                "Network Boot (PXE) - Realtek PXE B03 D00",
                "CD/DVD Drive (empty)"
            },
            correct = BootDevice,
            wrongText =
                "Reboot and Select proper Boot device.\n\n" +
                "That device has no installer on it. Set Boot Option #1 to the USB flash drive, " +
                "then Save & Exit.",
            teachText =
                "The boot order decides which device the firmware tries first. To install from your " +
                "flash drive the USB must be Boot Option #1 - and once Windows is installed you put the " +
                "SSD back on top, or the PC keeps restarting into the installer."
        },

        new StageDef
        {
            assetName = "OS_03_POST",
            stage = OSInstallManager.InstallStage.POST,
            duration = 2.5f,
            displayText =
                "Power-On Self-Test\n\n" +
                "CPU        : Intel(R) Core(TM) i5  @ 3.00GHz\n" +
                "Memory     : 8192MB  OK\n" +
                "SATA Port 0: 240GB SSD\n" +
                "USB Device : SanDisk Cruzer 16GB\n\n" +
                "Booting from USB..."
        },

        new StageDef
        {
            assetName = "OS_04_BootMediaCheck",
            stage = OSInstallManager.InstallStage.BootMediaCheck,
            duration = 2.5f,
            displayText =
                "Press any key to boot from USB . . .\n\n" +
                "Windows Setup is loading files..."
        },

        new StageDef
        {
            assetName = "OS_05_LanguageSelect",
            stage = OSInstallManager.InstallStage.LanguageSelect,
            userInput = true,
            displayText =
                "Windows Setup\n\n" +
                "Language to install, time and currency format, keyboard or input method.\n\n" +
                "Set these to what the job order asks for, then Next  >  Install now.",
            choices = new[]
            {
                "English (United States)  /  US keyboard",
                "English (United Kingdom)  /  UK keyboard",
                "Filipino (Philippines)  /  US keyboard"
            },
            // Deliberately NOT graded: any of these installs a working machine, and the
            // customer's preference is the only thing that decides it. Set correct = the
            // first entry if your assessment sheet specifies a language.
            correct = null,
            teachText =
                "Language, time and currency format, and keyboard layout come off the job order, not " +
                "off habit. The usual Philippine shop build is English (United States) with a US " +
                "keyboard layout - the physical keyboard in front of you is US-layout."
        },

        new StageDef
        {
            assetName = "OS_06_ProductKey",
            stage = OSInstallManager.InstallStage.ProductKey,
            userInput = true,
            displayText =
                "Activate Windows\n\n" +
                "If this is the first time you're installing Windows on this PC, you need to enter a " +
                "valid Windows product key.\n\n" +
                "There is no key on this job order and no COA sticker on the case.",
            choices = new[]
            {
                "Enter the product key from the COA sticker",
                NoProductKey
            },
            correct = NoProductKey,
            wrongText =
                "There is no COA sticker on this unit and no key on the job order, so there is nothing " +
                "to type. Choose \"I don't have a product key\" - the installation is identical, and " +
                "the unit is activated afterwards with the licence the customer supplies.",
            teachText =
                "A product key lives on the COA sticker, in the retail packaging, or embedded in the " +
                "UEFI firmware of a branded machine. When you have none of those, install first and " +
                "activate later. Never type a key from another machine."
        },

        new StageDef
        {
            assetName = "OS_07_EditionSelect",
            stage = OSInstallManager.InstallStage.EditionSelect,
            userInput = true,
            displayText =
                "Select the operating system you want to install\n\n" +
                "Operating system            Architecture   Date modified\n" +
                "-----------------------------------------------------------\n" +
                "Windows 10 Home             x64            10/14/2022\n" +
                "Windows 10 Pro              x64            10/14/2022\n" +
                "Windows 10 Education        x64            10/14/2022\n\n" +
                "The job order specifies " + TargetEdition + ", 64-bit.",
            choices = new[] { "Windows 10 Home", TargetEdition, "Windows 10 Education" },
            correct = TargetEdition,
            wrongText =
                "That is not the edition on the job order. This unit has to join a Windows domain " +
                "later, and Home cannot join a domain at all. Select " + TargetEdition + ".",
            teachText =
                "The edition is not cosmetic. Home has no domain join, no Group Policy Editor, no " +
                "BitLocker and no Remote Desktop host. Any unit destined for the server you set up in " +
                "COC 3 has to be Pro or better - and 64-bit, or it cannot address more than about 4GB " +
                "of the RAM you just installed."
        },

        new StageDef
        {
            assetName = "OS_08_LicenseTerms",
            stage = OSInstallManager.InstallStage.LicenseTerms,
            userInput = true,
            displayText =
                "Applicable notices and license terms\n\n" +
                "MICROSOFT SOFTWARE LICENSE TERMS\n" +
                "WINDOWS OPERATING SYSTEM\n\n" +
                "...IF YOU COMPLY WITH THESE LICENSE TERMS, YOU HAVE THE RIGHTS BELOW...\n\n" +
                "Setup cannot continue until the terms are accepted.",
            choices = new[] { AcceptTerms, "I do not accept the license terms" },
            correct = AcceptTerms,
            wrongText =
                "Setup closes if the terms are declined. You must accept them to continue - and the " +
                "customer is bound by them, which is why a legitimate licence matters."
        },

        new StageDef
        {
            assetName = "OS_09_InstallType",
            stage = OSInstallManager.InstallStage.InstallType,
            userInput = true,
            displayText =
                "Which type of installation do you want?\n\n" +
                "Upgrade  -  keeps files, settings and applications. Only available when Setup is " +
                "started from inside a working Windows.\n\n" +
                "Custom   -  installs a clean copy. This drive is bare.",
            choices = new[] { InstallTypeUpgrade, InstallTypeCustom },
            correct = InstallTypeCustom,
            wrongText =
                "Upgrade is greyed out here: there is no existing Windows on this drive to upgrade, and " +
                "Setup was booted from USB rather than from inside Windows. For a clean install choose " +
                "\"Custom: Install Windows only (advanced)\".",
            teachText =
                "Clean install = Custom. Upgrade is for an already-working Windows and carries that " +
                "system's faults, drivers and malware forward with it - which is usually the reason " +
                "the unit came into the shop in the first place."
        },

        new StageDef
        {
            assetName = "OS_10_PartitionSelect",
            stage = OSInstallManager.InstallStage.PartitionSelect,
            userInput = true,
            displayText =
                "Where do you want to install Windows?\n\n" +
                "Select Drive 0 Unallocated Space, click New to create a partition, Format it, " +
                "then click Next.",
            // MUST STAY NULL.
            // PartitionSelectView submits the PARTITION NAME it generated at runtime
            // ("Drive 0 Partition 1: New Volume"), not one of the strings above - so a
            // correctChoice here could never match and the stage would loop forever.
            // The partition rules are enforced inside PartitionSelectView itself: Next
            // refuses unallocated space and refuses an unformatted partition, and both
            // refusals are now reported as mistakes.
            correct = null,
            partitions = new[]
            {
                new PartitionOption
                {
                    partitionName = "Drive 0 Unallocated Space",
                    totalSizeGB = DiskSizeGB,
                    freeSpaceGB = DiskSizeGB,
                    kind = PartitionKind.Unallocated
                }
            },
            teachText =
                "You cannot install onto raw unallocated space, and you cannot install onto a partition " +
                "that has never been formatted. New creates the partition, Format lays down the NTFS " +
                "file system. Windows quietly adds its own System, MSR and Recovery partitions " +
                "alongside yours - leave those alone."
        },

        new StageDef
        {
            assetName = "OS_11_Copying",
            stage = OSInstallManager.InstallStage.Copying,
            duration = 6f,
            displayText =
                "Installing Windows\n\n" +
                "  Copying Windows files\n" +
                "  Getting files ready for installation\n\n" +
                "Do not switch the computer off."
        },

        new StageDef
        {
            assetName = "OS_12_Installing",
            stage = OSInstallManager.InstallStage.Installing,
            duration = 5f,
            displayText =
                "Installing Windows\n\n" +
                "  Installing features\n" +
                "  Installing updates\n" +
                "  Finishing up"
        },

        new StageDef
        {
            assetName = "OS_13_Reboot",
            stage = OSInstallManager.InstallStage.Reboot,
            duration = 2f,
            reboot = true,
            displayText =
                "Windows needs to restart to continue.\n\n" +
                "Restarting in 3 seconds...",
            teachText =
                "Pull the flash drive out at this first restart, or the PC boots the installer all over " +
                "again instead of the Windows you just installed. If you left the boot order pointing " +
                "at USB, this is where it bites."
        },

        new StageDef
        {
            assetName = "OS_14_FirstBoot",
            stage = OSInstallManager.InstallStage.FirstBoot,
            duration = 3f,
            displayText =
                "Getting devices ready\n\n" +
                "        45%\n\n" +
                "Just a moment..."
        },

        new StageDef
        {
            assetName = "OS_15_RegionKeyboard",
            stage = OSInstallManager.InstallStage.RegionKeyboard,
            userInput = true,
            displayText =
                "Let's start with region. Is this right?\n\n" +
                "...and is this the right keyboard layout?",
            choices = new[]
            {
                RegionAndKeyboard,
                "United States - US keyboard layout",
                "Philippines - Dvorak keyboard layout"
            },
            correct = RegionAndKeyboard,
            wrongText =
                "Region sets the time zone, date format and regional settings the customer will live " +
                "with; the keyboard layout has to match the physical keyboard on the bench. " +
                "Choose Philippines with the US keyboard layout.",
            teachText =
                "Region and keyboard are two separate questions. The Philippines uses a US physical " +
                "keyboard, so the right answer is a Philippine region with a US layout - picking a " +
                "Dvorak or UK layout leaves the customer with keys that type the wrong characters."
        },

        new StageDef
        {
            assetName = "OS_16_AccountSetup",
            stage = OSInstallManager.InstallStage.AccountSetup,
            userInput = true,
            displayText =
                "Who's going to use this PC?\n\n" +
                "Create an account for the customer, then set a password and security questions.",
            choices = new[]
            {
                LocalAccount,
                "Sign in with a Microsoft account",
                "Skip - leave the PC with no account"
            },
            correct = LocalAccount,
            wrongText =
                "This is a customer's machine being prepared on your bench. Signing in with a Microsoft " +
                "account ties it to somebody's personal identity, and Windows cannot be left with no " +
                "account at all. Create a local account.",
            teachText =
                "A local administrator account is the standard shop build: it works with no internet, " +
                "it is what you use to join the domain in COC 3, and it does not tie the customer's " +
                "machine to your Microsoft account. Set a password - an account with no password fails " +
                "the security check at handover."
        },

        new StageDef
        {
            assetName = "OS_17_Complete",
            stage = OSInstallManager.InstallStage.Complete,
            duration = 1.5f,
            displayText =
                "Installation complete.\n\n" +
                TargetEdition + " is installed and the desktop is up.\n\n" +
                "Next: drivers, application software, updates, then test and document.",
            teachText =
                "Windows being on the disk is not a finished job. A freshly installed machine is still " +
                "running generic Microsoft drivers, has no applications and no patches. Device Manager " +
                "is the next stop."
        }
    };

    // =====================================================================
    // 2. THE ISO IMAGES on the reference PC
    // =====================================================================
    // Two entries so the dropdown is a real decision. The Ubuntu image is a working
    // distractor: picking it is now refused and recorded, because reading the job order
    // before you write 5GB to a stick is part of the task.

    public static readonly List<ImageDef> Images = new List<ImageDef>
    {
        new ImageDef
        {
            assetName = "ISO_Windows10Pro",
            displayName = TargetImageName,
            sizeLabel = TargetImageSize,
            isRequired = true
        },
        new ImageDef
        {
            assetName = "ISO_Ubuntu2204",
            displayName = DecoyImageName,
            sizeLabel = DecoyImageSize,
            isRequired = false
        }
    };

    // =====================================================================
    // 3. DRIVERS  (Device Manager - group task "install_drivers")
    // =====================================================================
    // Chipset first, always. Everything else lists it as a prerequisite, so the order is
    // enforced rather than merely suggested.

    public static readonly List<ItemDef> Drivers = new List<ItemDef>
    {
        new ItemDef
        {
            assetName = "DRV_01_Chipset",
            itemTaskId = SystemConfigTaskIds.InstallDriverMotherboard,
            displayName = "Chipset / motherboard driver (INF)",
            description = "Intel Chipset Device Software. Installs the INF files that let Windows " +
                          "identify the board's controllers by name instead of \"Unknown device\".",
            duration = 4f,
            teachText =
                "The chipset driver goes on FIRST. Until it does, Device Manager only sees generic " +
                "controllers, and a display or audio driver installed on top of a guess is the usual " +
                "cause of \"I installed it but it still doesn't work\"."
        },
        new ItemDef
        {
            assetName = "DRV_02_Display",
            itemTaskId = SystemConfigTaskIds.InstallDriverDisplay,
            displayName = "Display / graphics driver",
            description = "Replaces Microsoft Basic Display Adapter. Without it the screen is stuck " +
                          "at a low resolution and cannot drive a second monitor.",
            duration = 4f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverMotherboard },
            blockedHint = "Install the chipset driver first - Windows still lists this card as " +
                          "\"Microsoft Basic Display Adapter\"."
        },
        new ItemDef
        {
            assetName = "DRV_03_Audio",
            itemTaskId = SystemConfigTaskIds.InstallDriverAudio,
            displayName = "Audio driver (Realtek High Definition Audio)",
            description = "Onboard sound. Until this is installed the tray shows a red cross on the " +
                          "speaker icon and no playback device exists.",
            duration = 3f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverMotherboard },
            blockedHint = "Install the chipset driver first - the audio controller is still an " +
                          "unknown device on the PCI bus."
        },
        new ItemDef
        {
            assetName = "DRV_04_Network",
            itemTaskId = SystemConfigTaskIds.InstallDriverNetwork,
            displayName = "Network adapter driver (Ethernet + Wi-Fi)",
            description = "Onboard Realtek Ethernet and the wireless card. Nothing that needs the " +
                          "internet - updates, antivirus definitions - can run until this is in.",
            duration = 3f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverMotherboard },
            blockedHint = "Install the chipset driver first - the network controller has not been " +
                          "identified yet.",
            teachText =
                "Install the network driver from the USB stick or the board's disc, not \"from the " +
                "internet\" - with no network driver there is no internet. This is the one driver you " +
                "must always have a local copy of before you wipe a machine."
        }
    };

    // =====================================================================
    // 4. APPLICATION SOFTWARE  (Software Center - group "install_app_software")
    // =====================================================================

    public static readonly List<ItemDef> AppSoftware = new List<ItemDef>
    {
        new ItemDef
        {
            assetName = "APP_01_Browser",
            itemTaskId = SystemConfigTaskIds.InstallAppBrowser,
            displayName = "Web browser",
            description = "Google Chrome / Mozilla Firefox. The customer's everyday browser, set as " +
                          "default once installed.",
            duration = 3f
        },
        new ItemDef
        {
            assetName = "APP_02_Office",
            itemTaskId = SystemConfigTaskIds.InstallAppOffice,
            displayName = "Office productivity suite",
            description = "Microsoft Office or LibreOffice, per the job order. Install only what the " +
                          "customer is licensed for.",
            duration = 5f,
            teachText =
                "Install only software the customer holds a licence for. Putting a cracked copy of " +
                "Office on a customer's machine is not a shortcut - it is software piracy, and under " +
                "CSS NC II it fails the whole unit of competency."
        },
        new ItemDef
        {
            assetName = "APP_03_Antivirus",
            itemTaskId = SystemConfigTaskIds.InstallAppAntivirus,
            displayName = "Antivirus / endpoint protection",
            description = "Installs the client, then pulls down current virus definitions - which " +
                          "needs a working network connection.",
            duration = 4f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverNetwork },
            blockedHint = "No network connection. Install the network adapter driver first, or the " +
                          "antivirus installs with definitions that are already months old."
        },
        new ItemDef
        {
            assetName = "APP_04_Utilities",
            itemTaskId = SystemConfigTaskIds.InstallAppUtilities,
            displayName = "Utilities (archiver, PDF reader, media player)",
            description = "7-Zip / WinRAR, a PDF reader, and a media player - the standard shop set.",
            duration = 3f
        }
    };

    // =====================================================================
    // 5. UPDATES AND PATCHES  (Windows Update - group "install_updates")
    // =====================================================================
    // All three need the network driver, for the obvious reason.

    public static readonly List<ItemDef> Updates = new List<ItemDef>
    {
        new ItemDef
        {
            assetName = "UPD_01_Cumulative",
            itemTaskId = SystemConfigTaskIds.InstallUpdateCumulative,
            displayName = "Cumulative update for Windows 10 (KB5034763)",
            description = "The current monthly rollup. Contains every fix shipped since the ISO was " +
                          "built - and this ISO is from 2022.",
            duration = 6f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverNetwork },
            blockedHint = "Windows Update cannot reach the internet. Install the network adapter " +
                          "driver first.",
            teachText =
                "An installation media image is a snapshot of one day. Everything found and fixed since " +
                "then arrives through Windows Update, which is why patching is part of the build and " +
                "not an optional extra."
        },
        new ItemDef
        {
            assetName = "UPD_02_Security",
            itemTaskId = SystemConfigTaskIds.InstallUpdateSecurity,
            displayName = "Servicing stack and security update (KB5034441)",
            description = "Updates the component that installs updates. Has to be in before later " +
                          "rollups will apply cleanly.",
            duration = 4f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverNetwork },
            blockedHint = "Windows Update cannot reach the internet. Install the network adapter " +
                          "driver first."
        },
        new ItemDef
        {
            assetName = "UPD_03_Defender",
            itemTaskId = SystemConfigTaskIds.InstallUpdateDefender,
            displayName = "Microsoft Defender definition update (KB2267602)",
            description = "Current malware definitions. Updated almost daily, so it is always the " +
                          "last thing you refresh before handover.",
            duration = 2f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverNetwork },
            blockedHint = "Windows Update cannot reach the internet. Install the network adapter " +
                          "driver first."
        }
    };

    // =====================================================================
    // 6. TESTING  (Diagnostics - group "test_computer")
    // =====================================================================
    // Each test passes only if the work it depends on was actually done, so skipping a
    // driver earlier shows up here as a real failure rather than the two steps being
    // unconnected. Re-runnable: fix the gap, run again, watch it turn green.

    public static readonly List<TestDef> Tests = new List<TestDef>
    {
        new TestDef
        {
            assetName = "TEST_01_HardwareInventory",
            testName = "Power-on self-test and hardware inventory",
            description = "Confirms CPU, installed memory and the storage device are detected and " +
                          "report the expected sizes.",
            duration = 2f
            // No prerequisites: this one reflects the assembly work, and by the time the
            // learner reaches diagnostics the machine has clearly booted.
        },
        new TestDef
        {
            assetName = "TEST_02_DeviceManager",
            testName = "Device Manager - no unknown devices",
            description = "Walks the device tree looking for a yellow warning triangle.",
            duration = 3f,
            prerequisites = new[]
            {
                SystemConfigTaskIds.InstallDriverMotherboard,
                SystemConfigTaskIds.InstallDriverDisplay,
                SystemConfigTaskIds.InstallDriverAudio,
                SystemConfigTaskIds.InstallDriverNetwork
            },
            failureHint = "Device Manager still shows a device with a yellow warning triangle. Open " +
                          "Device Manager and install the driver that is missing."
        },
        new TestDef
        {
            assetName = "TEST_03_Display",
            testName = "Display resolution and refresh rate",
            description = "Checks the panel is running at its native resolution rather than a " +
                          "fallback 1024x768.",
            duration = 2f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverDisplay },
            failureHint = "Stuck on Microsoft Basic Display Adapter. Install the display driver."
        },
        new TestDef
        {
            assetName = "TEST_04_Audio",
            testName = "Audio playback and microphone",
            description = "Plays a test tone through the front and rear jacks and checks the " +
                          "recording device.",
            duration = 2f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverAudio },
            failureHint = "No playback device found. Install the audio driver."
        },
        new TestDef
        {
            assetName = "TEST_05_Network",
            testName = "Network connectivity (ping gateway and DNS)",
            description = "Pings the default gateway, then an outside host by name to prove DNS " +
                          "resolution works too.",
            duration = 3f,
            prerequisites = new[] { SystemConfigTaskIds.InstallDriverNetwork },
            failureHint = "Network adapter not responding. Install the network driver, then check " +
                          "the cable is seated in the NIC and the switch."
        },
        new TestDef
        {
            assetName = "TEST_06_SoftwareAndSecurity",
            testName = "Application and security check",
            description = "Launches each installed application once, and confirms Windows Update " +
                          "and the antivirus report a healthy, up-to-date status.",
            duration = 3f,
            prerequisites = new[]
            {
                SystemConfigTaskIds.InstallAppSoftware,
                SystemConfigTaskIds.InstallUpdates
            },
            failureHint = "Applications or patches are still missing. Finish the Software Center and " +
                          "Windows Update lists, then run diagnostics again."
        }
    };

    // =====================================================================
    // Handy totals for the builder's summary box
    // =====================================================================

    public static int TotalGradedChoices
    {
        get
        {
            int n = 0;
            foreach (var s in Stages)
                if (s.userInput && !string.IsNullOrEmpty(s.correct)) n++;
            return n;
        }
    }

    public static int TotalAssets
    {
        get
        {
            return Stages.Count + Images.Count + Drivers.Count +
                   AppSoftware.Count + Updates.Count + Tests.Count;
        }
    }
}
