using MelonLoader;
using HarmonyLib;
using Il2CppScheduleOne.Graffiti;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppFishNet.Object;
using Il2CppFishNet.Connection;
using UnityEngine;

[assembly: MelonInfo(typeof(ThrowUpMod.ThrowUp), "Throw Up", "1.0.0", "useridredacted")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ThrowUpMod
{
    public class ThrowUp : MelonMod
    {
        private static int lastTeleportedIndex = 0;

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

        public override void OnUpdate()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.G))
                {
                    TryTeleportAndOpenCanvas();
                }
            }
            catch {}
        }

        private static SpraySurface GetNextSpraySurface()
        {
            try
            {
                var surfaces = Resources.FindObjectsOfTypeAll<SpraySurface>();
                if (surfaces == null || surfaces.Count == 0) return null;

                var activeSurfaces = new System.Collections.Generic.List<SpraySurface>();
                foreach (var s in surfaces)
                {
                    if (s.gameObject.activeInHierarchy && s.gameObject.scene.name != null)
                    {
                        activeSurfaces.Add(s);
                    }
                }

                if (activeSurfaces.Count == 0) return null;

                int index = lastTeleportedIndex % activeSurfaces.Count;
                lastTeleportedIndex++;
                return activeSurfaces[index];
            }
            catch
            {
                return null;
            }
        }

        private static void TryTeleportAndOpenCanvas()
        {
            if (Camera.main == null) return;

            // 1. Check if spray can is equipped
            bool isEquipped = false;
            try
            {
                var method = typeof(SpraySurfaceInteraction).GetMethod("IsSprayCanEquipped", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (method != null)
                {
                    isEquipped = (bool)method.Invoke(null, null);
                }
            }
            catch {}

            if (!isEquipped)
            {
                MelonLogger.Msg("Cannot place graffiti: Spray can is not equipped.");
                return;
            }

            // 2. Raycast from camera
            Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
            RaycastHit hit;
            int mask = ~LayerMask.GetMask("Player", "Ignore Raycast");
            if (Physics.Raycast(ray, out hit, 10f, mask))
            {
                // 3. Get next spray surface
                var surface = GetNextSpraySurface();
                if (surface == null)
                {
                    MelonLogger.Msg("No active spray surfaces found in this scene to teleport.");
                    return;
                }

                // 4. Teleport surface
                var surfaceGo = surface.gameObject;
                surfaceGo.transform.position = hit.point + hit.normal * 0.01f;
                surfaceGo.transform.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);

                MelonLogger.Msg($"Teleported SpraySurface {surface.name} to target wall.");

                // 5. Open interaction
                var interaction = surface.GetComponentInChildren<SpraySurfaceInteraction>();
                if (interaction == null) interaction = surface.GetComponentInParent<SpraySurfaceInteraction>();
                
                if (interaction != null)
                {
                    var openMethod = typeof(SpraySurfaceInteraction).GetMethod("Open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (openMethod != null)
                    {
                        openMethod.Invoke(interaction, null);
                        MelonLogger.Msg("Opened spray painting interface!");
                    }
                }
            }
            else
            {
                MelonLogger.Msg("No wall in range (max distance 10m).");
            }
        }
    }

    [HarmonyPatch(typeof(SpraySurfaceInteraction), "Update")]
    public static class Patch_Update
    {
        public static void Prefix(SpraySurfaceInteraction __instance)
        {
            try
            {
                if (__instance.IsOpen)
                {
                    __instance.PaintedPixelLimitMultiplier = 80000f;
                    __instance._allowDraw = true;
                }
            }
            catch {}
        }
    }

    [HarmonyPatch(typeof(SpraySurfaceInteraction), "FixedUpdate")]
    public static class Patch_FixedUpdate
    {
        public static void Prefix(SpraySurfaceInteraction __instance)
        {
            try
            {
                if (__instance.IsOpen)
                {
                    __instance.PaintedPixelLimitMultiplier = 80000f;
                    __instance._allowDraw = true;
                }
            }
            catch {}
        }
    }

    [HarmonyPatch(typeof(SpraySurface), "SetCurrentEditor_Client")]
    public static class Patch_SetCurrentEditor_Client
    {
        public static void Postfix(SpraySurface __instance, NetworkObject player)
        {
            try
            {
                if (player != null && !player.IsOwner)
                {
                    var playerComp = player.GetComponent<Player>();
                    if (playerComp != null)
                    {
                        // Use native Unity transform position/forward to avoid SyncVar lifecycle crashes
                        Vector3 lookPos = playerComp.transform.position + Vector3.up * 1.6f;
                        Vector3 lookDir = playerComp.transform.forward;

                        Ray ray = new Ray(lookPos, lookDir);
                        RaycastHit hit;
                        int mask = ~LayerMask.GetMask("Player", "Ignore Raycast");
                        if (Physics.Raycast(ray, out hit, 10f, mask))
                        {
                            __instance.transform.position = hit.point + hit.normal * 0.01f;
                            __instance.transform.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);
                            MelonLogger.Msg($"Teleported remote player's SpraySurface locally to match target wall!");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in SetCurrentEditor_Client patch: {ex}");
            }
        }
    }
}
