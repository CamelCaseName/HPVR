using HarmonyLib;
using HPVR.Gameplay.Behaviours;
using Il2Cpp;
using UnityEngine;
using UnityEngine.UI;
using Valve.VR.InteractionSystem;

namespace HPVR.Gameplay.Patches
{
    [HarmonyPatch(typeof(InventoryUI), nameof(InventoryUI.PopulateList))]
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
            if (container.GetComponentInParent<Canvas>().name == "InventoryCanvas")
            {
                foreach (var act in container.GetComponentsInChildren<InventoryGamepadAction>())
                {
                    var maybe = act.GetComponent<InventoryInteractable>();
                    if (maybe == null)
                    {
                        act.gameObject.AddComponent<InventoryInteractable>();
                        act.gameObject.GetComponent<UIElement>().enabled = false;
                    }
                }
            }
        }
    }
}
