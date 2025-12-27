using HPVR.UI;
using Il2CppEekCharacterEngine.Interaction;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace HPVR.Gameplay.Behaviours
{
    [RegisterTypeInIl2Cpp]
    internal class InventoryInteractable : MonoBehaviour
    {
        public InventoryInteractable(IntPtr value) : base(value) { }

        public InventoryInteractable() : base(ClassInjector.DerivedConstructorPointer<InventoryInteractable>()) => ClassInjector.DerivedConstructorBody(this);

        private Interactable? interactable = null;

        //-------------------------------------------------
        protected virtual void Awake()
        {
            //we can get the item name from the transform name and get the item object with that
            interactable = GetComponent<Interactable>();
            interactable.HandHoverUpdate += HandHoverUpdate;
            interactable.OnHandHoverBegin += OnHandHoverBegin;
            interactable.OnHandHoverEnd += OnHandHoverEnd;
        }

        //-------------------------------------------------
        // Called when a Hand starts hovering over this object
        //-------------------------------------------------
        private void OnHandHoverBegin(Hand hand, Vector2 pos, bool posIsValid)
        {
            InputModule.Instance.HoverBegin(gameObject, pos, posIsValid);
        }

        //-------------------------------------------------
        // Called when a Hand stops hovering over this object
        //-------------------------------------------------
        private void OnHandHoverEnd(Hand hand)
        {
            InputModule.Instance.HoverEnd(gameObject);
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

            if (hand.uiInteractAction != null && hand.uiInteractAction.stateUp)
            {
                Vector3 point = Vector3.zero;
                if (hand.handType == SteamVRInputSources.LeftHand)
                {
                    point = Laser.LeftLaser.LastHit.point;
                }
                else if (hand.handType == SteamVRInputSources.RightHand)
                {
                    point = Laser.RightLaser.LastHit.point;
                }

                RadialInteractable.ToggleRadial(point, ItemManager.GetItemByGameObjectName(gameObject.name));
            }
        }
    }
}
