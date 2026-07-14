using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    public enum GameContentEditAvailabilityState
    {
        ReadOnly = 0,
        Editable = 1
    }

    public enum GameContentFieldType
    {
        String = 0,
        Integer = 1,
        Number = 2,
        Boolean = 3,
        Enum = 4,
        RecordReference = 5,
        OrderedScalarCollection = 6,
        OrderedRecordReferenceCollection = 7
    }

    public enum GameContentEditSessionState
    {
        Clean = 0,
        Dirty = 1,
        Stale = 2,
        Committing = 3,
        Committed = 4,
        Conflict = 5,
        RecoveryRequired = 6,
        RolledBack = 7
    }

    public enum GameContentEditValidationState
    {
        Valid = 0,
        Warning = 1,
        Invalid = 2
    }

    public interface IGameContentPackEditProvider
    {
        GameContentEditAvailability CanEdit(GameContentEditRequest request);
        IGameContentEditSession BeginEdit(GameContentEditRequest request);
    }

    public interface IGameContentEditSession : IDisposable
    {
        string BackendId { get; }
        GameContentRecordKey RecordKey { get; }
        GameContentSourceTarget SourceTarget { get; }
        GameContentSourceRevision OriginalRevision { get; }
        GameContentEditSessionState State { get; }
        GameContentEditSnapshot Snapshot { get; }
        IReadOnlyList<GameContentFieldDescriptor> Fields { get; }
        IReadOnlyList<GameContentProposedChange> Changes { get; }
        bool CanUndo { get; }
        bool CanRedo { get; }
        GameContentEditOperationResult Apply(string fieldId, GameContentFieldValue value);
        GameContentEditOperationResult Undo();
        GameContentEditOperationResult Redo();
        GameContentValidationPreview Preview();
        GameContentStaleCheckResult CheckStale();
        GameContentCommitResult Commit(bool confirmWarnings);
        GameContentRollbackResult Rollback();
    }

    public sealed class GameContentEditAvailability
    {
        public GameContentEditAvailability(
            GameContentEditAvailabilityState state,
            string disabledReason,
            string backendId,
            int supportedFieldCount,
            GameContentSourceTarget sourceTarget = null)
        {
            State = state;
            DisabledReason = state == GameContentEditAvailabilityState.Editable
                ? string.Empty
                : Normalize(disabledReason, "This record is read-only.");
            BackendId = Normalize(backendId);
            SupportedFieldCount = Math.Max(0, supportedFieldCount);
            SourceTarget = sourceTarget;
        }

        public GameContentEditAvailabilityState State { get; }
        public bool IsEditable => State == GameContentEditAvailabilityState.Editable;
        public string DisabledReason { get; }
        public string BackendId { get; }
        public int SupportedFieldCount { get; }
        public GameContentSourceTarget SourceTarget { get; }

        public static GameContentEditAvailability Editable(
            string backendId,
            int supportedFieldCount,
            GameContentSourceTarget sourceTarget)
        {
            return new GameContentEditAvailability(
                GameContentEditAvailabilityState.Editable,
                string.Empty,
                backendId,
                supportedFieldCount,
                sourceTarget);
        }

        public static GameContentEditAvailability ReadOnly(
            string reason,
            string backendId = null,
            int supportedFieldCount = 0,
            GameContentSourceTarget sourceTarget = null)
        {
            return new GameContentEditAvailability(
                GameContentEditAvailabilityState.ReadOnly,
                reason,
                backendId,
                supportedFieldCount,
                sourceTarget);
        }

        private static string Normalize(string value, string fallback = "")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    public sealed class GameContentEditRequest
    {
        public GameContentEditRequest(
            string selectedPackKey,
            GameContentRecordKey recordKey,
            string providerId,
            string lensId = null)
        {
            SelectedPackKey = Normalize(selectedPackKey);
            RecordKey = recordKey;
            ProviderId = Normalize(providerId);
            LensId = Normalize(lensId);
        }

        public string SelectedPackKey { get; }
        public GameContentRecordKey RecordKey { get; }
        public string ProviderId { get; }
        public string LensId { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(SelectedPackKey) &&
                               RecordKey != null && RecordKey.IsValid &&
                               !string.IsNullOrWhiteSpace(ProviderId);

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class GameContentSourceTarget : IEquatable<GameContentSourceTarget>
    {
        public GameContentSourceTarget(
            string lockKey,
            string sourceLabel,
            string projectRelativeDescription,
            string providerToken)
        {
            LockKey = Normalize(lockKey);
            SourceLabel = Normalize(sourceLabel);
            ProjectRelativeDescription = Normalize(projectRelativeDescription);
            ProviderToken = Normalize(providerToken);
        }

        public string LockKey { get; }
        public string SourceLabel { get; }
        public string ProjectRelativeDescription { get; }
        public string ProviderToken { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(LockKey) && !string.IsNullOrWhiteSpace(SourceLabel);

        public bool Equals(GameContentSourceTarget other)
        {
            return other != null && string.Equals(LockKey, other.LockKey, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentSourceTarget);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(LockKey);
        }

        public override string ToString()
        {
            return SourceLabel;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class GameContentSourceRevision : IEquatable<GameContentSourceRevision>
    {
        public GameContentSourceRevision(string token)
        {
            Token = string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
        }

        public string Token { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Token);

        public bool Equals(GameContentSourceRevision other)
        {
            return other != null && string.Equals(Token, other.Token, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentSourceRevision);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Token);
        }

        public override string ToString()
        {
            return Token;
        }
    }

    public sealed class GameContentFieldValue : IEquatable<GameContentFieldValue>
    {
        private GameContentFieldValue(
            GameContentFieldType fieldType,
            string stringValue,
            long integerValue,
            double numberValue,
            bool booleanValue,
            GameContentRecordReferenceValue recordReferenceValue,
            GameContentOrderedCollectionValue orderedCollectionValue)
        {
            FieldType = fieldType;
            StringValue = stringValue ?? string.Empty;
            IntegerValue = integerValue;
            NumberValue = numberValue;
            BooleanValue = booleanValue;
            RecordReferenceValue = recordReferenceValue;
            OrderedCollectionValue = orderedCollectionValue;
        }

        public GameContentFieldType FieldType { get; }
        public string StringValue { get; }
        public long IntegerValue { get; }
        public double NumberValue { get; }
        public bool BooleanValue { get; }
        public GameContentRecordReferenceValue RecordReferenceValue { get; }
        public GameContentOrderedCollectionValue OrderedCollectionValue { get; }

        public static GameContentFieldValue FromString(string value)
        {
            return new GameContentFieldValue(GameContentFieldType.String, value, 0L, 0d, false, null, null);
        }

        public static GameContentFieldValue FromInteger(long value)
        {
            return new GameContentFieldValue(GameContentFieldType.Integer, string.Empty, value, 0d, false, null, null);
        }

        public static GameContentFieldValue FromNumber(double value)
        {
            return new GameContentFieldValue(GameContentFieldType.Number, string.Empty, 0L, value, false, null, null);
        }

        public static GameContentFieldValue FromBoolean(bool value)
        {
            return new GameContentFieldValue(GameContentFieldType.Boolean, string.Empty, 0L, 0d, value, null, null);
        }

        public static GameContentFieldValue FromEnum(string token)
        {
            return new GameContentFieldValue(GameContentFieldType.Enum, token, 0L, 0d, false, null, null);
        }

        public static GameContentFieldValue FromRecordReference(GameContentRecordReferenceValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new GameContentFieldValue(
                GameContentFieldType.RecordReference,
                string.Empty,
                0L,
                0d,
                false,
                value,
                null);
        }

        public static GameContentFieldValue FromOrderedCollection(GameContentOrderedCollectionValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new GameContentFieldValue(
                value.FieldType,
                string.Empty,
                0L,
                0d,
                false,
                null,
                value);
        }

        public static GameContentFieldValue FromOrderedScalarCollection(GameContentOrderedCollectionValue value)
        {
            if (value == null || value.FieldType != GameContentFieldType.OrderedScalarCollection)
                throw new ArgumentException("An ordered scalar collection value is required.", nameof(value));
            return FromOrderedCollection(value);
        }

        public static GameContentFieldValue FromOrderedRecordReferenceCollection(GameContentOrderedCollectionValue value)
        {
            if (value == null || value.FieldType != GameContentFieldType.OrderedRecordReferenceCollection)
                throw new ArgumentException("An ordered record-reference collection value is required.", nameof(value));
            return FromOrderedCollection(value);
        }

        public string ToDisplayString()
        {
            switch (FieldType)
            {
                case GameContentFieldType.Integer:
                    return IntegerValue.ToString(CultureInfo.InvariantCulture);
                case GameContentFieldType.Number:
                    return NumberValue.ToString("R", CultureInfo.InvariantCulture);
                case GameContentFieldType.Boolean:
                    return BooleanValue ? "True" : "False";
                case GameContentFieldType.RecordReference:
                    return RecordReferenceValue == null ? string.Empty : RecordReferenceValue.ToDisplayString();
                case GameContentFieldType.OrderedScalarCollection:
                case GameContentFieldType.OrderedRecordReferenceCollection:
                    return OrderedCollectionValue == null ? string.Empty : OrderedCollectionValue.ToDisplayString();
                default:
                    return StringValue;
            }
        }

        public bool Equals(GameContentFieldValue other)
        {
            if (other == null || FieldType != other.FieldType) return false;
            switch (FieldType)
            {
                case GameContentFieldType.Integer:
                    return IntegerValue == other.IntegerValue;
                case GameContentFieldType.Number:
                    return NumberValue.Equals(other.NumberValue);
                case GameContentFieldType.Boolean:
                    return BooleanValue == other.BooleanValue;
                case GameContentFieldType.RecordReference:
                    return Equals(RecordReferenceValue, other.RecordReferenceValue);
                case GameContentFieldType.OrderedScalarCollection:
                case GameContentFieldType.OrderedRecordReferenceCollection:
                    return Equals(OrderedCollectionValue, other.OrderedCollectionValue);
                default:
                    return string.Equals(StringValue, other.StringValue, StringComparison.Ordinal);
            }
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentFieldValue);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)FieldType;
                switch (FieldType)
                {
                    case GameContentFieldType.Integer:
                        return (hash * 397) ^ IntegerValue.GetHashCode();
                    case GameContentFieldType.Number:
                        return (hash * 397) ^ NumberValue.GetHashCode();
                    case GameContentFieldType.Boolean:
                        return (hash * 397) ^ BooleanValue.GetHashCode();
                    case GameContentFieldType.RecordReference:
                        return (hash * 397) ^ (RecordReferenceValue == null ? 0 : RecordReferenceValue.GetHashCode());
                    case GameContentFieldType.OrderedScalarCollection:
                    case GameContentFieldType.OrderedRecordReferenceCollection:
                        return (hash * 397) ^ (OrderedCollectionValue == null ? 0 : OrderedCollectionValue.GetHashCode());
                    default:
                        return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(StringValue);
                }
            }
        }

        public override string ToString()
        {
            return ToDisplayString();
        }
    }

    public sealed class GameContentEnumOption
    {
        public GameContentEnumOption(string token, string displayName)
        {
            Token = string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Token : displayName.Trim();
        }

        public string Token { get; }
        public string DisplayName { get; }
    }

    public sealed class GameContentFieldDescriptor
    {
        public GameContentFieldDescriptor(
            string fieldId,
            string semanticId,
            string displayName,
            string description,
            GameContentFieldType fieldType,
            bool readOnly = false,
            string readOnlyReason = null,
            int order = 0,
            string group = null,
            bool required = false,
            double? minimumNumber = null,
            double? maximumNumber = null,
            int? minimumLength = null,
            int? maximumLength = null,
            IEnumerable<GameContentEnumOption> enumOptions = null,
            GameContentRecordReferenceFieldDescriptor recordReference = null,
            GameContentCollectionFieldDescriptor collection = null)
        {
            FieldId = Normalize(fieldId);
            SemanticId = Normalize(semanticId);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? FieldId : displayName.Trim();
            Description = Normalize(description);
            FieldType = fieldType;
            IsReadOnly = readOnly;
            ReadOnlyReason = readOnly ? Normalize(readOnlyReason, "This field is read-only.") : string.Empty;
            Order = order;
            Group = Normalize(group, "General");
            Required = required;
            MinimumNumber = minimumNumber;
            MaximumNumber = maximumNumber;
            MinimumLength = minimumLength;
            MaximumLength = maximumLength;
            EnumOptions = enumOptions == null
                ? Array.Empty<GameContentEnumOption>()
                : enumOptions.Where(value => value != null && !string.IsNullOrWhiteSpace(value.Token)).ToArray();
            RecordReference = recordReference;
            Collection = collection;
        }

        public string FieldId { get; }
        public string SemanticId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public GameContentFieldType FieldType { get; }
        public bool IsReadOnly { get; }
        public string ReadOnlyReason { get; }
        public int Order { get; }
        public string Group { get; }
        public bool Required { get; }
        public double? MinimumNumber { get; }
        public double? MaximumNumber { get; }
        public int? MinimumLength { get; }
        public int? MaximumLength { get; }
        public IReadOnlyList<GameContentEnumOption> EnumOptions { get; }
        public GameContentRecordReferenceFieldDescriptor RecordReference { get; }
        public GameContentCollectionFieldDescriptor Collection { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(FieldId) &&
                               (FieldType == GameContentFieldType.RecordReference
                                   ? RecordReference != null && RecordReference.IsValid && Collection == null
                                   : FieldType.IsOrderedCollection()
                                       ? RecordReference == null && Collection != null && Collection.IsValidFor(FieldType)
                                       : RecordReference == null && Collection == null);

        public bool Accepts(GameContentFieldValue value, out string reason)
        {
            if (IsReadOnly)
            {
                reason = ReadOnlyReason;
                return false;
            }

            if (value == null || value.FieldType != FieldType)
            {
                reason = "The proposed value does not match the field type.";
                return false;
            }

            if (FieldType == GameContentFieldType.RecordReference)
            {
                GameContentRecordReferenceValue reference = value.RecordReferenceValue;
                if (RecordReference == null || reference == null)
                {
                    reason = "The record-reference field has no valid reference metadata or value.";
                    return false;
                }

                if (reference.IsNone)
                {
                    if (Required || !RecordReference.AllowClear)
                    {
                        reason = "A record reference is required.";
                        return false;
                    }

                    reason = string.Empty;
                    return true;
                }

                if (reference.IsBroken)
                {
                    reason = string.IsNullOrWhiteSpace(reference.BrokenReason)
                        ? "The current record reference is broken. Select a valid target before committing."
                        : reference.BrokenReason;
                    return false;
                }

                if (!reference.IsResolved || reference.TargetKey == null || !reference.TargetKey.IsValid)
                {
                    reason = "The record reference has no valid canonical target key.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (FieldType.IsOrderedCollection())
            {
                if (Collection == null || value.OrderedCollectionValue == null)
                {
                    reason = "The ordered-collection field has no valid collection metadata or value.";
                    return false;
                }
                return Collection.Accepts(FieldType, Required, value.OrderedCollectionValue, out reason);
            }

            if (FieldType == GameContentFieldType.String || FieldType == GameContentFieldType.Enum)
            {
                string text = value.StringValue ?? string.Empty;
                if (Required && string.IsNullOrWhiteSpace(text))
                {
                    reason = "A value is required.";
                    return false;
                }

                if (MinimumLength.HasValue && text.Length < MinimumLength.Value)
                {
                    reason = "The value is shorter than the allowed minimum.";
                    return false;
                }

                if (MaximumLength.HasValue && text.Length > MaximumLength.Value)
                {
                    reason = "The value is longer than the allowed maximum.";
                    return false;
                }

                if (FieldType == GameContentFieldType.Enum && EnumOptions.Count > 0 &&
                    !EnumOptions.Any(option => string.Equals(option.Token, text, StringComparison.Ordinal)))
                {
                    reason = "The enum token is not one of the supported options.";
                    return false;
                }
            }

            double numericValue = FieldType == GameContentFieldType.Integer
                ? value.IntegerValue
                : value.NumberValue;
            if (FieldType == GameContentFieldType.Number &&
                (double.IsNaN(numericValue) || double.IsInfinity(numericValue)))
            {
                reason = "The number must be finite.";
                return false;
            }

            if ((FieldType == GameContentFieldType.Integer || FieldType == GameContentFieldType.Number) &&
                MinimumNumber.HasValue && numericValue < MinimumNumber.Value)
            {
                reason = "The value is below the allowed minimum.";
                return false;
            }

            if ((FieldType == GameContentFieldType.Integer || FieldType == GameContentFieldType.Number) &&
                MaximumNumber.HasValue && numericValue > MaximumNumber.Value)
            {
                reason = "The value is above the allowed maximum.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static string Normalize(string value, string fallback = "")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    public sealed class GameContentEditSnapshot
    {
        private readonly IReadOnlyDictionary<string, GameContentFieldValue> _fieldValues;

        public GameContentEditSnapshot(
            GameContentRecordKey recordKey,
            GameContentSourceTarget sourceTarget,
            GameContentSourceRevision sourceRevision,
            IReadOnlyDictionary<string, GameContentFieldValue> fieldValues,
            DateTime createdUtc,
            string providerSchemaVersion = null)
        {
            RecordKey = recordKey;
            SourceTarget = sourceTarget;
            SourceRevision = sourceRevision;
            CreatedUtc = createdUtc.Kind == DateTimeKind.Utc ? createdUtc : createdUtc.ToUniversalTime();
            ProviderSchemaVersion = string.IsNullOrWhiteSpace(providerSchemaVersion)
                ? string.Empty
                : providerSchemaVersion.Trim();
            var copy = new Dictionary<string, GameContentFieldValue>(StringComparer.Ordinal);
            if (fieldValues != null)
            {
                foreach (KeyValuePair<string, GameContentFieldValue> pair in fieldValues)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                    copy[pair.Key.Trim()] = pair.Value;
                }
            }
            _fieldValues = new ReadOnlyDictionary<string, GameContentFieldValue>(copy);
        }

        public GameContentRecordKey RecordKey { get; }
        public GameContentSourceTarget SourceTarget { get; }
        public GameContentSourceRevision SourceRevision { get; }
        public IReadOnlyDictionary<string, GameContentFieldValue> FieldValues => _fieldValues;
        public DateTime CreatedUtc { get; }
        public string ProviderSchemaVersion { get; }

        public bool TryGetValue(string fieldId, out GameContentFieldValue value)
        {
            return _fieldValues.TryGetValue(fieldId ?? string.Empty, out value);
        }
    }

    public sealed class GameContentProposedChange
    {
        public GameContentProposedChange(
            string fieldId,
            GameContentFieldValue oldValue,
            GameContentFieldValue proposedValue,
            string displayName,
            string group,
            int order)
        {
            FieldId = string.IsNullOrWhiteSpace(fieldId) ? string.Empty : fieldId.Trim();
            OldValue = oldValue;
            ProposedValue = proposedValue;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? FieldId : displayName.Trim();
            Group = string.IsNullOrWhiteSpace(group) ? "General" : group.Trim();
            Order = order;
        }

        public string FieldId { get; }
        public GameContentFieldValue OldValue { get; }
        public GameContentFieldValue ProposedValue { get; }
        public string DisplayName { get; }
        public string Group { get; }
        public int Order { get; }
    }

    public sealed class GameContentValidationPreview
    {
        public GameContentValidationPreview(
            IEnumerable<GameContentAuthoringValidationIssue> issues,
            bool canCommit = true,
            bool warningsRequireConfirmation = true)
        {
            Issues = issues == null
                ? Array.Empty<GameContentAuthoringValidationIssue>()
                : issues.Where(value => value != null).ToArray();
            ErrorCount = Issues.Count(value => value.Severity == GameContentAuthoringValidationSeverity.Error);
            WarningCount = Issues.Count(value => value.Severity == GameContentAuthoringValidationSeverity.Warning);
            InfoCount = Issues.Count(value => value.Severity == GameContentAuthoringValidationSeverity.Info);
            CanCommit = canCommit && ErrorCount == 0;
            RequiresWarningConfirmation = CanCommit && warningsRequireConfirmation && WarningCount > 0;
            State = !CanCommit
                ? GameContentEditValidationState.Invalid
                : WarningCount > 0
                    ? GameContentEditValidationState.Warning
                    : GameContentEditValidationState.Valid;
        }

        public IReadOnlyList<GameContentAuthoringValidationIssue> Issues { get; }
        public int ErrorCount { get; }
        public int WarningCount { get; }
        public int InfoCount { get; }
        public bool CanCommit { get; }
        public bool RequiresWarningConfirmation { get; }
        public GameContentEditValidationState State { get; }
        public static GameContentValidationPreview Valid { get; } =
            new GameContentValidationPreview(Array.Empty<GameContentAuthoringValidationIssue>());

        public static GameContentValidationPreview Error(string path, string message)
        {
            return new GameContentValidationPreview(new[]
            {
                GameContentAuthoringValidationIssue.Error(path, message)
            }, false);
        }
    }

    public sealed class GameContentEditOperationResult
    {
        public GameContentEditOperationResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string Message { get; }

        public static GameContentEditOperationResult Success(string message = null)
        {
            return new GameContentEditOperationResult(true, message);
        }

        public static GameContentEditOperationResult Failure(string message)
        {
            return new GameContentEditOperationResult(false, message);
        }
    }

    public sealed class GameContentStaleCheckResult
    {
        public GameContentStaleCheckResult(
            bool isStale,
            string message,
            GameContentSourceRevision currentRevision)
        {
            IsStale = isStale;
            Message = message ?? string.Empty;
            CurrentRevision = currentRevision;
        }

        public bool IsStale { get; }
        public string Message { get; }
        public GameContentSourceRevision CurrentRevision { get; }

        public static GameContentStaleCheckResult Current(GameContentSourceRevision revision)
        {
            return new GameContentStaleCheckResult(false, string.Empty, revision);
        }

        public static GameContentStaleCheckResult Stale(string message, GameContentSourceRevision revision)
        {
            return new GameContentStaleCheckResult(true, message, revision);
        }
    }

    public sealed class GameContentRecoveryRecord
    {
        public GameContentRecoveryRecord(
            string backendId,
            string sourceLockKey,
            string sourceLabel,
            GameContentSourceRevision oldRevision,
            GameContentSourceRevision newRevision,
            DateTime timestampUtc,
            string phase,
            string actionableMessage)
        {
            BackendId = Normalize(backendId);
            SourceLockKey = Normalize(sourceLockKey);
            SourceLabel = Normalize(sourceLabel);
            OldRevision = oldRevision;
            NewRevision = newRevision;
            TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
            Phase = Normalize(phase);
            ActionableMessage = Normalize(actionableMessage);
        }

        public string BackendId { get; }
        public string SourceLockKey { get; }
        public string SourceLabel { get; }
        public GameContentSourceRevision OldRevision { get; }
        public GameContentSourceRevision NewRevision { get; }
        public DateTime TimestampUtc { get; }
        public string Phase { get; }
        public string ActionableMessage { get; }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class GameContentCommitResult
    {
        public GameContentCommitResult(
            bool succeeded,
            string message,
            GameContentSourceRevision previousRevision,
            GameContentSourceRevision newRevision,
            bool requiresRefresh = false,
            bool requiresRebind = false,
            bool requiresRestart = false,
            GameContentRecoveryRecord recovery = null)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            PreviousRevision = previousRevision;
            NewRevision = newRevision;
            RequiresRefresh = requiresRefresh;
            RequiresRebind = requiresRebind;
            RequiresRestart = requiresRestart;
            Recovery = recovery;
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public GameContentSourceRevision PreviousRevision { get; }
        public GameContentSourceRevision NewRevision { get; }
        public bool RequiresRefresh { get; }
        public bool RequiresRebind { get; }
        public bool RequiresRestart { get; }
        public GameContentRecoveryRecord Recovery { get; }

        public static GameContentCommitResult Failure(
            string message,
            GameContentSourceRevision previousRevision,
            GameContentRecoveryRecord recovery = null)
        {
            return new GameContentCommitResult(false, message, previousRevision, previousRevision, recovery: recovery);
        }
    }

    public sealed class GameContentRollbackResult
    {
        public GameContentRollbackResult(
            bool succeeded,
            string message,
            GameContentSourceRevision restoredRevision,
            GameContentRecoveryRecord recovery = null)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            RestoredRevision = restoredRevision;
            Recovery = recovery;
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public GameContentSourceRevision RestoredRevision { get; }
        public GameContentRecoveryRecord Recovery { get; }

        public static GameContentRollbackResult Failure(
            string message,
            GameContentSourceRevision restoredRevision,
            GameContentRecoveryRecord recovery = null)
        {
            return new GameContentRollbackResult(false, message, restoredRevision, recovery);
        }
    }
}
