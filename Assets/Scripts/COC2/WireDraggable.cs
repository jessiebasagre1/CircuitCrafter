using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Wire equivalent of PCPartItem. Unlike PC parts, wires don't shrink into a hotbar 
    /// they stay visible and move through 3D space while dragged, since the point is
    /// watching them get placed into the correct pin. Requires a WireIdentity on the
    /// same object so the drag controller and slots know this wire's color/end.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(WireIdentity))]
    public class WireDraggable : MonoBehaviour
    {
        public WirePinSlot HomeSlot { get; private set; }

        /// <summary>True once this wire's connector has been successfully crimped
        /// (see CrimpingStationController). A locked wire can no longer be picked
        /// back up, so a finished cable can't be pulled apart by an accidental tap.</summary>
        public bool IsLocked { get; private set; }

        [Header("Attach Scale Animation")]
        [Tooltip("How long, in seconds, the wire takes to grow from its loose-tray scale up to " +
                 "the slot's attachScale once it snaps in. Same idea as PCPartItem.scaleDuration.")]
        public float attachScaleDuration = 0.15f;

        Vector3 homeLocalPosition;
        Quaternion homeLocalRotation;
        Vector3 homeLocalScale;
        Transform homeParent;

        Coroutine scaleRoutine;

        void Awake()
        {
            // Remember where this wire started (its "loose wire tray" position)
            // so it can snap back there if dropped somewhere invalid.
            homeLocalPosition = transform.localPosition;
            homeLocalRotation = transform.localRotation;
            homeLocalScale = transform.localScale;
            homeParent = transform.parent;
        }

        /// <summary>
        /// Entry point for starting a drag. Deliberately NOT wired to OnMouseDown  that's a
        /// legacy callback tied to Input.mousePosition, which goes dead on mobile the moment
        /// anything in the scene sets Input.simulateMouseWithTouches = false (see
        /// Mobile/PointerInput.cs for the full story; COC1 hit this exact bug with
        /// PCPartItem/CableConnector before PartClickDetector fixed it). Instead,
        /// WireDragController raycasts under the pointer itself every frame (mouse OR touch,
        /// via PointerInput) while nothing is being dragged, and calls this directly - same
        /// shape as PartClickDetector.HandleClick() -> PCPartItem.TryBeginDrag().</summary>
        public void TryBeginDrag()
        {
            if (IsLocked) return; // cable's already crimped -- nothing more to rearrange

            // Block dragging entirely (and show the same "no tool yet" warning used
            // when entering the crimping view) if the player hasn't picked up the
            // crimping tool -- there's no reason they should be able to rearrange
            // wires before they even have the tool to crimp them with.
            if (CrimpingViewController.Instance != null && CrimpingViewController.Instance.WarnIfNoTool())
                return;

            if (WireDragController.Instance == null)
            {
                Debug.LogWarning($"{name}: can't drag  no WireDragController found in this scene.", this);
                return;
            }

            // On touch devices the same tap can simultaneously register as a UI press
            // (mobile crimp/exit button, invisible touch-look panel over the crimping
            // view, etc) AND a physics hit on this wire, since a 3D raycast doesn't know
            // about uGUI. Bail if the pointer is over UI so a button tap can't also yank
            // a wire loose. The parameterless IsPointerOverGameObject() only ever checks
            // the mouse pointer - on touch it needs the active finger's ID or it silently
            // reports false while a button is being tapped, same class of bug OnMouseDown had.
            if (EventSystem.current != null)
            {
                bool overUI = Input.touchCount > 0
                    ? EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
                    : EventSystem.current.IsPointerOverGameObject();
                if (overUI) return;
            }

            // If this wire is already seated in a slot, vacate it first.
            if (HomeSlot != null)
            {
                HomeSlot.ClearWire();
                HomeSlot = null;
            }

            WireDragController.Instance.BeginDrag(this);
        }

        /// <summary>Called by WireDragController when the wire is released over a valid, empty, matching-end slot.</summary>
        public void SnapIntoSlot(WirePinSlot slot)
        {
            HomeSlot = slot;
            slot.AssignWire(GetComponent<WireIdentity>());

            transform.SetParent(slot.transform, worldPositionStays: false);
            transform.localPosition = slot.attachPositionOffset;
            transform.localRotation = Quaternion.Euler(slot.attachRotationEulerOffset);

            // Grow from whatever scale the wire currently has (its small loose-tray size)
            // up to the slot's fitted size, so it visibly expands into place on the RJ45
            // instead of just popping to full size.
            Restart(ScaleTo(slot.attachScale));

            // Let the station know a wire just landed  it checks whether both ends are
            // now fully arranged and, if so, auto-validates the order right away.
            CrimpingStationController.Instance?.OnWirePlaced();
        }

        /// <summary>Called once this wire's connector has been successfully crimped, so it
        /// can no longer be picked back up and dragged out of its slot.</summary>
        public void SetLocked(bool locked)
        {
            IsLocked = locked;
        }

        /// <summary>Called by WireDragController when the wire is released somewhere invalid.</summary>
        public void ReturnHome()
        {
            if (scaleRoutine != null)
            {
                StopCoroutine(scaleRoutine);
                scaleRoutine = null;
            }

            transform.SetParent(homeParent, worldPositionStays: false);
            transform.localPosition = homeLocalPosition;
            transform.localRotation = homeLocalRotation;
            transform.localScale = homeLocalScale;
        }

        /// <summary>Sends this wire home even if it's currently seated in a slot  clears
        /// its slot reference first (unlike ReturnHome(), which assumes it's already been
        /// vacated). Used by ConnectorEnd.ReturnAllWiresHome() when an auto-check comes
        /// back wrong. No-ops on a locked (already-crimped) wire.</summary>
        public void ForceReturnHome()
        {
            if (IsLocked) return;

            if (HomeSlot != null)
            {
                HomeSlot.ClearWire();
                HomeSlot = null;
            }

            ReturnHome();
        }

        IEnumerator ScaleTo(Vector3 target)
        {
            Vector3 start = transform.localScale;
            float t = 0f;
            while (t < attachScaleDuration)
            {
                t += Time.deltaTime;
                transform.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / attachScaleDuration));
                yield return null;
            }
            transform.localScale = target;
        }

        void Restart(IEnumerator routine)
        {
            if (scaleRoutine != null) StopCoroutine(scaleRoutine);
            scaleRoutine = StartCoroutine(routine);
        }
    }
}