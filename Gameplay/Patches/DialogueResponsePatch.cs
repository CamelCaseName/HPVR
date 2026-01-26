using HarmonyLib;
using Il2CppEekUI;

namespace HPVR.Gameplay.Patches
{
#if VR_DISABLED
    [HarmonyPatch(typeof(DialogueUI._DisplayResponses_d__59), nameof(DialogueUI._DisplayResponses_d__59.MoveNext))]
#endif
    internal class DialogueResponsePatch
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via reflection")]
        private static void Postfix()
        {
            HPVR.Instance?.UpdateDialogueResponses();
        }
    }
}
