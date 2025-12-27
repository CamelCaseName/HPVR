using HarmonyLib;
using Il2Cpp;
using Il2CppHouseParty.Interface;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace HPVR.Gameplay.Patches
{
    [HarmonyPatch(typeof(MessageHandler), nameof(MessageHandler.OnDisplayMessage))]
    internal class MessageRotationPatch
    {
        //todo
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via reflection")]
        private static void Postfix()
        {
            //My guess is either rotation is off or like the responses the rect for every but the first message is somehow 0 in one direction
            if (MessageHandler.Singleton.IsShowing)
            {
                foreach (var item in MessageHandler.Singleton._displays)
                {
                    item.RectTransform.localEulerAngles = Vector3.zero;
                    item.RectTransform.localPosition = new Vector3(item.RectTransform.localPosition.x, item.RectTransform.localPosition.y);
                }
            }
        }
    }
}
