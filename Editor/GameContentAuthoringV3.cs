using System;
using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public enum GameContentAuthoringWorkbenchMode
    {
        Create = 0,
        Edit = 1
    }

    public static class GameContentAuthoringWorkbench
    {
        public static void Draw(
            GameContentAuthoringSurfaceContext context,
            Action drawList,
            Action drawWorkbench,
            Action drawPreviewLab)
        {
            if (context == null) return;

            if (context.Layout.Wide)
            {
                float totalWidth = Mathf.Max(760f, context.Window.position.width - context.Layout.SidebarWidth - DeucarianEditorSpacing.ExtraLarge * 2f);
                GameContentAuthoringWorkbenchWidths widths = CalculateWidths(totalWidth, 286f, 380f, 370f);
                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawPane(drawList, DeucarianEditorTheme.BorderSubtle, GUILayout.Width(widths.Left), GUILayout.ExpandHeight(true));
                    GUILayout.Space(DeucarianEditorSpacing.Small);
                    DrawPane(drawWorkbench, DeucarianEditorTheme.BorderSubtle, GUILayout.Width(widths.Center), GUILayout.ExpandHeight(true));
                    GUILayout.Space(DeucarianEditorSpacing.Small);
                    DrawPane(drawPreviewLab, DeucarianEditorTheme.Accent, GUILayout.Width(widths.Right), GUILayout.ExpandHeight(true));
                }

                return;
            }

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                DrawPane(drawList, DeucarianEditorTheme.BorderSubtle, GUILayout.MinHeight(170f), GUILayout.MaxHeight(240f), GUILayout.ExpandWidth(true));
                GUILayout.Space(DeucarianEditorSpacing.Small);
                DrawPane(drawWorkbench, DeucarianEditorTheme.BorderSubtle, GUILayout.MinHeight(280f), GUILayout.ExpandHeight(true));
                GUILayout.Space(DeucarianEditorSpacing.Small);
                DrawPane(drawPreviewLab, DeucarianEditorTheme.Accent, GUILayout.Height(context.Layout.StackedPreviewHeight));
            }
        }

        public static void DrawPane(Action draw, Color borderColor, params GUILayoutOption[] options)
        {
            Rect rect = EditorGUILayout.BeginVertical(GUIStyle.none, options);
            DeucarianEditorVisualShell.DrawInsetSurface(rect, DeucarianEditorTheme.GlassPanelSoft, borderColor, DeucarianEditorSpacing.CardRadius);
            draw?.Invoke();
            EditorGUILayout.EndVertical();
        }

        private static GameContentAuthoringWorkbenchWidths CalculateWidths(float totalWidth, float left, float center, float right)
        {
            float gutters = DeucarianEditorSpacing.Small * 2f;
            float available = Mathf.Max(1f, totalWidth - gutters);
            float requested = Mathf.Max(1f, left + center + right);
            if (requested <= available)
            {
                float extra = available - requested;
                return new GameContentAuthoringWorkbenchWidths(left + extra * 0.15f, center + extra * 0.45f, right + extra * 0.4f);
            }

            float scale = available / requested;
            return new GameContentAuthoringWorkbenchWidths(
                Mathf.Max(232f, left * scale),
                Mathf.Max(300f, center * scale),
                Mathf.Max(300f, right * scale));
        }
    }

    public readonly struct GameContentAuthoringWorkbenchWidths
    {
        public GameContentAuthoringWorkbenchWidths(float left, float center, float right)
        {
            Left = left;
            Center = center;
            Right = right;
        }

        public float Left { get; }
        public float Center { get; }
        public float Right { get; }
    }

    public sealed class GameContentAuthoringObjectEditorContext
    {
        public GameContentAuthoringObjectEditorContext(GameContentLibraryItem item, string baselineFingerprint)
        {
            Item = item;
            BaselineFingerprint = baselineFingerprint ?? string.Empty;
            CurrentFingerprint = BaselineFingerprint;
        }

        public GameContentLibraryItem Item { get; }
        public UnityEngine.Object Asset => Item == null ? null : Item.Asset;
        public string Key => Item == null ? string.Empty : Item.Key;
        public string DisplayName => Item == null ? string.Empty : Item.DisplayName;
        public string BaselineFingerprint { get; private set; }
        public string CurrentFingerprint { get; private set; }
        public GameContentAuthoringValidationResult Validation { get; private set; }
        public string StatusMessage { get; private set; } = string.Empty;
        public bool IsDirty => !string.Equals(BaselineFingerprint, CurrentFingerprint, StringComparison.Ordinal);

        public void Capture(string currentFingerprint, GameContentAuthoringValidationResult validation = null)
        {
            CurrentFingerprint = currentFingerprint ?? string.Empty;
            Validation = validation;
        }

        public void Accept(string fingerprint, string statusMessage)
        {
            BaselineFingerprint = fingerprint ?? string.Empty;
            CurrentFingerprint = BaselineFingerprint;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public void SetStatus(string statusMessage)
        {
            StatusMessage = statusMessage ?? string.Empty;
        }
    }

    public static class GameContentAuthoringCommandBar
    {
        public static GameContentAuthoringCommand Draw(
            GameContentAuthoringWorkbenchMode mode,
            bool primaryEnabled,
            bool dirty,
            string primaryLabel,
            string statusMessage = null)
        {
            GameContentAuthoringCommand command = GameContentAuthoringCommand.None;
            using (new EditorGUILayout.HorizontalScope())
            {
                DeucarianEditorStatusBadge.Draw(
                    mode == GameContentAuthoringWorkbenchMode.Create ? "Create" : dirty ? "Unsaved" : "Saved",
                    mode == GameContentAuthoringWorkbenchMode.Create
                        ? DeucarianEditorStatus.Info
                        : dirty
                            ? DeucarianEditorStatus.Warning
                            : DeucarianEditorStatus.Success,
                    GUILayout.Width(mode == GameContentAuthoringWorkbenchMode.Edit && dirty ? 78f : 64f));

                if (!string.IsNullOrWhiteSpace(statusMessage))
                    EditorGUILayout.LabelField(statusMessage, DeucarianEditorStyles.MutedLabel);

                GUILayout.FlexibleSpace();
                if (mode == GameContentAuthoringWorkbenchMode.Edit)
                {
                    if (DeucarianEditorButtons.Secondary("Revert", dirty, GUILayout.Width(74f), GUILayout.Height(24f)))
                        command = GameContentAuthoringCommand.Revert;
                    if (DeucarianEditorButtons.Primary(primaryLabel ?? "Save", primaryEnabled && dirty, GUILayout.Width(74f), GUILayout.Height(24f)))
                        command = GameContentAuthoringCommand.Save;
                }
                else if (DeucarianEditorButtons.Primary(primaryLabel ?? "Create", primaryEnabled, GUILayout.Width(88f), GUILayout.Height(24f)))
                {
                    command = GameContentAuthoringCommand.Create;
                }
            }

            return command;
        }
    }

    public enum GameContentAuthoringCommand
    {
        None = 0,
        Create = 1,
        Save = 2,
        Revert = 3
    }

    public sealed class GameContentPreviewLabState
    {
        public bool Muted { get; set; } = true;
        public bool Loop { get; set; } = true;
        public float Speed { get; set; } = 1f;
        public bool Playing { get; set; } = true;
        public GameContentAuthoringActionPreviewRenderMode RenderMode { get; set; } = GameContentAuthoringActionPreviewRenderMode.Game;
        public double StartTime { get; set; }
        public float PausedNormalizedTime { get; set; } = 0.5f;
        public string Status { get; set; } = "Preview idle";
    }

    public sealed class GameContentPreviewLabModel
    {
        public string Title { get; set; } = "Preview Lab";
        public string PreviewTitle { get; set; } = "Preview";
        public string ScopeLabel { get; set; } = "Selected";
        public UnityEngine.Object PrimaryAsset { get; set; }
        public string EmptyText { get; set; } = "No visual asset assigned.";
        public GameContentAuthoringObjectPreviewOptions PreviewOptions { get; set; }
        public IReadOnlyList<DeucarianEditorStatusChip> Chips { get; set; } = Array.Empty<DeucarianEditorStatusChip>();
        public Action DrawControls { get; set; }
        public Action DrawContext { get; set; }
        public Action DrawBody { get; set; }
    }

    public static class GameContentPreviewLabRenderer
    {
        public static void Draw(GameContentAuthoringPreviewContext context, GameContentPreviewLabModel model)
        {
            if (context == null || model == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(model.Title ?? "Preview Lab", HeaderStyle);
                GUILayout.FlexibleSpace();
                DeucarianEditorStatusBadge.Draw(model.ScopeLabel ?? "Selected", DeucarianEditorStatus.Info, GUILayout.Width(78f));
            }

            model.DrawControls?.Invoke();
            model.DrawContext?.Invoke();
            context.DrawObjectPreview(model.PrimaryAsset, model.PreviewTitle, model.EmptyText, model.PreviewOptions);
            model.DrawBody?.Invoke();
            DeucarianEditorStatusChipRow.Draw(model.Chips);
        }

        private static GUIStyle headerStyle;

        private static GUIStyle HeaderStyle
        {
            get
            {
                if (headerStyle == null)
                {
                    headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 14
                    };
                    headerStyle.normal.textColor = DeucarianEditorTheme.Text;
                }

                return headerStyle;
            }
        }
    }
}
