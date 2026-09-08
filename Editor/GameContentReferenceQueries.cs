using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentReferenceQueries
    {
        private readonly GameContentEditSessionRegistry _sessions;

        internal GameContentReferenceQueries(GameContentEditSessionRegistry sessions)
        {
            _sessions = sessions;
        }

        public GameContentReferenceCandidateSet GetReferenceCandidates(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentCollectionItemKey replacingItemKey = null)
        {
            if (!_sessions.Owns(active))
            {
                return new GameContentReferenceCandidateSet(
                    fieldId,
                    null,
                    null,
                    "The edit session is no longer active.");
            }

            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, fieldId);
            if (field == null || GameContentReferencePolicy.ResolveReferenceDescriptor(field) == null)
            {
                return new GameContentReferenceCandidateSet(
                    fieldId,
                    null,
                    null,
                    "The field is not an editable record reference or record-reference collection.");
            }

            var candidates = new List<GameContentReferenceCandidate>();
            var rejections = new List<GameContentReferenceCandidateRejection>();
            GameContentRecordDescriptor[] records = (active.PackRecords ?? Array.Empty<GameContentRecordDescriptor>())
                .Where(value => value != null && value.CanonicalKey != null)
                .OrderBy(value => value.CanonicalKey.StableKey, StringComparer.Ordinal)
                .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (int i = 0; i < records.Length; i++)
            {
                GameContentRecordDescriptor record = records[i];
                GameContentReferenceEvaluation evaluation = GameContentReferencePolicy.EvaluateReferenceTargetCore(
                    active,
                    field,
                    record.CanonicalKey);
                if (evaluation.IsValid && !GameContentReferencePolicy.IsDuplicateCollectionTarget(
                        active,
                        field,
                        record.CanonicalKey,
                        replacingItemKey))
                    candidates.Add(new GameContentReferenceCandidate(record, evaluation));
                else if (evaluation.IsValid)
                    rejections.Add(new GameContentReferenceCandidateRejection(
                        record.CanonicalKey,
                        "The target is already present and this collection does not allow duplicates."));
                else
                    rejections.Add(new GameContentReferenceCandidateRejection(record.CanonicalKey, evaluation.Reason));
            }

            string message = candidates.Count == 0
                ? "No compatible targets are available in the selected content pack."
                : string.Empty;
            return new GameContentReferenceCandidateSet(field.FieldId, candidates, rejections, message);
        }

        public GameContentReferenceEvaluation EvaluateReferenceTarget(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentRecordKey targetKey)
        {
            if (!_sessions.Owns(active))
                return GameContentReferenceEvaluation.Rejected(targetKey, "The edit session is no longer active.");
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, fieldId);
            if (field == null || GameContentReferencePolicy.ResolveReferenceDescriptor(field) == null)
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The field is not an editable record reference or record-reference collection.");
            return GameContentReferencePolicy.EvaluateReferenceTargetCore(active, field, targetKey);
        }

        public GameContentReferenceCandidateSet GetStructuredReferenceCandidates(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredRowKey rowKey,
            string rowFieldId)
        {
            if (!_sessions.Owns(active))
            {
                return new GameContentReferenceCandidateSet(
                    rowFieldId,
                    null,
                    null,
                    "The edit session is no longer active.");
            }

            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, fieldId);
            GameContentFieldDescriptor rowField = GameContentReferencePolicy.ResolveStructuredRowField(field, rowFieldId);
            if (rowField?.FieldType != GameContentFieldType.RecordReference)
            {
                return new GameContentReferenceCandidateSet(
                    rowFieldId,
                    null,
                    null,
                    "The selected structured-row field is not a canonical record reference.");
            }

            var candidates = new List<GameContentReferenceCandidate>();
            var rejections = new List<GameContentReferenceCandidateRejection>();
            GameContentRecordDescriptor[] records = (active.PackRecords ?? Array.Empty<GameContentRecordDescriptor>())
                .Where(value => value != null && value.CanonicalKey != null)
                .OrderBy(value => value.CanonicalKey.StableKey, StringComparer.Ordinal)
                .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (int i = 0; i < records.Length; i++)
            {
                GameContentRecordDescriptor record = records[i];
                GameContentReferenceEvaluation evaluation = GameContentReferencePolicy.EvaluateStructuredReferenceTargetCore(
                    active,
                    field,
                    rowKey,
                    rowField,
                    record.CanonicalKey);
                if (evaluation.IsValid)
                    candidates.Add(new GameContentReferenceCandidate(record, evaluation));
                else
                    rejections.Add(new GameContentReferenceCandidateRejection(record.CanonicalKey, evaluation.Reason));
            }

            return new GameContentReferenceCandidateSet(
                rowField.FieldId,
                candidates,
                rejections,
                candidates.Count == 0
                    ? "No compatible targets are available in the selected content pack."
                    : string.Empty);
        }

        public GameContentReferenceEvaluation EvaluateStructuredRowReference(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredRowKey rowKey,
            string rowFieldId,
            GameContentRecordKey targetKey)
        {
            if (!_sessions.Owns(active))
                return GameContentReferenceEvaluation.Rejected(targetKey, "The edit session is no longer active.");
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, fieldId);
            GameContentFieldDescriptor rowField = GameContentReferencePolicy.ResolveStructuredRowField(field, rowFieldId);
            if (rowField?.FieldType != GameContentFieldType.RecordReference)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The selected structured-row field is not a canonical record reference.");
            }
            return GameContentReferencePolicy.EvaluateStructuredReferenceTargetCore(active, field, rowKey, rowField, targetKey);
        }

        public GameContentReferenceChangeReview GetReferenceChangeReview(
            GameContentActiveEditSession active,
            GameContentProposedChange change)
        {
            if (!_sessions.Owns(active) || change == null) return null;
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, change.FieldId);
            if (field == null || field.FieldType != GameContentFieldType.RecordReference) return null;

            GameContentRecordReferenceValue oldValue = change.OldValue?.RecordReferenceValue;
            GameContentRecordReferenceValue newValue = change.ProposedValue?.RecordReferenceValue;
            GameContentRecordDescriptor oldTarget = GameContentReferencePolicy.ResolveReferenceRecord(active, oldValue);
            GameContentRecordDescriptor newTarget = GameContentReferencePolicy.ResolveReferenceRecord(active, newValue);
            bool targetChanged = oldValue == null || newValue == null || !oldValue.Equals(newValue);
            GameContentRecordDescriptor source = (active.PackRecords ?? Array.Empty<GameContentRecordDescriptor>())
                .FirstOrDefault(value => value != null && value.CanonicalKey.Equals(active.RecordKey));
            GameContentReferenceRuntimeImpact runtimeImpact = field.RecordReference?.RuntimeImpact ??
                                                               GameContentReferenceRuntimeImpact.None;
            if (newValue != null && newValue.IsResolved)
            {
                GameContentReferenceEvaluation evaluation = GameContentReferencePolicy.EvaluateReferenceTargetCore(active, field, newValue.TargetKey);
                runtimeImpact |= evaluation.RuntimeImpact;
            }

            return new GameContentReferenceChangeReview(
                active.RecordKey,
                field.FieldId,
                oldValue,
                newValue,
                oldTarget,
                newTarget,
                source?.InboundReferences.Count ?? 0,
                targetChanged && oldValue != null && oldValue.IsResolved ? -1 : 0,
                targetChanged && newValue != null && newValue.IsResolved ? 1 : 0,
                runtimeImpact);
        }

        public GameContentCollectionChangeReview GetCollectionChangeReview(
            GameContentActiveEditSession active,
            GameContentProposedChange change)
        {
            if (!_sessions.Owns(active) || change == null) return null;
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, change.FieldId);
            if (field == null || !field.FieldType.IsOrderedCollection() || field.Collection == null) return null;

            GameContentOrderedCollectionValue original = change.OldValue?.OrderedCollectionValue;
            GameContentOrderedCollectionValue proposed = change.ProposedValue?.OrderedCollectionValue;
            GameContentReferenceRuntimeImpact runtimeImpact = field.Collection.RuntimeImpact;
            if (field.FieldType == GameContentFieldType.OrderedRecordReferenceCollection && proposed != null)
            {
                for (int i = 0; i < proposed.Items.Count; i++)
                {
                    GameContentRecordReferenceValue reference = proposed.Items[i].Value.RecordReferenceValue;
                    if (reference == null || !reference.IsResolved || reference.TargetKey == null) continue;
                    GameContentReferenceEvaluation evaluation = GameContentReferencePolicy.EvaluateReferenceTargetCore(active, field, reference.TargetKey);
                    runtimeImpact |= evaluation.RuntimeImpact;
                }
            }

            return GameContentCollectionChangeReview.Create(
                active.RecordKey,
                field.FieldId,
                original,
                proposed,
                runtimeImpact);
        }

        public GameContentStructuredCollectionChangeReview GetStructuredCollectionChangeReview(
            GameContentActiveEditSession active,
            GameContentProposedChange change)
        {
            if (!_sessions.Owns(active) || change == null) return null;
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, change.FieldId);
            if (field?.FieldType != GameContentFieldType.OrderedStructuredCollection ||
                field.StructuredCollection == null)
                return null;

            GameContentOrderedStructuredCollectionValue original =
                change.OldValue?.OrderedStructuredCollectionValue;
            GameContentOrderedStructuredCollectionValue proposed =
                change.ProposedValue?.OrderedStructuredCollectionValue;
            GameContentReferenceRuntimeImpact runtimeImpact = field.StructuredCollection.RuntimeImpact;
            if (proposed != null)
            {
                for (int rowIndex = 0; rowIndex < proposed.Rows.Count; rowIndex++)
                {
                    GameContentStructuredRowValue row = proposed.Rows[rowIndex];
                    for (int fieldIndex = 0; fieldIndex < row.FieldValues.Count; fieldIndex++)
                    {
                        GameContentStructuredRowFieldValue child = row.FieldValues[fieldIndex];
                        if (child.Value.FieldType != GameContentFieldType.RecordReference) continue;
                        GameContentRecordReferenceValue reference = child.Value.RecordReferenceValue;
                        if (reference == null || !reference.IsResolved || reference.TargetKey == null) continue;
                        GameContentFieldDescriptor rowField = GameContentReferencePolicy.ResolveStructuredRowField(field, child.FieldId);
                        GameContentReferenceEvaluation evaluation = GameContentReferencePolicy.EvaluateStructuredReferenceTargetCore(
                            active,
                            field,
                            row.RowKey,
                            rowField,
                            reference.TargetKey);
                        runtimeImpact |= evaluation.RuntimeImpact;
                    }
                }
            }

            string pathPrefix = field.FieldId + "[";
            IReadOnlyList<GameContentAuthoringValidationIssue> findings = (active.Validation?.Issues ??
                Array.Empty<GameContentAuthoringValidationIssue>())
                .Where(issue => issue != null &&
                                (string.Equals(issue.Path, field.FieldId, StringComparison.Ordinal) ||
                                 issue.Path.StartsWith(pathPrefix, StringComparison.Ordinal)))
                .ToArray();
            return GameContentStructuredCollectionChangeReview.Create(
                active.RecordKey,
                field.FieldId,
                original,
                proposed,
                findings,
                runtimeImpact);
        }
    }
}
