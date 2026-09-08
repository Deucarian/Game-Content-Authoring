using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentAuthoringObjectPreviewRenderer
    {

        public static void Draw(Rect rect, UnityEngine.Object asset, GameContentAuthoringObjectPreviewOptions options)
        {
            DrawPreviewBackground(rect);

            GameContentAuthoringActionPreview actionPreview = options == null ? null : options.ActionPreview;
            if (actionPreview == null && Event.current != null && Event.current.type == EventType.Repaint)
            {
                if (!TryDrawRenderedGameObject(rect, asset))
                    DrawAssetTexture(rect, asset);
            }

            if (actionPreview != null)
            {
                double now = EditorApplication.timeSinceStartup;
                if (actionPreview.RenderMode == GameContentAuthoringActionPreviewRenderMode.Debug)
                {
                    GameContentPreviewOverlay.DrawActionOverlay(rect, actionPreview, now);
                }
                else if (Event.current != null && Event.current.type == EventType.Repaint)
                {
                    if (!TryDrawGameActionPreview(rect, actionPreview, now))
                        DrawAssetTexture(rect, actionPreview.PrimaryAsset ?? asset);
                }
            }
        }

        private static void DrawPreviewBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, GameContentPreviewScene.PreviewCameraBackground);
            DeucarianEditorVisualShell.DrawInsetSurface(
                rect,
                DeucarianEditorTheme.GlassPanelSoft,
                DeucarianEditorTheme.BorderSubtle,
                7f);
        }

        private static bool TryDrawRenderedGameObject(Rect rect, UnityEngine.Object asset)
        {
            GameObject prefab = asset as GameObject;
            if (prefab == null || rect.width <= 1f || rect.height <= 1f)
            {
                return false;
            }

            GameObject clone = null;
            PreviewRenderUtility utility = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(prefab);
                GameContentPreviewScene.SetPreviewHideFlags(clone);
                GameContentPreviewScene.SimulateParticles(clone, 0.2f);
                Bounds bounds;
                if (!GameContentPreviewScene.TryCalculateBounds(clone, out bounds))
                {
                    return false;
                }

                clone.transform.position -= bounds.center;
                bounds.center = Vector3.zero;

                utility = new PreviewRenderUtility(true);
                utility.cameraFieldOfView = 28f;
                utility.camera.clearFlags = CameraClearFlags.Color;
                utility.camera.backgroundColor = GameContentPreviewScene.PreviewCameraBackground;
                if (utility.lights.Length > 0)
                {
                    utility.lights[0].intensity = 1.55f;
                    utility.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
                }

                if (utility.lights.Length > 1)
                {
                    utility.lights[1].intensity = 1f;
                    utility.lights[1].transform.rotation = Quaternion.Euler(315f, 218f, 0f);
                }

                utility.AddSingleGO(clone);

                float radius = Mathf.Max(0.35f, bounds.extents.magnitude);
                float distance = Mathf.Max(1.5f, radius / Mathf.Sin(utility.cameraFieldOfView * 0.5f * Mathf.Deg2Rad) * 1.28f);
                Vector3 focus = Vector3.zero;
                utility.camera.transform.position = focus + new Vector3(radius * 0.34f, radius * 0.22f, -distance);
                utility.camera.transform.rotation = Quaternion.LookRotation(focus - utility.camera.transform.position, Vector3.up);
                utility.camera.nearClipPlane = 0.01f;
                utility.camera.farClipPlane = distance + radius * 4f;

                utility.BeginPreview(rect, GUIStyle.none);
                utility.Render();
                Texture texture = utility.EndPreview();
                if (texture == null)
                {
                    return false;
                }

                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (clone != null)
                    GameContentAuthoringEditorAssets.DestroyTransientObject(clone);
                if (utility != null)
                    utility.Cleanup();
            }
        }

        private static void DrawAssetTexture(Rect rect, UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return;
            }

            Texture2D texture = AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
            if (texture == null)
            {
                return;
            }

            Rect imageRect = GameContentAuthoringObjectPreviewUtility.FitRect(rect, new Vector2(texture.width, texture.height), 12f);
            GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit, true);
        }

        private static bool TryDrawGameActionPreview(Rect rect, GameContentAuthoringActionPreview preview, double now)
        {
            if (preview == null || rect.width <= 1f || rect.height <= 1f)
                return false;

            var roots = new List<GameObject>();
            PreviewRenderUtility utility = null;
            try
            {
                utility = GameContentPreviewScene.CreatePreviewUtility();
                float time = preview.GetNormalizedTime(now);
                float simulatedTime = Mathf.Lerp(0.08f, Mathf.Max(0.12f, preview.DurationSeconds), time);
                Vector3 sourcePosition = new Vector3(-1.45f, 0f, 0f);
                Vector3 targetPosition = new Vector3(1.45f, 0f, 0f);
                Vector3 centerPosition = Vector3.Lerp(sourcePosition, targetPosition, 0.62f);
                Quaternion facingTarget = Quaternion.LookRotation((targetPosition - sourcePosition).normalized, Vector3.up);

                if (!GameContentPreviewScene.TryAddPrefab(utility, roots, preview.SourcePrefab, sourcePosition, facingTarget, 0.86f, simulatedTime))
                    GameContentPreviewScene.AddPrimitive(utility, roots, PrimitiveType.Cylinder, "Game Preview Origin Emitter", sourcePosition, new Vector3(0.26f, 0.08f, 0.26f), Quaternion.Euler(90f, 0f, 90f));

                if (preview.Mode == GameContentAuthoringActionPreviewMode.Area || preview.Mode == GameContentAuthoringActionPreviewMode.Aura)
                {
                    GameContentPreviewScene.AddTargetInstance(utility, roots, preview.TargetPrefab, centerPosition + new Vector3(-0.35f, 0f, 0.18f), simulatedTime);
                    GameContentPreviewScene.AddTargetInstance(utility, roots, preview.TargetPrefab, centerPosition + new Vector3(0.32f, 0f, -0.12f), simulatedTime);
                }
                else
                {
                    GameContentPreviewScene.AddTargetInstance(utility, roots, preview.TargetPrefab, targetPosition, simulatedTime);
                }

                GameContentPreviewScene.TryAddPrefab(utility, roots, preview.CastVfxPrefab, sourcePosition, Quaternion.identity, 0.72f, simulatedTime);
                GameContentPreviewScene.TryAddPrefab(utility, roots, preview.FireVfxPrefab, sourcePosition + new Vector3(0.24f, 0.08f, 0f), Quaternion.identity, 0.72f, simulatedTime);

                if (preview.Mode == GameContentAuthoringActionPreviewMode.Hitscan)
                {
                    GameContentPreviewScene.TryAddPrefab(utility, roots, preview.BeamVfxPrefab, Vector3.Lerp(sourcePosition, targetPosition, 0.5f), facingTarget, 1.12f, simulatedTime);
                    GameContentPreviewScene.TryAddPrefab(utility, roots, preview.ImpactVfxPrefab, targetPosition, Quaternion.identity, 0.9f, simulatedTime);
                }
                else if (preview.Mode == GameContentAuthoringActionPreviewMode.Area)
                {
                    GameContentPreviewScene.TryAddPrefab(utility, roots, preview.ImpactVfxPrefab, centerPosition, Quaternion.identity, 1.05f, simulatedTime);
                    GameContentPreviewScene.TryAddPrefab(utility, roots, preview.TickVfxPrefab, centerPosition + new Vector3(0.18f, 0.04f, -0.08f), Quaternion.identity, 0.82f, simulatedTime);
                }
                else if (preview.Mode == GameContentAuthoringActionPreviewMode.Aura)
                {
                    GameContentPreviewScene.TryAddPrefab(utility, roots, preview.TickVfxPrefab ?? preview.ImpactVfxPrefab, centerPosition, Quaternion.identity, 1.05f, simulatedTime);
                    GameContentPreviewScene.TryAddPrefab(utility, roots, preview.ExpireVfxPrefab, centerPosition + new Vector3(0.2f, 0.04f, 0.1f), Quaternion.identity, 0.82f, simulatedTime);
                }
                else
                {
                    float travel = Mathf.Clamp01(Mathf.InverseLerp(0.18f, 0.76f, time));
                    Vector3 projectilePosition = GameContentPreviewScene.GetProjectilePosition(sourcePosition, targetPosition, travel);
                    GameContentPreviewScene.TryAddPrefab(utility, roots, preview.ProjectilePrefab, projectilePosition, facingTarget, 0.86f, simulatedTime);
                    GameContentPreviewScene.TryAddPrefab(utility, roots, preview.ImpactVfxPrefab, targetPosition, Quaternion.identity, 0.82f, simulatedTime);
                }

                if (roots.Count == 0)
                    return false;

                Bounds bounds;
                if (!GameContentPreviewScene.TryCalculateSceneBounds(roots, out bounds))
                    return false;

                GameContentPreviewScene.ConfigurePreviewCamera(utility, bounds);
                utility.BeginPreview(rect, GUIStyle.none);
                utility.Render();
                Texture texture = utility.EndPreview();
                if (texture == null)
                    return false;

                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                GameContentPreviewOverlay.DrawGamePreviewGuideOverlay(rect, preview, time);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    if (roots[i] != null)
                        GameContentAuthoringEditorAssets.DestroyTransientObject(roots[i]);
                }

                if (utility != null)
                    utility.Cleanup();
            }
        }

#if UNITY_INCLUDE_TESTS
        internal static bool TryCalculateBoundsForTests(GameObject root, out Bounds bounds)
        {
            return GameContentPreviewScene.TryCalculateBounds(root, out bounds);
        }

        internal static Bounds SanitizePreviewBoundsForTests(Bounds bounds, Vector3 fallbackCenter)
        {
            return GameContentPreviewScene.SanitizePreviewBounds(bounds, fallbackCenter);
        }
#endif

    }
}
