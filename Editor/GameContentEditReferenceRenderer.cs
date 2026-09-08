using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentEditReferenceRenderer
    {
        internal static string DescribeReference(GameContentRecordReferenceValue reference)
        {
            if (reference == null) return "Unavailable";
            if (reference.IsBroken) return "Broken: " + reference.OriginalReference;
            if (reference.IsNone) return "None";
            string display = string.IsNullOrWhiteSpace(reference.TargetDisplayName)
                ? reference.TargetKey?.SourceRecordId ?? string.Empty
                : reference.TargetDisplayName;
            return reference.TargetKey == null
                ? display
                : display + " (" + reference.TargetKey.SourceRecordId + ")";
        }

        internal static void DrawReferenceSelector(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentFieldValue current)
        {
            GameContentRecordReferenceValue reference = current.RecordReferenceValue;
            string label = reference == null ? "Unavailable" : reference.ToDisplayString();
            Rect selectorRect = GUILayoutUtility.GetRect(
                new GUIContent(label),
                EditorStyles.popup,
                GUILayout.ExpandWidth(true));
            if (EditorGUI.DropdownButton(selectorRect, new GUIContent(label), FocusType.Keyboard, EditorStyles.popup))
            {
                GameContentReferenceCandidateSet targets = context.EditSessions.GetReferenceCandidates(
                    active,
                    field.FieldId);
                var dropdown = new GameContentReferenceDropdown(
                    field.RecordReference?.TargetLabel ?? "Record",
                    targets,
                    !field.Required && (field.RecordReference?.AllowClear ?? false),
                    selected =>
                    {
                        GameContentEditOperationResult result = context.EditSessions.Apply(
                            active,
                            field.FieldId,
                            GameContentFieldValue.FromRecordReference(selected));
                        if (result.Succeeded) context.EditSessions.Preview(active);
                        context.RequestRepaint();
                    });
                dropdown.Show(selectorRect);
            }

            GameContentRecordDescriptor target = ResolveCurrentTarget(context, reference);
            using (new EditorGUI.DisabledScope(target == null))
            {
                if (GUILayout.Button("Open", GUILayout.Width(48f)))
                    OpenTarget(context, target);
            }
        }

        internal static void OpenTarget(
            GameContentAuthoringSurfaceContext context,
            GameContentRecordDescriptor target)
        {
            if (context == null || target == null) return;
            GameContentLensDescriptor lens = context.Lenses
                .Where(value => value != null && value.Matches(target))
                .OrderBy(value => value.SortOrder)
                .FirstOrDefault();
            if (lens != null) context.OpenLens(lens.LensId, target);
            else context.SelectRecord(target);
        }

        internal static void DrawReferenceStatus(
            GameContentAuthoringSurfaceContext context,
            GameContentFieldDescriptor field,
            GameContentFieldValue current)
        {
            GameContentRecordReferenceValue reference = current?.RecordReferenceValue;
            if (reference == null) return;
            if (reference.IsBroken)
            {
                EditorGUILayout.HelpBox(
                    "Broken reference '" + reference.OriginalReference + "': " + reference.BrokenReason,
                    MessageType.Error);
                return;
            }

            if (reference.IsNone)
            {
                if (field.Required)
                    EditorGUILayout.HelpBox("Select a valid target before committing.", MessageType.Error);
                return;
            }

            GameContentRecordDescriptor target = ResolveCurrentTarget(context, reference);
            if (target == null)
            {
                EditorGUILayout.HelpBox(
                    "The selected target is no longer present in this content pack.",
                    MessageType.Error);
                return;
            }

            GameContentRecordLensBrowser.DrawRow("Target ID", target.CanonicalKey.SourceRecordId);
            GameContentRecordLensBrowser.DrawRow("Target Pack", target.CanonicalKey.PackId);
            GameContentRecordLensBrowser.DrawRow(
                "Target Type",
                string.Join(", ", target.Capabilities.Select(value => value.Id).ToArray()));
            GameContentRecordLensBrowser.DrawRow(
                "Target Validation",
                target.Validation.ErrorCount > 0
                    ? "Invalid"
                    : target.Validation.WarningCount > 0 ? "Warning" : "Valid");
        }

        internal static GameContentRecordDescriptor ResolveCurrentTarget(
            GameContentAuthoringSurfaceContext context,
            GameContentRecordReferenceValue reference)
        {
            return reference != null && reference.IsResolved && reference.TargetKey != null
                ? context.PackContext?.ResolveRecord(reference.TargetKey)
                : null;
        }
    }
}
