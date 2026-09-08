using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
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

        public static bool IsGamePreview(GameContentAuthoringActionPreview preview)
        {
            return preview != null && preview.RenderMode == GameContentAuthoringActionPreviewRenderMode.Game;
        }

        public static bool RequestsRoleLabels(GameContentAuthoringActionPreview preview)
        {
            return preview != null
                && preview.RenderMode == GameContentAuthoringActionPreviewRenderMode.Debug
                && preview.Roles != null
                && preview.Roles.Count > 0;
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
}
