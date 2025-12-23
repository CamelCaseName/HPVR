using HarmonyLib;
using HPVR.UI;
using Il2CppEekCharacterEngine;
using Il2CppEekCharacterEngine.Interaction;
using UnityEngine;
using Valve.VR.InteractionSystem;

namespace HPVR.Gameplay
{
    [HarmonyPatch(typeof(InteractionManager), "FixedUpdate")]
    internal static class Patcher
    {
        internal static float addedDistance = 2.3f;
        internal static float maxDistance = 3f + addedDistance;
        //private static readonly int PhysicsLayerMask = LayerMask.GetMask("InteractiveItems", "Character", "Walls", "Ground", "Default");

        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via reflection")]
        private static bool Prefix()
        {
            if (InteractionManager.Singleton is null
                || InteractionManager.Singleton.Text is null
                || InteractionManager.Singleton.Image is null
                || PlayerCharacter.Player is null
                || Camera.main is null
                || Camera.main.transform is null
                || !HPVR.Enabled)
            {
                return true;
            }

            var interactive = InteractionManager.Singleton._focusedItemInteraction;
            if (interactive is null)
            {
                return false;
            }

            string? text = TranslationManager.TranslateByText(interactive.DisplayName);
            if (text != InteractionManager.Singleton._goalText)
            {
                InteractionManager.Singleton.ResetNameDelay();
                //MelonLogger.Msg("reset name delay");
            }

            InteractionManager.Singleton._goalText = text;
            //MelonLogger.Msg("set text");

            if (text is not null
                && text != string.Empty
                && InteractionManager.Singleton._currentCharacter < text.Length)
            {
                InteractionManager.Singleton._currentCharacter++;
                //MelonLogger.Msg("advanced character");
            }

            return false;
        }
    }
}