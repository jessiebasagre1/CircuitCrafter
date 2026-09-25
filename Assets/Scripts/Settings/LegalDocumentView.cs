using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Settings
{
    /// <summary>
    /// The Privacy Policy &amp; Terms and Conditions popup, opened from the "Privacy &amp;
    /// Terms" button pinned to the bottom-left of the Settings screen's sidebar.
    ///
    /// ATTACH THIS TO: the legal popup panel. CircuitCrafter &gt; Build Settings Panel builds
    /// and wires it automatically alongside the rest of the Settings screen.
    ///
    /// CONTENT LIVES IN THIS FILE, NOT ON THE SCENE OBJECT. The same reasoning as
    /// SettingsHelp: wording that changes rarely and needs one place to edit belongs in a
    /// string constant, not a TMP_Text sitting in a scene someone has to open Unity to
    /// touch. Edit PrivacyPolicyText / TermsText below and every copy of the panel picks it
    /// up on the next build - no re-wiring needed.
    ///
    /// THIS IS TEMPLATE TEXT, NOT LEGAL ADVICE. I'm not a lawyer, and a training tool used
    /// by students - especially one with an online AI Tutor feature that sends typed
    /// questions to a third-party API - has real requirements: the Data Privacy Act of 2012
    /// if this is deployed in the Philippines, and whatever your school or division's own
    /// policy requires for software used with minors. Have someone qualified (your school's
    /// data privacy officer, or a legal adviser) review and sign off on the wording below
    /// before treating it as final - especially the parts about the AI Tutor and about who
    /// to contact with questions.
    /// </summary>
    public class LegalDocumentView : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private TMP_Text titleText;

        [Header("Body")]
        [Tooltip("The whole Privacy Policy + Terms and Conditions text, as one rich-text block. Needs Rich Text and Word Wrap both ON.")]
        [SerializeField] private TMP_Text bodyText;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;

        // Set at the very start of Open(), before SetActive(true). Same Start/UnityEvent
        // trap this project has already hit and fixed five times over (PCPowerStation,
        // SiteObservationPanel, TicketDiagnosisPanel, RepairVerificationPanel,
        // SettingsPanel): if this object starts inactive in the built scene, Awake does not
        // run until Open() activates it for the FIRST time - and blindly hiding in Awake
        // would undo that very activation, forcing a second click before anything shows.
        private bool openRequested;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            if (titleText != null) titleText.text = "Privacy Policy & Terms and Conditions";
            if (bodyText != null) bodyText.text = BuildBody();

            if (gameObject.activeSelf && !openRequested) gameObject.SetActive(false);
        }

        public void Open()
        {
            openRequested = true;
            gameObject.SetActive(true);
        }

        public void Close() => gameObject.SetActive(false);

        // ==================================================================
        // Content - edit the two blocks below, not the scene
        // ==================================================================

        private const string LastUpdated = "09/25/26";

        private static string BuildBody()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<color=#8FA6C9><i>Last updated: {LastUpdated}</i></color>");
            sb.AppendLine();
            sb.AppendLine("<b><size=120%>Privacy Policy</size></b>");
            sb.AppendLine(PrivacyPolicyText);
            sb.AppendLine();
            sb.AppendLine("<b><size=120%>Terms and Conditions</size></b>");
            sb.AppendLine(TermsText);
            return sb.ToString();
        }

        private const string PrivacyPolicyText =
@"CircuitCrafter is a training simulation for Computer Systems Servicing NC II. This
policy explains what information the app keeps and why.

<b>What is stored, and where.</b> Your learner profile (the name/section you enter),
your task progress, and your assessment results are saved as files on THIS DEVICE
only - not on a remote server, and not shared with other students on a shared
computer, since each profile keeps its own save file. Nothing here is uploaded
anywhere unless your teacher or school specifically collects it (for example, to
grade your work).

<b>The AI Tutor is the one exception.</b> If you use the AI Tutor chat feature and are
connected to the internet, whatever you type is sent to a third-party AI service
(Google's Gemini) to generate a reply. Please do not type personal information -
your full name, address, contact details, or anyone else's - into the chat.
Everything else in the app works fully offline and sends nothing anywhere.

<b>No accounts, no ads, no selling data.</b> CircuitCrafter does not require you to
create an online account, does not show advertising, and does not sell or share
your information with advertisers or other third parties.

<b>Deleting your data.</b> The Reset Progress option in the main menu permanently
deletes your saved progress and history on this device. If you need your data
removed for another reason, ask your instructor or school - they can help locate
and delete the save files, since this app has no remote account to close.

<b>Questions.</b> If you have questions about this policy, contact your instructor or
your school's data privacy officer.";

        private const string TermsText =
@"By using CircuitCrafter, you agree to the following:

<b>Purpose.</b> This app is an educational training simulation intended to help
students practice Computer Systems Servicing NC II procedures. It is a practice
tool, not an official record - completing modules or assessments here does not by
itself grant TESDA certification. Official certification still requires TESDA's own
assessment process.

<b>Acceptable use.</b> Use this app for its intended educational purpose. Do not
attempt to tamper with, exploit, or circumvent the assessment or scoring systems in
a way that misrepresents your actual competency - the whole point of the practice
and assessment modes is to give you (and your instructor) an honest picture of what
you can do.

<b>No warranty.</b> This app is provided as a learning tool without guarantee that it
is free of errors or bugs. If something breaks or behaves oddly, please report it to
your instructor so it can be fixed.

<b>Changes.</b> These terms and the Privacy Policy above may be updated from time to
time as the app changes. Check the ""Last updated"" date at the top of this screen.

<b>Contact.</b> Questions about these terms should be directed to your instructor or
school.";
    }
}
