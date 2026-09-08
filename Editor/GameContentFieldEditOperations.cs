using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentFieldEditOperations
    {
        private readonly GameContentEditSessionRegistry _sessions;
        private readonly GameContentEditValidation _validation;

        internal GameContentFieldEditOperations(GameContentEditSessionRegistry sessions, GameContentEditValidation validation)
        {
            _sessions = sessions;
            _validation = validation;
        }

        public GameContentEditOperationResult Apply(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentFieldValue value)
        {
            if (!_sessions.Owns(active)) return GameContentEditOperationResult.Failure("The edit session is no longer active.");
            if (!GameContentEditBackendState.CanMutate(active)) return GameContentEditOperationResult.Failure("The edit session cannot accept changes in its current state.");
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, fieldId);
            if (field == null) return GameContentEditOperationResult.Failure("The field is not exposed by this edit session.");
            if (field.IsReadOnly) return GameContentEditOperationResult.Failure(field.ReadOnlyReason);
            if (field.FieldType.IsOrderedCollection())
                return GameContentEditOperationResult.Failure("Use an ordered collection operation to change this field.");
            if (field.FieldType == GameContentFieldType.OrderedStructuredCollection)
                return GameContentEditOperationResult.Failure("Use a structured-row operation to change this field.");
            if (value == null || value.FieldType != field.FieldType)
                return GameContentEditOperationResult.Failure("The proposed value does not match the field type.");
            if (field.FieldType == GameContentFieldType.RecordReference &&
                !field.Accepts(value, out string reason))
                return GameContentEditOperationResult.Failure(reason);

            if (field.FieldType == GameContentFieldType.RecordReference &&
                value.RecordReferenceValue != null && value.RecordReferenceValue.IsResolved)
            {
                GameContentReferenceEvaluation evaluation = GameContentReferencePolicy.EvaluateReferenceTargetCore(
                    active,
                    field,
                    value.RecordReferenceValue.TargetKey);
                if (!evaluation.IsValid) return GameContentEditOperationResult.Failure(evaluation.Reason);
            }

            try
            {
                GameContentEditOperationResult result = active.BackendSession.Apply(field.FieldId, value)
                    ?? GameContentEditOperationResult.Failure("The editing backend returned no apply result.");
                GameContentEditBackendState.RefreshFromBackend(active, false);
                active.Message = result.Message;
                return result;
            }
            catch (Exception exception)
            {
                return GameContentEditBackendState.OperationException(active, "Apply", exception, false);
            }
        }

        public GameContentEditOperationResult ValidateCollectionOperation(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentCollectionOperation operation)
        {
            if (!_sessions.Owns(active)) return GameContentEditOperationResult.Failure("The edit session is no longer active.");
            if (!GameContentEditBackendState.CanMutate(active))
                return GameContentEditOperationResult.Failure("The edit session cannot accept changes in its current state.");
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, fieldId);
            if (field == null || !field.FieldType.IsOrderedCollection() || field.Collection == null)
                return GameContentEditOperationResult.Failure("The field is not an editable ordered collection.");
            if (field.IsReadOnly) return GameContentEditOperationResult.Failure(field.ReadOnlyReason);
            if (!(active.BackendSession is IGameContentOrderedCollectionEditSession))
                return GameContentEditOperationResult.Failure("The editing backend does not support ordered collection operations.");

            GameContentOrderedCollectionValue current = active.GetEffectiveValue(field.FieldId)?.OrderedCollectionValue;
            if (!GameContentCollectionMutation.TryApply(field, current, operation, out _, out string reason))
                return GameContentEditOperationResult.Failure(reason);

            if (field.FieldType == GameContentFieldType.OrderedRecordReferenceCollection &&
                (operation.Kind == GameContentCollectionOperationKind.Add ||
                 operation.Kind == GameContentCollectionOperationKind.Replace))
            {
                GameContentRecordReferenceValue reference = operation.Value?.RecordReferenceValue;
                if (reference == null || !reference.IsResolved || reference.TargetKey == null)
                    return GameContentEditOperationResult.Failure("A resolved canonical record reference is required.");
                GameContentReferenceEvaluation evaluation = GameContentReferencePolicy.EvaluateReferenceTargetCore(active, field, reference.TargetKey);
                if (!evaluation.IsValid) return GameContentEditOperationResult.Failure(evaluation.Reason);
            }

            return GameContentEditOperationResult.Success();
        }

        public GameContentEditOperationResult ApplyCollectionOperation(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentCollectionOperation operation)
        {
            if (!_sessions.Owns(active)) return GameContentEditOperationResult.Failure("The edit session is no longer active.");
            GameContentStaleCheckResult stale = _validation.CheckStale(active);
            if (stale.IsStale)
                return GameContentEditOperationResult.Failure(
                    string.IsNullOrWhiteSpace(stale.Message) ? "The source changed after editing began." : stale.Message);

            GameContentEditOperationResult validation = ValidateCollectionOperation(active, fieldId, operation);
            if (!validation.Succeeded)
            {
                active.Message = validation.Message;
                return validation;
            }

            try
            {
                var collectionSession = (IGameContentOrderedCollectionEditSession)active.BackendSession;
                GameContentEditOperationResult result = collectionSession.ApplyCollectionOperation(fieldId, operation)
                    ?? GameContentEditOperationResult.Failure("The editing backend returned no collection-operation result.");
                GameContentEditBackendState.RefreshFromBackend(active, false);
                if (result.Succeeded) _validation.Preview(active);
                active.Message = result.Message;
                return result;
            }
            catch (Exception exception)
            {
                return GameContentEditBackendState.OperationException(active, "Collection operation", exception, false);
            }
        }

        public GameContentEditOperationResult RestoreOriginalCollectionOrder(
            GameContentActiveEditSession active,
            string fieldId)
        {
            if (!_sessions.Owns(active)) return GameContentEditOperationResult.Failure("The edit session is no longer active.");
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, fieldId);
            if (field == null || !field.FieldType.IsOrderedCollection())
                return GameContentEditOperationResult.Failure("The field is not an editable ordered collection.");
            GameContentOrderedCollectionValue current = active.GetEffectiveValue(fieldId)?.OrderedCollectionValue;
            IReadOnlyList<GameContentCollectionOperation> operations =
                GameContentCollectionMutation.BuildRestoreOriginalOrderOperations(current);
            if (operations.Count == 0)
                return GameContentEditOperationResult.Success("The collection is already in its original order.");

            for (int i = 0; i < operations.Count; i++)
            {
                GameContentEditOperationResult result = ApplyCollectionOperation(active, fieldId, operations[i]);
                if (!result.Succeeded) return result;
            }
            active.Message = "Restored the surviving original items to their original order.";
            return GameContentEditOperationResult.Success(active.Message);
        }

        public GameContentStructuredCollectionOperationResult ValidateStructuredOperation(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredCollectionOperation operation)
        {
            if (!_sessions.Owns(active))
                return GameContentStructuredCollectionOperationResult.Failure(
                    "The edit session is no longer active.");
            if (!GameContentEditBackendState.CanMutate(active))
            {
                return GameContentStructuredCollectionOperationResult.Failure(
                    "The edit session cannot accept changes in its current state.");
            }
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, fieldId);
            if (field?.FieldType != GameContentFieldType.OrderedStructuredCollection ||
                field.StructuredCollection == null)
            {
                return GameContentStructuredCollectionOperationResult.Failure(
                    "The field is not an editable ordered structured collection.");
            }
            if (field.IsReadOnly)
                return GameContentStructuredCollectionOperationResult.Failure(field.ReadOnlyReason);
            if (!(active.BackendSession is IGameContentStructuredCollectionEditSession))
            {
                return GameContentStructuredCollectionOperationResult.Failure(
                    "The editing backend does not support structured-row operations.");
            }

            GameContentOrderedStructuredCollectionValue current =
                active.GetEffectiveValue(field.FieldId)?.OrderedStructuredCollectionValue;
            if (!GameContentStructuredCollectionMutation.TryApply(
                    field,
                    current,
                    operation,
                    out GameContentOrderedStructuredCollectionValue proposed,
                    out GameContentStructuredRowKey affectedRowKey,
                    out string reason))
                return GameContentStructuredCollectionOperationResult.Failure(reason);

            IReadOnlyList<GameContentAuthoringValidationIssue> referenceIssues =
                GameContentReferencePolicy.EvaluateStructuredCollectionReferences(active, field, proposed);
            GameContentAuthoringValidationIssue error = referenceIssues.FirstOrDefault(issue =>
                issue.Severity == GameContentAuthoringValidationSeverity.Error);
            if (error != null)
                return GameContentStructuredCollectionOperationResult.Failure(error.Message);

            return GameContentStructuredCollectionOperationResult.Success(rowKey: affectedRowKey);
        }

        public GameContentStructuredCollectionOperationResult ApplyStructuredOperation(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredCollectionOperation operation)
        {
            if (!_sessions.Owns(active))
                return GameContentStructuredCollectionOperationResult.Failure(
                    "The edit session is no longer active.");
            GameContentStaleCheckResult stale = _validation.CheckStale(active);
            if (stale.IsStale)
            {
                return GameContentStructuredCollectionOperationResult.Failure(
                    string.IsNullOrWhiteSpace(stale.Message)
                        ? "The source changed after editing began."
                        : stale.Message);
            }

            GameContentStructuredCollectionOperation prepared = operation;
            if (operation?.Kind == GameContentStructuredCollectionOperationKind.AddRow &&
                operation.RowKey == null)
            {
                prepared = operation.BindGeneratedRowKey(GameContentStructuredRowKey.CreateSessionKey());
            }
            GameContentStructuredCollectionOperationResult validation = ValidateStructuredOperation(
                active,
                fieldId,
                prepared);
            if (!validation.Succeeded)
            {
                active.Message = validation.Message;
                return validation;
            }

            try
            {
                var structuredSession = (IGameContentStructuredCollectionEditSession)active.BackendSession;
                GameContentStructuredCollectionOperationResult result =
                    structuredSession.ApplyStructuredOperation(fieldId, prepared) ??
                    GameContentStructuredCollectionOperationResult.Failure(
                        "The editing backend returned no structured-operation result.");
                if (result.Succeeded && prepared.Kind == GameContentStructuredCollectionOperationKind.AddRow &&
                    (result.RowKey == null || !result.RowKey.Equals(prepared.RowKey)))
                {
                    result = GameContentStructuredCollectionOperationResult.Failure(
                        "The editing backend did not preserve the coordinator-generated structured-row key.");
                }
                GameContentEditBackendState.RefreshFromBackend(active, false);
                if (result.Succeeded) _validation.Preview(active);
                active.Message = result.Message;
                return result;
            }
            catch (Exception exception)
            {
                GameContentEditOperationResult contained = GameContentEditBackendState.OperationException(
                    active,
                    "Structured-row operation",
                    exception,
                    false);
                return GameContentStructuredCollectionOperationResult.Failure(contained.Message);
            }
        }

        public GameContentStructuredCollectionOperationResult RestoreOriginalStructuredOrder(
            GameContentActiveEditSession active,
            string fieldId)
        {
            GameContentFieldDescriptor field = GameContentReferencePolicy.FindField(active, fieldId);
            if (field?.FieldType != GameContentFieldType.OrderedStructuredCollection)
            {
                return GameContentStructuredCollectionOperationResult.Failure(
                    "The field is not an editable ordered structured collection.");
            }
            GameContentOrderedStructuredCollectionValue current =
                active.GetEffectiveValue(fieldId)?.OrderedStructuredCollectionValue;
            if (!GameContentStructuredCollectionMutation.NeedsRestoreOriginalOrder(current))
            {
                return GameContentStructuredCollectionOperationResult.Success(
                    "The structured rows are already in their original order.");
            }
            return ApplyStructuredOperation(
                active,
                fieldId,
                GameContentStructuredCollectionOperation.RestoreOriginalOrder());
        }

        public GameContentEditOperationResult Undo(GameContentActiveEditSession active)
        {
            if (!_sessions.Owns(active)) return GameContentEditOperationResult.Failure("The edit session is no longer active.");
            if (!GameContentEditBackendState.CanMutate(active) || !active.CanUndo) return GameContentEditOperationResult.Failure("There is no staged change to undo.");
            GameContentStaleCheckResult stale = _validation.CheckStale(active);
            if (stale.IsStale) return GameContentEditOperationResult.Failure(active.Message);
            try
            {
                GameContentEditOperationResult result = active.BackendSession.Undo()
                    ?? GameContentEditOperationResult.Failure("The editing backend returned no Undo result.");
                GameContentEditBackendState.RefreshFromBackend(active, false);
                if (result.Succeeded) _validation.Preview(active);
                active.Message = result.Message;
                return result;
            }
            catch (Exception exception)
            {
                return GameContentEditBackendState.OperationException(active, "Undo", exception, false);
            }
        }

        public GameContentEditOperationResult Redo(GameContentActiveEditSession active)
        {
            if (!_sessions.Owns(active)) return GameContentEditOperationResult.Failure("The edit session is no longer active.");
            if (!GameContentEditBackendState.CanMutate(active) || !active.CanRedo) return GameContentEditOperationResult.Failure("There is no staged change to redo.");
            GameContentStaleCheckResult stale = _validation.CheckStale(active);
            if (stale.IsStale) return GameContentEditOperationResult.Failure(active.Message);
            try
            {
                GameContentEditOperationResult result = active.BackendSession.Redo()
                    ?? GameContentEditOperationResult.Failure("The editing backend returned no Redo result.");
                GameContentEditBackendState.RefreshFromBackend(active, false);
                if (result.Succeeded) _validation.Preview(active);
                active.Message = result.Message;
                return result;
            }
            catch (Exception exception)
            {
                return GameContentEditBackendState.OperationException(active, "Redo", exception, false);
            }
        }
    }
}
