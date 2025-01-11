using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using Valve.VR.InteractionSystem;

namespace HPVR.Components
{
    [RegisterTypeInIl2Cpp]
    internal class AutoInteractable : MonoBehaviour
    {
        public AutoInteractable(IntPtr value) : base(value) { }

        public AutoInteractable() : base(ClassInjector.DerivedConstructorPointer<AutoInteractable>()) => ClassInjector.DerivedConstructorBody(this);

        private string generalText = string.Empty;
        private string hoveringText = string.Empty;
        private Vector3 oldPosition;
        private Quaternion oldRotation;

        private float attachTime;

        private readonly Hand.AttachmentFlags attachmentFlags = Hand.defaultAttachmentFlags & (~Hand.AttachmentFlags.SnapOnAttach) & (~Hand.AttachmentFlags.DetachOthers) & (~Hand.AttachmentFlags.VelocityMovement);

        private Interactable? interactable = null;

        //-------------------------------------------------
        void Awake()
        {
            GeneralText = "No Hand Hovering";
            HoveringText = "Hovering: False";

            interactable = this.GetComponent<Interactable>();
        }

        //-------------------------------------------------
        // Called when a Hand starts hovering over this object
        //-------------------------------------------------
        private void OnHandHoverBegin(Hand hand)
        {
            GeneralText = "Hovering hand: " + hand.name;
        }

        //-------------------------------------------------
        // Called when a Hand stops hovering over this object
        //-------------------------------------------------
        protected void OnHandHoverEnd(Hand hand)
        {
            GeneralText = "No Hand Hovering";
        }

        //-------------------------------------------------
        // Called every Update() while a Hand is hovering over this object
        //-------------------------------------------------
        protected void HandHoverUpdate(Hand hand)
        {
            if (interactable is null)
            { return; }
            GrabTypes startingGrabType = hand.GetGrabStarting();
            bool isGrabEnding = hand.IsGrabEnding(this.gameObject);

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
        }

        //-------------------------------------------------
        // Called when this GameObject becomes attached to the hand
        //-------------------------------------------------
        protected void OnAttachedToHand(Hand hand)
        {
            GeneralText = string.Format("Attached: {0}", hand.name);
            attachTime = Time.time;
        }

        //-------------------------------------------------
        // Called when this GameObject is detached from the hand
        //-------------------------------------------------
        protected void OnDetachedFromHand(Hand hand)
        {
            GeneralText = string.Format("Detached: {0}", hand.name);
        }

        //-------------------------------------------------
        // Called every Update() while this GameObject is attached to the hand
        //-------------------------------------------------
        protected void HandAttachedUpdate(Hand hand)
        {
            GeneralText = string.Format("Attached: {0} :: Time: {1:F2}", hand.name, (Time.time - attachTime));
        }

        private bool lastHovering = false;

        public string GeneralText { get => generalText; set { generalText = value; MelonLogger.Msg(value); } }
        public string HoveringText { get => hoveringText; set { hoveringText = value; MelonLogger.Msg(value); } }

        private void Update()
        {
            if (interactable is null)
            { return; }
            if (interactable.isHovering != lastHovering) //save on the .tostrings a bit
            {
                HoveringText = string.Format("Hovering: {0}", interactable.isHovering);
                lastHovering = interactable.isHovering;
            }
        }

        //-------------------------------------------------
        // Called when this attached GameObject becomes the primary attached object
        //-------------------------------------------------
        protected void OnHandFocusAcquired(Hand hand)
        {
        }

        //-------------------------------------------------
        // Called when another attached GameObject becomes the primary attached object
        //-------------------------------------------------
        protected void OnHandFocusLost(Hand hand)
        {
        }
    }
}
