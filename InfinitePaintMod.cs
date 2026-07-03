using MelonLoader;
using HarmonyLib;
using Il2CppScheduleOne.Graffiti;

[assembly: MelonInfo(typeof(ThrowUpMod.ThrowUp), "Throw Up", "1.0.0", "useridredacted")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ThrowUpMod
{
    public class ThrowUp : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Throw Up Mod initializing...");
            try
            {
                HarmonyInstance.PatchAll();
                LoggerInstance.Msg("Harmony patches applied successfully!");
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"Failed to apply Harmony patches: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SpraySurfaceInteraction), "Update")]
    public static class Patch_Update
    {
        public static void Prefix(SpraySurfaceInteraction __instance)
        {
            if (__instance.IsOpen)
            {
                __instance.PaintedPixelLimitMultiplier = 80000f;
                __instance._allowDraw = true;
            }
        }
    }

    [HarmonyPatch(typeof(SpraySurfaceInteraction), "FixedUpdate")]
    public static class Patch_FixedUpdate
    {
        public static void Prefix(SpraySurfaceInteraction __instance)
        {
            if (__instance.IsOpen)
            {
                __instance.PaintedPixelLimitMultiplier = 80000f;
                __instance._allowDraw = true;
            }
        }
    }
}
