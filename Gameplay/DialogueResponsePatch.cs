using HarmonyLib;
using Il2CppEekUI;

namespace HPVR.Gameplay
{
    [HarmonyPatch(typeof(DialogueUI._DisplayResponses_d__59), "MoveNext")]
    internal class DialogueResponsePatch
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via reflection")]
        private static void Postfix()
        {
            HPVR.Instance?.UpdateDialogueResponses();
        }
    }
}
