using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives the OS install screen five different LOOKS across its 17 stages, without touching
/// OSInstallManager or OSInstallView.
///
/// ATTACH THIS TO: the install panel's parent, or any always-active object in the COC 1 scene.
///
/// WHY FIVE LOOKS AND NOT SEVENTEEN PANELS
/// A real Windows 10 install genuinely only shows you five kinds of screen: the firmware setup
/// utility, a text-mode POST screen, the grey Setup wizard window, the full-screen "Installing
/// Windows" progress screen, and the blue out-of-box experience at the end. Seventeen separate
/// panels would be sixteen more sets of references to keep in sync for no extra fidelity.
///
/// HOW IT COEXISTS WITH OSInstallView
/// Both subscribe to OSInstallManager.OnStageChanged, and they touch different things:
///   OSInstallView   decides WHAT is visible - which controls, what text, dropdown options.
///   OSInstallSkinner decides WHERE it sits and WHAT IT LOOKS LIKE - backdrop, position, colour.
/// Nothing is written by both, so the order they happen to run in does not matter.
///
/// The controls themselves are shared: there is one stage label, one dropdown, one Continue
/// button and one progress bar in the whole panel, and this moves them to the right place for
/// the current skin. Same approach as the browser panel in the Rufus sequence.
/// </summary>
public class OSInstallSkinner : MonoBehaviour
{
    [Serializable]
    public class Skin
    {
        [Tooltip("For your own reference in the Inspector - not used by anything.")]
        public string name = "Skin";

        [Tooltip("The backdrop and chrome for this look. Exactly one skin root is active at a time.")]
        public GameObject root;

        [Tooltip("Which install stages use this look.")]
        public OSInstallManager.InstallStage[] stages;

        [Header("Where the shared controls sit - normalised 0-1 from the TOP-LEFT")]
        public Rect labelRect = new Rect(0.1f, 0.2f, 0.8f, 0.4f);
        public Rect dropdownRect = new Rect(0.3f, 0.65f, 0.4f, 0.05f);
        public Rect continueRect = new Rect(0.62f, 0.75f, 0.12f, 0.05f);
        public Rect progressRect = new Rect(0.3f, 0.7f, 0.4f, 0.02f);

        [Header("How the stage text is drawn on this look")]
        public Color labelColor = Color.white;
        public float labelFontSize = 22f;
        public TextAlignmentOptions labelAlign = TextAlignmentOptions.TopLeft;
    }

    [Tooltip("The five looks. Built by Tools > COC 1 > Panel Builder.")]
    [SerializeField] private List<Skin> skins = new List<Skin>();

    [Header("The shared controls - the same objects OSInstallView drives")]
    [SerializeField] private TMP_Text stageLabel;
    [SerializeField] private TMP_Dropdown choiceDropdown;
    [SerializeField] private Button continueButton;
    [SerializeField] private Slider progressBar;

    [Tooltip("Parent of the skin roots. Only used to switch off skins that are not listed above.")]
    [SerializeField] private Transform skinsRoot;

    private void Start()
    {
        // Start(), not OnEnable(): OSInstallManager assigns its Instance in Awake, and
        // subscribing earlier would silently bind to nothing. Same reasoning OSInstallView
        // already documents for itself.
        if (OSInstallManager.Instance == null)
        {
            Debug.LogWarning("[OSInstallSkinner] OSInstallManager.Instance is null in Start(). " +
                             "Is the manager in this scene and enabled?", this);
            return;
        }

        OSInstallManager.Instance.OnStageChanged += HandleStageChanged;
        HideAllSkins();
    }

    private void OnDestroy()
    {
        if (OSInstallManager.Instance != null)
            OSInstallManager.Instance.OnStageChanged -= HandleStageChanged;
    }

    // ==================================================================

    private void HandleStageChanged(OSInstallStageData stageData)
    {
        if (stageData == null) return;

        var skin = FindSkin(stageData.stage);

        if (skin == null)
        {
            // A stage with no skin would show the previous one, which reads as the install
            // being stuck. Say which stage is unmapped instead.
            Debug.LogWarning($"[OSInstallSkinner] No skin covers stage '{stageData.stage}'. " +
                             "Add it to one of the skins' Stages arrays.", this);
            return;
        }

        HideAllSkins();
        if (skin.root != null) skin.root.SetActive(true);

        Apply(skin);
    }

    private Skin FindSkin(OSInstallManager.InstallStage stage)
    {
        foreach (var skin in skins)
        {
            if (skin == null || skin.stages == null) continue;

            foreach (var s in skin.stages)
                if (s == stage) return skin;
        }
        return null;
    }

    private void Apply(Skin skin)
    {
        if (stageLabel != null)
        {
            Place(stageLabel.rectTransform, skin.labelRect);
            stageLabel.color = skin.labelColor;
            stageLabel.fontSize = skin.labelFontSize;
            stageLabel.alignment = skin.labelAlign;
        }

        // Position only - whether each of these is VISIBLE is OSInstallView's business, decided
        // from the stage data. Setting active state here as well would mean two scripts fighting
        // over the same flag.
        if (choiceDropdown != null) Place(choiceDropdown.transform as RectTransform, skin.dropdownRect);
        if (continueButton != null) Place(continueButton.transform as RectTransform, skin.continueRect);
        if (progressBar != null) Place(progressBar.transform as RectTransform, skin.progressRect);
    }

    private void HideAllSkins()
    {
        foreach (var skin in skins)
            if (skin != null && skin.root != null) skin.root.SetActive(false);

        // Catch any skin root that was added to the hierarchy but never listed above, so a
        // half-finished edit does not leave two backdrops stacked on each other.
        if (skinsRoot != null)
        {
            for (int i = 0; i < skinsRoot.childCount; i++)
            {
                var child = skinsRoot.GetChild(i).gameObject;
                if (!IsListed(child)) child.SetActive(false);
            }
        }
    }

    private bool IsListed(GameObject go)
    {
        foreach (var skin in skins)
            if (skin != null && skin.root == go) return true;
        return false;
    }

    /// <summary>
    /// Normalised top-left Rect to anchors. Unity's anchors run from the BOTTOM-left with y
    /// going up; these rects are written top-left with y going down, because that is how
    /// anyone reads a position off a screen. The 1f - (y + height) is the whole conversion.
    /// </summary>
    private static void Place(RectTransform rt, Rect n)
    {
        if (rt == null) return;

        rt.anchorMin = new Vector2(n.x, 1f - (n.y + n.height));
        rt.anchorMax = new Vector2(n.x + n.width, 1f - n.y);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
