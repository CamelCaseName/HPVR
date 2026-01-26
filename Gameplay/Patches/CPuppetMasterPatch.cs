using HarmonyLib;
using Il2CppEekAddOns;
using Il2CppEekCharacterEngine;
using Il2CppRootMotion.Dynamics;

namespace HPVR.Gameplay.Patches
{
#if VR_DISABLED
    [HarmonyPatch(typeof(CPuppetMaster), nameof(CPuppetMaster.CUpdate))]
#endif
    internal class CPuppetMasterPatch
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via reflection")]
        private static bool Prefix(CPuppetMaster __instance)
        {
            // works :)
            PuppetMaster puppet = __instance.Puppet;
            if (puppet is null)
            {
                return false;
            }
            if (puppet.state == PuppetMaster.State.Dead)
            {
                __instance.Cast<ComponentBase>().CUpdate();
                return false;
            }
            if (PlayerCharacter.Player is not null)
            {
                bool inRange = (__instance.Character.transform.position - PlayerCharacter.Player.transform.position).sqrMagnitude < 9;
                if (inRange)
                {
                    if (puppet.mode != PuppetMaster.Mode.Kinematic)
                    {
                        //MelonLogger.Msg(__instance.Character.name + " in range, turning puppet on");
                        puppet.SwitchToKinematicMode();
                        __instance.Cast<ComponentBase>().CUpdate();
                    }
                    return false;
                }
            }
            if (puppet.mode != PuppetMaster.Mode.Disabled)
            {
                //MelonLogger.Msg(__instance.Character.name + " turning puppet off");
                puppet.SwitchToDisabledMode();
                __instance.Cast<ComponentBase>().CUpdate();
            }
            return false;
        }
    }
}
