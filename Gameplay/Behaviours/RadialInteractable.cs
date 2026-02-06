using HPVR.UI;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppEekEvents.Helper;
using Il2CppEekUI;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using SteamVR_Melon.InteractionSystem;
using UnityEngine;
using Valve.VR;

namespace HPVR.Gameplay.Behaviours
{
    [RegisterTypeInIl2Cpp]
    internal class RadialInteractable : MonoBehaviour
    {

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
            if (interactable is null || interactiveItem is null)
            {
                MelonLogger.Msg("interactable on " + name + " is null!!");
                enabled = false;
                return;
            }

            Interactable? lastInteract = null;
            Vector3 point = Vector3.zero;
            //todo replace by unity input
            //if (hand.handType == SteamVRInputSources.LeftHand)
            //{
            //    lastInteract = Laser.LeftLaser.pointingAt;
            //    point = Laser.LeftLaser.LastHit.point;
            //}
            //else if (hand.handType == SteamVRInputSources.RightHand)
            //{
            //    lastInteract = Laser.RightLaser.pointingAt;
            //    point = Laser.RightLaser.LastHit.point;
            //}
            ////MelonLogger.Msg(hand.name + " hovering over " + gameObject.name);

            //if (lastInteract?.name == name && hand.uiInteractAction != null && hand.uiInteractAction.stateUp)
            //{
            //    ToggleRadial(point, interactiveItem);

            //    //MelonLogger.Msg("toggled radial on");
            //}
        }

        public static void ToggleRadial(Vector3 point, InteractiveItem item)
        {
            MelonLogger.Msg("toggling radial on for " + item.name);
            InteractionManager.Singleton.CurrentFocusedItem = item;
            InteractiveItem.ActiveItem = item;
            RadialMenu.Singleton._lastInteractedItem = item;
            //if its already showing toggle twice to turn off and on again
            if (RadialMenu.Singleton.IsShowing)
            {
                RadialMenu.Singleton.Toggle();
            }
            RadialMenu.Singleton.Toggle();

            //only done once
            radialCanvas ??= GameObject.Find("RadialMenuCanvas").GetComponent<Canvas>();

            if (radialCanvas is null)
            {
                MelonLogger.Msg("didnt find radialcanvas");
                return;
            }

            Transform camera = SteamVRCamera.instance.transform;
            if (point != Vector3.zero)
            {
                radialCanvas.transform.position = point + camera.rotation * Vector3.forward * -0.3f;
            }
            else
            {
                radialCanvas.transform.position = camera.position + camera.rotation * Vector3.forward * 1.45f;
            }
            //invert distance else it shows flipped
            radialCanvas.transform.rotation = camera.rotation;
            RadialMenu.Singleton.transform.FindDeepChild("Target")?.gameObject?.SetActive(false);
            RadialMenu.Singleton.transform.FindDeepChild("Line")?.gameObject?.SetActive(false);
            return;
        }
    }
}
