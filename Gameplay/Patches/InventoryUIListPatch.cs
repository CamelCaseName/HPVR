using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace HPVR.Gameplay.Patches
{
    [HarmonyPatch(typeof(InventoryUI), nameof(InventoryUI.PopulateList))]
    internal class InventoryUIListPatch
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via reflection")]
        private static void Postfix(ref RectTransform container)
        {
            var childs = container.transform.GetComponentsInChildren<Image>();
            foreach (var child in childs)
            {
                //MelonLogger.Msg($"{child.name} + {child.transform.position.x} + {child.transform.position.x}");
                child.transform.localEulerAngles = Vector3.zero;
            }
        }
    }
}
