using HPVR.UI;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekUI;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using Valve.VR;
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
        private Vector3 speed = Vector3.zero;
        private Vector3 oldPos = Vector3.zero;
        private Vector3 oldRot = Vector3.zero;
        private Vector3 angularSpeed = Vector3.zero;
        private static Canvas? radialCanvas;

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

        //-------------------------------------------------
        // Called when a Hand starts hovering over this object
        //-------------------------------------------------
        private void OnHandHoverBegin(Hand hand, Vector2 pos, bool posIsValid)
        {
            GeneralText = gameObject.name + " Hovering hand: " + hand.name;
            if (gameObject.GetComponent<NonPlayerCharacter>())
            {
                //is npc
                MelonLogger.Msg("focused NPC");
            }
            if (!RadialMenu.Singleton.IsShowing)
            {
                MelonLogger.Msg("radial not visible, updating item:");
                InteractionManager.Singleton.CurrentFocusedItem = interactiveItem;
                InteractiveItem.ActiveItem = interactiveItem;
                RadialMenu.Singleton._lastInteractedItem = interactiveItem;
            }
        }

        //-------------------------------------------------
        // Called when a Hand stops hovering over this object
        //-------------------------------------------------
        private void OnHandHoverEnd(Hand hand)
        {
            GeneralText = gameObject.name + " No Hand Hovering";
            if (!RadialMenu.Singleton.IsShowing)
            {
                InteractionManager.Singleton.CurrentFocusedItem = null;
                InteractiveItem.ActiveItem = null;
                RadialMenu.Singleton._lastInteractedItem = null;
            }
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

            //&& !interactable.CompareTag("Door")
            if (interactable.attachedToHand == null && startingGrabType != GrabTypes.None)
            {
                // Save our position/rotation so that we can restore it when we detach
                // no we can just take them and not return 
                //oldPosition = transform.position;
                //oldRotation = transform.rotation;

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
                // no
                //transform.position = oldPosition;
                //transform.rotation = oldRotation;
                MelonLogger.Msg($"let go of {gameObject.name} with speed: {speed.x} {speed.y} {speed.z}");
                gameObject.GetComponent<Rigidbody>().velocity = speed;
                gameObject.GetComponent<Rigidbody>().angularVelocity = angularSpeed;
            }

            //MelonLogger.Msg(hand.name + " hovering over " + gameObject.name);

            //toggles correctly for items, but doesnt for characters. opens the last item then.
            if (hand.uiInteractAction != null && (hand.uiInteractAction.stateUp || hand.otherHand.uiInteractAction.stateUp))
            {
                MelonLogger.Msg("toggling radial for " + gameObject.name);
                InteractionManager.Singleton.CurrentFocusedItem = interactiveItem;
                InteractiveItem.ActiveItem = interactiveItem;
                RadialMenu.Singleton._lastInteractedItem = interactiveItem;
                RadialMenu.Singleton.Toggle();
                radialCanvas ??= GameObject.Find("RadialMenuCanvas").GetComponent<Canvas>();

                if (radialCanvas is null)
                {
                    MelonLogger.Msg("didnt find radialcanvas");
                    return;
                }

                Transform camera = SteamVR_Camera.instance.transform;
                if (Laser.LastHit.point != Vector3.zero)
                {
                    radialCanvas.transform.position = Laser.LastHit.point + (camera.rotation * Vector3.forward * -0.3f);
                }
                else
                {
                    radialCanvas.transform.position = camera.position + (camera.rotation * Vector3.forward * 1.45f);
                }
                //invert distance else it shows flipped
                radialCanvas.transform.rotation = camera.rotation;
                RadialMenu.Singleton.transform.FindChild("Target")?.gameObject?.SetActive(false);
                RadialMenu.Singleton.transform.FindChild("Line")?.gameObject?.SetActive(false);
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
            speed = (gameObject.transform.position - oldPos);
            angularSpeed = (gameObject.transform.rotation.eulerAngles - oldRot);
            oldPos = gameObject.transform.position;
            oldRot = gameObject.transform.rotation.eulerAngles;
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
