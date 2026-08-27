// PunchToolIcon.cs
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The punch-down tool: a UI icon the player drags over the 3D stage and releases on a pin that
    /// already has a conductor seated in it.
    ///
    /// THIS IS THE ONE UI PIECE. The pins and conductors are real 3D objects seen through the stage
    /// camera; the tool is a flat icon on a canvas drawn over the top of them. On release it hands
    /// its SCREEN POSITION to the board, which fires a ray from the stage camera to work out which
    /// pin is underneath - that is the whole of the bridge between the two worlds.
    ///
    /// It always returns to its holster afterwards, hit or miss - the tool is a thing you pick up
    /// and put down, not a thing you leave lying on the block.
    ///
    /// Setup:
    ///   1. A UI Image of the tool on your stage canvas. Raycast Target ON.
    ///   2. Put this on it and assign it to PunchStageBoard > Tool.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PunchToolIcon : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Feel")]
        [Tooltip("Scale while held, so it reads as picked up.")]
        public float dragScale = 1.1f;

        [Tooltip("Degrees the tool tilts while held. Purely cosmetic; 0 for none.")]
        public float dragTilt = -12f;

        [Header("Visuals")]
        [Tooltip("Optional. Dimmed when there is nothing ready to strike, so the player can see at " +
                 "a glance that they need to seat a conductor first.")]
        public Image icon;
        public Color readyTint = Color.white;
        public Color notReadyTint = new Color(1f, 1f, 1f, 0.35f);

        public bool IsDragging { get; private set; }

        PunchStageBoard board;
        RectTransform rect;
        Transform homeParent;
        int homeSiblingIndex;
        Vector2 homeAnchored;
        Vector3 homeScale;
        Quaternion homeRotation;
        Vector2 pointerOffset;
        bool lastReady;
        bool captured;

        /// <summary>
        /// Records the holster pose - where this icon sits, at what size, in what order.
        ///
        /// THIS MUST NOT WAIT FOR Awake. The stage canvas starts switched off, so Unity does not run
        /// Awake on anything under it until the player first opens the stage - while the board, which
        /// lives on an active object, resets on ITS Awake and calls ReturnHome() here long before
        /// that. With the pose still unrecorded, ReturnHome() would apply the field defaults: scale
        /// (0,0,0) and anchoredPosition (0,0). A zero scale is invisible, permanently, and looks
        /// exactly like a missing sprite or a broken canvas.
        ///
        /// Transform values are valid whether or not the object is active, so this is safe to call
        /// from anywhere - including Bind(), which the board calls while the canvas is still off.
        /// </summary>
        void Capture()
        {
            if (captured) return;
            if (rect == null) rect = transform as RectTransform;
            if (rect == null) return;

            captured = true;
            homeParent = rect.parent;
            homeSiblingIndex = rect.GetSiblingIndex();
            homeAnchored = rect.anchoredPosition;
            homeScale = rect.localScale;
            homeRotation = rect.localRotation;
        }

        void Awake()
        {
            Capture();
            if (icon == null) icon = GetComponent<Image>();
            lastReady = !lastReady; // force the first RefreshReadyTint to apply
        }

        void OnEnable()
        {
            Capture();
        }

        public void Bind(PunchStageBoard owner)
        {
            board = owner;
            Capture();
        }

        void Update()
        {
            if (board == null || IsDragging) return;
            RefreshReadyTint(board.AnyReadyToPunch);
        }

        void RefreshReadyTint(bool ready)
        {
            if (ready == lastReady) return;
            lastReady = ready;
            if (icon != null) icon.color = ready ? readyTint : notReadyTint;
        }

        // ------------------------------------------------------------------

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (board == null || !board.AcceptsInput) return;

            IsDragging = true;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect, eventData.position, board.UICamera, out pointerOffset);

            if (board.dragLayer != null) rect.SetParent(board.dragLayer, true);
            rect.SetAsLastSibling();
            rect.localScale = homeScale * dragScale;
            if (dragTilt != 0f) rect.localRotation = Quaternion.Euler(0f, 0f, dragTilt);

            if (icon != null) icon.color = readyTint;
            board.SetHint(null);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsDragging) return;

            RectTransform parent = rect.parent as RectTransform;
            if (parent == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, board.UICamera, out Vector2 local))
            {
                rect.anchoredPosition = local - pointerOffset;
            }

            board.HighlightPunchableUnder(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!IsDragging) return;
            IsDragging = false;

            if (board != null) board.HandleToolDropped(eventData.position);

            // Home either way. A strike that landed is finished with the tool; a miss should not
            // leave it parked on the block.
            ReturnHome();
        }

        public void ReturnHome()
        {
            // Capture first. If the holster pose has never been recorded, do nothing at all rather
            // than write the field defaults over it - see the note on Capture().
            Capture();
            if (!captured) return;

            if (homeParent != null)
            {
                rect.SetParent(homeParent, false);
                rect.SetSiblingIndex(homeSiblingIndex);
            }
            rect.anchoredPosition = homeAnchored;
            rect.localScale = homeScale;
            rect.localRotation = homeRotation;
            lastReady = !lastReady;   // re-evaluate the tint next frame
        }
    }
}