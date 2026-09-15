using System;
using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Ui = Deucarian.Editor.DeucarianEditorWorkspaceControls;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentToolkitPlayback
    {
        public bool Playing { get; internal set; }
        public bool Loop { get; internal set; } = true;
        public bool Muted { get; internal set; } = true;
        public float Speed { get; internal set; } = 1;
        public float Position { get; internal set; } = .5f;
        public double Started { get; internal set; }
        public GameContentAuthoringActionPreviewRenderMode RenderMode { get; internal set; }
        internal void Apply(GameContentAuthoringActionPreview preview)
        {
            if (preview == null) return;
            preview.Playing = Playing; preview.Loop = Loop; preview.Muted = Muted;
            preview.Speed = Speed; preview.StartTime = Started;
            preview.StaticNormalizedTime = Position; preview.RenderMode = RenderMode;
        }
    }

    public static class GameContentToolkitPreview
    {
        public static DeucarianEditorWorkspaceForm Add(VisualElement root, Func<UnityEngine.Object> asset,
            Func<GameContentToolkitPlayback, GameContentAuthoringActionPreview> build,
            Func<IReadOnlyList<GameContentAuthoringPreviewRow>> summary = null,
            Action<GameContentAuthoringActionPreview> tick = null, Action stop = null)
        {
            var form = new DeucarianEditorWorkspaceForm(root).Section("Preview", true);
            var foldout = (Foldout)form.Root;
            var state = new GameContentToolkitPlayback();
            GameContentAuthoringActionPreview current = null;
            var viewport = new IMGUIContainer(() =>
            {
                current = build?.Invoke(state); state.Apply(current);
                var rect = GUILayoutUtility.GetRect(1, 280, GUILayout.ExpandWidth(true));
                GameContentAuthoringObjectPreviewRenderer.Draw(rect, asset?.Invoke(),
                    new GameContentAuthoringObjectPreviewOptions { ActionPreview = current });
                if (state.Playing && current != null) tick?.Invoke(current);
            });
            viewport.AddToClassList("dw-object-preview");
            form.Root.Add(viewport);
            Button play = null;
            Action pause = () =>
            {
                if (current != null) state.Position = current.GetNormalizedTime(EditorApplication.timeSinceStartup);
                state.Playing = false; stop?.Invoke(); if (play != null) play.text = "Play";
            };
            play = Ui.Button("Play", () =>
            {
                if (state.Playing) pause();
                else
                {
                    float duration = Mathf.Max(.01f, current?.DurationSeconds ?? 1);
                    state.Started = EditorApplication.timeSinceStartup - state.Position * duration / state.Speed;
                    state.Playing = true; play.text = "Pause";
                }
                viewport.MarkDirtyRepaint();
            });
            var controls = Ui.Actions(play, Ui.Button("Stop", () => { pause(); state.Position = 0; form.Refresh(); viewport.MarkDirtyRepaint(); }),
                Ui.Button("Restart", () => { pause(); state.Position = 0; state.Started = EditorApplication.timeSinceStartup; state.Playing = true; play.text = "Pause"; }));
            form.Root.Add(controls);
            if (build != null)
            {
            form.Toggle("preview-loop", "Loop", () => state.Loop, value => state.Loop = value);
            form.Toggle("preview-muted", "Mute audio", () => state.Muted, value => { state.Muted = value; if (value) stop?.Invoke(); });
            form.Slider("preview-position", "Position", 0, 1, () => state.Position, value => { pause(); state.Position = value; viewport.MarkDirtyRepaint(); });
            form.Choice("preview-speed", "Speed", new[] { "0.5×", "1×", "2×" },
                () => state.Speed < 1 ? 0 : state.Speed > 1 ? 2 : 1,
                value => { pause(); state.Speed = value == 0 ? .5f : value == 2 ? 2 : 1; });
            form.Enum("preview-mode", "View", () => state.RenderMode, value => { state.RenderMode = value; viewport.MarkDirtyRepaint(); });
            }
            else Ui.Show(controls, false);
            if (summary != null)
            {
                var report = new VisualElement(); form.Root.Add(report);
                Action refresh = () =>
                {
                    report.Clear(); var rows = summary(); if (rows == null) return;
                    var fields = new DeucarianEditorWorkspaceForm(report);
                    foreach (var row in rows) fields.ReadOnly(null, row.Label, () => row.Value);
                };
                refresh(); form.Action("preview-refresh-summary", "Refresh summary", refresh);
            }
            viewport.schedule.Execute(() =>
            {
                if (!foldout.value || !state.Playing) return;
                if (!state.Loop && current != null && current.GetNormalizedTime(EditorApplication.timeSinceStartup) >= 1) pause();
                viewport.MarkDirtyRepaint();
            }).Every(33);
            foldout.RegisterValueChangedCallback(evt => { if (evt.target == foldout && !evt.newValue) pause(); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => pause());
            return form;
        }
    }
}
