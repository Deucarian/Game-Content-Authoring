using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEngine.UIElements;
using Ui = Deucarian.Editor.DeucarianEditorWorkspaceControls;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentToolkitFields
    {
        internal static void Build(VisualElement root, GameContentAuthoringSurfaceContext context, GameContentActiveEditSession active)
        {
            var form = new DeucarianEditorWorkspaceForm(root);
            var advanced = form.Section("More fields", true);
            int count = 0;
            foreach (var field in active.Fields)
            {
                var host = !field.IsReadOnly && count++ < 5 ? root : advanced.Root;
                var value = active.GetEffectiveValue(field.FieldId);
                bool enabled = (active.State == GameContentEditSessionState.Clean || active.State == GameContentEditSessionState.Dirty) && !field.IsReadOnly;
                if (field.FieldType.IsOrderedCollection()) GameContentToolkitCollections.Build(host, context, active, field, enabled);
                else if (field.FieldType == GameContentFieldType.OrderedStructuredCollection) GameContentToolkitStructuredRows.Build(host, context, active, field, enabled);
                else
                {
                    var input = Value(field, value, replacement =>
                    {
                        var result = context.EditSessions.Apply(active, field.FieldId, replacement);
                        if (result.Succeeded) context.EditSessions.Preview(active);
                        else context.Authoring.SetValidation(new GameContentAuthoringValidationResult(new[] { GameContentAuthoringValidationIssue.Error(field.DisplayName, result.Message) }));
                        context.RequestRepaint();
                    }, () => context.EditSessions.GetReferenceCandidates(active, field.FieldId));
                    input.SetEnabled(enabled && value != null); host.Add(Ui.Field(field.DisplayName, input));
                }
            }
            // Keep disclosure after the primary fields even when provider schemas interleave groups.
            root.Add(advanced.Root);
        }

        internal static VisualElement Value(GameContentFieldDescriptor field, GameContentFieldValue value,
            Action<GameContentFieldValue> write, Func<GameContentReferenceCandidateSet> references = null)
        {
            VisualElement input;
            if (value == null) input = Ui.Label("Unavailable", "dw-readonly");
            else switch (field.FieldType)
            {
                case GameContentFieldType.Integer:
                    var integer = new LongField { value = value.IntegerValue, isDelayed = true };
                    integer.RegisterValueChangedCallback(evt => write(GameContentFieldValue.FromInteger(evt.newValue))); input = integer; break;
                case GameContentFieldType.Number:
                    var number = new DoubleField { value = value.NumberValue, isDelayed = true };
                    number.RegisterValueChangedCallback(evt => write(GameContentFieldValue.FromNumber(evt.newValue))); input = number; break;
                case GameContentFieldType.Boolean:
                    var toggle = new DeucarianEditorSwitch { value = value.BooleanValue };
                    toggle.RegisterValueChangedCallback(evt => write(GameContentFieldValue.FromBoolean(evt.newValue))); input = toggle; break;
                case GameContentFieldType.Enum:
                    var tokens = field.EnumOptions.Select(option => option.Token).ToList();
                    var labels = field.EnumOptions.Select(option => option.DisplayName).ToList();
                    int current = tokens.IndexOf(value.StringValue);
                    if (current < 0) { tokens.Insert(0, value.StringValue); labels.Insert(0, value.StringValue + " (current)"); current = 0; }
                    var choice = new PopupField<string>(labels, current);
                    choice.RegisterValueChangedCallback(_ => write(GameContentFieldValue.FromEnum(tokens[choice.index]))); input = choice; break;
                case GameContentFieldType.RecordReference:
                    var selector = new DropdownField { choices = new List<string> { GameContentEditReferenceRenderer.DescribeReference(value.RecordReferenceValue) }, index = 0 };
                    var targets = new List<GameContentRecordReferenceValue> { value.RecordReferenceValue };
                    if (!field.Required && field.RecordReference?.AllowClear == true)
                    { selector.choices.Add("None"); targets.Add(GameContentRecordReferenceValue.None()); }
                    foreach (var candidate in references?.Invoke()?.Candidates ?? Array.Empty<GameContentReferenceCandidate>())
                    {
                        selector.choices.Add(candidate.Record.DisplayName + " · " + candidate.Record.CanonicalKey.SourceRecordId);
                        targets.Add(GameContentRecordReferenceValue.Resolved(candidate.Record.CanonicalKey, candidate.Record.DisplayName, candidate.Record.SourcePath));
                    }
                    selector.RegisterValueChangedCallback(_ => { if (selector.index > 0) write(GameContentFieldValue.FromRecordReference(targets[selector.index])); }); input = selector; break;
                default:
                    var text = new TextField { value = value.StringValue ?? string.Empty, isDelayed = true };
                    text.RegisterValueChangedCallback(evt => write(GameContentFieldValue.FromString(evt.newValue))); input = text; break;
            }
            input.name = "content-field-" + field.FieldId;
            input.tooltip = field.IsReadOnly ? field.ReadOnlyReason : GameContentEditFieldRenderer.BuildFieldDetail(field);
            input.SetEnabled(!field.IsReadOnly && value != null); return input;
        }

        internal static GameContentFieldValue DefaultValue(GameContentFieldDescriptor field) =>
            field.FieldType == GameContentFieldType.RecordReference ? GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.None())
            : GameContentEditFieldRenderer.CreateDefaultScalarValue(field);
    }
}
