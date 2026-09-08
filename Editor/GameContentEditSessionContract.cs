using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentEditSessionContract
    {
        internal static string ValidateSessionContract(
            GameContentEditRequest request,
            GameContentEditAvailability availability,
            string backendId,
            GameContentRecordKey recordKey,
            GameContentSourceTarget sourceTarget,
            GameContentSourceRevision originalRevision,
            GameContentEditSnapshot snapshot,
            IReadOnlyList<GameContentFieldDescriptor> fields,
            IGameContentEditSession backendSession)
        {
            if (!string.Equals(backendId, request.ProviderId, StringComparison.OrdinalIgnoreCase))
                return "The edit session backend ID does not match the registered provider.";
            if (recordKey == null || !recordKey.Equals(request.RecordKey))
                return "The edit session does not target the requested canonical record.";
            if (sourceTarget == null || !sourceTarget.IsValid ||
                !sourceTarget.Equals(availability.SourceTarget))
                return "The edit session does not target the source reported by availability.";
            if (originalRevision == null || !originalRevision.IsValid)
                return "The edit session has no valid original source revision.";
            if (snapshot == null || snapshot.RecordKey == null || !snapshot.RecordKey.Equals(recordKey) ||
                snapshot.SourceTarget == null || !snapshot.SourceTarget.Equals(sourceTarget) ||
                snapshot.SourceRevision == null || !snapshot.SourceRevision.Equals(originalRevision))
                return "The edit session snapshot does not match the requested record and source.";
            if (fields == null || fields.Count == 0 || fields.All(value => value.IsReadOnly))
                return "The edit session exposes no writable fields.";
            GameContentFieldDescriptor boundaryViolation = fields.FirstOrDefault(value =>
                value.FieldType == GameContentFieldType.OrderedStructuredCollection &&
                !string.IsNullOrWhiteSpace(value.StructuredCollection?.BoundaryViolationReason));
            if (boundaryViolation != null)
            {
                return "The edit session violates the structured-row boundary: " +
                       boundaryViolation.StructuredCollection.BoundaryViolationReason;
            }
            if (fields.Any(value => !value.IsValid)) return "The edit session exposes a field without a stable field ID.";
            if (fields.GroupBy(value => value.FieldId, StringComparer.Ordinal).Any(group => group.Count() > 1))
                return "The edit session exposes duplicate field IDs.";
            if (fields.Any(value => !Enum.IsDefined(typeof(GameContentFieldType), value.FieldType)))
                return "The edit session exposes an unsupported field type.";
            if (fields.Any(value => !snapshot.FieldValues.ContainsKey(value.FieldId)))
                return "The edit session snapshot is missing an exposed field value.";
            if (fields.Any(value => snapshot.FieldValues[value.FieldId] == null ||
                                    snapshot.FieldValues[value.FieldId].FieldType != value.FieldType))
                return "The edit session snapshot contains a field value with the wrong type.";
            if (fields.Any(value => value.FieldType == GameContentFieldType.RecordReference && !value.IsReadOnly) &&
                !(backendSession is IGameContentRecordReferenceEditSession))
                return "The edit session exposes a writable record reference without the optional reference-session contract.";
            if (fields.Any(value => value.FieldType.IsOrderedCollection() && !value.IsReadOnly) &&
                !(backendSession is IGameContentOrderedCollectionEditSession))
                return "The edit session exposes a writable ordered collection without the optional collection-session contract.";
            if (fields.Any(value => value.FieldType == GameContentFieldType.OrderedRecordReferenceCollection &&
                                    !value.IsReadOnly) &&
                !(backendSession is IGameContentRecordReferenceEditSession))
                return "The edit session exposes a writable record-reference collection without the optional reference-session contract.";
            if (fields.Any(value => value.FieldType == GameContentFieldType.OrderedStructuredCollection &&
                                    !value.IsReadOnly) &&
                !(backendSession is IGameContentStructuredCollectionEditSession))
            {
                return "The edit session exposes a writable structured-row collection without the optional structured-session contract.";
            }
            return string.Empty;
        }

        internal static string GetProviderId(GameContentPackContext context)
        {
            if (context?.Provider is IGameContentAuthoringProvider authoringProvider)
                return authoringProvider.ProviderId ?? string.Empty;
            return context?.Pack?.ProviderId ?? string.Empty;
        }

        internal static GameContentEditBeginResult BeginFailure(
            GameContentEditAvailability availability,
            string message)
        {
            return new GameContentEditBeginResult(false, message, availability, null, false);
        }
    }
}
