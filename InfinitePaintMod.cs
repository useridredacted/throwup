using MelonLoader;
using HarmonyLib;
using Il2CppScheduleOne.Graffiti;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Core.Items.Framework;
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
        private static bool isHoldingPreview = false;
        private static SpraySurface currentPreviewSurface = null;

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

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Global Definition Spoofing to force game engine to recognize "Spray Paint" as a "Spray Can"
            try
            {
                var definitions = Resources.FindObjectsOfTypeAll<BaseItemDefinition>();
                int spoofCount = 0;
                foreach (var def in definitions)
                {
                    if (def != null && (def.ID == "spraypaint" || def.Name.Contains("Spray Paint")))
                    {
                        def.ID = "spraycan";
                        spoofCount++;
                    }
                }
                if (spoofCount > 0)
                {
                    LoggerInstance.Msg($"Successfully spoofed {spoofCount} spraypaint definition(s) to spraycan globally!");
                }
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"Failed to spoof item definitions: {ex}");
            }
        }

        public override void OnUpdate()
        {
            try
            {
                // 1. Remove Canvas Check
                if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
                {
                    TryRemoveCanvas();
                }

                // 2. Hold-to-Preview Placement Check
                if (Input.GetKeyDown(KeyCode.G))
                {
                    if (IsSprayItemEquipped())
                    {
                        isHoldingPreview = true;
                        currentPreviewSurface = GetNextSpraySurface();
                        if (currentPreviewSurface != null)
                        {
                            MelonLogger.Msg($"Started preview with SpraySurface: {currentPreviewSurface.name}");
                        }
                    }
                    else
                    {
                        MelonLogger.Msg("Cannot place graffiti: Spray paint is not equipped.");
                    }
                }

                if (isHoldingPreview)
                {
                    if (Input.GetKey(KeyCode.G))
                    {
                        UpdatePlacementPreview();
                    }
                    else
                    {
                        isHoldingPreview = false;
                        FinishPlacement();
                    }
                }
            }
            catch {}
        }

        private static bool IsSprayItemEquipped()
        {
            if (PlayerInventory.InstanceExists && PlayerInventory.Instance != null && PlayerInventory.Instance.EquippedItem != null)
            {
                string id = PlayerInventory.Instance.EquippedItem.ID.ToLower();
                string name = PlayerInventory.Instance.EquippedItem.Name.ToLower();
                if (id.Contains("spray") || id.Contains("paint") || name.Contains("spray") || name.Contains("paint") || id.Contains("spraycan"))
                {
                    return true;
                }
            }
            return false;
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

        private static void UpdatePlacementPreview()
        {
            if (currentPreviewSurface == null || Camera.main == null) return;

            var surfaceGo = currentPreviewSurface.gameObject;
            
            // Temporarily disable the surface so we don't raycast hit ourselves
            bool wasActive = surfaceGo.activeSelf;
            surfaceGo.SetActive(false);

            Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
            RaycastHit hit;
            int mask = ~LayerMask.GetMask("Player", "Ignore Raycast");
            bool didHit = Physics.Raycast(ray, out hit, 10f, mask);

            surfaceGo.SetActive(true);

            if (didHit)
            {
                surfaceGo.transform.position = hit.point + hit.normal * 0.01f;
                surfaceGo.transform.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);
            }
        }

        private static void FinishPlacement()
        {
            if (currentPreviewSurface == null) return;

            MelonLogger.Msg($"Finishing placement for: {currentPreviewSurface.name}");

            // Open drawing UI
            var interaction = currentPreviewSurface.GetComponentInChildren<SpraySurfaceInteraction>();
            if (interaction == null) interaction = currentPreviewSurface.GetComponentInParent<SpraySurfaceInteraction>();
            
            if (interaction != null)
            {
                var openMethod = typeof(SpraySurfaceInteraction).GetMethod("Open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (openMethod != null)
                {
                    openMethod.Invoke(interaction, null);
                    MelonLogger.Msg("Opened spray painting interface!");
                }
            }
            
            currentPreviewSurface = null;
        }

        private static void TryRemoveCanvas()
        {
            if (Camera.main == null) return;
            Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
            RaycastHit hit;
            int mask = ~LayerMask.GetMask("Player", "Ignore Raycast");
            if (Physics.Raycast(ray, out hit, 10f, mask))
            {
                var surface = hit.collider.GetComponentInParent<SpraySurface>();
                if (surface == null) surface = hit.collider.GetComponentInChildren<SpraySurface>();
                if (surface == null) surface = hit.collider.GetComponent<SpraySurface>();

                if (surface != null)
                {
                    surface.ClearDrawing();
                    surface.transform.position = Vector3.down * 1000f;
                    MelonLogger.Msg("Removed spray paint canvas successfully.");
                }
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

    [HarmonyPatch(typeof(PlayerInventory), "get_EquippedItem")]
    public static class Patch_get_EquippedItem
    {
        public static void Postfix(ref ItemInstance __result)
        {
            try
            {
                if (__result != null && __result.Definition != null)
                {
                    string id = __result.Definition.ID.ToLower();
                    string name = __result.Name.ToLower();
                    if (id.Contains("spray") || id.Contains("paint") || name.Contains("spray") || name.Contains("paint"))
                    {
                        __result.Definition.ID = "spraycan";
                    }
                }
            }
            catch {}
        }
    }

    [HarmonyPatch(typeof(SpraySurfaceInteraction), "IsSprayCanEquipped")]
    public static class Patch_IsSprayCanEquipped
    {
        public static bool Prefix(ref bool __result)
        {
            try
            {
                if (PlayerInventory.Instance != null && PlayerInventory.Instance.EquippedItem != null)
                {
                    string id = PlayerInventory.Instance.EquippedItem.ID.ToLower();
                    string name = PlayerInventory.Instance.EquippedItem.Name.ToLower();
                    if (id.Contains("spray") || id.Contains("paint") || name.Contains("spray") || name.Contains("paint") || id.Contains("spraycan"))
                    {
                        __result = true;
                        return false;
                    }
                }
            }
            catch {}
            return true;
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
