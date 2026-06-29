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
            Func<string> getStatus = null,
            GameContentAuthoringPreviewSelection selectedExistingItem = null)
        {
            Window = window;
            Provider = provider;
            SelectedExistingItem = selectedExistingItem;
            _setStatus = setStatus;
            _getStatus = getStatus;
        }

        public EditorWindow Window { get; }
        public IGameContentAuthoringProvider Provider { get; }
        public GameContentAuthoringPreviewSelection SelectedExistingItem { get; }
        public bool HasSelectedExistingItem => GetSelectedAsset<UnityEngine.Object>() != null;
        public GUIStyle MutedStyle => DeucarianEditorStyles.MutedLabel;
        public GUIStyle SectionTitleStyle => DeucarianEditorStyles.SectionTitle;
        public string CurrentStatus => _getStatus == null ? string.Empty : _getStatus() ?? string.Empty;

        public void RequestRepaint()
        {
            if (Window != null)
                Window.Repaint();
        }

        public T GetSelectedAsset<T>() where T : UnityEngine.Object
        {
            if (SelectedExistingItem == null || Provider == null)
            {
                return null;
            }

            if (!string.Equals(SelectedExistingItem.ProviderId, Provider.ProviderId, StringComparison.Ordinal))
            {
                return null;
            }

            return SelectedExistingItem.Asset as T;
        }

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
            DrawObjectPreview(asset, title, emptyText, null);
        }

        public void DrawObjectPreview(UnityEngine.Object asset, string title, string emptyText, GameContentAuthoringObjectPreviewOptions options)
        {
            DrawInlineCard(() =>
            {
                EditorGUILayout.LabelField(title ?? "Preview Asset", SectionTitleStyle);
                if (asset == null)
                {
                    EditorGUILayout.LabelField(emptyText ?? "No asset assigned.", MutedStyle);
                    return;
                }

                float previewHeight = options == null ? 184f : Mathf.Max(132f, options.MinimumHeight);
                Rect previewRect = GUILayoutUtility.GetRect(132f, previewHeight, GUILayout.ExpandWidth(true));
                GameContentAuthoringObjectPreviewRenderer.Draw(previewRect, asset, options);
                if (options != null && options.ActionPreview != null && options.ActionPreview.Playing)
                    RequestRepaint();

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

    public sealed class GameContentAuthoringPreviewSelection
    {
        public GameContentAuthoringPreviewSelection(
            string providerId,
            string displayName,
            string stableId,
            string category,
            string path,
            UnityEngine.Object asset)
        {
            ProviderId = providerId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? asset != null ? asset.name : "(unnamed)" : displayName;
            StableId = stableId ?? string.Empty;
            Category = category ?? string.Empty;
            Path = path ?? string.Empty;
            Asset = asset;
        }

        public string ProviderId { get; }
        public string DisplayName { get; }
        public string StableId { get; }
        public string Category { get; }
        public string Path { get; }
        public UnityEngine.Object Asset { get; }
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
