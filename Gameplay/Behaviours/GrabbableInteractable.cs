using HPVR.UI;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace HPVR.Gameplay.Behaviours
{
    [RegisterTypeInIl2Cpp]
    internal class GrabbableInteractable : MonoBehaviour
    {
        public GrabbableInteractable(IntPtr value) : base(value) { }

        public GrabbableInteractable() : base(ClassInjector.DerivedConstructorPointer<GrabbableInteractable>()) => ClassInjector.DerivedConstructorBody(this);

        private Vector3 speed = Vector3.zero;
        private Vector3 oldPos = Vector3.zero;
        private Vector3 oldRot = Vector3.zero;
        private Vector3 angularSpeed = Vector3.zero;

        private readonly Hand.AttachmentFlags attachmentFlags = Hand.defaultAttachmentFlags & ~Hand.AttachmentFlags.SnapOnAttach & ~Hand.AttachmentFlags.DetachOthers & ~Hand.AttachmentFlags.VelocityMovement;

        private Interactable? interactable = null;

        //-------------------------------------------------
        protected virtual void Awake()
        {
            interactable = GetComponent<Interactable>();
            interactable.HandHoverUpdate += HandHoverUpdate;
            interactable.OnAttachedToHand += OnAttachedToHand;
            interactable.OnDetachedFromHand += OnDetachedFromHand;
            interactable.HandAttachedUpdate += HandAttachedUpdate;
        }

        //-------------------------------------------------
        // Called every Update() while a Hand is hovering over this object
        //-------------------------------------------------
        private void HandHoverUpdate(Hand hand, Vector2 pos, bool posIsValid)
        {
            //we now get here
            //MelonLogger.Msg("grabbable " + name + " update");
            if (interactable is null)
            {
                MelonLogger.Msg("interactable on " + name + " is null!!");
                enabled = false;
                return;
            }

            GrabTypes startingGrabType = hand.GetGrabStarting();

            Interactable? laserPointingAt = null;
            float distance = Laser.LastHit.distance;
            if (hand.handType == SteamVR_Input_Sources.LeftHand)
            {
                laserPointingAt = Laser.LeftLaser.pointingAt;
            }
            else if (hand.handType == SteamVR_Input_Sources.RightHand)
            {
                laserPointingAt = Laser.RightLaser.pointingAt;
            }

            //todo move this part to its own grabbable/throwable Interactive item component.
            if (interactable.attachedToHand == null && startingGrabType != GrabTypes.None)
            {
                //MelonLogger.Msg("checking if allowed to attach");
                // only attach if not chosen via the laser, or at a very small distance
                //MelonLogger.Msg($"inter: {laserPointingAt?.name} - {name}");
                if (laserPointingAt?.name != name || (distance != 0 && distance < 0.3f))
                {
                    //MelonLogger.Msg("yes");
                    // Call this to continue receiving HandHoverUpdate messages,
                    // and prevent the hand from hovering over anything else
                    hand.HoverLock(interactable);

                    // Attach this object to the hand
                    hand.AttachObject(gameObject, startingGrabType, attachmentFlags);
                }
                else
                {
                    //MelonLogger.Msg("no");
                }
            }
            else
            {
                TryEndGrab(hand);
            }
        }

        //-------------------------------------------------
        // Called when this GameObject becomes attached to the hand
        //-------------------------------------------------
        private void OnAttachedToHand(Hand hand)
        {
            MelonLogger.Msg($"Attached: {hand.name}");
        }

        //-------------------------------------------------
        // Called when this GameObject is detached from the hand
        //-------------------------------------------------
        private void OnDetachedFromHand(Hand hand)
        {
            MelonLogger.Msg($"Detached: {hand.name}");
        }

        //-------------------------------------------------
        // Called every Update() while this GameObject is attached to the hand
        //-------------------------------------------------
        private void HandAttachedUpdate(Hand hand)
        {
            speed = gameObject.transform.position - oldPos;
            angularSpeed = gameObject.transform.rotation.eulerAngles - oldRot;
            oldPos = gameObject.transform.position;
            oldRot = gameObject.transform.rotation.eulerAngles;

            TryEndGrab(hand);
        }

        private void TryEndGrab(Hand hand)
        {
            bool isGrabEnding = hand.IsGrabEnding(gameObject);
            if (isGrabEnding)
            {
                // Detach this object from the hand
                hand.DetachObject(gameObject);

                // Call this to undo HoverLock
                hand.HoverUnlock(interactable);

                //add speed and rotation when letting to to keep physics
                //MelonLogger.Msg($"let go of {gameObject.name} with speed: {speed.x} {speed.y} {speed.z}");
                gameObject.GetComponent<Rigidbody>().velocity = speed / Time.deltaTime;
                gameObject.GetComponent<Rigidbody>().angularVelocity = angularSpeed / Time.deltaTime;
            }
        }

        protected virtual void Update()
        {
            if (interactable is null)
            { return; }
        }
    }
}
