using MelonLoader;
using HarmonyLib;
using Il2CppScheduleOne.Graffiti;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.UI.Shop;
using Il2CppFishNet.Object;
using Il2CppFishNet.Connection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[assembly: MelonInfo(typeof(ThrowUpMod.ThrowUp), "Throw Up", "1.0.0", "useridredacted")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ThrowUpMod
{
    public class ThrowUp : MelonMod
    {
        private static int lastTeleportedIndex = 0;
        private static bool isHoldingPreview = false;
        private static SpraySurface currentPreviewSurface = null;
        private static System.Collections.Generic.Dictionary<SpraySurface, System.Collections.Generic.List<MeshRenderer>> hiddenRenderersMap = 
            new System.Collections.Generic.Dictionary<SpraySurface, System.Collections.Generic.List<MeshRenderer>>();
        private static SpraySurface activeResizeCanvas = null;
        private static string draggedHandleName = null;
        private static bool isResizing = false;
        private static SpraySurface lastActiveCanvasWithHandles = null;
        private static int initialCanvasWidth = 512;
        private static int initialCanvasHeight = 256;
        private static float accumulatedMouseX = 0f;
        private static float accumulatedMouseY = 0f;
        private static int lastCanvasScanFrame = -100;
        private static System.Collections.Generic.List<SpraySurface> cachedCanvases = new System.Collections.Generic.List<SpraySurface>();

        public static System.Collections.Generic.Dictionary<SpraySurface, float> canvasPixelOffsetX = new System.Collections.Generic.Dictionary<SpraySurface, float>();
        public static System.Collections.Generic.Dictionary<SpraySurface, float> canvasPixelOffsetY = new System.Collections.Generic.Dictionary<SpraySurface, float>();
        private static bool hasRestoredForCurrentScene = false;

        public static float GetPixelOffsetX(SpraySurface surface)
        {
            if (surface != null && canvasPixelOffsetX.TryGetValue(surface, out float val)) return val;
            return 0f;
        }

        public static float GetPixelOffsetY(SpraySurface surface)
        {
            if (surface != null && canvasPixelOffsetY.TryGetValue(surface, out float val)) return val;
            return 0f;
        }

        public static UnityEngine.Camera GetMainCamera()
        {
            try
            {
                if (PlayerCamera.InstanceExists && PlayerCamera.Instance != null && PlayerCamera.Instance.Camera != null)
                {
                    return PlayerCamera.Instance.Camera;
                }
            }
            catch {}
            return UnityEngine.Camera.main;
        }

        public static UnityEngine.BoxCollider FindMainCanvasCollider(SpraySurface canvas)
        {
            if (canvas == null) return null;
            
            var col = canvas.GetComponent<UnityEngine.BoxCollider>();
            if (col != null) return col;

            var cols = canvas.GetComponentsInChildren<UnityEngine.BoxCollider>();
            if (cols != null)
            {
                foreach (var c in cols)
                {
                    if (c != null && !c.gameObject.name.StartsWith("ResizeHandle_"))
                    {
                        return c;
                    }
                }
            }

            var interaction = FindInteractionForSurface(canvas);
            if (interaction != null)
            {
                col = interaction.GetComponent<UnityEngine.BoxCollider>();
                if (col != null) return col;

                cols = interaction.GetComponentsInChildren<UnityEngine.BoxCollider>();
                if (cols != null)
                {
                    foreach (var c in cols)
                    {
                        if (c != null && !c.gameObject.name.StartsWith("ResizeHandle_"))
                        {
                            return c;
                        }
                    }
                }
            }

            return null;
        }

        public static int GetHighestQueueInRange(Vector3 position, float radius)
        {
            int highestQueue = 2000;
            try
            {
                var projectors = Resources.FindObjectsOfTypeAll<DecalProjector>();
                if (projectors != null)
                {
                    foreach (var proj in projectors)
                    {
                        if (proj == null || proj.material == null) continue;
                        
                        float dist = Vector3.Distance(proj.transform.position, position);
                        
                        if (!proj.gameObject.activeInHierarchy) continue;
                        if (proj == currentPreviewSurface?.Projector) continue;

                        if (dist <= radius)
                        {
                            int queue = proj.material.renderQueue;
                            if (queue > highestQueue && queue < 3000)
                            {
                                highestQueue = queue;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in GetHighestQueueInRange: {ex}");
            }
            return highestQueue;
        }

        private static void HideOverlappingGraffitiMeshes(SpraySurface surface)
        {
            if (surface == null) return;
            Vector3 position = surface.transform.position;
            float radius = 2.0f;
            
            var list = new System.Collections.Generic.List<MeshRenderer>();
            try
            {
                var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                if (renderers != null)
                {
                    foreach (var r in renderers)
                    {
                        if (r == null || !r.gameObject.activeInHierarchy) continue;
                        
                        float dist = Vector3.Distance(r.transform.position, position);
                        if (dist <= radius)
                        {
                            // Skip our own canvas
                            if (r.transform.root == surface.transform || r.GetComponentInParent<SpraySurface>() != null)
                            {
                                continue;
                            }

                            string goName = r.gameObject.name.ToLower();
                            string matName = (r.material != null) ? r.material.name.ToLower() : "";

                            if (goName.Contains("graffiti") || goName.Contains("decal") || goName.Contains("art") ||
                                goName.Contains("paint") || goName.Contains("poster") || goName.Contains("sticker") ||
                                matName.Contains("graffiti") || matName.Contains("decal") || matName.Contains("art") ||
                                matName.Contains("paint") || matName.Contains("poster") || matName.Contains("sticker"))
                            {
                                r.enabled = false;
                                list.Add(r);
                                MelonLogger.Msg($"[Decal Cleanup] Hid overlapping graffiti mesh: {r.gameObject.name} (Material: {matName}) at dist {dist:F2}m");
                            }
                        }
                    }
                }
                
                if (list.Count > 0)
                {
                    hiddenRenderersMap[surface] = list;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in HideOverlappingGraffitiMeshes: {ex}");
            }
        }

        private const float PIXEL_SIZE = 0.006666671f;

        private static void UpdateHandlePositions(SpraySurface surface)
        {
            if (surface == null) return;

            string[] names = { 
                "ResizeHandle_TR", "ResizeHandle_TL", "ResizeHandle_BR", "ResizeHandle_BL",
                "ResizeHandle_R", "ResizeHandle_L", "ResizeHandle_T", "ResizeHandle_B"
            };

            float pxOffset = GetPixelOffsetX(surface);
            float pyOffset = GetPixelOffsetY(surface);
            float offX = pxOffset * PIXEL_SIZE;
            float offY = pyOffset * PIXEL_SIZE;

            float w_m = surface.Width * PIXEL_SIZE / 2f;
            float h_m = surface.Height * PIXEL_SIZE / 2f;

            UnityEngine.Vector3[] localPositions = {
                new UnityEngine.Vector3(w_m + offX, h_m + offY, 0.5f),   // TR
                new UnityEngine.Vector3(-w_m + offX, h_m + offY, 0.5f),  // TL
                new UnityEngine.Vector3(w_m + offX, -h_m + offY, 0.5f),  // BR
                new UnityEngine.Vector3(-w_m + offX, -h_m + offY, 0.5f), // BL
                new UnityEngine.Vector3(w_m + offX, offY, 0.5f),    // R
                new UnityEngine.Vector3(-w_m + offX, offY, 0.5f),   // L
                new UnityEngine.Vector3(offX, h_m + offY, 0.5f),    // T
                new UnityEngine.Vector3(offX, -h_m + offY, 0.5f)    // B
            };

            for (int i = 0; i < 8; i++)
            {
                var handle = surface.transform.Find(names[i]);
                if (handle != null)
                {
                    handle.transform.localPosition = localPositions[i];
                    handle.transform.localScale = new UnityEngine.Vector3(0.15f, 0.15f, 0.15f);
                }
            }
        }

        private static void CreateResizeHandles(SpraySurface surface)
        {
            return;
            
            // Check if handles already exist
            if (surface.transform.Find("ResizeHandle_TR") != null) return;

            string[] names = { 
                "ResizeHandle_TR", "ResizeHandle_TL", "ResizeHandle_BR", "ResizeHandle_BL",
                "ResizeHandle_R", "ResizeHandle_L", "ResizeHandle_T", "ResizeHandle_B"
            };

            float w_m = surface.Width * PIXEL_SIZE / 2f;
            float h_m = surface.Height * PIXEL_SIZE / 2f;

            UnityEngine.Vector3[] localPositions = {
                new UnityEngine.Vector3(w_m, h_m, 0.5f),   // TR
                new UnityEngine.Vector3(-w_m, h_m, 0.5f),  // TL
                new UnityEngine.Vector3(w_m, -h_m, 0.5f),  // BR
                new UnityEngine.Vector3(-w_m, -h_m, 0.5f), // BL
                new UnityEngine.Vector3(w_m, 0f, 0.5f),    // R
                new UnityEngine.Vector3(-w_m, 0f, 0.5f),   // L
                new UnityEngine.Vector3(0f, h_m, 0.5f),    // T
                new UnityEngine.Vector3(0f, -h_m, 0.5f)    // B
            };

            for (int i = 0; i < 8; i++)
            {
                var handle = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Sphere);
                handle.name = names[i];
                handle.transform.parent = surface.transform;
                handle.transform.localPosition = localPositions[i];
                handle.transform.localRotation = UnityEngine.Quaternion.identity;
                
                var renderer = handle.GetComponent<UnityEngine.MeshRenderer>();
                if (renderer != null)
                {
                    renderer.material = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit"));
                    renderer.material.color = UnityEngine.Color.cyan;
                }
                
                handle.transform.localScale = new UnityEngine.Vector3(0.15f, 0.15f, 0.15f);
            }
        }

        private static void SetHandlesActive(SpraySurface surface, bool active)
        {
            if (surface == null) return;
            string[] names = { 
                "ResizeHandle_TR", "ResizeHandle_TL", "ResizeHandle_BR", "ResizeHandle_BL",
                "ResizeHandle_R", "ResizeHandle_L", "ResizeHandle_T", "ResizeHandle_B"
            };
            foreach (var name in names)
            {
                var t = surface.transform.Find(name);
                if (t != null)
                {
                    t.gameObject.SetActive(active);
                }
            }
            if (active)
            {
                UpdateHandlePositions(surface);
            }
        }

        private static void HideAllOtherHandles(SpraySurface activeSurface)
        {
            if (activeSurface == null)
            {
                if (lastActiveCanvasWithHandles != null)
                {
                    SetHandlesActive(lastActiveCanvasWithHandles, false);
                    lastActiveCanvasWithHandles = null;
                }
            }
            else
            {
                if (lastActiveCanvasWithHandles != null && lastActiveCanvasWithHandles != activeSurface)
                {
                    SetHandlesActive(lastActiveCanvasWithHandles, false);
                }
                lastActiveCanvasWithHandles = activeSurface;
            }
        }

        public static void AutoResizeCanvasToHuge(SpraySurface surface)
        {
            if (surface == null) return;
            try
            {
                int targetWidth = 2048;
                int targetHeight = 2048;

                surface.Width = targetWidth;
                surface.Height = targetHeight;

                var newDrawing = new Il2CppScheduleOne.Graffiti.Drawing(targetWidth, targetHeight, true);
                SetDrawingOnSurface(surface, newDrawing);

                CallResizeProjector(surface);
                if (surface.Projector != null)
                {
                    surface.Projector.transform.localPosition = new UnityEngine.Vector3(0f, 0f, surface.Projector.transform.localPosition.z);
                }

                var boxCol = FindMainCanvasCollider(surface);
                if (boxCol != null)
                {
                    boxCol.size = new UnityEngine.Vector3(targetWidth * PIXEL_SIZE, targetHeight * PIXEL_SIZE, 0.01f);
                    boxCol.center = UnityEngine.Vector3.zero;
                }

                if (surface.BottomLeftPoint != null)
                {
                    surface.BottomLeftPoint.localPosition = new UnityEngine.Vector3(-targetWidth * PIXEL_SIZE / 2f, -targetHeight * PIXEL_SIZE / 2f, 0f);
                }

                // CallInteractionResizeCanvas(surface);
                // ResizeCanvasUI(surface, targetWidth, targetHeight);

                RepositionCanvas(surface, surface.transform.position, surface.transform.rotation);
                MelonLogger.Msg($"[AutoResize] Automatically expanded canvas {surface.name} to {targetWidth}x{targetHeight} (unrestricted painting area).");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error auto-resizing canvas {surface.name}: {ex}");
            }
        }

        public static void EnsureCanvasIsHuge(SpraySurface surface)
        {
            if (surface == null || surface.Width >= 2048) return;
            try
            {
                int targetWidth = 2048;
                int targetHeight = 2048;

                Il2CppSystem.Collections.Generic.List<SprayStroke> strokesToPreserve = null;
                var fieldInfo = typeof(SpraySurface).GetField("cachedDrawing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (fieldInfo != null)
                {
                    var drawing = fieldInfo.GetValue(surface) as Drawing;
                    if (drawing != null)
                    {
                        strokesToPreserve = drawing.GetStrokes();
                    }
                }

                ushort offsetX = (ushort)((targetWidth - surface.Width) / 2);
                ushort offsetY = (ushort)((targetHeight - surface.Height) / 2);

                surface.Width = targetWidth;
                surface.Height = targetHeight;

                var newDrawing = new Il2CppScheduleOne.Graffiti.Drawing(targetWidth, targetHeight, true);
                if (strokesToPreserve != null && strokesToPreserve.Count > 0)
                {
                    var shifted = SprayStroke.CopyAndShiftStrokes(strokesToPreserve, new UShort2(offsetX, offsetY));
                    newDrawing.AddStrokes(shifted);
                }

                SetDrawingOnSurface(surface, newDrawing);

                CallResizeProjector(surface);
                if (surface.Projector != null)
                {
                    surface.Projector.transform.localPosition = new UnityEngine.Vector3(0f, 0f, surface.Projector.transform.localPosition.z);
                }

                var boxCol = FindMainCanvasCollider(surface);
                if (boxCol != null)
                {
                    boxCol.size = new UnityEngine.Vector3(targetWidth * PIXEL_SIZE, targetHeight * PIXEL_SIZE, 0.01f);
                    boxCol.center = UnityEngine.Vector3.zero;
                }

                if (surface.BottomLeftPoint != null)
                {
                    surface.BottomLeftPoint.localPosition = new UnityEngine.Vector3(-targetWidth * PIXEL_SIZE / 2f, -targetHeight * PIXEL_SIZE / 2f, 0f);
                }

                RepositionCanvas(surface, surface.transform.position, surface.transform.rotation);
                MelonLogger.Msg($"[EnsureHuge] Safely expanded canvas {surface.name} to {targetWidth}x{targetHeight} (preserved {(strokesToPreserve != null ? strokesToPreserve.Count : 0)} strokes).");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error ensuring canvas is huge: {ex}");
            }
        }

        private static void CallInteractionResizeCanvas(SpraySurface surface)
        {
            try
            {
                var interaction = FindInteractionForSurface(surface);
                if (interaction != null)
                {
                    var method = typeof(SpraySurfaceInteraction).GetMethod("ResizeCanvas", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (method != null)
                    {
                        method.Invoke(interaction, null);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error calling SpraySurfaceInteraction.ResizeCanvas: {ex}");
            }
        }

        private static void ResizeCanvasUI(SpraySurface surface, float newWidth, float newHeight)
        {
            try
            {
                var interaction = FindInteractionForSurface(surface);
                var rects = surface.GetComponentsInChildren<UnityEngine.RectTransform>(true);
                if (rects != null)
                {
                    foreach (var r in rects)
                    {
                        if (r == null) continue;
                        if (interaction != null && interaction.SprayImg != null && r == interaction.SprayImg.rectTransform) continue;
                        
                        r.sizeDelta = new UnityEngine.Vector2(newWidth, newHeight);
                        r.anchoredPosition = UnityEngine.Vector2.zero;
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in ResizeCanvasUI: {ex}");
            }
        }

        private static void SetDrawingOnSurface(SpraySurface surface, Il2CppScheduleOne.Graffiti.Drawing drawing)
        {
            try
            {
                var fieldInfo = typeof(SpraySurface).GetField("NativeFieldInfoPtr_drawing", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (fieldInfo != null)
                {
                    System.IntPtr fieldPtr = (System.IntPtr)fieldInfo.GetValue(null);
                    if (fieldPtr != System.IntPtr.Zero)
                    {
                        unsafe
                        {
                            Il2CppInterop.Runtime.IL2CPP.il2cpp_field_set_value(surface.Pointer, fieldPtr, (void*)drawing.Pointer);
                        }
                    }
                }

                var cachedFieldInfo = typeof(SpraySurface).GetField("NativeFieldInfoPtr_cachedDrawing", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (cachedFieldInfo != null)
                {
                    System.IntPtr cachedFieldPtr = (System.IntPtr)cachedFieldInfo.GetValue(null);
                    if (cachedFieldPtr != System.IntPtr.Zero)
                    {
                        unsafe
                        {
                            Il2CppInterop.Runtime.IL2CPP.il2cpp_field_set_value(surface.Pointer, cachedFieldPtr, (void*)drawing.Pointer);
                        }
                    }
                }

                if (surface.onDrawingChanged != null)
                {
                    surface.onDrawingChanged.Invoke();
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in SetDrawingOnSurface: {ex}");
            }
        }

        private static void CallResizeProjector(SpraySurface surface)
        {
            try
            {
                var method = typeof(SpraySurface).GetMethod("ResizeProjector", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (method != null)
                {
                    method.Invoke(surface, null);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error calling ResizeProjector: {ex}");
            }
        }

        private static SpraySurface FindClosestCanvasCached(UnityEngine.Vector3 position, float maxDistance)
        {
            try
            {
                int currentFrame = UnityEngine.Time.frameCount;
                if (currentFrame - lastCanvasScanFrame >= 30)
                {
                    lastCanvasScanFrame = currentFrame;
                    cachedCanvases.Clear();
                    
                    var surfaces = UnityEngine.Resources.FindObjectsOfTypeAll<SpraySurface>();
                    if (surfaces != null)
                    {
                        foreach (var s in surfaces)
                        {
                            if (s != null && s.gameObject.activeInHierarchy && s.gameObject.transform.position.y > -500f)
                            {
                                cachedCanvases.Add(s);
                            }
                        }
                    }
                }
                
                SpraySurface closest = null;
                float minDist = maxDistance;
                foreach (var c in cachedCanvases)
                {
                    if (c == null) continue;
                    float dist = UnityEngine.Vector3.Distance(c.transform.position, position);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closest = c;
                    }
                }
                return closest;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in FindClosestCanvasCached: {ex}");
            }
            return null;
        }

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
            hasRestoredForCurrentScene = false;
        }

        public override void OnUpdate()
        {
            try
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "MainMenu" &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "LoadingScene" &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Intro")
                {
                    if (!hasRestoredForCurrentScene)
                    {
                        RestoreCanvasPositions();
                        hasRestoredForCurrentScene = true;
                    }
                }
                // 1. Remove Canvas Check
                if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
                {
                    TryRemoveCanvasBetter();
                }

                // 2. Hold-to-Preview Placement Check
                if (Input.GetKeyDown(KeyCode.G))
                {
                    if (IsSprayItemEquipped())
                    {
                        currentPreviewSurface = GetNextSpraySurface();
                        if (currentPreviewSurface != null)
                        {
                            isHoldingPreview = true;
                            SetCollidersEnabled(currentPreviewSurface, false);
                            

                            
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
                        SetCollidersEnabled(currentPreviewSurface, false);
                        UpdatePlacementPreview();
                    }
                    else
                    {
                        isHoldingPreview = false;
                        FinishPlacement();
                    }
                }

                // 3. Canvas Resizing Handle Loop
                bool holdingCan = IsSprayItemEquipped();
                if (!holdingCan)
                {
                    if (isResizing)
                    {
                        isResizing = false;
                        activeResizeCanvas = null;
                        draggedHandleName = null;
                        if (PlayerCamera.InstanceExists && PlayerCamera.Instance != null)
                        {
                            PlayerCamera.Instance.SetCanLook(true);
                        }
                    }
                    HideAllOtherHandles(null);
                }
                else
                {
                    // Find the closest canvas within 5 meters
                    var targetCanvas = FindClosestCanvasCached(GetMainCamera().transform.position, 5.0f);
                    if (targetCanvas != null)
                    {
                        // Only allow resizing on unpainted canvases (no strokes and no painted pixels)
                        bool isUnpainted = true;
                        try
                        {
                            isUnpainted = (targetCanvas.DrawingStrokeCount == 0 && targetCanvas.DrawingPaintedPixelCount == 0);
                        }
                        catch
                        {
                            isUnpainted = true; // Fallback if drawing object is not yet fully initialized on native side
                        }

                        if (isUnpainted)
                        {
                            CreateResizeHandles(targetCanvas);
                            SetHandlesActive(targetCanvas, true);
                            HideAllOtherHandles(targetCanvas);

                             if (isResizing && activeResizeCanvas == targetCanvas)
                             {
                                 if (UnityEngine.Input.GetMouseButton(0))
                                 {
                                     float mouseX = 0f;
                                     float mouseY = 0f;
                                     try
                                     {
                                         var mouse = UnityEngine.InputSystem.Mouse.current;
                                         if (mouse != null)
                                         {
                                             var delta = mouse.delta.ReadValue();
                                             mouseX = delta.x;
                                             mouseY = delta.y;
                                         }
                                     }
                                     catch {}

                                     accumulatedMouseX += mouseX;
                                     accumulatedMouseY += mouseY;

                                     float sensitivity = 0.05f;
                                     int deltaWidth = UnityEngine.Mathf.RoundToInt(accumulatedMouseX * sensitivity);
                                     int deltaHeight = UnityEngine.Mathf.RoundToInt(accumulatedMouseY * sensitivity);

                                     int newWidth = initialCanvasWidth;
                                     int newHeight = initialCanvasHeight;

                                      if (draggedHandleName == "ResizeHandle_R")
                                      {
                                          newWidth -= deltaWidth;
                                      }
                                      else if (draggedHandleName == "ResizeHandle_L")
                                      {
                                          newWidth += deltaWidth;
                                      }
                                      else if (draggedHandleName == "ResizeHandle_T")
                                      {
                                          newHeight += deltaHeight;
                                      }
                                      else if (draggedHandleName == "ResizeHandle_B")
                                      {
                                          newHeight -= deltaHeight;
                                      }
                                      else if (draggedHandleName == "ResizeHandle_TR")
                                      {
                                          newWidth -= deltaWidth;
                                          newHeight += deltaHeight;
                                      }
                                      else if (draggedHandleName == "ResizeHandle_TL")
                                      {
                                          newWidth += deltaWidth;
                                          newHeight += deltaHeight;
                                      }
                                      else if (draggedHandleName == "ResizeHandle_BR")
                                      {
                                          newWidth -= deltaWidth;
                                          newHeight -= deltaHeight;
                                      }
                                      else if (draggedHandleName == "ResizeHandle_BL")
                                      {
                                          newWidth += deltaWidth;
                                          newHeight -= deltaHeight;
                                      }

                                     newWidth = UnityEngine.Mathf.Clamp(newWidth, 64, 4096);
                                     newHeight = UnityEngine.Mathf.Clamp(newHeight, 64, 4096);

                                      if (newWidth != activeResizeCanvas.Width || newHeight != activeResizeCanvas.Height)
                                      {
                                          int widthDiff = newWidth - activeResizeCanvas.Width;
                                          int heightDiff = newHeight - activeResizeCanvas.Height;

                                          activeResizeCanvas.Width = newWidth;
                                          activeResizeCanvas.Height = newHeight;

                                          float pxOffset = GetPixelOffsetX(activeResizeCanvas);
                                          float pyOffset = GetPixelOffsetY(activeResizeCanvas);

                                          if (draggedHandleName == "ResizeHandle_R")
                                          {
                                              pxOffset -= widthDiff / 2f;
                                          }
                                          else if (draggedHandleName == "ResizeHandle_L")
                                          {
                                              pxOffset += widthDiff / 2f;
                                          }
                                          else if (draggedHandleName == "ResizeHandle_T")
                                          {
                                              pyOffset += heightDiff / 2f;
                                          }
                                          else if (draggedHandleName == "ResizeHandle_B")
                                          {
                                              pyOffset -= heightDiff / 2f;
                                          }
                                          else if (draggedHandleName == "ResizeHandle_TR")
                                          {
                                              pxOffset -= widthDiff / 2f;
                                              pyOffset += heightDiff / 2f;
                                          }
                                          else if (draggedHandleName == "ResizeHandle_TL")
                                          {
                                              pxOffset += widthDiff / 2f;
                                              pyOffset += heightDiff / 2f;
                                          }
                                          else if (draggedHandleName == "ResizeHandle_BR")
                                          {
                                              pxOffset -= widthDiff / 2f;
                                              pyOffset -= heightDiff / 2f;
                                          }
                                          else if (draggedHandleName == "ResizeHandle_BL")
                                          {
                                              pxOffset += widthDiff / 2f;
                                              pyOffset -= heightDiff / 2f;
                                          }

                                          canvasPixelOffsetX[activeResizeCanvas] = pxOffset;
                                          canvasPixelOffsetY[activeResizeCanvas] = pyOffset;

                                          var newDrawing = new Il2CppScheduleOne.Graffiti.Drawing(newWidth, newHeight, true);
                                          SetDrawingOnSurface(activeResizeCanvas, newDrawing);
                                          
                                          // Native projector size updates
                                          CallResizeProjector(activeResizeCanvas);
                                          if (activeResizeCanvas.Projector != null)
                                          {
                                              activeResizeCanvas.Projector.transform.localPosition = new UnityEngine.Vector3(pxOffset * PIXEL_SIZE, pyOffset * PIXEL_SIZE, activeResizeCanvas.Projector.transform.localPosition.z);
                                          }

                                          // Box collider size and center updates (meters)
                                          var boxCol = FindMainCanvasCollider(activeResizeCanvas);
                                          if (boxCol != null)
                                          {
                                              boxCol.size = new UnityEngine.Vector3(newWidth * PIXEL_SIZE, newHeight * PIXEL_SIZE, 0.75f);
                                              boxCol.center = new UnityEngine.Vector3(pxOffset * PIXEL_SIZE, pyOffset * PIXEL_SIZE, boxCol.center.z);
                                          }

                                          // Update BottomLeftPoint local position to match new dimensions and offsets
                                          if (activeResizeCanvas.BottomLeftPoint != null)
                                          {
                                              activeResizeCanvas.BottomLeftPoint.localPosition = new UnityEngine.Vector3(-newWidth / 2f + pxOffset, -newHeight / 2f + pyOffset, 0f);
                                          }

                                         // Update UI Canvas / RectTransform border sizes
                                         CallInteractionResizeCanvas(activeResizeCanvas);
                                         ResizeCanvasUI(activeResizeCanvas, newWidth, newHeight);

                                         UpdateHandlePositions(activeResizeCanvas);
                                         RepositionCanvas(activeResizeCanvas, activeResizeCanvas.transform.position, activeResizeCanvas.transform.rotation);
                                     }
                                 }
                                 else
                                 {
                                     isResizing = false;
                                     activeResizeCanvas = null;
                                     draggedHandleName = null;
                                     if (PlayerCamera.InstanceExists && PlayerCamera.Instance != null)
                                     {
                                         PlayerCamera.Instance.SetCanLook(true);
                                     }
                                 }
                             }
                             else
                             {
                                 // Detect handle click under cursor
                                 if (UnityEngine.Input.GetMouseButtonDown(0))
                                 {
                                     var cam = GetMainCamera();
                                     UnityEngine.Ray lookRay = new UnityEngine.Ray(cam.transform.position, cam.transform.forward);
                                     UnityEngine.RaycastHit hitInfo;
                                     int mask = ~UnityEngine.LayerMask.GetMask("Player", "Ignore Raycast");

                                     if (UnityEngine.Physics.Raycast(lookRay, out hitInfo, 6f, mask))
                                     {
                                         if (hitInfo.collider != null)
                                         {
                                             var go = hitInfo.collider.gameObject;
                                             if (go.name.StartsWith("ResizeHandle_"))
                                             {
                                                 activeResizeCanvas = targetCanvas;
                                                 draggedHandleName = go.name;
                                                 isResizing = true;
                                                 initialCanvasWidth = targetCanvas.Width;
                                                 initialCanvasHeight = targetCanvas.Height;
                                                 accumulatedMouseX = 0f;
                                                 accumulatedMouseY = 0f;
                                                 if (PlayerCamera.InstanceExists && PlayerCamera.Instance != null)
                                                 {
                                                     PlayerCamera.Instance.SetCanLook(false);
                                                 }
                                                 MelonLogger.Msg($"Started resizing unpainted canvas: {targetCanvas.name} via handle {go.name} (baseline size: {initialCanvasWidth}x{initialCanvasHeight})");
                                             }
                                         }
                                     }
                                 }
                             }
                        }
                        else
                        {
                            // Canvas is painted - hide handles
                            if (isResizing)
                            {
                                isResizing = false;
                                activeResizeCanvas = null;
                                draggedHandleName = null;
                                if (PlayerCamera.InstanceExists && PlayerCamera.Instance != null)
                                {
                                    PlayerCamera.Instance.SetCanLook(true);
                                }
                            }
                            HideAllOtherHandles(null);
                        }
                    }
                    else
                    {
                        // No canvas nearby - hide handles
                        if (isResizing)
                        {
                            isResizing = false;
                            activeResizeCanvas = null;
                            draggedHandleName = null;
                            if (PlayerCamera.InstanceExists && PlayerCamera.Instance != null)
                            {
                                PlayerCamera.Instance.SetCanLook(true);
                            }
                        }
                        HideAllOtherHandles(null);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in OnUpdate: {ex}");
            }
        }

        public override void OnLateUpdate()
        {
            try
            {
                if (Patch_Update.IsAnyCanvasOpen && Patch_Update.activeOpenInteraction != null)
                {
                    var interaction = Patch_Update.activeOpenInteraction;
                    var cam = GetMainCamera();
                    if (interaction.CameraPosition != null && cam != null)
                    {
                        // Calculate target position and force camera position/rotation
                        UnityEngine.Vector3 targetPos = Patch_Update.initialCameraPos + 
                            (interaction.CameraPosition.right * Patch_Update.cameraOffset.x) + 
                            (interaction.CameraPosition.up * Patch_Update.cameraOffset.y);
                        
                        interaction.CameraPosition.position = targetPos;
                        
                        cam.transform.position = targetPos;
                        cam.transform.rotation = interaction.CameraPosition.rotation;

                        // Override CameraContainer position and rotation in PlayerCamera
                        if (PlayerCamera.InstanceExists && PlayerCamera.Instance != null && PlayerCamera.Instance.CameraContainer != null)
                        {
                            PlayerCamera.Instance.CameraContainer.position = targetPos;
                            PlayerCamera.Instance.CameraContainer.rotation = interaction.CameraPosition.rotation;
                        }

                        if (UnityEngine.Time.frameCount % 60 == 0)
                        {
                            MelonLogger.Msg($"[OnLateUpdate Diag] Camera: {cam.name}, Parent: {(cam.transform.parent == null ? "None" : cam.transform.parent.name)}, Offset: {Patch_Update.cameraOffset}, TargetPos: {targetPos}, ActualPos: {cam.transform.position}, LocalPos: {cam.transform.localPosition}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in OnLateUpdate camera force: {ex}");
            }
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
                var placedGuids = new System.Collections.Generic.HashSet<string>();
                try
                {
                    string path = "UserData/PlacedCanvases.json";
                    if (System.IO.File.Exists(path))
                    {
                        string json = System.IO.File.ReadAllText(path);
                        var data = DeserializeCustomJson(json);
                        if (data != null && data.canvases != null)
                        {
                            foreach (var c in data.canvases)
                            {
                                placedGuids.Add(c.guid);
                            }
                        }
                    }
                }
                catch {}

                foreach (var s in surfaces)
                {
                    if (s.gameObject.activeInHierarchy && s.gameObject.scene.name != null)
                    {
                        var ws = s.TryCast<WorldSpraySurface>();
                        if (ws != null)
                        {
                            string guidStr = ws.GUID.ToString();
                            if (placedGuids.Contains(guidStr)) continue;
                        }

                        bool hasDrawings = false;
                        try
                        {
                            hasDrawings = (s.DrawingStrokeCount > 0 || s.DrawingPaintedPixelCount > 0);
                        }
                        catch {}
                        if (hasDrawings) continue;

                        activeSurfaces.Add(s);
                    }
                }

                if (activeSurfaces.Count == 0)
                {
                    foreach (var s in surfaces)
                    {
                        if (s.gameObject.activeInHierarchy && s.gameObject.scene.name != null)
                        {
                            activeSurfaces.Add(s);
                        }
                    }
                }

                if (activeSurfaces.Count == 0) return null;

                int index = lastTeleportedIndex % activeSurfaces.Count;
                lastTeleportedIndex++;
                var selected = activeSurfaces[index];
                try
                {
                    LogHierarchy("SpraySurface", selected.transform);
                    var inter = FindInteractionForSurface(selected);
                    if (inter != null)
                    {
                        LogHierarchy("Interaction", inter.transform);
                        if (inter.CameraPosition != null) LogHierarchy("CameraPos", inter.CameraPosition);
                        if (inter.IntObj != null) LogHierarchy("IntObj", inter.IntObj.transform);
                    }
                    else
                    {
                        MelonLogger.Msg("No interaction component found for SpraySurface!");
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in LogHierarchy: {ex}");
                }
                return selected;
            }
            catch
            {
                return null;
            }
        }

        public static void LogHierarchy(string context, Transform t)
        {
            if (t == null) return;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(context + ": ");
            Transform current = t;
            while (current != null)
            {
                sb.Append(current.name);
                var comps = current.GetComponents<Component>();
                sb.Append(" [");
                foreach (var c in comps)
                {
                    if (c != null) sb.Append(c.GetType().Name + ", ");
                }
                sb.Append("]");
                current = current.parent;
                if (current != null) sb.Append(" -> ");
            }
            MelonLogger.Msg(sb.ToString());
        }

        public static SpraySurfaceInteraction FindInteractionForSurface(SpraySurface surface)
        {
            if (surface == null) return null;
            var interaction = surface.GetComponentInParent<SpraySurfaceInteraction>() ??
                              surface.GetComponentInChildren<SpraySurfaceInteraction>() ??
                              surface.GetComponent<SpraySurfaceInteraction>();
            if (interaction != null) return interaction;

            var interactions = Resources.FindObjectsOfTypeAll<SpraySurfaceInteraction>();
            if (interactions != null)
            {
                foreach (var inter in interactions)
                {
                    if (inter.SpraySurface == surface)
                    {
                        return inter;
                    }
                }
            }
            return null;
        }

        public static void DisableNetworkTransform(GameObject go)
        {
            if (go == null) return;
            try
            {
                var comps = go.GetComponentsInChildren<Component>();
                if (comps != null)
                {
                    foreach (var c in comps)
                    {
                        if (c == null) continue;

                        string typeName = "";
                        try { typeName = c.GetIl2CppType().FullName; } catch {}
                        if (string.IsNullOrEmpty(typeName))
                        {
                            try { typeName = c.ToString(); } catch {}
                        }

                        MelonLogger.Msg($"Component on {go.name}: {typeName} (Type: {c.GetType().Name})");

                        if (typeName != null && typeName.Contains("NetworkTransform"))
                        {
                            try
                            {
                                var behaviour = c.TryCast<Behaviour>();
                                if (behaviour != null) behaviour.enabled = false;
                            }
                            catch {}

                            try
                            {
                                UnityEngine.Object.Destroy(c);
                                MelonLogger.Msg($"Successfully destroyed component {typeName} on {go.name}");
                            }
                            catch (System.Exception ex)
                            {
                                MelonLogger.Error($"Failed to destroy component {typeName}: {ex}");
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in DisableNetworkTransform: {ex}");
            }
        }

        public static void RepositionCanvas(SpraySurface surface, Vector3 position, Quaternion rotation)
        {
            if (surface == null) return;

            // Force drawing initialization
            try
            {
                surface.EnsureDrawingExists();
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error ensuring drawing exists: {ex}");
            }

            if (surface.Projector != null)
            {
                try
                {
                    var proj = surface.Projector;
                    proj.enabled = true;
                    proj.renderingLayerMask = uint.MaxValue;
                    
                    // drawOrder does not exist in this URP version, sorting is done via relative renderQueue adjustments

                    // Get SprayDisplay component and update its cachedMaterial field
                    var display = surface.GetComponent<SprayDisplay>() ?? surface.GetComponentInChildren<SprayDisplay>();
                    if (display != null)
                    {
                        try
                        {
                            var cachedMaterialField = typeof(SprayDisplay).GetField("NativeFieldInfoPtr_cachedMaterial", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                            if (cachedMaterialField != null)
                            {
                                System.IntPtr fieldPtr = (System.IntPtr)cachedMaterialField.GetValue(null);
                                if (fieldPtr != System.IntPtr.Zero)
                                {
                                    var cachedMat = proj.material;
                                    if (cachedMat != null)
                                    {
                                        if (!cachedMat.name.Contains("(Instance)"))
                                        {
                                            cachedMat = UnityEngine.Object.Instantiate(cachedMat);
                                            proj.material = cachedMat;
                                        }

                                        int highestQueue = GetHighestQueueInRange(position, 4.5f);
                                        int targetQueue = highestQueue + 5;
                                        if (targetQueue < 2050) targetQueue = 2050;
                                        if (targetQueue > 2950) targetQueue = 2950;
                                        cachedMat.renderQueue = targetQueue;
                                        MelonLogger.Msg($"[Decal Setup] Assigned cachedMat.renderQueue = {targetQueue} (highest in area was {highestQueue})");

                                        unsafe
                                        {
                                            Il2CppInterop.Runtime.IL2CPP.il2cpp_field_set_value(display.Pointer, fieldPtr, (void*)cachedMat.Pointer);
                                        }

                                        if (surface.DrawingOutputTexture != null)
                                        {
                                            try
                                            {
                                                var propNames = cachedMat.GetTexturePropertyNames();
                                                if (propNames != null)
                                                {
                                                    foreach (var name in propNames)
                                                    {
                                                        cachedMat.SetTexture(name, surface.DrawingOutputTexture);
                                                    }
                                                }
                                            }
                                            catch {}

                                            cachedMat.SetTexture("_BaseMap", surface.DrawingOutputTexture);
                                            cachedMat.SetTexture("_MainTex", surface.DrawingOutputTexture);
                                            cachedMat.SetTexture("_Base_Map", surface.DrawingOutputTexture);
                                        }
                                    }
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            MelonLogger.Error($"Error setting cachedMaterial via reflection: {ex}");
                        }
                    }
                    else if (proj.material != null)
                    {
                        // Fallback if SprayDisplay is not found
                        if (!proj.material.name.Contains("(Instance)"))
                        {
                            proj.material = UnityEngine.Object.Instantiate(proj.material);
                        }
                        int highestQueue = GetHighestQueueInRange(position, 4.5f);
                        int targetQueue = highestQueue + 5;
                        if (targetQueue < 2050) targetQueue = 2050;
                        if (targetQueue > 2950) targetQueue = 2950;
                        proj.material.renderQueue = targetQueue;
                        MelonLogger.Msg($"[Decal Setup] Assigned fallback material.renderQueue = {targetQueue} (highest in area was {highestQueue})");
                        
                        if (surface.DrawingOutputTexture != null)
                        {
                            try
                            {
                                var propNames = proj.material.GetTexturePropertyNames();
                                if (propNames != null)
                                {
                                    foreach (var name in propNames)
                                    {
                                        proj.material.SetTexture(name, surface.DrawingOutputTexture);
                                    }
                                }
                            }
                            catch {}
                            proj.material.SetTexture("_BaseMap", surface.DrawingOutputTexture);
                            proj.material.SetTexture("_MainTex", surface.DrawingOutputTexture);
                            proj.material.SetTexture("_Base_Map", surface.DrawingOutputTexture);
                        }
                    }

                    MelonLogger.Msg($"[Decal Info] Name: {proj.gameObject.name}, Enabled: {proj.enabled}, Active: {proj.gameObject.activeInHierarchy}");
                    MelonLogger.Msg($"[Decal Info] Size: {proj.size.ToString()}, FadeFactor: {proj.fadeFactor}");
                    MelonLogger.Msg($"[Decal Info] Material: {(proj.material != null ? proj.material.name : "null")}");
                    MelonLogger.Msg($"[Decal Info] Surface Global Pos: {surface.transform.position}, Rot: {surface.transform.rotation.eulerAngles}");
                    MelonLogger.Msg($"[Decal Info] Projector Local Pos: {proj.transform.localPosition}, Rot: {proj.transform.localRotation.eulerAngles}");
                    MelonLogger.Msg($"[Decal Info] Projector Global Pos: {proj.transform.position}, Rot: {proj.transform.rotation.eulerAngles}");
                    
                    if (surface.DrawingOutputTexture != null)
                    {
                        MelonLogger.Msg($"[Decal Info] DrawingOutputTexture: {surface.DrawingOutputTexture.name} (w={surface.DrawingOutputTexture.width}, h={surface.DrawingOutputTexture.height})");
                    }
                    else
                    {
                        MelonLogger.Msg($"[Decal Info] DrawingOutputTexture is null!");
                    }

                    if (proj.material != null)
                    {
                        try
                        {
                            var propNames = proj.material.GetTexturePropertyNames();
                            if (propNames != null)
                            {
                                foreach (var name in propNames)
                                {
                                    var tex = proj.material.GetTexture(name);
                                    MelonLogger.Msg($"[Decal Info] Material Texture Prop: {name} = {(tex != null ? tex.name : "null")}");
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            MelonLogger.Error($"Error printing texture property names: {ex}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error reading DecalProjector properties: {ex}");
                }
            }
            else
            {
                MelonLogger.Msg("[Decal Info] surface.Projector is null!");
            }

            // Force wall renderers in range to receive decals
            try
            {
                var hitColliders = Physics.OverlapSphere(position, 2.0f);
                if (hitColliders != null)
                {
                    foreach (var col in hitColliders)
                    {
                        if (col == null) continue;
                        
                        var renderers = col.GetComponentsInParent<Renderer>();
                        if (renderers != null)
                        {
                            foreach (var r in renderers)
                            {
                                if (r == null) continue;
                                try
                                {
                                    var prop = r.GetType().GetProperty("receiveDecals") ?? r.GetType().GetProperty("ReceiveDecals");
                                    if (prop != null)
                                    {
                                        prop.SetValue(r, true);
                                    }
                                    else
                                    {
                                        var setMethod = r.GetType().GetMethod("set_receiveDecals") ?? r.GetType().GetMethod("set_ReceiveDecals");
                                        if (setMethod != null)
                                        {
                                            setMethod.Invoke(r, new object[] { true });
                                        }
                                    }
                                }
                                catch {}
                            }
                        }

                        var childRenderers = col.GetComponentsInChildren<Renderer>();
                        if (childRenderers != null)
                        {
                            foreach (var r in childRenderers)
                            {
                                if (r == null) continue;
                                try
                                {
                                    var prop = r.GetType().GetProperty("receiveDecals") ?? r.GetType().GetProperty("ReceiveDecals");
                                    if (prop != null)
                                    {
                                        prop.SetValue(r, true);
                                    }
                                    else
                                    {
                                        var setMethod = r.GetType().GetMethod("set_receiveDecals") ?? r.GetType().GetMethod("set_ReceiveDecals");
                                        if (setMethod != null)
                                        {
                                            setMethod.Invoke(r, new object[] { true });
                                        }
                                    }
                                }
                                catch {}
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error setting receiveDecals: {ex}");
            }

            var interaction = FindInteractionForSurface(surface);
            if (interaction != null)
            {
                var interGo = interaction.gameObject;
                var surfGo = surface.gameObject;

                DisableNetworkTransform(interGo);
                DisableNetworkTransform(surfGo);

                interGo.transform.localScale = UnityEngine.Vector3.one;
                surfGo.transform.localScale = UnityEngine.Vector3.one;

                if (interGo == surfGo)
                {
                    interGo.transform.position = position;
                    interGo.transform.rotation = rotation;
                }
                else if (surfGo.transform.IsChildOf(interGo.transform))
                {
                    interGo.transform.position = position;
                    interGo.transform.rotation = rotation;
                }
                else if (interGo.transform.IsChildOf(surfGo.transform))
                {
                    surfGo.transform.position = position;
                    surfGo.transform.rotation = rotation;
                }
                else
                {
                    interGo.transform.position = position;
                    interGo.transform.rotation = rotation;
                    surfGo.transform.position = position;
                    surfGo.transform.rotation = rotation;
                }

                // Manually calculate and force world coordinates to bypass any static transform locking/batching
                if (interaction.CameraPosition != null)
                {
                    // Position camera 1.5 meters back from canvas center, looking straight at it
                    interaction.CameraPosition.position = position + rotation * Vector3.forward * 1.5f;
                    interaction.CameraPosition.rotation = rotation * Quaternion.Euler(0, 180f, 0);
                }
                if (interaction.IntObj != null)
                {
                    // Center the interaction collider on the canvas center
                    interaction.IntObj.transform.position = position;
                    interaction.IntObj.transform.rotation = rotation;
                    // Disable the angle limit so the player can interact directly from the front
                    interaction.IntObj.LimitInteractionAngle = false;
                }
            }
            else
            {
                DisableNetworkTransform(surface.gameObject);
                surface.gameObject.transform.position = position;
                surface.gameObject.transform.rotation = rotation;
                surface.gameObject.transform.localScale = UnityEngine.Vector3.one;
            }
        }

        public static void SetCollidersEnabled(SpraySurface surface, bool enabled)
        {
            if (surface == null) return;

            var surfColliders = surface.GetComponentsInChildren<Collider>();
            foreach (var col in surfColliders)
            {
                col.enabled = enabled;
            }

            var interaction = FindInteractionForSurface(surface);
            if (interaction != null && interaction.gameObject != surface.gameObject)
            {
                var interColliders = interaction.GetComponentsInChildren<Collider>();
                foreach (var col in interColliders)
                {
                    col.enabled = enabled;
                }
            }
        }

        private static void UpdatePlacementPreview()
        {
            var cam = GetMainCamera();
            if (currentPreviewSurface == null || cam == null) return;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            int mask = ~LayerMask.GetMask("Player", "Ignore Raycast");
            
            var hits = Physics.RaycastAll(ray, 10f, mask);
            if (hits != null && hits.Length > 0)
            {
                // Sort by distance (closest first)
                System.Array.Sort(hits, new System.Comparison<RaycastHit>((a, b) => a.distance.CompareTo(b.distance)));

                RaycastHit bestHit = default(RaycastHit);
                bool found = false;

                foreach (var hit in hits)
                {
                    if (hit.collider == null) continue;
                    if (hit.collider.isTrigger) continue;

                    var go = hit.collider.gameObject;

                    // Allow snapping to existing canvases
                    bool isCanvas = go.GetComponentInParent<SpraySurface>() != null || 
                                   go.GetComponentInChildren<SpraySurface>() != null || 
                                   go.GetComponent<SpraySurface>() != null;

                    // Snapping to static environment walls
                    bool isStatic = go.isStatic || (go.transform.parent != null && go.transform.parent.gameObject.isStatic);
                    bool isMeshCol = hit.collider is MeshCollider;

                    if (isCanvas || isStatic || isMeshCol)
                    {
                        bestHit = hit;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // Fallback to the closest non-trigger hit if no static environment was matched
                    foreach (var hit in hits)
                    {
                        if (hit.collider != null && !hit.collider.isTrigger)
                        {
                            bestHit = hit;
                            found = true;
                            break;
                        }
                    }
                }

                if (found)
                {
                    RepositionCanvas(currentPreviewSurface, bestHit.point + bestHit.normal * 0.05f, Quaternion.LookRotation(bestHit.normal, Vector3.up));
                }
            }
        }

        private static void FinishPlacement()
        {
            if (currentPreviewSurface == null) return;

            SetCollidersEnabled(currentPreviewSurface, true);
            MelonLogger.Msg($"Finishing placement for: {currentPreviewSurface.name}");
            
            try
            {
                var inter = FindInteractionForSurface(currentPreviewSurface);
                MelonLogger.Msg($"Canvas final position: {currentPreviewSurface.gameObject.transform.position.ToString()}");
                if (inter != null && inter.CameraPosition != null)
                {
                    MelonLogger.Msg($"Camera final position: {inter.CameraPosition.position.ToString()}");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error printing final coordinates: {ex}");
            }

            HideOverlappingGraffitiMeshes(currentPreviewSurface);
            try
            {
                var worldSurf = currentPreviewSurface.TryCast<WorldSpraySurface>();
                if (worldSurf != null)
                {
                    SaveCanvasPosition(worldSurf);
                }
            }
            catch {}
            currentPreviewSurface = null;
        }

        private static void TryRemoveCanvas()
        {
            var cam = GetMainCamera();
            if (cam == null) return;
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            RaycastHit hit;
            int mask = ~LayerMask.GetMask("Player", "Ignore Raycast");
            if (Physics.Raycast(ray, out hit, 10f, mask))
            {
                var surface = hit.collider.GetComponentInParent<SpraySurface>() ??
                              hit.collider.GetComponentInChildren<SpraySurface>() ??
                              hit.collider.GetComponent<SpraySurface>();

                if (surface != null)
                {
                    // Re-enable hidden renderers
                    if (hiddenRenderersMap.TryGetValue(surface, out var list))
                    {
                        foreach (var r in list)
                        {
                            if (r != null)
                            {
                                r.enabled = true;
                                MelonLogger.Msg($"[Decal Cleanup] Restored overlapping graffiti mesh: {r.gameObject.name}");
                            }
                        }
                        hiddenRenderersMap.Remove(surface);
                    }
                    surface.ClearDrawing();
                    try
                    {
                        var worldSurf = surface.TryCast<WorldSpraySurface>();
                        if (worldSurf != null)
                        {
                            RemoveCanvasPosition(worldSurf.GUID.ToString());
                        }
                    }
                    catch {}
                    RepositionCanvas(surface, Vector3.down * 1000f, Quaternion.identity);
                    MelonLogger.Msg($"Removed spray paint canvas successfully.");
                }
            }
        }

        private static void TryRemoveCanvasBetter()
        {
            try
            {
                var cam = GetMainCamera();
                if (cam == null) return;

                UnityEngine.Vector3 rayOrigin = cam.transform.position;
                UnityEngine.Vector3 rayDir = cam.transform.forward;

                var surfaces = Resources.FindObjectsOfTypeAll<SpraySurface>();
                if (surfaces == null || surfaces.Count == 0) return;

                SpraySurface bestTarget = null;
                float bestScore = float.MaxValue;

                foreach (var s in surfaces)
                {
                    if (s == null || !s.gameObject.activeInHierarchy) continue;
                    if (s.gameObject.transform.position.y < -500f) continue;

                    UnityEngine.Vector3 canvasPos = s.transform.position;
                    UnityEngine.Vector3 toCanvas = canvasPos - rayOrigin;
                    float distance = toCanvas.magnitude;

                    if (distance > 15f) continue;

                    UnityEngine.Vector3 dirToCanvas = toCanvas / distance;
                    float dot = UnityEngine.Vector3.Dot(rayDir, dirToCanvas);

                    if (dot < 0.5f) continue;

                    UnityEngine.Vector3 planeNormal = s.transform.forward;
                    float dotNormal = UnityEngine.Vector3.Dot(rayDir, planeNormal);

                    bool isLookingDirectlyAtCanvas = false;
                    if (UnityEngine.Mathf.Abs(dotNormal) > 0.0001f)
                    {
                        float t = UnityEngine.Vector3.Dot(canvasPos - rayOrigin, planeNormal) / dotNormal;
                        if (t >= 0f && t <= 15f)
                        {
                            UnityEngine.Vector3 hitPoint = rayOrigin + rayDir * t;
                            UnityEngine.Vector3 localHit = s.transform.InverseTransformPoint(hitPoint);

                            float w_m = s.Width * PIXEL_SIZE / 2f;
                            float h_m = s.Height * PIXEL_SIZE / 2f;

                            if (localHit.x >= -w_m - 0.2f && localHit.x <= w_m + 0.2f &&
                                localHit.y >= -h_m - 0.2f && localHit.y <= h_m + 0.2f)
                            {
                                isLookingDirectlyAtCanvas = true;
                            }
                        }
                    }

                    float angleDiff = 1f - dot;
                    float score = distance * angleDiff;

                    if (isLookingDirectlyAtCanvas)
                    {
                        score = -1000f + distance;
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestTarget = s;
                    }
                }

                if (bestTarget != null)
                {
                    MelonLogger.Msg($"[Better Cleanup] Target canvas identified: {bestTarget.name} at distance {UnityEngine.Vector3.Distance(bestTarget.transform.position, rayOrigin):F2}m.");
                    
                    if (hiddenRenderersMap.TryGetValue(bestTarget, out var list))
                    {
                        foreach (var r in list)
                        {
                            if (r != null) r.enabled = true;
                        }
                        hiddenRenderersMap.Remove(bestTarget);
                    }

                    bestTarget.ClearDrawing();

                    try
                    {
                        var worldSurf = bestTarget.TryCast<WorldSpraySurface>();
                        if (worldSurf != null)
                        {
                            RemoveCanvasPosition(worldSurf.GUID.ToString());
                        }
                    }
                    catch {}

                    RepositionCanvas(bestTarget, UnityEngine.Vector3.down * 1000f, UnityEngine.Quaternion.identity);
                    MelonLogger.Msg($"[Better Cleanup] Removed spray paint canvas successfully.");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in TryRemoveCanvasBetter: {ex}");
            }
        }

        private static void SaveCanvasPosition(WorldSpraySurface surface)
        {
            try
            {
                string path = "UserData/PlacedCanvases.json";
                var data = new SaveCanvasData();
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    data = DeserializeCustomJson(json) ?? new SaveCanvasData();
                }

                string guidStr = surface.GUID.ToString();

                CanvasTransformData toRemove = null;
                foreach (var c in data.canvases)
                {
                    if (c.guid == guidStr)
                    {
                        toRemove = c;
                        break;
                    }
                }
                if (toRemove != null)
                {
                    data.canvases.Remove(toRemove);
                }

                var entry = new CanvasTransformData();
                entry.guid = guidStr;
                entry.px = surface.transform.position.x;
                entry.py = surface.transform.position.y;
                entry.pz = surface.transform.position.z;
                entry.rx = surface.transform.rotation.x;
                entry.ry = surface.transform.rotation.y;
                entry.rz = surface.transform.rotation.z;
                entry.rw = surface.transform.rotation.w;
                data.canvases.Add(entry);

                string newJson = SerializeCustomJson(data);
                System.IO.File.WriteAllText(path, newJson);
                MelonLogger.Msg($"[Save/Load] Saved canvas position for GUID: {guidStr}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error saving canvas position: {ex}");
            }
        }

        private static void RemoveCanvasPosition(string guidStr)
        {
            try
            {
                string path = "UserData/PlacedCanvases.json";
                if (!System.IO.File.Exists(path)) return;

                string json = System.IO.File.ReadAllText(path);
                var data = DeserializeCustomJson(json);
                if (data == null || data.canvases == null) return;

                CanvasTransformData toRemove = null;
                foreach (var c in data.canvases)
                {
                    if (c.guid == guidStr)
                    {
                        toRemove = c;
                        break;
                    }
                }
                if (toRemove != null)
                {
                    data.canvases.Remove(toRemove);
                }

                string newJson = SerializeCustomJson(data);
                System.IO.File.WriteAllText(path, newJson);
                MelonLogger.Msg($"[Save/Load] Removed canvas position for GUID: {guidStr}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error removing canvas position: {ex}");
            }
        }

        public static void RestoreCanvasPositions()
        {
            try
            {
                string path = "UserData/PlacedCanvases.json";
                if (!System.IO.File.Exists(path)) return;

                string json = System.IO.File.ReadAllText(path);
                var data = DeserializeCustomJson(json);
                if (data == null || data.canvases == null) return;

                var surfaces = Resources.FindObjectsOfTypeAll<WorldSpraySurface>();
                if (surfaces == null) return;

                int count = 0;
                foreach (var tData in data.canvases)
                {
                    foreach (var s in surfaces)
                    {
                        if (s != null && s.GUID.ToString() == tData.guid)
                        {
                            s.transform.position = new Vector3(tData.px, tData.py, tData.pz);
                            s.transform.rotation = new Quaternion(tData.rx, tData.ry, tData.rz, tData.rw);

                            if (s.Projector != null)
                            {
                                s.Projector.transform.localPosition = new Vector3(0f, 0f, s.Projector.transform.localPosition.z);
                            }
                            count++;
                            break;
                        }
                    }
                }
                MelonLogger.Msg($"[Save/Load] Restored {count} canvas positions.");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error restoring canvas positions: {ex}");
            }
        }

        private static string SerializeCustomJson(SaveCanvasData data)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"canvases\": [");
            for (int i = 0; i < data.canvases.Count; i++)
            {
                var c = data.canvases[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"guid\": \"{c.guid}\",");
                sb.AppendLine($"      \"px\": {c.px.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
                sb.AppendLine($"      \"py\": {c.py.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
                sb.AppendLine($"      \"pz\": {c.pz.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
                sb.AppendLine($"      \"rx\": {c.rx.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
                sb.AppendLine($"      \"ry\": {c.ry.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
                sb.AppendLine($"      \"rz\": {c.rz.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
                sb.AppendLine($"      \"rw\": {c.rw.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                sb.Append("    }" + (i < data.canvases.Count - 1 ? "," : ""));
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static SaveCanvasData DeserializeCustomJson(string json)
        {
            var data = new SaveCanvasData();
            try
            {
                string[] parts = json.Split(new string[] { "}" }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (!part.Contains("\"guid\"")) continue;
                    var entry = new CanvasTransformData();
                    entry.guid = ExtractJsonField(part, "guid");
                    entry.px = ParseFloat(ExtractJsonField(part, "px"));
                    entry.py = ParseFloat(ExtractJsonField(part, "py"));
                    entry.pz = ParseFloat(ExtractJsonField(part, "pz"));
                    entry.rx = ParseFloat(ExtractJsonField(part, "rx"));
                    entry.ry = ParseFloat(ExtractJsonField(part, "ry"));
                    entry.rz = ParseFloat(ExtractJsonField(part, "rz"));
                    entry.rw = ParseFloat(ExtractJsonField(part, "rw"));
                    data.canvases.Add(entry);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error deserializing custom JSON: {ex}");
            }
            return data;
        }

        private static string ExtractJsonField(string json, string field)
        {
            string key = "\"" + field + "\"";
            int idx = json.IndexOf(key);
            if (idx == -1) return "";
            int start = json.IndexOf(":", idx);
            if (start == -1) return "";
            start++;
            int end = json.IndexOf(",", start);
            if (end == -1) end = json.IndexOf("\n", start);
            if (end == -1) end = json.Length;

            string val = json.Substring(start, end - start).Trim().Trim('"');
            return val;
        }

        private static float ParseFloat(string s)
        {
            if (float.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                return val;
            }
            return 0f;
        }

        [System.Serializable]
        public class CanvasTransformData
        {
            public string guid;
            public float px, py, pz;
            public float rx, ry, rz, rw;
        }

        [System.Serializable]
        public class SaveCanvasData
        {
            public System.Collections.Generic.List<CanvasTransformData> canvases = new System.Collections.Generic.List<CanvasTransformData>();
        }
    }

    [HarmonyPatch(typeof(SpraySurfaceInteraction), "Update")]
    public static class Patch_Update
    {
        public static bool IsAnyCanvasOpen = false;
        public static SpraySurfaceInteraction activeOpenInteraction = null;
        public static UnityEngine.Vector3 initialCameraPos = UnityEngine.Vector3.zero;
        public static UnityEngine.Vector2 cameraOffset = UnityEngine.Vector2.zero;
        public static bool enteredPaintingThisFrame = false;

        public static void Postfix(SpraySurfaceInteraction __instance)
        {
            try
            {
                if (__instance.IsOpen)
                {
                    IsAnyCanvasOpen = true;
                    activeOpenInteraction = __instance;
                    
                    __instance.PaintedPixelLimitMultiplier = 80000f;
                    __instance._allowDraw = true;
                    if (__instance.SpraySurface != null && __instance.SpraySurface.Projector != null)
                    {
                        __instance.SpraySurface.Projector.enabled = true;
                    }

                    // WASD camera panning logic
                    var mainCam = ThrowUp.GetMainCamera();
                    if (__instance.CameraPosition != null && mainCam != null)
                    {
                        if (!enteredPaintingThisFrame)
                        {
                            initialCameraPos = __instance.CameraPosition.position;
                            cameraOffset = UnityEngine.Vector2.zero;
                            enteredPaintingThisFrame = true;
                            MelonLogger.Msg($"Entered painting. Stored initial camera position: {initialCameraPos}");

                                ThrowUp.EnsureCanvasIsHuge(__instance.SpraySurface);
                        }

                        float horiz = 0f;
                        float vert = 0f;
                        bool hasNewKeyboard = false;
                        string kbStateStr = "None";
                        try
                        {
                            var keyboard = UnityEngine.InputSystem.Keyboard.current;
                            if (keyboard != null)
                            {
                                if (keyboard[UnityEngine.InputSystem.Key.W].isPressed) vert += 1f;
                                if (keyboard[UnityEngine.InputSystem.Key.S].isPressed) vert -= 1f;
                                if (keyboard[UnityEngine.InputSystem.Key.D].isPressed) horiz += 1f;
                                if (keyboard[UnityEngine.InputSystem.Key.A].isPressed) horiz -= 1f;
                                hasNewKeyboard = true;
                                kbStateStr = $"NewInput (W={keyboard[UnityEngine.InputSystem.Key.W].isPressed},S={keyboard[UnityEngine.InputSystem.Key.S].isPressed},D={keyboard[UnityEngine.InputSystem.Key.D].isPressed},A={keyboard[UnityEngine.InputSystem.Key.A].isPressed})";
                            }
                            else
                            {
                                kbStateStr = "NewInput_Null";
                            }
                        }
                        catch (System.Exception ex)
                        {
                            kbStateStr = $"NewInput_Error ({ex.Message})";
                        }

                        if (!hasNewKeyboard)
                        {
                            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.W)) vert += 1f;
                            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.S)) vert -= 1f;
                            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.D)) horiz += 1f;
                            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.A)) horiz -= 1f;
                            kbStateStr += " + LegacyFallback";
                        }

                        // Clamp the distance relative to the canvas size!
                        float maxPanX = 3.0f;
                        float maxPanY = 3.0f;
                        if (__instance.SpraySurface != null)
                        {
                            maxPanX = (__instance.SpraySurface.Width * 0.006666671f) / 2f + 0.5f;
                            maxPanY = (__instance.SpraySurface.Height * 0.006666671f) / 2f + 0.5f;
                        }

                        if (horiz != 0f || vert != 0f)
                        {
                            float speed = 2.0f; // 2 meters per second
                            cameraOffset.x += horiz * speed * UnityEngine.Time.deltaTime;
                            cameraOffset.y += vert * speed * UnityEngine.Time.deltaTime;
                        }

                        // Clamp the accumulated offset
                        cameraOffset.x = UnityEngine.Mathf.Clamp(cameraOffset.x, -maxPanX, maxPanX);
                        cameraOffset.y = UnityEngine.Mathf.Clamp(cameraOffset.y, -maxPanY, maxPanY);

                        if (UnityEngine.Time.frameCount % 60 == 0)
                        {
                            MelonLogger.Msg($"[Diag Keyboard] horiz={horiz}, vert={vert}, offset={cameraOffset}, kbState={kbStateStr}");
                        }

                        // Reconstruct target position and force camera
                        UnityEngine.Vector3 targetPos = initialCameraPos + (__instance.CameraPosition.right * cameraOffset.x) + (__instance.CameraPosition.up * cameraOffset.y);
                        __instance.CameraPosition.position = targetPos;
                        var cam = ThrowUp.GetMainCamera();
                        if (cam != null)
                        {
                            cam.transform.position = targetPos;
                        }
                    }
                }
                else if (__instance == activeOpenInteraction)
                {
                    IsAnyCanvasOpen = false;
                    if (enteredPaintingThisFrame)
                    {
                        // Reset camera position back to initial position on exit
                        if (__instance.CameraPosition != null && initialCameraPos != UnityEngine.Vector3.zero)
                        {
                            __instance.CameraPosition.position = initialCameraPos;
                            var cam = ThrowUp.GetMainCamera();
                            if (cam != null)
                            {
                                cam.transform.position = initialCameraPos;
                            }
                            MelonLogger.Msg("Exited painting. Reset camera position.");
                        }

                        // Disable the collider on completed canvas so it doesn't block overlapping canvas placements/interactions
                        if (__instance.SpraySurface != null)
                        {
                            bool hasStrokes = false;
                            try
                            {
                                hasStrokes = (__instance.SpraySurface.DrawingStrokeCount > 0 || __instance.SpraySurface.DrawingPaintedPixelCount > 0);
                            }
                            catch {}

                            if (hasStrokes)
                            {
                                ThrowUp.SetCollidersEnabled(__instance.SpraySurface, false);
                                MelonLogger.Msg($"Disabled collider for completed canvas: {__instance.SpraySurface.name}");
                            }
                        }

                        enteredPaintingThisFrame = false;
                        initialCameraPos = UnityEngine.Vector3.zero;
                        cameraOffset = UnityEngine.Vector2.zero;
                    }
                    activeOpenInteraction = null;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in painting WASD update: {ex}");
            }
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
                    if (__instance.SpraySurface != null && __instance.SpraySurface.Projector != null)
                    {
                        __instance.SpraySurface.Projector.enabled = true;
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
                            ThrowUp.RepositionCanvas(__instance, hit.point + hit.normal * 0.05f, Quaternion.LookRotation(-hit.normal, Vector3.up));
                            MelonLogger.Msg($"Teleported remote player's SpraySurface target locally to match target wall!");
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


    [HarmonyPatch(typeof(SpraySurface), "AddStrokes_Server")]
    public static class Patch_AddStrokes_Server
    {
        public static void Prefix(SpraySurface __instance, object newStrokes, int requestID)
        {
            try
            {
                MelonLogger.Msg($"AddStrokes_Server CALLED on canvas {__instance?.name}: requestID={requestID}, strokesType={newStrokes?.GetType().FullName}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in AddStrokes_Server trace: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SpraySurface), "AddStrokes_Client")]
    public static class Patch_AddStrokes_Client
    {
        public static void Prefix(SpraySurface __instance, object newStrokes, int requestID)
        {
            try
            {
                MelonLogger.Msg($"AddStrokes_Client CALLED on canvas {__instance?.name}: requestID={requestID}, strokesType={newStrokes?.GetType().FullName}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in AddStrokes_Client trace: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SpraySurface), "AddTextureToHistory_Server")]
    public static class Patch_AddTextureToHistory_Server
    {
        public static void Prefix(SpraySurface __instance, int requestID)
        {
            try
            {
                MelonLogger.Msg($"AddTextureToHistory_Server CALLED: requestID={requestID}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in AddTextureToHistory_Server trace: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SpraySurface), "AddTextureToHistory_Client")]
    public static class Patch_AddTextureToHistory_Client
    {
        public static void Prefix(SpraySurface __instance, int requestID)
        {
            try
            {
                MelonLogger.Msg($"AddTextureToHistory_Client CALLED: requestID={requestID}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in AddTextureToHistory_Client trace: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SprayDisplay), "Redraw")]
    public static class Patch_SprayDisplay_Redraw
    {
        public static void Postfix(SprayDisplay __instance)
        {
            try
            {
                if (__instance != null && __instance.Projector != null && __instance.SpraySurface != null)
                {
                    var proj = __instance.Projector;
                    var surf = __instance.SpraySurface;
                    surf.EnsureDrawingExists();

                    var display = __instance;
                    var cachedMaterialField = typeof(SprayDisplay).GetField("NativeFieldInfoPtr_cachedMaterial", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (cachedMaterialField != null && surf.DrawingOutputTexture != null)
                    {
                        System.IntPtr fieldPtr = (System.IntPtr)cachedMaterialField.GetValue(null);
                        if (fieldPtr != System.IntPtr.Zero)
                        {
                            var cachedMat = proj.material;
                            if (cachedMat != null)
                            {
                                if (!cachedMat.name.Contains("(Instance)"))
                                {
                                    cachedMat = UnityEngine.Object.Instantiate(cachedMat);
                                    proj.material = cachedMat;
                                }

                                unsafe
                                {
                                    Il2CppInterop.Runtime.IL2CPP.il2cpp_field_set_value(display.Pointer, fieldPtr, (void*)cachedMat.Pointer);
                                }

                                int defQueue = cachedMat.renderQueue;
                                if (defQueue == 2000 || defQueue == 2001 || defQueue == 2250 || defQueue == 2251)
                                {
                                    int highestQueue = ThrowUp.GetHighestQueueInRange(surf.transform.position, 4.5f);
                                    int targetQueue = highestQueue + 5;
                                    if (targetQueue < 2050) targetQueue = 2050;
                                    if (targetQueue > 2950) targetQueue = 2950;
                                    cachedMat.renderQueue = targetQueue;
                                    MelonLogger.Msg($"[Decal Setup Redraw] Set cachedMat.renderQueue = {targetQueue} (highest was {highestQueue})");
                                }

                                try
                                {
                                    var propNames = cachedMat.GetTexturePropertyNames();
                                    if (propNames != null)
                                    {
                                        foreach (var name in propNames)
                                        {
                                            cachedMat.SetTexture(name, surf.DrawingOutputTexture);
                                        }
                                    }
                                }
                                catch {}

                                cachedMat.SetTexture("_BaseMap", surf.DrawingOutputTexture);
                                cachedMat.SetTexture("_MainTex", surf.DrawingOutputTexture);
                                cachedMat.SetTexture("_Base_Map", surf.DrawingOutputTexture);
                            }
                        }
                    }
                    else if (proj.material != null && surf.DrawingOutputTexture != null)
                    {
                        if (!proj.material.name.Contains("(Instance)"))
                        {
                            proj.material = UnityEngine.Object.Instantiate(proj.material);
                        }
                        int defQueue = proj.material.renderQueue;
                        if (defQueue == 2000 || defQueue == 2001 || defQueue == 2250 || defQueue == 2251)
                        {
                            int highestQueue = ThrowUp.GetHighestQueueInRange(surf.transform.position, 4.5f);
                            int targetQueue = highestQueue + 5;
                            if (targetQueue < 2050) targetQueue = 2050;
                            if (targetQueue > 2950) targetQueue = 2950;
                            proj.material.renderQueue = targetQueue;
                            MelonLogger.Msg($"[Decal Setup Redraw] Assigned fallback material.renderQueue = {targetQueue} (highest in area was {highestQueue})");
                        }
                        try
                        {
                            var propNames = proj.material.GetTexturePropertyNames();
                            if (propNames != null)
                            {
                                foreach (var name in propNames)
                                {
                                    proj.material.SetTexture(name, surf.DrawingOutputTexture);
                                }
                            }
                        }
                        catch {}
                        proj.material.SetTexture("_BaseMap", surf.DrawingOutputTexture);
                        proj.material.SetTexture("_MainTex", surf.DrawingOutputTexture);
                        proj.material.SetTexture("_Base_Map", surf.DrawingOutputTexture);
                    }

                    proj.enabled = false;
                    proj.enabled = true;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error forcing projector redraw refresh: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SpraySurfaceInteraction), "CheckCameraInBounds")]
    public static class Patch_CheckCameraInBounds
    {
        public static bool Prefix()
        {
            return false; // Skip native camera bounds checks
        }
    }

    [HarmonyPatch(typeof(SpraySurfaceInteraction), "GetCursorPositionOnSurface")]
    public static class Patch_GetCursorPositionOnSurface
    {
        public static bool Prefix(SpraySurfaceInteraction __instance, out ushort pixelX, out ushort pixelY, ref bool __result)
        {
            pixelX = 0;
            pixelY = 0;
            __result = false;
            try
            {
                if (__instance.SpraySurface == null) return true;
                
                var boxCol = ThrowUp.FindMainCanvasCollider(__instance.SpraySurface);
                if (boxCol == null) return true;

                UnityEngine.Camera mainCam = ThrowUp.GetMainCamera();
                if (mainCam == null) return true;

                UnityEngine.Ray ray;
                var rayMethod = typeof(SpraySurfaceInteraction).GetMethod("GetCursorRay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (rayMethod != null)
                {
                    ray = (UnityEngine.Ray)rayMethod.Invoke(__instance, null);
                }
                else
                {
                    ray = new UnityEngine.Ray(mainCam.transform.position, mainCam.transform.forward);
                }

                if (boxCol.Raycast(ray, out var hitInfo, 10f))
                {
                    UnityEngine.Vector3 toHit = hitInfo.point - __instance.SpraySurface.transform.position;
                    
                    float worldX = UnityEngine.Vector3.Dot(toHit, __instance.SpraySurface.transform.right);
                    float worldY = UnityEngine.Vector3.Dot(toHit, __instance.SpraySurface.transform.up);
                    
                    float x = (__instance.SpraySurface.Width / 2f) - (worldX / 0.006666671f);
                    float y = (worldY / 0.006666671f) + (__instance.SpraySurface.Height / 2f);
                    
                    int px = UnityEngine.Mathf.RoundToInt(x);
                    int py = UnityEngine.Mathf.RoundToInt(y);
                    
                    if (px >= 0 && px < __instance.SpraySurface.Width && py >= 0 && py < __instance.SpraySurface.Height)
                    {
                        pixelX = (ushort)px;
                        pixelY = (ushort)py;
                        __result = true;
                        return false; // Override native method
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in GetCursorPositionOnSurface prefix patch: {ex}");
            }
            return true; // Fallback to native mapping if anything errors or doesn't hit
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Persistence.Loaders.GraffitiLoader), "LoadSpraySurface")]
    public static class Patch_GraffitiLoader_LoadSpraySurface
    {
        public static void Prefix(Il2CppScheduleOne.Persistence.Loaders.GraffitiLoader __instance, Il2CppScheduleOne.Persistence.Datas.WorldSpraySurfaceData surfaceData)
        {
            try
            {
                if (surfaceData == null || string.IsNullOrEmpty(surfaceData.GUID)) return;

                ThrowUp.RestoreCanvasPositions();

                var surfaces = Resources.FindObjectsOfTypeAll<WorldSpraySurface>();
                if (surfaces == null) return;

                foreach (var s in surfaces)
                {
                    if (s != null && s.GUID.ToString() == surfaceData.GUID)
                    {
                        ThrowUp.EnsureCanvasIsHuge(s);
                        break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in LoadSpraySurface prefix patch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SpraySurface), "LoadSerializedDrawing")]
    public static class Patch_SpraySurface_LoadSerializedDrawing
    {
        public static void Prefix(SpraySurface __instance, ref SerializedGraffitiDrawing serializedDrawing)
        {
            try
            {
                if (serializedDrawing == null) return;

                int targetWidth = 2048;
                int targetHeight = 2048;

                if (serializedDrawing.Width < targetWidth || serializedDrawing.Height < targetHeight)
                {
                    ushort offsetX = (ushort)((targetWidth - serializedDrawing.Width) / 2);
                    ushort offsetY = (ushort)((targetHeight - serializedDrawing.Height) / 2);

                    var oldStrokes = serializedDrawing.Strokes;
                    if (oldStrokes != null && oldStrokes.Count > 0)
                    {
                        var shiftedStrokes = SprayStroke.CopyAndShiftStrokes(oldStrokes, new UShort2(offsetX, offsetY));
                        
                        var newDrawing = ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.Of<SerializedGraffitiDrawing>()).TryCast<SerializedGraffitiDrawing>();
                        newDrawing.Width = targetWidth;
                        newDrawing.Height = targetHeight;
                        newDrawing.DrawingName = serializedDrawing.DrawingName;
                        newDrawing.SetStrokes(shiftedStrokes);

                        serializedDrawing = newDrawing;
                    }

                    ThrowUp.EnsureCanvasIsHuge(__instance);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in LoadSerializedDrawing prefix patch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(ShopListing), "Initialize")]
    public static class Patch_ShopListing_Initialize
    {
        public static void Postfix(ShopListing __instance)
        {
            try
            {
                if (__instance == null || __instance.Pointer == System.IntPtr.Zero) return;
                if (__instance.Item == null || __instance.Item.Pointer == System.IntPtr.Zero) return;

                string id = __instance.Item.ID;
                string name = __instance.Item.Name;
                if (id == null || name == null) return;

                id = id.ToLower();
                name = name.ToLower();

                if (id.Contains("spray") || id.Contains("paint") || name.Contains("spray") || name.Contains("paint") || id.Contains("spraycan"))
                {
                    __instance.LimitedStock = false;
                    __instance.TieStockToNumberVariable = false;
                    __instance.DefaultStock = 999;
                    
                    try
                    {
                        __instance.SetStock(999, false);
                    }
                    catch {}

                    MelonLogger.Msg($"[InfinitePaintMod] Set LimitedStock = false and DefaultStock = 999 for shop listing: {name} ({id})");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in Patch_ShopListing_Initialize: {ex}");
            }
        }
    }
}
