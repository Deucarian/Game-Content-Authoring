using System;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public enum GameContentAuthoringActionPreviewMode
    {
        Static = 0,
        Projectile = 1,
        Hitscan = 2,
        Area = 3,
        Aura = 4
    }

    public sealed class GameContentAuthoringObjectPreviewOptions
    {
        public float MinimumHeight { get; set; } = 184f;
        public GameContentAuthoringActionPreview ActionPreview { get; set; }
    }

    public sealed class GameContentAuthoringActionPreview
    {
        public UnityEngine.Object PrimaryAsset { get; set; }
        public GameObject ProjectilePrefab { get; set; }
        public GameObject BeamVfxPrefab { get; set; }
        public GameObject ImpactVfxPrefab { get; set; }
        public GameObject FireVfxPrefab { get; set; }
        public GameObject TargetPrefab { get; set; }
        public GameContentAuthoringActionPreviewMode Mode { get; set; }
        public bool IncludeStatusEffect { get; set; }
        public bool Playing { get; set; }
        public bool Loop { get; set; } = true;
        public float Speed { get; set; } = 1f;
        public double StartTime { get; set; }
        public float DurationSeconds { get; set; } = 2.4f;
        public float StaticNormalizedTime { get; set; }
        public string Label { get; set; }
        public Color AccentColor { get; set; } = new Color(0.12f, 0.78f, 0.86f, 1f);

        public float GetNormalizedTime(double now)
        {
            float duration = Mathf.Max(0.001f, DurationSeconds);
            if (!Playing)
            {
                return Mathf.Clamp01(StaticNormalizedTime);
            }

            double elapsed = Math.Max(0d, now - StartTime);
            elapsed *= Mathf.Max(0.01f, Speed);
            if (Loop)
            {
                return (float)(elapsed % duration / duration);
            }

            return Mathf.Clamp01((float)(elapsed / duration));
        }

        public string GetPhaseLabel(double now)
        {
            float time = GetNormalizedTime(now);
            if (time < 0.14f) return "OnCast";
            if (time < 0.28f) return "OnFire";
            if (time < 0.72f) return GetDeliveryLabel();
            if (time < 0.88f) return "OnImpact";
            return IncludeStatusEffect ? "Status / Expire" : "Resolved";
        }

        private string GetDeliveryLabel()
        {
            switch (Mode)
            {
                case GameContentAuthoringActionPreviewMode.Projectile:
                    return "Projectile travel";
                case GameContentAuthoringActionPreviewMode.Hitscan:
                    return "Beam trace";
                case GameContentAuthoringActionPreviewMode.Area:
                    return "Area burst";
                case GameContentAuthoringActionPreviewMode.Aura:
                    return "Aura tick";
                default:
                    return "Delivery";
            }
        }
    }

    public static class GameContentAuthoringObjectPreviewUtility
    {
        public static Rect FitRect(Rect container, Vector2 contentSize, float padding)
        {
            Rect padded = new Rect(
                container.x + padding,
                container.y + padding,
                Mathf.Max(0f, container.width - padding * 2f),
                Mathf.Max(0f, container.height - padding * 2f));
            if (contentSize.x <= 0f || contentSize.y <= 0f)
            {
                return padded;
            }

            float contentAspect = contentSize.x / contentSize.y;
            float rectAspect = padded.width / Mathf.Max(1f, padded.height);
            if (contentAspect > rectAspect)
            {
                float height = padded.width / contentAspect;
                return new Rect(padded.x, padded.y + (padded.height - height) * 0.5f, padded.width, height);
            }

            float width = padded.height * contentAspect;
            return new Rect(padded.x + (padded.width - width) * 0.5f, padded.y, width, padded.height);
        }
    }

    internal static class GameContentAuthoringObjectPreviewRenderer
    {
        public static void Draw(Rect rect, UnityEngine.Object asset, GameContentAuthoringObjectPreviewOptions options)
        {
            DrawPreviewBackground(rect);

            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                if (!TryDrawRenderedGameObject(rect, asset))
                    DrawAssetTexture(rect, asset);
            }

            GameContentAuthoringActionPreview actionPreview = options == null ? null : options.ActionPreview;
            if (actionPreview != null)
            {
                DrawActionOverlay(rect, actionPreview, EditorApplication.timeSinceStartup);
            }
        }

        private static void DrawPreviewBackground(Rect rect)
        {
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
                SetPreviewHideFlags(clone);
                SimulateParticles(clone, 0.2f);
                Bounds bounds;
                if (!TryCalculateBounds(clone, out bounds))
                {
                    return false;
                }

                clone.transform.position -= bounds.center;
                bounds.center = Vector3.zero;

                utility = new PreviewRenderUtility(true);
                utility.cameraFieldOfView = 28f;
                utility.camera.clearFlags = CameraClearFlags.Color;
                utility.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                if (utility.lights.Length > 0)
                {
                    utility.lights[0].intensity = 1.25f;
                    utility.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
                }

                if (utility.lights.Length > 1)
                {
                    utility.lights[1].intensity = 0.7f;
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
                    UnityEngine.Object.DestroyImmediate(clone);
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

        private static void DrawActionOverlay(Rect rect, GameContentAuthoringActionPreview preview, double now)
        {
            float time = preview.GetNormalizedTime(now);
            Rect lane = new Rect(rect.x + 20f, rect.y + rect.height * 0.56f, Mathf.Max(1f, rect.width - 40f), 1f);
            Vector2 source = new Vector2(lane.x, lane.y);
            Vector2 target = new Vector2(lane.xMax, lane.y - 6f);
            Color accent = preview.AccentColor;
            Color muted = new Color(accent.r, accent.g, accent.b, 0.24f);

            Handles.BeginGUI();
            Handles.color = muted;
            Handles.DrawAAPolyLine(2f, source, target);
            DrawSolidDisc(source, 7f, new Color(0.95f, 0.95f, 0.95f, 0.88f));
            DrawTargetDummy(target, accent);

            if (preview.Mode == GameContentAuthoringActionPreviewMode.Hitscan)
            {
                float beamAlpha = time >= 0.24f && time <= 0.76f ? 0.9f : 0.32f;
                Handles.color = new Color(accent.r, accent.g, accent.b, beamAlpha);
                Handles.DrawAAPolyLine(5f, source, target);
            }
            else if (preview.Mode == GameContentAuthoringActionPreviewMode.Area || preview.Mode == GameContentAuthoringActionPreviewMode.Aura)
            {
                float radius = Mathf.Lerp(18f, 36f, Mathf.PingPong(time * 2f, 1f));
                Handles.color = new Color(accent.r, accent.g, accent.b, 0.54f);
                Handles.DrawWireDisc(target, Vector3.forward, radius);
            }
            else
            {
                float travel = Mathf.InverseLerp(0.28f, 0.72f, time);
                Vector2 projectile = Vector2.Lerp(source, target, Mathf.Clamp01(travel));
                DrawSolidDisc(projectile, 6f, accent);
            }

            if (time >= 0.72f || time < 0.08f)
            {
                float pulse = time >= 0.72f ? Mathf.InverseLerp(0.72f, 0.96f, time) : time / 0.08f;
                Handles.color = new Color(accent.r, accent.g, accent.b, 1f - pulse);
                Handles.DrawWireDisc(target, Vector3.forward, Mathf.Lerp(8f, 30f, pulse));
            }

            Handles.EndGUI();

            DrawOverlayLabel(rect, preview.GetPhaseLabel(now), preview.Label);
        }

        private static void DrawOverlayLabel(Rect rect, string phase, string label)
        {
            string text = string.IsNullOrWhiteSpace(label) ? phase : label + " - " + phase;
            Rect labelRect = new Rect(rect.x + 10f, rect.y + 8f, Mathf.Max(1f, rect.width - 20f), 18f);
            Color old = GUI.color;
            GUI.color = new Color(0.02f, 0.05f, 0.07f, 0.78f);
            GUI.DrawTexture(labelRect, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(labelRect, text, OverlayLabelStyle);
        }

        private static void DrawTargetDummy(Vector2 center, Color accent)
        {
            Rect body = new Rect(center.x - 7f, center.y - 22f, 14f, 28f);
            EditorGUI.DrawRect(body, new Color(0.88f, 0.92f, 0.95f, 0.86f));
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.9f);
            Handles.DrawAAPolyLine(2f, new Vector2(body.xMin, body.yMin), new Vector2(body.xMax, body.yMin), new Vector2(body.xMax, body.yMax), new Vector2(body.xMin, body.yMax), new Vector2(body.xMin, body.yMin));
        }

        private static void DrawSolidDisc(Vector2 center, float radius, Color color)
        {
            Color old = Handles.color;
            Handles.color = color;
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            Handles.color = old;
        }

        private static void SetPreviewHideFlags(GameObject root)
        {
            if (root == null) return;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private static void SimulateParticles(GameObject root, float time)
        {
            if (root == null) return;
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Simulate(time, true, true, true);
            }
        }

        private static bool TryCalculateBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.one);
            if (root == null) return false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled) continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds || !IsFinite(bounds.center) || !IsFinite(bounds.size))
            {
                bounds = new Bounds(root.transform.position, Vector3.one);
                hasBounds = true;
            }

            if (bounds.size.sqrMagnitude < 0.001f)
            {
                bounds.Expand(1f);
            }

            return hasBounds;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x)
                && !float.IsNaN(value.y)
                && !float.IsNaN(value.z)
                && !float.IsInfinity(value.x)
                && !float.IsInfinity(value.y)
                && !float.IsInfinity(value.z);
        }

        private static GUIStyle overlayLabelStyle;

        private static GUIStyle OverlayLabelStyle
        {
            get
            {
                if (overlayLabelStyle == null)
                {
                    overlayLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        clipping = TextClipping.Clip
                    };
                    overlayLabelStyle.normal.textColor = DeucarianEditorTheme.Text;
                }

                return overlayLabelStyle;
            }
        }
    }
}
