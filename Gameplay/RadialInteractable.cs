using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekUI;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using Valve.VR.InteractionSystem;

namespace HPVR.Gameplay
{
    [RegisterTypeInIl2Cpp]
    internal class RadialInteractable : MonoBehaviour
    {
        public RadialInteractable(IntPtr value) : base(value) { }

        public RadialInteractable() : base(ClassInjector.DerivedConstructorPointer<RadialInteractable>()) => ClassInjector.DerivedConstructorBody(this);

        private string generalText = string.Empty;
        private string hoveringText = string.Empty;
        private Vector3 oldPosition;
        private Quaternion oldRotation;

        private float attachTime;

        private readonly Hand.AttachmentFlags attachmentFlags = Hand.defaultAttachmentFlags & ~Hand.AttachmentFlags.SnapOnAttach & ~Hand.AttachmentFlags.DetachOthers & ~Hand.AttachmentFlags.VelocityMovement;

        private Interactable? interactable = null;
        private InteractiveItem? interactiveItem = null;

        //-------------------------------------------------
        protected virtual void Awake()
        {
            GeneralText = gameObject.name + " No Hand Hovering";
            HoveringText = gameObject.name + " Hovering: False";

            interactable = GetComponent<Interactable>();
            interactiveItem = GetComponent<InteractiveItem>();
            interactable.HandHoverUpdate += HandHoverUpdate;
            interactable.OnAttachedToHand += OnAttachedToHand;
            interactable.OnDetachedFromHand += OnDetachedFromHand;
            interactable.OnHandHoverBegin += OnHandHoverBegin;
            interactable.OnHandHoverEnd += OnHandHoverEnd;
            interactable.HandAttachedUpdate += HandAttachedUpdate;
        }

        //todo debug this behaviour

        //-------------------------------------------------
        // Called when a Hand starts hovering over this object
        //-------------------------------------------------
        private void OnHandHoverBegin(Hand hand, Vector2 pos, bool posIsValid)
        {
            GeneralText = gameObject.name + " Hovering hand: " + hand.name;
            InteractionManager.Singleton._focusedItemInteraction = interactiveItem;
            InteractiveItem.ActiveItem = interactiveItem;
        }

        //-------------------------------------------------
        // Called when a Hand stops hovering over this object
        //-------------------------------------------------
        private void OnHandHoverEnd(Hand hand)
        {
            GeneralText = gameObject.name + " No Hand Hovering";
            InteractionManager.Singleton._focusedItemInteraction = null;
            InteractiveItem.ActiveItem = null;
        }

        //-------------------------------------------------
        // Called every Update() while a Hand is hovering over this object
        //-------------------------------------------------
        private void HandHoverUpdate(Hand hand, Vector2 pos, bool posIsValid)
        {
            if (interactable is null)
            {
                MelonLogger.Msg("interactable on " + name + " is null!!");
                return;
            }
            GrabTypes startingGrabType = hand.GetGrabStarting();
            bool isGrabEnding = hand.IsGrabEnding(gameObject);

            if (interactable.attachedToHand == null && startingGrabType != GrabTypes.None)
            {
                // Save our position/rotation so that we can restore it when we detach
                oldPosition = transform.position;
                oldRotation = transform.rotation;

                // Call this to continue receiving HandHoverUpdate messages,
                // and prevent the hand from hovering over anything else
                hand.HoverLock(interactable);

                // Attach this object to the hand
                hand.AttachObject(gameObject, startingGrabType, attachmentFlags);
            }
            else if (isGrabEnding)
            {
                // Detach this object from the hand
                hand.DetachObject(gameObject);

                // Call this to undo HoverLock
                hand.HoverUnlock(interactable);

                // Restore position/rotation
                transform.position = oldPosition;
                transform.rotation = oldRotation;
            }

            MelonLogger.Msg(hand.name + " hovering over " + gameObject.name);

            //toggles correctly for items, but doesnt for characters. opens the last item then.
            if (hand.uiInteractAction != null && hand.uiInteractAction.GetStateUp(hand.handType))
            {
                //we get here correctly, but nothing happens. either unityexplorers fault or we need to just hook the internal bit where the action resides and call it ourselves...
                //it is unityexplorers fault because of its own input system
                MelonLogger.Msg("toggling radial for " + gameObject.name);
                RadialMenu.Singleton.Toggle();
            }
        }

        //-------------------------------------------------
        // Called when this GameObject becomes attached to the hand
        //-------------------------------------------------
        private void OnAttachedToHand(Hand hand)
        {
            GeneralText = string.Format("Attached: {0}", hand.name);
            attachTime = Time.time;
        }

        //-------------------------------------------------
        // Called when this GameObject is detached from the hand
        //-------------------------------------------------
        private void OnDetachedFromHand(Hand hand)
        {
            GeneralText = string.Format("Detached: {0}", hand.name);
        }

        //-------------------------------------------------
        // Called every Update() while this GameObject is attached to the hand
        //-------------------------------------------------
        private void HandAttachedUpdate(Hand hand)
        {
            GeneralText = string.Format("Attached: {0} :: Time: {1:F2}", hand.name, Time.time - attachTime);
        }

        private bool lastHovering = false;

        public string GeneralText { get => generalText; set { generalText = value; MelonLogger.Msg(value); } }
        public string HoveringText { get => hoveringText; set { hoveringText = value; MelonLogger.Msg(value); } }

        protected virtual void Update()
        {
            if (interactable is null)
            { return; }
            if (interactable.isHovering != lastHovering) //save on the .tostrings a bit
            {
                HoveringText = string.Format("Hovering: {0}", interactable.isHovering);
                lastHovering = interactable.isHovering;
            }
        }
    }
}
