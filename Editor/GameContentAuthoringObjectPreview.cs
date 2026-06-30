using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly List<GameContentAuthoringActionPreviewRole> _roles = new List<GameContentAuthoringActionPreviewRole>();

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
        public string DeliveryTypeLabel { get; set; }
        public bool Muted { get; set; }
        public Color AccentColor { get; set; } = new Color(0.12f, 0.78f, 0.86f, 1f);
        public IList<GameContentAuthoringActionPreviewRole> Roles => _roles;

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

    public sealed class GameContentAuthoringActionPreviewRole
    {
        public GameContentAuthoringActionPreviewRole(string role, string label, UnityEngine.Object asset = null, string tooltip = null)
        {
            Role = role ?? string.Empty;
            Label = label ?? string.Empty;
            Asset = asset;
            Tooltip = tooltip ?? string.Empty;
        }

        public string Role { get; }
        public string Label { get; }
        public UnityEngine.Object Asset { get; }
        public string Tooltip { get; }
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

        public static string BuildRoleLegend(GameContentAuthoringActionPreview preview)
        {
            if (preview == null || preview.Roles == null || preview.Roles.Count == 0)
                return string.Empty;
            return string.Join(" -> ", preview.Roles.Select(role => role == null ? string.Empty : role.Role).Where(role => !string.IsNullOrWhiteSpace(role)));
        }

        public static string BuildViewportHeader(GameContentAuthoringActionPreview preview)
        {
            if (preview == null)
                return string.Empty;

            string title = string.IsNullOrWhiteSpace(preview.Label) ? "Attack Preview" : preview.Label.Trim();
            string delivery = string.IsNullOrWhiteSpace(preview.DeliveryTypeLabel) ? "Delivery" : preview.DeliveryTypeLabel.Trim();
            string state = preview.Playing ? "Playing" : "Paused";
            string audio = preview.Muted ? "Muted" : "Audio";
            string loop = preview.Loop ? "Loop" : "Once";
            return title + " | " + delivery + " | " + state + " | " + audio + " | " + loop;
        }

        public static GUIContent BuildRoleLabelContent(GameContentAuthoringActionPreviewRole role)
        {
            if (role == null)
                return GUIContent.none;

            string text = string.IsNullOrWhiteSpace(role.Label)
                ? role.Role
                : string.IsNullOrWhiteSpace(role.Role)
                    ? role.Label
                    : role.Role + ": " + role.Label;
            return new GUIContent(text, role.Tooltip ?? string.Empty);
        }
    }

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
            Rect stage = new Rect(rect.x + 14f, rect.y + 44f, Mathf.Max(1f, rect.width - 28f), Mathf.Max(1f, rect.height - 66f));
            Rect lane = new Rect(stage.x + 16f, stage.y + stage.height * 0.58f, Mathf.Max(1f, stage.width - 32f), 1f);
            Vector2 source = new Vector2(lane.x, lane.y);
            Vector2 target = new Vector2(lane.xMax, lane.y - 6f);
            Vector2 center = new Vector2(Mathf.Lerp(source.x, target.x, 0.62f), lane.y - 6f);
            Color accent = preview.AccentColor;
            Color muted = new Color(accent.r, accent.g, accent.b, 0.24f);

            DrawViewportHeader(rect, preview);

            Handles.BeginGUI();
            if (preview.Mode == GameContentAuthoringActionPreviewMode.Hitscan)
            {
                DrawBeamStage(source, target, accent, muted, time);
            }
            else if (preview.Mode == GameContentAuthoringActionPreviewMode.Area)
            {
                DrawAreaStage(source, center, accent, time);
            }
            else if (preview.Mode == GameContentAuthoringActionPreviewMode.Aura)
            {
                DrawStatusStage(center, accent, time);
            }
            else
            {
                DrawProjectileStage(source, target, accent, muted, time);
            }

            Handles.EndGUI();
            DrawRoleLabels(preview, stage, source, target, center, time);
        }

        private static void DrawViewportHeader(Rect rect, GameContentAuthoringActionPreview preview)
        {
            Rect headerRect = new Rect(rect.x + 10f, rect.y + 8f, Mathf.Max(1f, rect.width - 20f), 34f);
            DrawLabelBackground(headerRect, 0.72f);
            Rect titleRect = new Rect(headerRect.x + 7f, headerRect.y + 2f, headerRect.width - 14f, 15f);
            Rect legendRect = new Rect(headerRect.x + 7f, headerRect.y + 17f, headerRect.width - 14f, 15f);
            GUI.Label(titleRect, GameContentAuthoringObjectPreviewUtility.BuildViewportHeader(preview), OverlayHeaderStyle);
            GUI.Label(legendRect, GameContentAuthoringObjectPreviewUtility.BuildRoleLegend(preview), OverlayLabelStyle);
        }

        private static void DrawProjectileStage(Vector2 source, Vector2 target, Color accent, Color muted, float time)
        {
            Handles.color = muted;
            Handles.DrawAAPolyLine(2f, source, target);
            DrawSourceMarker(source, accent);
            DrawTargetDummy(target, accent);

            float travel = Mathf.InverseLerp(0.28f, 0.72f, time);
            Vector2 projectile = Vector2.Lerp(source, target, Mathf.Clamp01(travel));
            DrawSolidDisc(projectile, 8f, accent);
            DrawDirectionArrow(projectile, target, accent);

            if (time >= 0.72f || time < 0.08f)
            {
                float pulse = time >= 0.72f ? Mathf.InverseLerp(0.72f, 0.96f, time) : time / 0.08f;
                Handles.color = new Color(accent.r, accent.g, accent.b, 1f - pulse);
                Handles.DrawWireDisc(target, Vector3.forward, Mathf.Lerp(8f, 30f, pulse));
            }
        }

        private static void DrawBeamStage(Vector2 source, Vector2 target, Color accent, Color muted, float time)
        {
            Handles.color = muted;
            Handles.DrawAAPolyLine(2f, source, target);
            DrawSourceMarker(source, accent);
            DrawTargetDummy(target, accent);
            float beamAlpha = time >= 0.24f && time <= 0.76f ? 0.9f : 0.4f;
            Handles.color = new Color(accent.r, accent.g, accent.b, beamAlpha);
            Handles.DrawAAPolyLine(5f, source, target);
            Handles.DrawWireDisc(target, Vector3.forward, 12f);
        }

        private static void DrawAreaStage(Vector2 source, Vector2 center, Color accent, float time)
        {
            DrawSourceMarker(source, accent);
            float radius = Mathf.Lerp(30f, 44f, Mathf.PingPong(time * 2f, 1f));
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.5f);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.16f);
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            DrawTargetDummy(new Vector2(center.x - radius * 0.35f, center.y + 5f), accent);
            DrawTargetDummy(new Vector2(center.x + radius * 0.2f, center.y - 8f), accent);
            DrawSolidDisc(center, 5f, new Color(0.95f, 0.95f, 0.95f, 0.88f));
        }

        private static void DrawStatusStage(Vector2 center, Color accent, float time)
        {
            float radius = Mathf.Lerp(32f, 42f, Mathf.PingPong(time * 2f, 1f));
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.38f);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.12f);
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            DrawTargetDummy(center, accent);
            Vector2 tick = new Vector2(center.x + Mathf.Cos(time * Mathf.PI * 2f) * 18f, center.y + Mathf.Sin(time * Mathf.PI * 2f) * 13f);
            DrawSolidDisc(tick, 7f, accent);
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.64f);
            Handles.DrawWireDisc(tick, Vector3.forward, 11f);
        }

        private static void DrawRoleLabels(GameContentAuthoringActionPreview preview, Rect bounds, Vector2 source, Vector2 target, Vector2 center, float time)
        {
            switch (preview.Mode)
            {
                case GameContentAuthoringActionPreviewMode.Hitscan:
                    DrawRoleLabel(source + new Vector2(0f, 18f), FindRole(preview, "Source"), TextAnchor.UpperLeft, bounds);
                    DrawRoleLabel(Vector2.Lerp(source, target, 0.5f) + new Vector2(0f, -30f), FindRole(preview, "Beam"), TextAnchor.LowerCenter, bounds);
                    DrawRoleLabel(target + new Vector2(-4f, 18f), FindRole(preview, "Impact"), TextAnchor.UpperRight, bounds);
                    break;
                case GameContentAuthoringActionPreviewMode.Area:
                    DrawRoleLabel(source + new Vector2(0f, 18f), FindRole(preview, "Origin"), TextAnchor.UpperLeft, bounds);
                    DrawRoleLabel(center + new Vector2(0f, -52f), FindRole(preview, "Radius"), TextAnchor.LowerCenter, bounds);
                    DrawRoleLabel(center + new Vector2(0f, 46f), FindRole(preview, "Targets"), TextAnchor.UpperCenter, bounds);
                    break;
                case GameContentAuthoringActionPreviewMode.Aura:
                    DrawRoleLabel(center + new Vector2(0f, -54f), FindRole(preview, "Status Area"), TextAnchor.LowerCenter, bounds);
                    DrawRoleLabel(center + new Vector2(0f, 46f), FindRole(preview, "Target"), TextAnchor.UpperCenter, bounds);
                    DrawRoleLabel(center + new Vector2(44f, -10f), FindRole(preview, "Tick"), TextAnchor.MiddleLeft, bounds);
                    break;
                default:
                    Vector2 projectile = Vector2.Lerp(source, target, Mathf.Clamp01(Mathf.InverseLerp(0.28f, 0.72f, time)));
                    DrawRoleLabel(source + new Vector2(0f, 18f), FindRole(preview, "Source"), TextAnchor.UpperLeft, bounds);
                    DrawAssetMarker(projectile, FindRole(preview, "Projectile"), preview.AccentColor, bounds);
                    DrawRoleLabel(projectile + new Vector2(0f, -34f), FindRole(preview, "Projectile"), TextAnchor.LowerCenter, bounds);
                    DrawRoleLabel(target + new Vector2(-4f, 18f), FindRole(preview, "Target"), TextAnchor.UpperRight, bounds);
                    break;
            }
        }

        private static GameContentAuthoringActionPreviewRole FindRole(GameContentAuthoringActionPreview preview, string role)
        {
            if (preview == null || preview.Roles == null)
                return null;

            for (int i = 0; i < preview.Roles.Count; i++)
            {
                GameContentAuthoringActionPreviewRole candidate = preview.Roles[i];
                if (candidate != null && string.Equals(candidate.Role, role, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return null;
        }

        private static void DrawRoleLabel(Vector2 anchor, GameContentAuthoringActionPreviewRole role, TextAnchor alignment, Rect bounds)
        {
            if (role == null)
                return;

            GUIContent content = GameContentAuthoringObjectPreviewUtility.BuildRoleLabelContent(role);
            GUIStyle style = new GUIStyle(OverlayLabelStyle)
            {
                alignment = alignment,
                wordWrap = false
            };
            float maxWidth = Mathf.Max(24f, Mathf.Min(168f, bounds.width * 0.46f));
            float minWidth = Mathf.Min(44f, maxWidth);
            Vector2 size = style.CalcSize(content);
            if (size.x + 12f > maxWidth && !string.IsNullOrWhiteSpace(role.Role))
            {
                content = new GUIContent(role.Role, string.IsNullOrWhiteSpace(content.tooltip) ? role.Label : content.tooltip);
                size = style.CalcSize(content);
            }

            float width = Mathf.Clamp(size.x + 12f, minWidth, maxWidth);
            Rect labelRect = new Rect(anchor.x, anchor.y, width, 18f);
            if (alignment == TextAnchor.UpperCenter || alignment == TextAnchor.MiddleCenter || alignment == TextAnchor.LowerCenter)
                labelRect.x -= width * 0.5f;
            else if (alignment == TextAnchor.UpperRight || alignment == TextAnchor.MiddleRight || alignment == TextAnchor.LowerRight)
                labelRect.x -= width;

            float xMin = bounds.x + 2f;
            float xMax = Mathf.Max(xMin, bounds.xMax - width - 2f);
            float yMin = bounds.y + 2f;
            float yMax = Mathf.Max(yMin, bounds.yMax - labelRect.height - 2f);
            labelRect.x = Mathf.Clamp(labelRect.x, xMin, xMax);
            labelRect.y = Mathf.Clamp(labelRect.y, yMin, yMax);

            DrawLabelBackground(labelRect, 0.72f);
            GUI.Label(labelRect, content, style);
        }

        private static void DrawAssetMarker(Vector2 center, GameContentAuthoringActionPreviewRole role, Color accent, Rect bounds)
        {
            if (role == null || role.Asset == null)
                return;

            Texture2D texture = AssetPreview.GetAssetPreview(role.Asset) ?? AssetPreview.GetMiniThumbnail(role.Asset);
            if (texture == null)
                return;

            Rect rect = new Rect(center.x - 18f, center.y - 18f, 36f, 36f);
            float xMin = bounds.x + 2f;
            float yMin = bounds.y + 2f;
            rect.x = Mathf.Clamp(rect.x, xMin, Mathf.Max(xMin, bounds.xMax - rect.width - 2f));
            rect.y = Mathf.Clamp(rect.y, yMin, Mathf.Max(yMin, bounds.yMax - rect.height - 2f));
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            Handles.BeginGUI();
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.88f);
            Handles.DrawAAPolyLine(2f, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin));
            Handles.EndGUI();
        }

        private static void DrawLabelBackground(Rect labelRect, float alpha)
        {
            Color old = GUI.color;
            GUI.color = new Color(0.02f, 0.05f, 0.07f, alpha);
            GUI.DrawTexture(labelRect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void DrawSourceMarker(Vector2 center, Color accent)
        {
            DrawSolidDisc(center, 8f, new Color(0.94f, 0.96f, 0.98f, 0.92f));
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.88f);
            Vector2 nose = center + new Vector2(13f, 0f);
            Handles.DrawAAPolyLine(2f, center, nose);
            Handles.DrawAAPolyLine(2f, nose, nose + new Vector2(-5f, -4f), nose, nose + new Vector2(-5f, 4f));
        }

        private static void DrawDirectionArrow(Vector2 from, Vector2 target, Color accent)
        {
            Vector2 direction = (target - from).normalized;
            if (direction.sqrMagnitude <= 0.001f)
                return;

            Vector2 tip = from + direction * 16f;
            Vector2 side = new Vector2(-direction.y, direction.x);
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.84f);
            Handles.DrawAAPolyLine(2f, from, tip);
            Handles.DrawAAPolyLine(2f, tip, tip - direction * 6f + side * 4f, tip, tip - direction * 6f - side * 4f);
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
        private static GUIStyle overlayHeaderStyle;

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

        private static GUIStyle OverlayHeaderStyle
        {
            get
            {
                if (overlayHeaderStyle == null)
                {
                    overlayHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        clipping = TextClipping.Clip
                    };
                    overlayHeaderStyle.normal.textColor = DeucarianEditorTheme.Text;
                }

                return overlayHeaderStyle;
            }
        }
    }
}
