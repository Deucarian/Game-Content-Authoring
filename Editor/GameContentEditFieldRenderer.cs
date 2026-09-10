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
    internal static class GameContentEditFieldRenderer
    {
        internal static void DrawField(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field)
        {
            GameContentFieldValue current = active.GetEffectiveValue(field.FieldId);
            bool sessionWritable = active.State == GameContentEditSessionState.Clean ||
                                   active.State == GameContentEditSessionState.Dirty;
            bool enabled = sessionWritable && !field.IsReadOnly && current != null;
            GameContentFieldValue next = current;

            using (new EditorGUILayout.VerticalScope(DeucarianEditorStyles.SectionBox))
            {
                if (field.FieldType == GameContentFieldType.OrderedStructuredCollection)
                {
                    GameContentEditStructuredListRenderer.DrawStructuredCollectionField(context, active, field, current, enabled);
                }
                else if (field.FieldType.IsOrderedCollection())
                {
                    GameContentEditCollectionRenderer.DrawCollectionField(context, active, field, current, enabled);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(!enabled))
                    {
                        EditorGUI.BeginChangeCheck();
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            DeucarianEditorTextGUI.LabelField(field.DisplayName, GUILayout.Width(128f));
                            next = DrawValue(context, active, field, current);
                        }
                        if (EditorGUI.EndChangeCheck() && next != null && !next.Equals(current))
                        {
                            GameContentEditOperationResult result = context.EditSessions.Apply(active, field.FieldId, next);
                            if (result.Succeeded) context.EditSessions.Preview(active);
                            context.RequestRepaint();
                        }
                    }
                }

                string detail = BuildFieldDetail(field);
                if (!string.IsNullOrWhiteSpace(detail))
                    DeucarianEditorTextGUI.LabelField(detail, DeucarianEditorStyles.MutedLabel);
                if (field.IsReadOnly && !string.IsNullOrWhiteSpace(field.ReadOnlyReason))
                    DeucarianEditorTextGUI.LabelField(field.ReadOnlyReason, DeucarianEditorStyles.MutedLabel);
                if (field.FieldType == GameContentFieldType.RecordReference)
                    GameContentEditReferenceRenderer.DrawReferenceStatus(context, field, current);
                DrawFieldValidation(active.Validation, field);
            }
        }

        internal static GameContentFieldValue DrawValue(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentFieldValue current)
        {
            if (current == null)
            {
                DeucarianEditorTextGUI.LabelField("Unavailable", DeucarianEditorStyles.MutedLabel);
                return null;
            }

            switch (field.FieldType)
            {
                case GameContentFieldType.Integer:
                    return GameContentFieldValue.FromInteger(DeucarianEditorInputGUI.LongField(current.IntegerValue));
                case GameContentFieldType.Number:
                    return GameContentFieldValue.FromNumber(DeucarianEditorInputGUI.DoubleField(current.NumberValue));
                case GameContentFieldType.Boolean:
                    return GameContentFieldValue.FromBoolean(DeucarianEditorInputGUI.Toggle(current.BooleanValue));
                case GameContentFieldType.Enum:
                    return DrawEnum(field, current);
                case GameContentFieldType.RecordReference:
                    GameContentEditReferenceRenderer.DrawReferenceSelector(context, active, field, current);
                    return current;
                default:
                    return GameContentFieldValue.FromString(DeucarianEditorInputGUI.TextField(current.StringValue ?? string.Empty));
            }
        }

        internal static GameContentFieldValue DrawScalarValue(
            GameContentFieldDescriptor descriptor,
            GameContentFieldValue current,
            bool delayed)
        {
            switch (descriptor.FieldType)
            {
                case GameContentFieldType.Integer:
                    return GameContentFieldValue.FromInteger(delayed
                        ? DrawDelayedInteger(current.IntegerValue)
                        : DeucarianEditorInputGUI.LongField(current.IntegerValue));
                case GameContentFieldType.Number:
                    return GameContentFieldValue.FromNumber(delayed
                        ? DeucarianEditorInputGUI.DelayedDoubleField(current.NumberValue)
                        : DeucarianEditorInputGUI.DoubleField(current.NumberValue));
                case GameContentFieldType.Boolean:
                    return GameContentFieldValue.FromBoolean(DeucarianEditorInputGUI.Toggle(current.BooleanValue));
                case GameContentFieldType.Enum:
                    return DrawEnum(descriptor, current);
                default:
                    return GameContentFieldValue.FromString(delayed
                        ? DeucarianEditorInputGUI.DelayedTextField(current.StringValue ?? string.Empty)
                        : DeucarianEditorInputGUI.TextField(current.StringValue ?? string.Empty));
            }
        }

        internal static long DrawDelayedInteger(long current)
        {
            string text = DeucarianEditorInputGUI.DelayedTextField(current.ToString(CultureInfo.InvariantCulture));
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value
                : current;
        }

        internal static GameContentFieldValue CreateDefaultScalarValue(GameContentFieldDescriptor descriptor)
        {
            switch (descriptor.FieldType)
            {
                case GameContentFieldType.Integer:
                    return GameContentFieldValue.FromInteger(
                        descriptor.MinimumNumber.HasValue
                            ? (long)Math.Ceiling(descriptor.MinimumNumber.Value)
                            : 0L);
                case GameContentFieldType.Number:
                    return GameContentFieldValue.FromNumber(descriptor.MinimumNumber ?? 0d);
                case GameContentFieldType.Boolean:
                    return GameContentFieldValue.FromBoolean(false);
                case GameContentFieldType.Enum:
                    return GameContentFieldValue.FromEnum(
                        descriptor.EnumOptions.Count == 0 ? string.Empty : descriptor.EnumOptions[0].Token);
                default:
                    return GameContentFieldValue.FromString(string.Empty);
            }
        }

        internal static GameContentFieldValue DrawEnum(
            GameContentFieldDescriptor field,
            GameContentFieldValue current)
        {
            if (field.EnumOptions.Count == 0)
            {
                DeucarianEditorTextGUI.LabelField(current.StringValue, DeucarianEditorStyles.MutedLabel);
                return current;
            }

            string[] tokens = field.EnumOptions.Select(value => value.Token).ToArray();
            string[] labels = field.EnumOptions.Select(value => value.DisplayName).ToArray();
            int currentIndex = Array.FindIndex(tokens, value => string.Equals(value, current.StringValue, StringComparison.Ordinal));
            if (currentIndex < 0) currentIndex = 0;
            int nextIndex = DeucarianEditorInputGUI.Popup(currentIndex, labels);
            return GameContentFieldValue.FromEnum(tokens[Mathf.Clamp(nextIndex, 0, tokens.Length - 1)]);
        }

        internal static string BuildFieldDetail(GameContentFieldDescriptor field)
        {
            string detail = field.Description;
            string constraints = string.Empty;
            if (field.FieldType == GameContentFieldType.OrderedStructuredCollection &&
                field.StructuredCollection != null)
            {
                string maximum = field.StructuredCollection.MaximumCount.HasValue
                    ? field.StructuredCollection.MaximumCount.Value.ToString(CultureInfo.InvariantCulture)
                    : "any";
                constraints = "Rows: " + field.StructuredCollection.MinimumCount + " to " + maximum + ". " +
                              field.StructuredCollection.OrderingSemantics + " Duplicates: " +
                              field.StructuredCollection.DuplicatePolicy + ". Runtime impact: " +
                              field.StructuredCollection.RuntimeImpact + ".";
            }
            else if (field.FieldType.IsOrderedCollection() && field.Collection != null)
            {
                int minimum = Math.Max(field.Collection.MinimumCount, field.Required ? 1 : 0);
                string maximum = field.Collection.MaximumCount.HasValue
                    ? field.Collection.MaximumCount.Value.ToString(CultureInfo.InvariantCulture)
                    : "any";
                constraints = "Count: " + minimum + " to " + maximum + ". " +
                              (field.Collection.AllowDuplicates ? "Duplicates allowed. " : "Duplicates are not allowed. ") +
                              field.Collection.OrderingDescription + " Runtime impact: " +
                              field.Collection.RuntimeImpact + ".";
            }
            else if (field.MinimumNumber.HasValue || field.MaximumNumber.HasValue)
            {
                string minimum = field.MinimumNumber.HasValue
                    ? field.MinimumNumber.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : "any";
                string maximum = field.MaximumNumber.HasValue
                    ? field.MaximumNumber.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : "any";
                constraints = "Range: " + minimum + " to " + maximum + ".";
            }
            else if (field.MinimumLength.HasValue || field.MaximumLength.HasValue)
            {
                string minimum = field.MinimumLength.HasValue ? field.MinimumLength.Value.ToString(CultureInfo.InvariantCulture) : "0";
                string maximum = field.MaximumLength.HasValue ? field.MaximumLength.Value.ToString(CultureInfo.InvariantCulture) : "any";
                constraints = "Length: " + minimum + " to " + maximum + ".";
            }
            if (field.Required) constraints = string.IsNullOrWhiteSpace(constraints) ? "Required." : constraints + " Required.";
            return string.IsNullOrWhiteSpace(detail)
                ? constraints
                : string.IsNullOrWhiteSpace(constraints) ? detail : detail + " " + constraints;
        }

        internal static void DrawFieldValidation(
            GameContentValidationPreview preview,
            GameContentFieldDescriptor field)
        {
            if (preview == null) return;
            GameContentAuthoringValidationIssue[] issues = preview.Issues.Where(issue =>
                string.Equals(issue.Path, field.FieldId, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(field.SemanticId) &&
                 string.Equals(issue.Path, field.SemanticId, StringComparison.Ordinal))).ToArray();
            for (int i = 0; i < issues.Length; i++)
                DeucarianEditorTextGUI.HelpBox(issues[i].Message, ToMessageType(issues[i].Severity));
        }

        internal static MessageType ToMessageType(GameContentAuthoringValidationSeverity severity)
        {
            if (severity == GameContentAuthoringValidationSeverity.Error) return MessageType.Error;
            if (severity == GameContentAuthoringValidationSeverity.Warning) return MessageType.Warning;
            return MessageType.Info;
        }
    }
}
