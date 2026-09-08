using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentPreviewOverlay
    {
        internal static void DrawGamePreviewGuideOverlay(Rect rect, GameContentAuthoringActionPreview preview, float time)
        {
            if (preview == null)
                return;

            Rect lane = new Rect(rect.x + 18f, rect.yMax - 26f, Mathf.Max(1f, rect.width - 36f), 1f);
            Vector2 source = new Vector2(lane.x, lane.y);
            Vector2 target = new Vector2(lane.xMax, lane.y);
            Vector2 center = Vector2.Lerp(source, target, 0.62f);
            Color accent = preview.AccentColor;
            Color laneColor = new Color(accent.r, accent.g, accent.b, 0.34f);

            Handles.BeginGUI();
            Handles.color = laneColor;
            Handles.DrawAAPolyLine(2f, source, target);
            DrawSolidDisc(source, 4f, new Color(0.9f, 0.96f, 1f, 0.72f));
            if (preview.Mode == GameContentAuthoringActionPreviewMode.Hitscan)
            {
                Handles.color = new Color(accent.r, accent.g, accent.b, 0.62f);
                Handles.DrawAAPolyLine(3f, source, target);
            }
            else if (preview.Mode == GameContentAuthoringActionPreviewMode.Area || preview.Mode == GameContentAuthoringActionPreviewMode.Aura)
            {
                Handles.color = new Color(accent.r, accent.g, accent.b, 0.54f);
                Handles.DrawWireDisc(center, Vector3.forward, 9f);
            }
            else
            {
                float travel = Mathf.Clamp01(Mathf.InverseLerp(0.18f, 0.76f, time));
                DrawSolidDisc(Vector2.Lerp(source, target, travel), 5f, new Color(accent.r, accent.g, accent.b, 0.82f));
            }

            DrawTargetDummy(target, accent);
            Handles.EndGUI();
        }

        internal static void DrawActionOverlay(Rect rect, GameContentAuthoringActionPreview preview, double now)
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

        internal static void DrawViewportHeader(Rect rect, GameContentAuthoringActionPreview preview)
        {
            Rect headerRect = new Rect(rect.x + 10f, rect.y + 8f, Mathf.Max(1f, rect.width - 20f), 34f);
            DrawLabelBackground(headerRect, 0.72f);
            Rect titleRect = new Rect(headerRect.x + 7f, headerRect.y + 2f, headerRect.width - 14f, 15f);
            Rect legendRect = new Rect(headerRect.x + 7f, headerRect.y + 17f, headerRect.width - 14f, 15f);
            GUI.Label(titleRect, GameContentAuthoringObjectPreviewUtility.BuildViewportHeader(preview), OverlayHeaderStyle);
            GUI.Label(legendRect, GameContentAuthoringObjectPreviewUtility.BuildRoleLegend(preview), OverlayLabelStyle);
        }

        internal static void DrawProjectileStage(Vector2 source, Vector2 target, Color accent, Color muted, float time)
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

        internal static void DrawBeamStage(Vector2 source, Vector2 target, Color accent, Color muted, float time)
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

        internal static void DrawAreaStage(Vector2 source, Vector2 center, Color accent, float time)
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

        internal static void DrawStatusStage(Vector2 center, Color accent, float time)
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

        internal static void DrawRoleLabels(GameContentAuthoringActionPreview preview, Rect bounds, Vector2 source, Vector2 target, Vector2 center, float time)
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

        internal static GameContentAuthoringActionPreviewRole FindRole(GameContentAuthoringActionPreview preview, string role)
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

        internal static void DrawRoleLabel(Vector2 anchor, GameContentAuthoringActionPreviewRole role, TextAnchor alignment, Rect bounds)
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

        internal static void DrawAssetMarker(Vector2 center, GameContentAuthoringActionPreviewRole role, Color accent, Rect bounds)
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

        internal static void DrawLabelBackground(Rect labelRect, float alpha)
        {
            Color old = GUI.color;
            GUI.color = new Color(0.02f, 0.05f, 0.07f, alpha);
            GUI.DrawTexture(labelRect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        internal static void DrawSourceMarker(Vector2 center, Color accent)
        {
            DrawSolidDisc(center, 8f, new Color(0.94f, 0.96f, 0.98f, 0.92f));
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.88f);
            Vector2 nose = center + new Vector2(13f, 0f);
            Handles.DrawAAPolyLine(2f, center, nose);
            Handles.DrawAAPolyLine(2f, nose, nose + new Vector2(-5f, -4f), nose, nose + new Vector2(-5f, 4f));
        }

        internal static void DrawDirectionArrow(Vector2 from, Vector2 target, Color accent)
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

        internal static void DrawTargetDummy(Vector2 center, Color accent)
        {
            Rect body = new Rect(center.x - 7f, center.y - 22f, 14f, 28f);
            EditorGUI.DrawRect(body, new Color(0.88f, 0.92f, 0.95f, 0.86f));
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.9f);
            Handles.DrawAAPolyLine(2f, new Vector2(body.xMin, body.yMin), new Vector2(body.xMax, body.yMin), new Vector2(body.xMax, body.yMax), new Vector2(body.xMin, body.yMax), new Vector2(body.xMin, body.yMin));
        }

        internal static void DrawSolidDisc(Vector2 center, float radius, Color color)
        {
            Color old = Handles.color;
            Handles.color = color;
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            Handles.color = old;
        }

        private static readonly GameContentAuthoringStyleCache styles = new GameContentAuthoringStyleCache();
        internal static GUIStyle OverlayLabelStyle => styles.OverlayLabel;
        internal static GUIStyle OverlayHeaderStyle => styles.OverlayLabel;
    }
}
