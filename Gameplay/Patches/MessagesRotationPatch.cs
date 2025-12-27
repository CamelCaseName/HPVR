using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace HPVR.Gameplay.Patches
{
    //[HarmonyPatch(typeof(InventoryUI), nameof(InventoryUI.PopulateList))]
    internal class MessageRotationPatch
    {
        //todo
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via reflection")]
        private static void Postfix(ref RectTransform container)
        {
            //My guess is either rotation is off or like the responses the rect for every but the first message is somehow 0 in one direction
            var childs = container.transform.GetComponentsInChildren<Image>();
            foreach (var child in childs)
            {
                MelonLogger.Msg($"{child.name} + {child.transform.position.x} + {child.transform.position.x}");
            }
        }
    }
}
