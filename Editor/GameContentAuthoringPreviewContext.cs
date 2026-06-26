using System;
using System.Collections.Generic;
using Deucarian.Editor;
using Deucarian.GameplayFoundation;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringPreviewContext
    {
        private readonly Action<string> _setStatus;
        private readonly Func<string> _getStatus;

        public GameContentAuthoringPreviewContext(
            EditorWindow window,
            IGameContentAuthoringProvider provider,
            Action<string> setStatus = null,
            Func<string> getStatus = null)
        {
            Window = window;
            Provider = provider;
            _setStatus = setStatus;
            _getStatus = getStatus;
        }

        public EditorWindow Window { get; }
        public IGameContentAuthoringProvider Provider { get; }
        public GUIStyle MutedStyle => DeucarianEditorStyles.MutedLabel;
        public GUIStyle SectionTitleStyle => DeucarianEditorStyles.SectionTitle;
        public string CurrentStatus => _getStatus == null ? string.Empty : _getStatus() ?? string.Empty;

        public void SetStatus(string message)
        {
            _setStatus?.Invoke(message ?? string.Empty);
        }

        public void DrawCard(string title, Action draw, string subtitle = null)
        {
            DeucarianEditorCards.DrawCard(title, draw, subtitle);
        }

        public void DrawInlineCard(Action draw)
        {
            DeucarianEditorCards.DrawInlineCard(draw);
        }

        public bool DrawPrimaryButton(string label, bool enabled = true, params GUILayoutOption[] options)
        {
            return DeucarianEditorButtons.Primary(label, enabled, options);
        }

        public bool DrawSecondaryButton(string label, bool enabled = true, params GUILayoutOption[] options)
        {
            return DeucarianEditorButtons.Secondary(label, enabled, options);
        }

        public void DrawStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            DeucarianEditorStatusPanel.DrawStatusCard(message, DeucarianEditorStatus.Info);
        }

        public void DrawSummaryRows(IReadOnlyList<GameContentAuthoringPreviewRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                EditorGUILayout.LabelField("No preview details available.", MutedStyle);
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                DrawSummaryRow(rows[i].Label, rows[i].Value);
            }
        }

        public void DrawSummaryRow(string label, string value)
        {
            DeucarianEditorFieldRow.Draw(label, () =>
            {
                EditorGUILayout.LabelField(value ?? string.Empty, PreviewValueStyle);
            });
        }

        public void DrawAssetRow(string label, UnityEngine.Object asset, string emptyText)
        {
            DeucarianEditorFieldRow.Draw(label, () =>
            {
                string value = asset == null ? emptyText ?? "Not assigned" : asset.name;
                EditorGUILayout.LabelField(value, asset == null ? PreviewMutedValueStyle : PreviewValueStyle);
                DeucarianEditorMiniToolbar.PingButton(asset);
                DeucarianEditorMiniToolbar.SelectButton(asset);
            });
        }

        public void DrawObjectPreview(UnityEngine.Object asset, string title, string emptyText)
        {
            DrawInlineCard(() =>
            {
                EditorGUILayout.LabelField(title ?? "Preview Asset", SectionTitleStyle);
                if (asset == null)
                {
                    EditorGUILayout.LabelField(emptyText ?? "No asset assigned.", MutedStyle);
                    return;
                }

                Texture2D texture = AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
                Rect previewRect = GUILayoutUtility.GetRect(96f, 132f, GUILayout.ExpandWidth(true));
                DeucarianEditorVisualShell.DrawInsetSurface(
                    previewRect,
                    DeucarianEditorTheme.GlassPanelSoft,
                    DeucarianEditorTheme.BorderSubtle,
                    7f);

                if (texture != null && Event.current != null && Event.current.type == EventType.Repaint)
                {
                    Rect imageRect = FitTexture(previewRect, texture, 10f);
                    GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit, true);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(asset.name, PreviewValueStyle);
                    if (DrawSecondaryButton("Ping", true, GUILayout.Width(54f), GUILayout.Height(22f)))
                        EditorGUIUtility.PingObject(asset);
                }
            });
        }

        public void DrawTimeline(IReadOnlyList<GameContentAuthoringPreviewTimelineItem> items)
        {
            if (items == null || items.Count == 0)
            {
                EditorGUILayout.LabelField("No timeline entries.", MutedStyle);
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                GameContentAuthoringPreviewTimelineItem item = items[i];
                DrawInlineCard(() =>
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(item.Label, PreviewValueStyle);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField(item.TimeLabel, PreviewMutedValueStyle, GUILayout.Width(72f));
                    }

                    if (!string.IsNullOrWhiteSpace(item.Detail))
                        EditorGUILayout.LabelField(item.Detail, MutedStyle);
                });
            }
        }

        public void DrawWarnings(IReadOnlyList<string> warnings)
        {
            if (warnings == null || warnings.Count == 0)
            {
                return;
            }

            DeucarianEditorStatusPanel.DrawValidationCard(
                warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " preview warning(s).",
                warnings,
                DeucarianEditorStatus.Warning);
        }

        public void DrawValidation(ContentValidationReport report, string readyMessage = "No validation issues found.")
        {
            GameContentAuthoringValidationResult result = GameContentAuthoringValidationReports.ToAuthoringResult(report);
            if (result.Issues.Count == 0)
            {
                DeucarianEditorStatusPanel.DrawStatusCard(readyMessage, DeucarianEditorStatus.Success);
                return;
            }

            var messages = new List<string>();
            for (int index = 0; index < result.Issues.Count; index++)
            {
                GameContentAuthoringValidationIssue issue = result.Issues[index];
                string prefix = string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : issue.Path + ": ";
                messages.Add(prefix + issue.Message);
            }

            DeucarianEditorStatus status = result.ErrorCount > 0
                ? DeucarianEditorStatus.Error
                : result.WarningCount > 0
                    ? DeucarianEditorStatus.Warning
                    : DeucarianEditorStatus.Info;
            DeucarianEditorStatusPanel.DrawValidationCard(
                GameContentAuthoringValidationReports.BuildSummary(report, readyMessage),
                messages,
                status);
        }

        private static Rect FitTexture(Rect container, Texture texture, float padding)
        {
            Rect padded = new Rect(
                container.x + padding,
                container.y + padding,
                Mathf.Max(0f, container.width - padding * 2f),
                Mathf.Max(0f, container.height - padding * 2f));
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return padded;
            }

            float textureAspect = (float)texture.width / texture.height;
            float rectAspect = padded.width / Mathf.Max(1f, padded.height);
            if (textureAspect > rectAspect)
            {
                float height = padded.width / textureAspect;
                return new Rect(padded.x, padded.y + (padded.height - height) * 0.5f, padded.width, height);
            }

            float width = padded.height * textureAspect;
            return new Rect(padded.x + (padded.width - width) * 0.5f, padded.y, width, padded.height);
        }

        private static GUIStyle previewLabelStyle;
        private static GUIStyle previewValueStyle;
        private static GUIStyle previewMutedValueStyle;

        private static GUIStyle PreviewLabelStyle
        {
            get
            {
                if (previewLabelStyle == null)
                {
                    previewLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        wordWrap = true
                    };
                    previewLabelStyle.normal.textColor = DeucarianEditorTheme.MutedText;
                }

                return previewLabelStyle;
            }
        }

        private static GUIStyle PreviewValueStyle
        {
            get
            {
                if (previewValueStyle == null)
                {
                    previewValueStyle = new GUIStyle(EditorStyles.label)
                    {
                        wordWrap = true
                    };
                    previewValueStyle.normal.textColor = DeucarianEditorTheme.Text;
                }

                return previewValueStyle;
            }
        }

        private static GUIStyle PreviewMutedValueStyle
        {
            get
            {
                if (previewMutedValueStyle == null)
                {
                    previewMutedValueStyle = new GUIStyle(EditorStyles.label)
                    {
                        wordWrap = true
                    };
                    previewMutedValueStyle.normal.textColor = DeucarianEditorTheme.MutedText;
                }

                return previewMutedValueStyle;
            }
        }
    }

    public readonly struct GameContentAuthoringPreviewRow
    {
        public GameContentAuthoringPreviewRow(string label, string value)
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Label { get; }
        public string Value { get; }
    }

    public readonly struct GameContentAuthoringPreviewTimelineItem
    {
        public GameContentAuthoringPreviewTimelineItem(string label, string timeLabel, string detail)
        {
            Label = label ?? string.Empty;
            TimeLabel = timeLabel ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public string Label { get; }
        public string TimeLabel { get; }
        public string Detail { get; }
    }
}
