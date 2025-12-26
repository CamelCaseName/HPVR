using HPVR.UI;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekEvents.Helper;
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

        private static Canvas? radialCanvas;
        private Interactable? interactable = null;
        private InteractiveItem? interactiveItem = null;

        //-------------------------------------------------
        protected virtual void Awake()
        {
            interactable = GetComponent<Interactable>();
            interactiveItem = GetComponent<InteractiveItem>();
            interactable.HandHoverUpdate += HandHoverUpdate;
            interactable.OnHandHoverBegin += OnHandHoverBegin;
            interactable.OnHandHoverEnd += OnHandHoverEnd;
        }

        //-------------------------------------------------
        // Called when a Hand starts hovering over this object
        //-------------------------------------------------
        private void OnHandHoverBegin(Hand hand, Vector2 pos, bool posIsValid)
        {
            if (!RadialMenu.Singleton.IsShowing)
            {
                //MelonLogger.Msg("radial not visible, updating item:");
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
                enabled = false;
                return;
            }

            Interactable? lastInteract = null;
            if (hand.handType == SteamVR_Input_Sources.LeftHand)
            {
                lastInteract = Laser.LeftLaser.pointingAt;
            }
            else if (hand.handType == SteamVR_Input_Sources.RightHand)
            {
                lastInteract = Laser.RightLaser.pointingAt;
            }
            //MelonLogger.Msg(hand.name + " hovering over " + gameObject.name);

            if (lastInteract?.name == this.name && hand.uiInteractAction != null && hand.uiInteractAction.stateUp)
            {
                MelonLogger.Msg("toggling radial on for " + gameObject.name);
                InteractionManager.Singleton.CurrentFocusedItem = interactiveItem;
                InteractiveItem.ActiveItem = interactiveItem;
                RadialMenu.Singleton._lastInteractedItem = interactiveItem;
                //if its already showing toggle twice to turn off and on again
                if (RadialMenu.Singleton.IsShowing)
                {
                    RadialMenu.Singleton.Toggle();
                }
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
                RadialMenu.Singleton.transform.FindDeepChild("Target")?.gameObject?.SetActive(false);
                RadialMenu.Singleton.transform.FindDeepChild("Line")?.gameObject?.SetActive(false);

                //MelonLogger.Msg("toggled radial on");
            }
        }
    }
}
