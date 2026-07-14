using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    [Flags]
    public enum GameContentStructuredCollectionPermittedOperations
    {
        None = 0,
        AddRow = 1 << 0,
        RemoveRow = 1 << 1,
        MoveRow = 1 << 2,
        ReplaceRowField = 1 << 3,
        RestoreOriginalOrder = 1 << 4,
        All = AddRow | RemoveRow | MoveRow | ReplaceRowField | RestoreOriginalOrder
    }

    public enum GameContentStructuredRowDuplicatePolicy
    {
        Allow = 0,
        RejectPersistedDuplicates = 1
    }

    public sealed class GameContentStructuredRowNativeKeyDescriptor
    {
        public GameContentStructuredRowNativeKeyDescriptor(
            string displayName,
            string helpText = null,
            bool representsIndependentCanonicalRecord = false)
        {
            DisplayName = Normalize(displayName, "Provider Native Key");
            HelpText = Normalize(helpText);
            RepresentsIndependentCanonicalRecord = representsIndependentCanonicalRecord;
        }

        public string DisplayName { get; }
        public string HelpText { get; }
        public bool RepresentsIndependentCanonicalRecord { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(DisplayName) &&
                               !RepresentsIndependentCanonicalRecord;
        public string BoundaryViolationReason => RepresentsIndependentCanonicalRecord
            ? "A child with independent canonical identity is a record and must use CRUD, not structured-row editing."
            : string.Empty;

        private static string Normalize(string value, string fallback = "")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    public sealed class GameContentStructuredRowDescriptor
    {
        private readonly IReadOnlyList<GameContentFieldDescriptor> _fields;
        private readonly IReadOnlyList<string> _summaryFieldIds;

        public GameContentStructuredRowDescriptor(
            string rowSchemaId,
            string displayName,
            string helpText,
            IEnumerable<GameContentFieldDescriptor> fields,
            IEnumerable<string> summaryFieldIds = null,
            GameContentStructuredRowNativeKeyDescriptor nativeKey = null,
            bool supportsAdd = true,
            bool supportsRemove = true,
            bool supportsMove = true,
            bool supportsRowFieldReplacement = true,
            bool representsIndependentCanonicalRecord = false)
        {
            RowSchemaId = Normalize(rowSchemaId);
            DisplayName = Normalize(displayName, RowSchemaId);
            HelpText = Normalize(helpText);
            _fields = new ReadOnlyCollection<GameContentFieldDescriptor>(
                (fields ?? Array.Empty<GameContentFieldDescriptor>())
                .Where(value => value != null)
                .OrderBy(value => value.Order)
                .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.FieldId, StringComparer.Ordinal)
                .ToArray());
            _summaryFieldIds = new ReadOnlyCollection<string>(
                (summaryFieldIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray());
            NativeKey = nativeKey;
            SupportsAdd = supportsAdd;
            SupportsRemove = supportsRemove;
            SupportsMove = supportsMove;
            SupportsRowFieldReplacement = supportsRowFieldReplacement;
            RepresentsIndependentCanonicalRecord = representsIndependentCanonicalRecord;
        }

        public string RowSchemaId { get; }
        public string DisplayName { get; }
        public string HelpText { get; }
        public IReadOnlyList<GameContentFieldDescriptor> Fields => _fields;
        public IReadOnlyList<string> SummaryFieldIds => _summaryFieldIds;
        public GameContentStructuredRowNativeKeyDescriptor NativeKey { get; }
        public bool SupportsAdd { get; }
        public bool SupportsRemove { get; }
        public bool SupportsMove { get; }
        public bool SupportsRowFieldReplacement { get; }
        public bool RepresentsIndependentCanonicalRecord { get; }
        public string BoundaryViolationReason
        {
            get
            {
                if (RepresentsIndependentCanonicalRecord)
                {
                    return "A structured row cannot represent an independently canonical record. " +
                           "Adding or removing that child belongs to record CRUD.";
                }
                return NativeKey?.BoundaryViolationReason ?? string.Empty;
            }
        }

        public bool IsValid
        {
            get
            {
                if (string.IsNullOrWhiteSpace(RowSchemaId) || _fields.Count < 2 ||
                    !string.IsNullOrWhiteSpace(BoundaryViolationReason))
                    return false;
                if (_fields.Any(field => !field.IsValid || !IsSupportedRowField(field.FieldType))) return false;
                if (_fields.GroupBy(field => field.FieldId, StringComparer.Ordinal).Any(group => group.Count() > 1))
                    return false;
                if (_summaryFieldIds.Any(id => FindField(id) == null)) return false;
                if (NativeKey != null && !NativeKey.IsValid) return false;
                if (SupportsAdd && _fields.Any(field => field.IsReadOnly && field.Required)) return false;
                return true;
            }
        }

        public GameContentFieldDescriptor FindField(string fieldId)
        {
            return _fields.FirstOrDefault(field =>
                string.Equals(field.FieldId, fieldId, StringComparison.Ordinal));
        }

        public bool AcceptsRow(GameContentStructuredRowValue row, out string reason)
        {
            if (!IsValid)
            {
                reason = string.IsNullOrWhiteSpace(BoundaryViolationReason)
                    ? "The structured-row schema is invalid."
                    : BoundaryViolationReason;
                return false;
            }
            if (row == null || !string.Equals(row.SchemaId, RowSchemaId, StringComparison.Ordinal))
            {
                reason = "The structured row does not match the declared row schema.";
                return false;
            }
            if (row.FieldValues.GroupBy(value => value.FieldId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
            {
                reason = "The structured row contains duplicate field IDs.";
                return false;
            }
            string[] actualOrder = row.FieldValues.Select(value => value.FieldId).ToArray();
            string[] expectedOrder = _fields
                .Where(field => row.TryGetFieldValue(field.FieldId, out _))
                .Select(field => field.FieldId)
                .ToArray();
            if (!actualOrder.SequenceEqual(expectedOrder, StringComparer.Ordinal))
            {
                reason = "Structured-row field values must follow deterministic descriptor order.";
                return false;
            }
            for (int i = 0; i < row.FieldValues.Count; i++)
            {
                GameContentStructuredRowFieldValue value = row.FieldValues[i];
                GameContentFieldDescriptor field = FindField(value.FieldId);
                if (field == null)
                {
                    reason = "The structured row contains unknown field '" + value.FieldId + "'.";
                    return false;
                }
                if (!field.AcceptsStoredValue(value.Value, out string fieldReason))
                {
                    reason = field.DisplayName + ": " + fieldReason;
                    return false;
                }
            }
            for (int i = 0; i < _fields.Count; i++)
            {
                GameContentFieldDescriptor field = _fields[i];
                if (!field.Required || row.TryGetFieldValue(field.FieldId, out _)) continue;
                reason = "The structured row is missing required field '" + field.FieldId + "'.";
                return false;
            }
            if (NativeKey == null && !string.IsNullOrWhiteSpace(row.NativeKeyDisplayMetadata))
            {
                reason = "The row supplies provider-native key metadata without declaring an immutable native key.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        public string BuildSummary(IEnumerable<GameContentStructuredRowFieldValue> values)
        {
            var byId = (values ?? Array.Empty<GameContentStructuredRowFieldValue>())
                .Where(value => value != null)
                .GroupBy(value => value.FieldId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
            IEnumerable<string> ids = _summaryFieldIds.Count > 0
                ? _summaryFieldIds
                : _fields.Take(2).Select(field => field.FieldId);
            string[] parts = ids
                .Where(byId.ContainsKey)
                .Select(id => byId[id]?.ToDisplayString() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            return parts.Length == 0 ? DisplayName : string.Join(" | ", parts);
        }

        public static bool IsSupportedRowField(GameContentFieldType fieldType)
        {
            return fieldType.IsScalarValue() || fieldType == GameContentFieldType.RecordReference;
        }

        private static string Normalize(string value, string fallback = "")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    public sealed class GameContentStructuredRowKey : IEquatable<GameContentStructuredRowKey>
    {
        private readonly string _token;

        private GameContentStructuredRowKey(string token)
        {
            _token = token ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrWhiteSpace(_token);

        public static GameContentStructuredRowKey CreateSessionKey()
        {
            return new GameContentStructuredRowKey(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(GameContentStructuredRowKey other)
        {
            return other != null && string.Equals(_token, other._token, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentStructuredRowKey);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(_token);
        }

        public override string ToString()
        {
            return "Structured row";
        }
    }

    public sealed class GameContentStructuredRowFieldValue : IEquatable<GameContentStructuredRowFieldValue>
    {
        public GameContentStructuredRowFieldValue(string fieldId, GameContentFieldValue value)
        {
            if (string.IsNullOrWhiteSpace(fieldId))
                throw new ArgumentException("A structured-row field requires a stable field ID.", nameof(fieldId));
            Value = value ?? throw new ArgumentNullException(nameof(value));
            FieldId = fieldId.Trim();
        }

        public string FieldId { get; }
        public GameContentFieldValue Value { get; }

        public bool Equals(GameContentStructuredRowFieldValue other)
        {
            return other != null &&
                   string.Equals(FieldId, other.FieldId, StringComparison.Ordinal) &&
                   Equals(Value, other.Value);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentStructuredRowFieldValue);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(FieldId) * 397) ^ Value.GetHashCode();
            }
        }
    }

    public sealed class GameContentStructuredRowValue
    {
        private readonly IReadOnlyList<GameContentStructuredRowFieldValue> _fieldValues;

        public GameContentStructuredRowValue(
            GameContentStructuredRowKey rowKey,
            int originalIndex,
            string schemaId,
            IEnumerable<GameContentStructuredRowFieldValue> fieldValues,
            GameContentEditValidationState validationState = GameContentEditValidationState.Valid,
            string displaySummary = null,
            string nativeKeyDisplayMetadata = null)
        {
            if (rowKey == null || !rowKey.IsValid)
                throw new ArgumentException("A structured row requires a valid session row key.", nameof(rowKey));
            if (originalIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(originalIndex), "Original index must be -1 or greater.");
            if (string.IsNullOrWhiteSpace(schemaId))
                throw new ArgumentException("A structured row requires a stable schema ID.", nameof(schemaId));
            if (!Enum.IsDefined(typeof(GameContentEditValidationState), validationState))
                throw new ArgumentOutOfRangeException(nameof(validationState));

            GameContentStructuredRowFieldValue[] copy = (fieldValues ??
                Array.Empty<GameContentStructuredRowFieldValue>()).ToArray();
            if (copy.Any(value => value == null || value.Value == null))
                throw new ArgumentException("Structured-row field values cannot be null.", nameof(fieldValues));
            if (copy.GroupBy(value => value.FieldId, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw new ArgumentException("Structured-row field IDs must be unique.", nameof(fieldValues));

            RowKey = rowKey;
            OriginalIndex = originalIndex;
            SchemaId = schemaId.Trim();
            _fieldValues = new ReadOnlyCollection<GameContentStructuredRowFieldValue>(copy);
            ValidationState = validationState;
            DisplaySummary = Normalize(displaySummary);
            NativeKeyDisplayMetadata = Normalize(nativeKeyDisplayMetadata);
        }

        public GameContentStructuredRowKey RowKey { get; }
        public int OriginalIndex { get; }
        public bool IsAdded => OriginalIndex < 0;
        public string SchemaId { get; }
        public IReadOnlyList<GameContentStructuredRowFieldValue> FieldValues => _fieldValues;
        public GameContentEditValidationState ValidationState { get; }
        public string DisplaySummary { get; }
        public string NativeKeyDisplayMetadata { get; }

        public bool TryGetFieldValue(string fieldId, out GameContentFieldValue value)
        {
            GameContentStructuredRowFieldValue field = _fieldValues.FirstOrDefault(candidate =>
                string.Equals(candidate.FieldId, fieldId, StringComparison.Ordinal));
            value = field?.Value;
            return field != null;
        }

        public bool PersistedEquals(GameContentStructuredRowValue other)
        {
            if (other == null || !string.Equals(SchemaId, other.SchemaId, StringComparison.Ordinal) ||
                !string.Equals(NativeKeyDisplayMetadata, other.NativeKeyDisplayMetadata, StringComparison.Ordinal) ||
                _fieldValues.Count != other._fieldValues.Count)
                return false;
            for (int i = 0; i < _fieldValues.Count; i++)
            {
                if (!_fieldValues[i].Equals(other._fieldValues[i])) return false;
            }
            return true;
        }

        public int GetPersistedHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(SchemaId);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(NativeKeyDisplayMetadata);
                for (int i = 0; i < _fieldValues.Count; i++)
                    hash = (hash * 397) ^ _fieldValues[i].GetHashCode();
                return hash;
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class GameContentOrderedStructuredCollectionValue :
        IEquatable<GameContentOrderedStructuredCollectionValue>
    {
        private readonly IReadOnlyList<GameContentStructuredRowValue> _rows;

        public GameContentOrderedStructuredCollectionValue(
            string rowSchemaId,
            IEnumerable<GameContentStructuredRowValue> rows)
        {
            if (string.IsNullOrWhiteSpace(rowSchemaId))
                throw new ArgumentException("A structured collection requires a stable row schema ID.", nameof(rowSchemaId));
            GameContentStructuredRowValue[] copy = (rows ?? Array.Empty<GameContentStructuredRowValue>()).ToArray();
            if (copy.Any(row => row == null || !string.Equals(row.SchemaId, rowSchemaId, StringComparison.Ordinal)))
                throw new ArgumentException("Every row must match the structured collection schema.", nameof(rows));
            if (copy.Select(row => row.RowKey).Distinct().Count() != copy.Length)
                throw new ArgumentException("Structured-row session keys must be unique.", nameof(rows));
            if (copy.Where(row => row.OriginalIndex >= 0)
                .GroupBy(row => row.OriginalIndex)
                .Any(group => group.Count() > 1))
                throw new ArgumentException("Original structured-row indexes must be unique.", nameof(rows));

            RowSchemaId = rowSchemaId.Trim();
            _rows = new ReadOnlyCollection<GameContentStructuredRowValue>(copy);
        }

        public string RowSchemaId { get; }
        public IReadOnlyList<GameContentStructuredRowValue> Rows => _rows;
        public int Count => _rows.Count;

        public bool TryGetRow(GameContentStructuredRowKey rowKey, out GameContentStructuredRowValue row)
        {
            row = rowKey == null ? null : _rows.FirstOrDefault(candidate => candidate.RowKey.Equals(rowKey));
            return row != null;
        }

        public bool Equals(GameContentOrderedStructuredCollectionValue other)
        {
            if (other == null || !string.Equals(RowSchemaId, other.RowSchemaId, StringComparison.Ordinal) ||
                Count != other.Count)
                return false;
            for (int i = 0; i < Count; i++)
            {
                if (!_rows[i].PersistedEquals(other._rows[i])) return false;
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentOrderedStructuredCollectionValue);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(RowSchemaId);
                for (int i = 0; i < _rows.Count; i++) hash = (hash * 397) ^ _rows[i].GetPersistedHashCode();
                return hash;
            }
        }

        public string ToDisplayString()
        {
            return Count == 0
                ? "Empty"
                : string.Join(" -> ", _rows.Select(row => string.IsNullOrWhiteSpace(row.DisplaySummary)
                    ? "Row"
                    : row.DisplaySummary).ToArray());
        }

        public override string ToString()
        {
            return ToDisplayString();
        }
    }

    public sealed class GameContentStructuredCollectionFieldDescriptor
    {
        public GameContentStructuredCollectionFieldDescriptor(
            string fieldId,
            string semanticId,
            string displayName,
            string helpText,
            GameContentStructuredRowDescriptor rowDescriptor,
            int minimumCount = 0,
            int? maximumCount = null,
            string orderingSemantics = null,
            GameContentStructuredRowDuplicatePolicy duplicatePolicy = GameContentStructuredRowDuplicatePolicy.Allow,
            GameContentStructuredCollectionPermittedOperations permittedOperations =
                GameContentStructuredCollectionPermittedOperations.All,
            GameContentReferenceRuntimeImpact runtimeImpact = GameContentReferenceRuntimeImpact.None,
            string readOnlyReason = null)
        {
            FieldId = Normalize(fieldId);
            SemanticId = Normalize(semanticId);
            DisplayName = Normalize(displayName, FieldId);
            HelpText = Normalize(helpText);
            RowDescriptor = rowDescriptor;
            MinimumCount = minimumCount;
            MaximumCount = maximumCount;
            OrderingSemantics = Normalize(orderingSemantics, "Row order is significant.");
            DuplicatePolicy = duplicatePolicy;
            PermittedOperations = permittedOperations;
            RuntimeImpact = runtimeImpact;
            ReadOnlyReason = Normalize(readOnlyReason);
        }

        public string FieldId { get; }
        public string SemanticId { get; }
        public string DisplayName { get; }
        public string HelpText { get; }
        public GameContentStructuredRowDescriptor RowDescriptor { get; }
        public int MinimumCount { get; }
        public int? MaximumCount { get; }
        public string OrderingSemantics { get; }
        public GameContentStructuredRowDuplicatePolicy DuplicatePolicy { get; }
        public GameContentStructuredCollectionPermittedOperations PermittedOperations { get; }
        public GameContentReferenceRuntimeImpact RuntimeImpact { get; }
        public string ReadOnlyReason { get; }
        public bool IsReadOnly => !string.IsNullOrWhiteSpace(ReadOnlyReason);
        public string BoundaryViolationReason => RowDescriptor?.BoundaryViolationReason ?? string.Empty;

        public bool IsValid
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FieldId) || RowDescriptor == null || !RowDescriptor.IsValid ||
                    MinimumCount < 0 || (MaximumCount.HasValue && MaximumCount.Value < MinimumCount) ||
                    !Enum.IsDefined(typeof(GameContentStructuredRowDuplicatePolicy), DuplicatePolicy))
                    return false;
                const GameContentStructuredCollectionPermittedOperations known =
                    GameContentStructuredCollectionPermittedOperations.All;
                if ((PermittedOperations & ~known) != 0) return false;
                if (Allows(GameContentStructuredCollectionPermittedOperations.AddRow) && !RowDescriptor.SupportsAdd)
                    return false;
                if (Allows(GameContentStructuredCollectionPermittedOperations.RemoveRow) && !RowDescriptor.SupportsRemove)
                    return false;
                if (Allows(GameContentStructuredCollectionPermittedOperations.MoveRow) && !RowDescriptor.SupportsMove)
                    return false;
                if (Allows(GameContentStructuredCollectionPermittedOperations.ReplaceRowField) &&
                    !RowDescriptor.SupportsRowFieldReplacement)
                    return false;
                if (Allows(GameContentStructuredCollectionPermittedOperations.RestoreOriginalOrder) &&
                    !Allows(GameContentStructuredCollectionPermittedOperations.MoveRow))
                    return false;
                if (IsReadOnly && PermittedOperations != GameContentStructuredCollectionPermittedOperations.None)
                    return false;
                return true;
            }
        }

        public bool IsValidFor(GameContentFieldDescriptor field)
        {
            return IsValid && field != null &&
                   field.FieldType == GameContentFieldType.OrderedStructuredCollection &&
                   string.Equals(FieldId, field.FieldId, StringComparison.Ordinal) &&
                   string.Equals(SemanticId, field.SemanticId, StringComparison.Ordinal) &&
                   string.Equals(DisplayName, field.DisplayName, StringComparison.Ordinal) &&
                   string.Equals(HelpText, field.Description, StringComparison.Ordinal) &&
                   string.Equals(ReadOnlyReason, field.ReadOnlyReason, StringComparison.Ordinal) &&
                   field.IsReadOnly == IsReadOnly;
        }

        public bool Allows(GameContentStructuredCollectionPermittedOperations operation)
        {
            return (PermittedOperations & operation) == operation;
        }

        public bool Accepts(GameContentOrderedStructuredCollectionValue value, out string reason)
        {
            if (!IsValid)
            {
                reason = string.IsNullOrWhiteSpace(BoundaryViolationReason)
                    ? "The structured collection contract is invalid."
                    : BoundaryViolationReason;
                return false;
            }
            if (value == null || !string.Equals(
                    value.RowSchemaId,
                    RowDescriptor.RowSchemaId,
                    StringComparison.Ordinal))
            {
                reason = "The structured collection does not match its declared row schema.";
                return false;
            }
            if (value.Count < MinimumCount)
            {
                reason = "The structured collection requires at least " + MinimumCount + " row(s).";
                return false;
            }
            if (MaximumCount.HasValue && value.Count > MaximumCount.Value)
            {
                reason = "The structured collection allows at most " + MaximumCount.Value + " row(s).";
                return false;
            }
            for (int i = 0; i < value.Rows.Count; i++)
            {
                if (!RowDescriptor.AcceptsRow(value.Rows[i], out string rowReason))
                {
                    reason = "Row " + (i + 1) + ": " + rowReason;
                    return false;
                }
            }
            if (DuplicatePolicy == GameContentStructuredRowDuplicatePolicy.RejectPersistedDuplicates)
            {
                for (int i = 0; i < value.Rows.Count; i++)
                {
                    for (int other = i + 1; other < value.Rows.Count; other++)
                    {
                        if (!value.Rows[i].PersistedEquals(value.Rows[other])) continue;
                        reason = "Duplicate structured rows are not allowed.";
                        return false;
                    }
                }
            }
            if (RowDescriptor.NativeKey != null)
            {
                string duplicate = value.Rows
                    .Where(row => !string.IsNullOrWhiteSpace(row.NativeKeyDisplayMetadata))
                    .GroupBy(row => row.NativeKeyDisplayMetadata, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(duplicate))
                {
                    reason = "Provider-native row keys must be unique. Duplicate: " + duplicate + ".";
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        internal bool ContainsDuplicate(
            IReadOnlyList<GameContentStructuredRowValue> rows,
            GameContentStructuredRowValue value,
            GameContentStructuredRowKey ignoredRowKey = null)
        {
            return DuplicatePolicy == GameContentStructuredRowDuplicatePolicy.RejectPersistedDuplicates &&
                   rows.Any(row =>
                       (ignoredRowKey == null || !row.RowKey.Equals(ignoredRowKey)) &&
                       row.PersistedEquals(value));
        }

        private static string Normalize(string value, string fallback = "")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    public enum GameContentStructuredCollectionOperationKind
    {
        AddRow = 0,
        RemoveRow = 1,
        MoveRow = 2,
        ReplaceRowField = 3,
        RestoreOriginalOrder = 4
    }

    public sealed class GameContentStructuredCollectionOperation
    {
        private GameContentStructuredCollectionOperation(
            GameContentStructuredCollectionOperationKind kind,
            GameContentStructuredRowKey rowKey,
            IEnumerable<GameContentStructuredRowFieldValue> fieldValues,
            string rowFieldId,
            GameContentFieldValue value,
            int newIndex)
        {
            Kind = kind;
            RowKey = rowKey;
            GameContentStructuredRowFieldValue[] supplied = (fieldValues ??
                Array.Empty<GameContentStructuredRowFieldValue>()).ToArray();
            if (supplied.Any(item => item == null))
                throw new ArgumentException("Structured-row operation field values cannot contain null entries.", nameof(fieldValues));
            FieldValues = new ReadOnlyCollection<GameContentStructuredRowFieldValue>(
                supplied);
            RowFieldId = string.IsNullOrWhiteSpace(rowFieldId) ? string.Empty : rowFieldId.Trim();
            Value = value;
            NewIndex = newIndex;
        }

        public GameContentStructuredCollectionOperationKind Kind { get; }
        public GameContentStructuredRowKey RowKey { get; }
        public IReadOnlyList<GameContentStructuredRowFieldValue> FieldValues { get; }
        public string RowFieldId { get; }
        public GameContentFieldValue Value { get; }
        public int NewIndex { get; }

        public static GameContentStructuredCollectionOperation AddRow(
            IEnumerable<GameContentStructuredRowFieldValue> fieldValues)
        {
            if (fieldValues == null) throw new ArgumentNullException(nameof(fieldValues));
            return new GameContentStructuredCollectionOperation(
                GameContentStructuredCollectionOperationKind.AddRow,
                null,
                fieldValues,
                null,
                null,
                -1);
        }

        public static GameContentStructuredCollectionOperation RemoveRow(GameContentStructuredRowKey rowKey)
        {
            RequireKey(rowKey);
            return new GameContentStructuredCollectionOperation(
                GameContentStructuredCollectionOperationKind.RemoveRow,
                rowKey,
                null,
                null,
                null,
                -1);
        }

        public static GameContentStructuredCollectionOperation MoveRow(
            GameContentStructuredRowKey rowKey,
            int newIndex)
        {
            RequireKey(rowKey);
            if (newIndex < 0) throw new ArgumentOutOfRangeException(nameof(newIndex));
            return new GameContentStructuredCollectionOperation(
                GameContentStructuredCollectionOperationKind.MoveRow,
                rowKey,
                null,
                null,
                null,
                newIndex);
        }

        public static GameContentStructuredCollectionOperation ReplaceRowField(
            GameContentStructuredRowKey rowKey,
            string rowFieldId,
            GameContentFieldValue value)
        {
            RequireKey(rowKey);
            if (string.IsNullOrWhiteSpace(rowFieldId))
                throw new ArgumentException("A row-field replacement requires a stable field ID.", nameof(rowFieldId));
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new GameContentStructuredCollectionOperation(
                GameContentStructuredCollectionOperationKind.ReplaceRowField,
                rowKey,
                null,
                rowFieldId,
                value,
                -1);
        }

        public static GameContentStructuredCollectionOperation RestoreOriginalOrder()
        {
            return new GameContentStructuredCollectionOperation(
                GameContentStructuredCollectionOperationKind.RestoreOriginalOrder,
                null,
                null,
                null,
                null,
                -1);
        }

        internal GameContentStructuredCollectionOperation BindGeneratedRowKey(
            GameContentStructuredRowKey rowKey)
        {
            if (Kind != GameContentStructuredCollectionOperationKind.AddRow || RowKey != null)
                throw new InvalidOperationException("Only an unbound AddRow operation can receive a generated row key.");
            RequireKey(rowKey);
            return new GameContentStructuredCollectionOperation(
                Kind,
                rowKey,
                FieldValues,
                RowFieldId,
                Value,
                NewIndex);
        }

        private static void RequireKey(GameContentStructuredRowKey rowKey)
        {
            if (rowKey == null || !rowKey.IsValid)
                throw new ArgumentException("The structured operation requires a valid existing session row key.", nameof(rowKey));
        }
    }

    public sealed class GameContentStructuredCollectionOperationResult
    {
        public GameContentStructuredCollectionOperationResult(
            bool succeeded,
            string message,
            GameContentStructuredRowKey rowKey = null)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            RowKey = rowKey;
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public GameContentStructuredRowKey RowKey { get; }

        public static GameContentStructuredCollectionOperationResult Success(
            string message = null,
            GameContentStructuredRowKey rowKey = null)
        {
            return new GameContentStructuredCollectionOperationResult(true, message, rowKey);
        }

        public static GameContentStructuredCollectionOperationResult Failure(string message)
        {
            return new GameContentStructuredCollectionOperationResult(false, message);
        }
    }

    public interface IGameContentStructuredCollectionEditSession
    {
        GameContentStructuredCollectionOperationResult ApplyStructuredOperation(
            string fieldId,
            GameContentStructuredCollectionOperation operation);

        GameContentReferenceEvaluation EvaluateStructuredRowReference(
            string fieldId,
            GameContentStructuredRowKey rowKey,
            string rowFieldId,
            GameContentRecordKey targetKey);
    }

    public static class GameContentStructuredCollectionMutation
    {
        public static bool TryApply(
            GameContentFieldDescriptor field,
            GameContentOrderedStructuredCollectionValue current,
            GameContentStructuredCollectionOperation operation,
            out GameContentOrderedStructuredCollectionValue proposed,
            out GameContentStructuredRowKey affectedRowKey,
            out string reason)
        {
            proposed = current;
            affectedRowKey = operation?.RowKey;
            if (field == null || field.FieldType != GameContentFieldType.OrderedStructuredCollection ||
                field.StructuredCollection == null || !field.StructuredCollection.IsValidFor(field))
            {
                reason = "The field has no valid ordered structured-row contract.";
                return false;
            }
            GameContentStructuredCollectionFieldDescriptor descriptor = field.StructuredCollection;
            if (current == null || !string.Equals(
                    current.RowSchemaId,
                    descriptor.RowDescriptor.RowSchemaId,
                    StringComparison.Ordinal))
            {
                reason = "The current structured collection does not match its field contract.";
                return false;
            }
            if (operation == null)
            {
                reason = "No structured collection operation was provided.";
                return false;
            }
            if (!IsPermitted(descriptor, operation.Kind))
            {
                reason = "The structured collection does not permit " + operation.Kind + ".";
                return false;
            }

            var rows = current.Rows.ToList();
            switch (operation.Kind)
            {
                case GameContentStructuredCollectionOperationKind.AddRow:
                {
                    if (descriptor.MaximumCount.HasValue && rows.Count >= descriptor.MaximumCount.Value)
                    {
                        reason = "The structured collection already contains its maximum number of rows.";
                        return false;
                    }
                    GameContentStructuredRowKey generatedKey = operation.RowKey ??
                                                               GameContentStructuredRowKey.CreateSessionKey();
                    if (rows.Any(row => row.RowKey.Equals(generatedKey)))
                    {
                        reason = "The generated structured-row key is already in use by this session.";
                        return false;
                    }
                    if (!TryCreateAddedRow(descriptor.RowDescriptor, generatedKey, operation.FieldValues,
                            out GameContentStructuredRowValue added, out reason))
                        return false;
                    if (descriptor.ContainsDuplicate(rows, added))
                    {
                        reason = "Duplicate structured rows are not allowed.";
                        return false;
                    }
                    rows.Add(added);
                    affectedRowKey = generatedKey;
                    break;
                }

                case GameContentStructuredCollectionOperationKind.RemoveRow:
                {
                    int index = IndexOf(rows, operation.RowKey);
                    if (index < 0)
                    {
                        reason = "The structured-row key is unknown to this session.";
                        return false;
                    }
                    if (rows.Count - 1 < descriptor.MinimumCount)
                    {
                        reason = "Removing this row would violate the minimum structured-row count.";
                        return false;
                    }
                    rows.RemoveAt(index);
                    break;
                }

                case GameContentStructuredCollectionOperationKind.MoveRow:
                {
                    int index = IndexOf(rows, operation.RowKey);
                    if (index < 0)
                    {
                        reason = "The structured-row key is unknown to this session.";
                        return false;
                    }
                    if (operation.NewIndex < 0 || operation.NewIndex >= rows.Count)
                    {
                        reason = "The structured-row move target is outside the current sequence.";
                        return false;
                    }
                    if (index != operation.NewIndex)
                    {
                        GameContentStructuredRowValue moved = rows[index];
                        rows.RemoveAt(index);
                        rows.Insert(operation.NewIndex, moved);
                    }
                    break;
                }

                case GameContentStructuredCollectionOperationKind.ReplaceRowField:
                {
                    int index = IndexOf(rows, operation.RowKey);
                    if (index < 0)
                    {
                        reason = "The structured-row key is unknown to this session.";
                        return false;
                    }
                    GameContentFieldDescriptor rowField = descriptor.RowDescriptor.FindField(operation.RowFieldId);
                    if (rowField == null)
                    {
                        reason = "The structured-row field is unknown.";
                        return false;
                    }
                    if (rowField.IsReadOnly)
                    {
                        reason = "The structured-row field is read-only. " + rowField.ReadOnlyReason;
                        return false;
                    }
                    if (!rowField.Accepts(operation.Value, out reason)) return false;
                    GameContentStructuredRowValue existing = rows[index];
                    var replacements = existing.FieldValues
                        .ToDictionary(value => value.FieldId, value => value.Value, StringComparer.Ordinal);
                    replacements[rowField.FieldId] = operation.Value;
                    GameContentStructuredRowFieldValue[] orderedValues = descriptor.RowDescriptor.Fields
                        .Where(candidate => replacements.ContainsKey(candidate.FieldId))
                        .Select(candidate => new GameContentStructuredRowFieldValue(
                            candidate.FieldId,
                            replacements[candidate.FieldId]))
                        .ToArray();
                    var replaced = new GameContentStructuredRowValue(
                        existing.RowKey,
                        existing.OriginalIndex,
                        existing.SchemaId,
                        orderedValues,
                        GameContentEditValidationState.Valid,
                        descriptor.RowDescriptor.BuildSummary(orderedValues),
                        existing.NativeKeyDisplayMetadata);
                    if (!descriptor.RowDescriptor.AcceptsRow(replaced, out reason)) return false;
                    if (descriptor.ContainsDuplicate(rows, replaced, existing.RowKey))
                    {
                        reason = "Duplicate structured rows are not allowed.";
                        return false;
                    }
                    rows[index] = replaced;
                    break;
                }

                case GameContentStructuredCollectionOperationKind.RestoreOriginalOrder:
                    rows = rows.Where(row => row.OriginalIndex >= 0)
                        .OrderBy(row => row.OriginalIndex)
                        .Concat(rows.Where(row => row.OriginalIndex < 0))
                        .ToList();
                    affectedRowKey = null;
                    break;

                default:
                    reason = "The structured collection operation kind is unsupported.";
                    return false;
            }

            proposed = new GameContentOrderedStructuredCollectionValue(current.RowSchemaId, rows);
            if (!descriptor.Accepts(proposed, out reason))
            {
                proposed = current;
                return false;
            }
            reason = string.Empty;
            return true;
        }

        public static bool NeedsRestoreOriginalOrder(GameContentOrderedStructuredCollectionValue current)
        {
            if (current == null) return false;
            GameContentStructuredRowKey[] actual = current.Rows.Select(row => row.RowKey).ToArray();
            GameContentStructuredRowKey[] target = current.Rows
                .Where(row => row.OriginalIndex >= 0)
                .OrderBy(row => row.OriginalIndex)
                .Concat(current.Rows.Where(row => row.OriginalIndex < 0))
                .Select(row => row.RowKey)
                .ToArray();
            return !actual.SequenceEqual(target);
        }

        private static bool TryCreateAddedRow(
            GameContentStructuredRowDescriptor rowDescriptor,
            GameContentStructuredRowKey generatedKey,
            IReadOnlyList<GameContentStructuredRowFieldValue> supplied,
            out GameContentStructuredRowValue row,
            out string reason)
        {
            row = null;
            supplied = supplied ?? Array.Empty<GameContentStructuredRowFieldValue>();
            if (supplied.GroupBy(value => value.FieldId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            {
                reason = "The proposed row contains duplicate field IDs.";
                return false;
            }
            var suppliedById = supplied.ToDictionary(value => value.FieldId, value => value.Value, StringComparer.Ordinal);
            foreach (KeyValuePair<string, GameContentFieldValue> pair in suppliedById)
            {
                GameContentFieldDescriptor descriptor = rowDescriptor.FindField(pair.Key);
                if (descriptor == null)
                {
                    reason = "The proposed row contains unknown field '" + pair.Key + "'.";
                    return false;
                }
                if (descriptor.IsReadOnly)
                {
                    reason = "The proposed row supplies read-only field '" + pair.Key + "'. " +
                             descriptor.ReadOnlyReason;
                    return false;
                }
                if (!descriptor.Accepts(pair.Value, out string fieldReason))
                {
                    reason = descriptor.DisplayName + ": " + fieldReason;
                    return false;
                }
            }
            for (int i = 0; i < rowDescriptor.Fields.Count; i++)
            {
                GameContentFieldDescriptor descriptor = rowDescriptor.Fields[i];
                if (!descriptor.Required || suppliedById.ContainsKey(descriptor.FieldId)) continue;
                reason = "The proposed row is missing required field '" + descriptor.FieldId + "'.";
                return false;
            }
            GameContentStructuredRowFieldValue[] ordered = rowDescriptor.Fields
                .Where(field => suppliedById.ContainsKey(field.FieldId))
                .Select(field => new GameContentStructuredRowFieldValue(field.FieldId, suppliedById[field.FieldId]))
                .ToArray();
            row = new GameContentStructuredRowValue(
                generatedKey,
                -1,
                rowDescriptor.RowSchemaId,
                ordered,
                GameContentEditValidationState.Valid,
                rowDescriptor.BuildSummary(ordered));
            return rowDescriptor.AcceptsRow(row, out reason);
        }

        private static bool IsPermitted(
            GameContentStructuredCollectionFieldDescriptor descriptor,
            GameContentStructuredCollectionOperationKind kind)
        {
            switch (kind)
            {
                case GameContentStructuredCollectionOperationKind.AddRow:
                    return descriptor.Allows(GameContentStructuredCollectionPermittedOperations.AddRow);
                case GameContentStructuredCollectionOperationKind.RemoveRow:
                    return descriptor.Allows(GameContentStructuredCollectionPermittedOperations.RemoveRow);
                case GameContentStructuredCollectionOperationKind.MoveRow:
                    return descriptor.Allows(GameContentStructuredCollectionPermittedOperations.MoveRow);
                case GameContentStructuredCollectionOperationKind.ReplaceRowField:
                    return descriptor.Allows(GameContentStructuredCollectionPermittedOperations.ReplaceRowField);
                case GameContentStructuredCollectionOperationKind.RestoreOriginalOrder:
                    return descriptor.Allows(GameContentStructuredCollectionPermittedOperations.RestoreOriginalOrder);
                default:
                    return false;
            }
        }

        private static int IndexOf(
            IReadOnlyList<GameContentStructuredRowValue> rows,
            GameContentStructuredRowKey rowKey)
        {
            if (rowKey == null) return -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].RowKey.Equals(rowKey)) return i;
            }
            return -1;
        }
    }

    public sealed class GameContentStructuredRowMove
    {
        public GameContentStructuredRowMove(
            GameContentStructuredRowKey rowKey,
            int oldIndex,
            int newIndex,
            string summary)
        {
            RowKey = rowKey;
            OldIndex = oldIndex;
            NewIndex = newIndex;
            Summary = summary ?? string.Empty;
        }

        public GameContentStructuredRowKey RowKey { get; }
        public int OldIndex { get; }
        public int NewIndex { get; }
        public string Summary { get; }
    }

    public sealed class GameContentStructuredRowFieldChange
    {
        public GameContentStructuredRowFieldChange(
            GameContentStructuredRowKey rowKey,
            int oldIndex,
            int newIndex,
            string rowFieldId,
            GameContentFieldValue oldValue,
            GameContentFieldValue newValue,
            string rowSummary)
        {
            RowKey = rowKey;
            OldIndex = oldIndex;
            NewIndex = newIndex;
            RowFieldId = rowFieldId ?? string.Empty;
            OldValue = oldValue;
            NewValue = newValue;
            RowSummary = rowSummary ?? string.Empty;
        }

        public GameContentStructuredRowKey RowKey { get; }
        public int OldIndex { get; }
        public int NewIndex { get; }
        public string RowFieldId { get; }
        public GameContentFieldValue OldValue { get; }
        public GameContentFieldValue NewValue { get; }
        public string RowSummary { get; }
        public bool IsReference => OldValue?.FieldType == GameContentFieldType.RecordReference ||
                                   NewValue?.FieldType == GameContentFieldType.RecordReference;
    }

    public sealed class GameContentStructuredCollectionChangeReview
    {
        private GameContentStructuredCollectionChangeReview(
            GameContentRecordKey sourceRecordKey,
            string fieldId,
            GameContentOrderedStructuredCollectionValue originalValue,
            GameContentOrderedStructuredCollectionValue proposedValue,
            IEnumerable<GameContentStructuredRowValue> addedRows,
            IEnumerable<GameContentStructuredRowValue> removedRows,
            IEnumerable<GameContentStructuredRowMove> movedRows,
            IEnumerable<GameContentStructuredRowFieldChange> fieldChanges,
            IEnumerable<GameContentAuthoringValidationIssue> validationFindings,
            GameContentReferenceRuntimeImpact runtimeImpact)
        {
            SourceRecordKey = sourceRecordKey;
            FieldId = fieldId ?? string.Empty;
            OriginalValue = originalValue;
            ProposedValue = proposedValue;
            AddedRows = ToReadOnly(addedRows);
            RemovedRows = ToReadOnly(removedRows);
            MovedRows = ToReadOnly(movedRows);
            FieldChanges = ToReadOnly(fieldChanges);
            ValidationFindings = ToReadOnly(validationFindings);
            RuntimeImpact = runtimeImpact;
        }

        public GameContentRecordKey SourceRecordKey { get; }
        public string FieldId { get; }
        public GameContentOrderedStructuredCollectionValue OriginalValue { get; }
        public GameContentOrderedStructuredCollectionValue ProposedValue { get; }
        public IReadOnlyList<GameContentStructuredRowValue> OriginalOrder => OriginalValue?.Rows ??
                                                                            Array.Empty<GameContentStructuredRowValue>();
        public IReadOnlyList<GameContentStructuredRowValue> ProposedOrder => ProposedValue?.Rows ??
                                                                            Array.Empty<GameContentStructuredRowValue>();
        public IReadOnlyList<GameContentStructuredRowValue> AddedRows { get; }
        public IReadOnlyList<GameContentStructuredRowValue> RemovedRows { get; }
        public IReadOnlyList<GameContentStructuredRowMove> MovedRows { get; }
        public IReadOnlyList<GameContentStructuredRowFieldChange> FieldChanges { get; }
        public IReadOnlyList<GameContentAuthoringValidationIssue> ValidationFindings { get; }
        public GameContentReferenceRuntimeImpact RuntimeImpact { get; }

        public static GameContentStructuredCollectionChangeReview Create(
            GameContentRecordKey sourceRecordKey,
            string fieldId,
            GameContentOrderedStructuredCollectionValue originalValue,
            GameContentOrderedStructuredCollectionValue proposedValue,
            IEnumerable<GameContentAuthoringValidationIssue> validationFindings,
            GameContentReferenceRuntimeImpact runtimeImpact)
        {
            if (originalValue == null || proposedValue == null ||
                !string.Equals(originalValue.RowSchemaId, proposedValue.RowSchemaId, StringComparison.Ordinal))
                return null;

            var originalByKey = originalValue.Rows.ToDictionary(row => row.RowKey, row => row);
            var proposedByKey = proposedValue.Rows.ToDictionary(row => row.RowKey, row => row);
            GameContentStructuredRowValue[] removed = originalValue.Rows
                .Where(row => !proposedByKey.ContainsKey(row.RowKey)).ToArray();
            GameContentStructuredRowValue[] added = proposedValue.Rows
                .Where(row => !originalByKey.ContainsKey(row.RowKey)).ToArray();
            var fieldChanges = new List<GameContentStructuredRowFieldChange>();
            foreach (GameContentStructuredRowValue row in proposedValue.Rows)
            {
                if (!originalByKey.TryGetValue(row.RowKey, out GameContentStructuredRowValue original)) continue;
                var originalFields = original.FieldValues.ToDictionary(value => value.FieldId, value => value.Value);
                var proposedFields = row.FieldValues.ToDictionary(value => value.FieldId, value => value.Value);
                foreach (string childFieldId in originalFields.Keys.Union(proposedFields.Keys)
                             .OrderBy(id => id, StringComparer.Ordinal))
                {
                    originalFields.TryGetValue(childFieldId, out GameContentFieldValue oldValue);
                    proposedFields.TryGetValue(childFieldId, out GameContentFieldValue newValue);
                    if (Equals(oldValue, newValue)) continue;
                    fieldChanges.Add(new GameContentStructuredRowFieldChange(
                        row.RowKey,
                        IndexOf(originalValue.Rows, row.RowKey),
                        IndexOf(proposedValue.Rows, row.RowKey),
                        childFieldId,
                        oldValue,
                        newValue,
                        row.DisplaySummary));
                }
            }

            GameContentStructuredRowKey[] originalSurvivors = originalValue.Rows
                .Where(row => proposedByKey.ContainsKey(row.RowKey))
                .Select(row => row.RowKey)
                .ToArray();
            GameContentStructuredRowKey[] proposedSurvivors = proposedValue.Rows
                .Where(row => originalByKey.ContainsKey(row.RowKey))
                .Select(row => row.RowKey)
                .ToArray();
            var working = originalSurvivors.ToList();
            var moves = new List<GameContentStructuredRowMove>();
            for (int targetIndex = 0; targetIndex < proposedSurvivors.Length; targetIndex++)
            {
                GameContentStructuredRowKey key = proposedSurvivors[targetIndex];
                if (working[targetIndex].Equals(key)) continue;
                int currentIndex = working.FindIndex(candidate => candidate.Equals(key));
                if (currentIndex < 0) continue;
                GameContentStructuredRowValue row = proposedByKey[key];
                moves.Add(new GameContentStructuredRowMove(
                    key,
                    IndexOf(originalValue.Rows, key),
                    IndexOf(proposedValue.Rows, key),
                    row.DisplaySummary));
                working.RemoveAt(currentIndex);
                working.Insert(targetIndex, key);
            }

            return new GameContentStructuredCollectionChangeReview(
                sourceRecordKey,
                fieldId,
                originalValue,
                proposedValue,
                added,
                removed,
                moves,
                fieldChanges,
                validationFindings,
                runtimeImpact);
        }

        private static IReadOnlyList<T> ToReadOnly<T>(IEnumerable<T> values) where T : class
        {
            return new ReadOnlyCollection<T>((values ?? Array.Empty<T>()).Where(value => value != null).ToArray());
        }

        private static int IndexOf(
            IReadOnlyList<GameContentStructuredRowValue> rows,
            GameContentStructuredRowKey rowKey)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].RowKey.Equals(rowKey)) return i;
            }
            return -1;
        }
    }
}
