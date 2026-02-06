using HPVR.Gameplay.Behaviours;
using Il2Cpp;
using SteamVR_Melon.InteractionSystem;
using UnityEngine;
using UnityEngine.UI;

namespace HPVR.Gameplay.Patches
{
#if !VR_DISABLED
    //[HarmonyPatch(typeof(InventoryUI), nameof(InventoryUI.PopulateList))]
#endif
    internal class InventoryUIListPatch
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via reflection")]
        private static void Postfix(ref RectTransform container)
        {
            var childs = container.GetComponentsInChildren<Image>();
            foreach (var child in childs)
            {
                //MelonLogger.Msg($"{child.name} + {child.transform.position.x} + {child.transform.position.x}");
                child.transform.localEulerAngles = Vector3.zero;
            }

            if (container.parent?.parent?.parent?.parent?.parent?.name == "InventoryCanvas")
            {
                foreach (var act in container.GetComponentsInChildren<InventoryGamepadAction>())
                {
                    var maybe = act.GetComponent<InventoryInteractable>();
                    if (maybe == null)
                    {
                        act.gameObject.AddComponent<InventoryInteractable>();
                        var ui = act.gameObject.GetComponent<UIElement>();
                        if (ui is not null)
                        {
                            ui.enabled = false;
                        }
                    }
                }
            }
        }
    }
}
