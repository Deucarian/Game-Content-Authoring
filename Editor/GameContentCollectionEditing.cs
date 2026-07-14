using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentFieldTypeExtensions
    {
        public static bool IsOrderedCollection(this GameContentFieldType fieldType)
        {
            return fieldType == GameContentFieldType.OrderedScalarCollection ||
                   fieldType == GameContentFieldType.OrderedRecordReferenceCollection;
        }

        public static bool IsScalarValue(this GameContentFieldType fieldType)
        {
            return fieldType == GameContentFieldType.String ||
                   fieldType == GameContentFieldType.Integer ||
                   fieldType == GameContentFieldType.Number ||
                   fieldType == GameContentFieldType.Boolean ||
                   fieldType == GameContentFieldType.Enum;
        }

        public static bool IsSupportedCollectionItem(this GameContentFieldType fieldType)
        {
            return fieldType.IsScalarValue() || fieldType == GameContentFieldType.RecordReference;
        }

        public static GameContentFieldType ToOrderedCollectionType(this GameContentFieldType itemType)
        {
            if (itemType == GameContentFieldType.RecordReference)
                return GameContentFieldType.OrderedRecordReferenceCollection;
            if (itemType.IsScalarValue()) return GameContentFieldType.OrderedScalarCollection;
            throw new ArgumentException("The field type is not a supported ordered-collection item type.", nameof(itemType));
        }
    }

    public sealed class GameContentCollectionItemKey : IEquatable<GameContentCollectionItemKey>
    {
        private readonly string _token;

        private GameContentCollectionItemKey(string token)
        {
            _token = token ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrWhiteSpace(_token);

        public static GameContentCollectionItemKey Create()
        {
            return new GameContentCollectionItemKey(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(GameContentCollectionItemKey other)
        {
            return other != null && string.Equals(_token, other._token, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentCollectionItemKey);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(_token);
        }

        public override string ToString()
        {
            return "Collection item";
        }
    }

    public sealed class GameContentCollectionItem
    {
        public GameContentCollectionItem(
            GameContentCollectionItemKey itemKey,
            int originalIndex,
            GameContentFieldValue value)
        {
            if (itemKey == null || !itemKey.IsValid)
                throw new ArgumentException("A collection item requires a valid session item key.", nameof(itemKey));
            if (originalIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(originalIndex), "Original index must be -1 or greater.");
            if (value == null || !value.FieldType.IsSupportedCollectionItem())
                throw new ArgumentException("A collection item requires one supported scalar or record-reference value.", nameof(value));
            ItemKey = itemKey;
            OriginalIndex = originalIndex;
            Value = value;
        }

        public GameContentCollectionItemKey ItemKey { get; }
        public int OriginalIndex { get; }
        public bool IsAdded => OriginalIndex < 0;
        public GameContentFieldValue Value { get; }
    }

    public sealed class GameContentOrderedCollectionValue : IEquatable<GameContentOrderedCollectionValue>
    {
        private readonly IReadOnlyList<GameContentCollectionItem> _items;

        public GameContentOrderedCollectionValue(
            GameContentFieldType itemType,
            IEnumerable<GameContentCollectionItem> items)
        {
            if (!itemType.IsSupportedCollectionItem())
                throw new ArgumentException("The ordered collection has an unsupported item type.", nameof(itemType));

            GameContentCollectionItem[] copy = items == null
                ? Array.Empty<GameContentCollectionItem>()
                : items.ToArray();
            if (copy.Any(item => item == null || item.Value == null || item.Value.FieldType != itemType))
                throw new ArgumentException("Every ordered-collection item must match the declared item type.", nameof(items));
            if (copy.Select(item => item.ItemKey).Distinct().Count() != copy.Length)
                throw new ArgumentException("Ordered-collection item keys must be unique within a session.", nameof(items));
            if (copy.Where(item => item.OriginalIndex >= 0)
                .GroupBy(item => item.OriginalIndex)
                .Any(group => group.Count() > 1))
                throw new ArgumentException("Original collection indexes must be unique within a session.", nameof(items));

            ItemType = itemType;
            FieldType = itemType.ToOrderedCollectionType();
            _items = new ReadOnlyCollection<GameContentCollectionItem>(copy);
        }

        public GameContentFieldType FieldType { get; }
        public GameContentFieldType ItemType { get; }
        public IReadOnlyList<GameContentCollectionItem> Items => _items;
        public int Count => _items.Count;

        public bool TryGetItem(GameContentCollectionItemKey itemKey, out GameContentCollectionItem item)
        {
            item = itemKey == null ? null : _items.FirstOrDefault(candidate => candidate.ItemKey.Equals(itemKey));
            return item != null;
        }

        public bool Equals(GameContentOrderedCollectionValue other)
        {
            if (other == null || FieldType != other.FieldType || ItemType != other.ItemType || Count != other.Count)
                return false;
            for (int i = 0; i < Count; i++)
            {
                if (!_items[i].Value.Equals(other._items[i].Value)) return false;
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentOrderedCollectionValue);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ((int)FieldType * 397) ^ (int)ItemType;
                for (int i = 0; i < _items.Count; i++) hash = (hash * 397) ^ _items[i].Value.GetHashCode();
                return hash;
            }
        }

        public string ToDisplayString()
        {
            return _items.Count == 0
                ? "Empty"
                : string.Join(", ", _items.Select(item => item.Value.ToDisplayString()).ToArray());
        }

        public override string ToString()
        {
            return ToDisplayString();
        }
    }

    public sealed class GameContentCollectionFieldDescriptor
    {
        public GameContentCollectionFieldDescriptor(
            GameContentFieldDescriptor itemDescriptor,
            int minimumCount = 0,
            int? maximumCount = null,
            bool allowDuplicates = true,
            string orderingDescription = null,
            GameContentReferenceRuntimeImpact runtimeImpact = GameContentReferenceRuntimeImpact.None)
        {
            ItemDescriptor = itemDescriptor;
            MinimumCount = minimumCount;
            MaximumCount = maximumCount;
            AllowDuplicates = allowDuplicates;
            OrderingDescription = string.IsNullOrWhiteSpace(orderingDescription)
                ? "Item order is significant."
                : orderingDescription.Trim();
            RuntimeImpact = runtimeImpact;
        }

        public GameContentFieldDescriptor ItemDescriptor { get; }
        public int MinimumCount { get; }
        public int? MaximumCount { get; }
        public bool AllowDuplicates { get; }
        public string OrderingDescription { get; }
        public GameContentReferenceRuntimeImpact RuntimeImpact { get; }
        public bool IsValid => ItemDescriptor != null &&
                               ItemDescriptor.FieldType.IsSupportedCollectionItem() &&
                               ItemDescriptor.IsValid &&
                               !ItemDescriptor.IsReadOnly &&
                               MinimumCount >= 0 &&
                               (!MaximumCount.HasValue || MaximumCount.Value >= MinimumCount) &&
                               (ItemDescriptor.FieldType != GameContentFieldType.RecordReference ||
                                (ItemDescriptor.Required &&
                                 ItemDescriptor.RecordReference != null &&
                                 !ItemDescriptor.RecordReference.AllowClear));

        public bool IsValidFor(GameContentFieldType collectionFieldType)
        {
            return IsValid && collectionFieldType.IsOrderedCollection() &&
                   ItemDescriptor.FieldType.ToOrderedCollectionType() == collectionFieldType;
        }

        public bool Accepts(
            GameContentFieldType collectionFieldType,
            bool required,
            GameContentOrderedCollectionValue value,
            out string reason)
        {
            if (!IsValidFor(collectionFieldType))
            {
                reason = "The ordered-collection field contract is invalid.";
                return false;
            }
            if (value == null || value.FieldType != collectionFieldType ||
                value.ItemType != ItemDescriptor.FieldType)
            {
                reason = "The ordered collection does not match its field or item type.";
                return false;
            }

            int minimum = Math.Max(MinimumCount, required ? 1 : 0);
            if (value.Count < minimum)
            {
                reason = "The collection requires at least " + minimum + " item(s).";
                return false;
            }
            if (MaximumCount.HasValue && value.Count > MaximumCount.Value)
            {
                reason = "The collection allows at most " + MaximumCount.Value + " item(s).";
                return false;
            }

            for (int i = 0; i < value.Items.Count; i++)
            {
                if (!ItemDescriptor.Accepts(value.Items[i].Value, out string itemReason))
                {
                    reason = "Item " + (i + 1) + ": " + itemReason;
                    return false;
                }
            }

            if (!AllowDuplicates)
            {
                for (int i = 0; i < value.Items.Count; i++)
                {
                    for (int other = i + 1; other < value.Items.Count; other++)
                    {
                        if (!value.Items[i].Value.Equals(value.Items[other].Value)) continue;
                        reason = "Duplicate collection items are not allowed.";
                        return false;
                    }
                }
            }

            reason = string.Empty;
            return true;
        }

        internal bool ContainsDuplicate(
            IReadOnlyList<GameContentCollectionItem> items,
            GameContentFieldValue value,
            GameContentCollectionItemKey ignoredItemKey = null)
        {
            return !AllowDuplicates && items.Any(item =>
                (ignoredItemKey == null || !item.ItemKey.Equals(ignoredItemKey)) && item.Value.Equals(value));
        }
    }

    public enum GameContentCollectionOperationKind
    {
        Add = 0,
        Remove = 1,
        Move = 2,
        Replace = 3
    }

    public sealed class GameContentCollectionOperation
    {
        private GameContentCollectionOperation(
            GameContentCollectionOperationKind kind,
            GameContentCollectionItemKey itemKey,
            GameContentFieldValue value,
            int newIndex)
        {
            Kind = kind;
            ItemKey = itemKey;
            Value = value;
            NewIndex = newIndex;
        }

        public GameContentCollectionOperationKind Kind { get; }
        public GameContentCollectionItemKey ItemKey { get; }
        public GameContentFieldValue Value { get; }
        public int NewIndex { get; }

        public static GameContentCollectionOperation Add(GameContentFieldValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new GameContentCollectionOperation(GameContentCollectionOperationKind.Add, null, value, -1);
        }

        public static GameContentCollectionOperation Remove(GameContentCollectionItemKey itemKey)
        {
            RequireKey(itemKey);
            return new GameContentCollectionOperation(GameContentCollectionOperationKind.Remove, itemKey, null, -1);
        }

        public static GameContentCollectionOperation Move(GameContentCollectionItemKey itemKey, int newIndex)
        {
            RequireKey(itemKey);
            if (newIndex < 0) throw new ArgumentOutOfRangeException(nameof(newIndex));
            return new GameContentCollectionOperation(GameContentCollectionOperationKind.Move, itemKey, null, newIndex);
        }

        public static GameContentCollectionOperation Replace(
            GameContentCollectionItemKey itemKey,
            GameContentFieldValue value)
        {
            RequireKey(itemKey);
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new GameContentCollectionOperation(GameContentCollectionOperationKind.Replace, itemKey, value, -1);
        }

        private static void RequireKey(GameContentCollectionItemKey itemKey)
        {
            if (itemKey == null || !itemKey.IsValid)
                throw new ArgumentException("The collection operation requires a valid existing session item key.", nameof(itemKey));
        }
    }

    public interface IGameContentOrderedCollectionEditSession
    {
        GameContentEditOperationResult ApplyCollectionOperation(
            string fieldId,
            GameContentCollectionOperation operation);
    }

    public static class GameContentCollectionMutation
    {
        public static bool TryApply(
            GameContentFieldDescriptor field,
            GameContentOrderedCollectionValue current,
            GameContentCollectionOperation operation,
            out GameContentOrderedCollectionValue proposed,
            out string reason)
        {
            proposed = current;
            if (field == null || !field.FieldType.IsOrderedCollection() || field.Collection == null ||
                !field.Collection.IsValidFor(field.FieldType))
            {
                reason = "The field has no valid ordered-collection contract.";
                return false;
            }
            if (current == null || current.FieldType != field.FieldType ||
                current.ItemType != field.Collection.ItemDescriptor.FieldType)
            {
                reason = "The current ordered collection does not match its field contract.";
                return false;
            }
            if (operation == null)
            {
                reason = "No collection operation was provided.";
                return false;
            }

            var items = current.Items.ToList();
            switch (operation.Kind)
            {
                case GameContentCollectionOperationKind.Add:
                    if (!AcceptsOperationValue(field.Collection, operation.Value, out reason)) return false;
                    if (field.Collection.MaximumCount.HasValue && items.Count >= field.Collection.MaximumCount.Value)
                    {
                        reason = "The collection already contains its maximum number of items.";
                        return false;
                    }
                    if (field.Collection.ContainsDuplicate(items, operation.Value))
                    {
                        reason = "Duplicate collection items are not allowed.";
                        return false;
                    }
                    items.Add(new GameContentCollectionItem(
                        GameContentCollectionItemKey.Create(),
                        -1,
                        operation.Value));
                    break;

                case GameContentCollectionOperationKind.Remove:
                {
                    int index = IndexOf(items, operation.ItemKey);
                    if (index < 0)
                    {
                        reason = "The collection item key is unknown to this session.";
                        return false;
                    }
                    int minimum = Math.Max(field.Collection.MinimumCount, field.Required ? 1 : 0);
                    if (items.Count - 1 < minimum)
                    {
                        reason = "Removing this item would violate the minimum collection count.";
                        return false;
                    }
                    items.RemoveAt(index);
                    break;
                }

                case GameContentCollectionOperationKind.Move:
                {
                    int index = IndexOf(items, operation.ItemKey);
                    if (index < 0)
                    {
                        reason = "The collection item key is unknown to this session.";
                        return false;
                    }
                    if (operation.NewIndex < 0 || operation.NewIndex >= items.Count)
                    {
                        reason = "The collection move target is outside the current sequence.";
                        return false;
                    }
                    if (index == operation.NewIndex)
                    {
                        reason = string.Empty;
                        return true;
                    }
                    GameContentCollectionItem moved = items[index];
                    items.RemoveAt(index);
                    items.Insert(operation.NewIndex, moved);
                    break;
                }

                case GameContentCollectionOperationKind.Replace:
                {
                    int index = IndexOf(items, operation.ItemKey);
                    if (index < 0)
                    {
                        reason = "The collection item key is unknown to this session.";
                        return false;
                    }
                    if (!AcceptsOperationValue(field.Collection, operation.Value, out reason)) return false;
                    if (field.Collection.ContainsDuplicate(items, operation.Value, operation.ItemKey))
                    {
                        reason = "Duplicate collection items are not allowed.";
                        return false;
                    }
                    GameContentCollectionItem existing = items[index];
                    items[index] = new GameContentCollectionItem(
                        existing.ItemKey,
                        existing.OriginalIndex,
                        operation.Value);
                    break;
                }

                default:
                    reason = "The collection operation kind is unsupported.";
                    return false;
            }

            proposed = new GameContentOrderedCollectionValue(current.ItemType, items);
            reason = string.Empty;
            return true;
        }

        public static IReadOnlyList<GameContentCollectionOperation> BuildRestoreOriginalOrderOperations(
            GameContentOrderedCollectionValue current)
        {
            if (current == null) return Array.Empty<GameContentCollectionOperation>();
            GameContentCollectionItem[] target = current.Items
                .Where(item => item.OriginalIndex >= 0)
                .OrderBy(item => item.OriginalIndex)
                .Concat(current.Items.Where(item => item.OriginalIndex < 0))
                .ToArray();
            var working = current.Items.ToList();
            var operations = new List<GameContentCollectionOperation>();
            for (int targetIndex = 0; targetIndex < target.Length; targetIndex++)
            {
                if (working[targetIndex].ItemKey.Equals(target[targetIndex].ItemKey)) continue;
                int currentIndex = IndexOf(working, target[targetIndex].ItemKey);
                if (currentIndex < 0) continue;
                operations.Add(GameContentCollectionOperation.Move(target[targetIndex].ItemKey, targetIndex));
                GameContentCollectionItem item = working[currentIndex];
                working.RemoveAt(currentIndex);
                working.Insert(targetIndex, item);
            }
            return new ReadOnlyCollection<GameContentCollectionOperation>(operations);
        }

        private static bool AcceptsOperationValue(
            GameContentCollectionFieldDescriptor descriptor,
            GameContentFieldValue value,
            out string reason)
        {
            if (value == null || value.FieldType != descriptor.ItemDescriptor.FieldType)
            {
                reason = "The collection item does not match the declared item type.";
                return false;
            }
            return descriptor.ItemDescriptor.Accepts(value, out reason);
        }

        private static int IndexOf(
            IReadOnlyList<GameContentCollectionItem> items,
            GameContentCollectionItemKey itemKey)
        {
            if (itemKey == null) return -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].ItemKey.Equals(itemKey)) return i;
            }
            return -1;
        }
    }

    public sealed class GameContentCollectionProposedChange
    {
        public GameContentCollectionProposedChange(
            GameContentCollectionOperationKind operation,
            GameContentCollectionItemKey itemKey,
            int oldIndex,
            int newIndex,
            GameContentFieldValue oldValue,
            GameContentFieldValue newValue,
            string summary)
        {
            Operation = operation;
            ItemKey = itemKey;
            OldIndex = oldIndex;
            NewIndex = newIndex;
            OldValue = oldValue;
            NewValue = newValue;
            Summary = summary ?? string.Empty;
        }

        public GameContentCollectionOperationKind Operation { get; }
        public GameContentCollectionItemKey ItemKey { get; }
        public int OldIndex { get; }
        public int NewIndex { get; }
        public GameContentFieldValue OldValue { get; }
        public GameContentFieldValue NewValue { get; }
        public string Summary { get; }
    }

    public sealed class GameContentCollectionChangeReview
    {
        private readonly IReadOnlyList<GameContentCollectionProposedChange> _changes;

        private GameContentCollectionChangeReview(
            GameContentRecordKey sourceRecordKey,
            string fieldId,
            GameContentOrderedCollectionValue originalValue,
            GameContentOrderedCollectionValue proposedValue,
            IEnumerable<GameContentCollectionProposedChange> changes,
            GameContentReferenceRuntimeImpact runtimeImpact)
        {
            SourceRecordKey = sourceRecordKey;
            FieldId = fieldId ?? string.Empty;
            OriginalValue = originalValue;
            ProposedValue = proposedValue;
            _changes = new ReadOnlyCollection<GameContentCollectionProposedChange>(
                (changes ?? Array.Empty<GameContentCollectionProposedChange>()).Where(change => change != null).ToArray());
            RuntimeImpact = runtimeImpact;
        }

        public GameContentRecordKey SourceRecordKey { get; }
        public string FieldId { get; }
        public GameContentOrderedCollectionValue OriginalValue { get; }
        public GameContentOrderedCollectionValue ProposedValue { get; }
        public IReadOnlyList<GameContentCollectionProposedChange> Changes => _changes;
        public GameContentReferenceRuntimeImpact RuntimeImpact { get; }
        public bool ContainsRecordReferences => ProposedValue?.ItemType == GameContentFieldType.RecordReference;

        public static GameContentCollectionChangeReview Create(
            GameContentRecordKey sourceRecordKey,
            string fieldId,
            GameContentOrderedCollectionValue originalValue,
            GameContentOrderedCollectionValue proposedValue,
            GameContentReferenceRuntimeImpact runtimeImpact)
        {
            if (originalValue == null || proposedValue == null || originalValue.ItemType != proposedValue.ItemType)
                return null;

            var changes = new List<GameContentCollectionProposedChange>();
            var originalByKey = originalValue.Items.ToDictionary(item => item.ItemKey, item => item);
            var proposedByKey = proposedValue.Items.ToDictionary(item => item.ItemKey, item => item);

            for (int i = 0; i < originalValue.Items.Count; i++)
            {
                GameContentCollectionItem item = originalValue.Items[i];
                if (proposedByKey.ContainsKey(item.ItemKey)) continue;
                changes.Add(new GameContentCollectionProposedChange(
                    GameContentCollectionOperationKind.Remove,
                    item.ItemKey,
                    i,
                    -1,
                    item.Value,
                    null,
                    "Remove " + Describe(item.Value) + " from position " + (i + 1) + "."));
            }

            for (int i = 0; i < proposedValue.Items.Count; i++)
            {
                GameContentCollectionItem item = proposedValue.Items[i];
                if (originalByKey.ContainsKey(item.ItemKey)) continue;
                changes.Add(new GameContentCollectionProposedChange(
                    GameContentCollectionOperationKind.Add,
                    item.ItemKey,
                    -1,
                    i,
                    null,
                    item.Value,
                    "Add " + Describe(item.Value) + " at position " + (i + 1) + "."));
            }

            for (int i = 0; i < proposedValue.Items.Count; i++)
            {
                GameContentCollectionItem item = proposedValue.Items[i];
                if (!originalByKey.TryGetValue(item.ItemKey, out GameContentCollectionItem original) ||
                    original.Value.Equals(item.Value)) continue;
                int oldIndex = IndexOf(originalValue.Items, item.ItemKey);
                changes.Add(new GameContentCollectionProposedChange(
                    GameContentCollectionOperationKind.Replace,
                    item.ItemKey,
                    oldIndex,
                    i,
                    original.Value,
                    item.Value,
                    "Replace " + Describe(original.Value) + " with " + Describe(item.Value) +
                    " at position " + (i + 1) + "."));
            }

            GameContentCollectionItemKey[] originalSurvivors = originalValue.Items
                .Where(item => proposedByKey.ContainsKey(item.ItemKey))
                .Select(item => item.ItemKey)
                .ToArray();
            GameContentCollectionItemKey[] proposedSurvivors = proposedValue.Items
                .Where(item => originalByKey.ContainsKey(item.ItemKey))
                .Select(item => item.ItemKey)
                .ToArray();
            var working = originalSurvivors.ToList();
            for (int targetIndex = 0; targetIndex < proposedSurvivors.Length; targetIndex++)
            {
                GameContentCollectionItemKey key = proposedSurvivors[targetIndex];
                if (working[targetIndex].Equals(key)) continue;
                int currentIndex = working.FindIndex(candidate => candidate.Equals(key));
                if (currentIndex < 0) continue;
                GameContentCollectionItem proposedItem = proposedByKey[key];
                int oldIndex = IndexOf(originalValue.Items, key);
                int newIndex = IndexOf(proposedValue.Items, key);
                changes.Add(new GameContentCollectionProposedChange(
                    GameContentCollectionOperationKind.Move,
                    key,
                    oldIndex,
                    newIndex,
                    proposedItem.Value,
                    proposedItem.Value,
                    "Move " + Describe(proposedItem.Value) + " from position " + (oldIndex + 1) +
                    " to position " + (newIndex + 1) + "."));
                working.RemoveAt(currentIndex);
                working.Insert(targetIndex, key);
            }

            return new GameContentCollectionChangeReview(
                sourceRecordKey,
                fieldId,
                originalValue,
                proposedValue,
                changes,
                runtimeImpact);
        }

        private static int IndexOf(
            IReadOnlyList<GameContentCollectionItem> items,
            GameContentCollectionItemKey itemKey)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].ItemKey.Equals(itemKey)) return i;
            }
            return -1;
        }

        private static string Describe(GameContentFieldValue value)
        {
            if (value?.FieldType == GameContentFieldType.RecordReference)
            {
                GameContentRecordReferenceValue reference = value.RecordReferenceValue;
                if (reference != null && reference.IsResolved && reference.TargetKey != null)
                {
                    string display = string.IsNullOrWhiteSpace(reference.TargetDisplayName)
                        ? reference.TargetKey.SourceRecordId
                        : reference.TargetDisplayName;
                    return display + " (" + reference.TargetKey.SourceRecordId + ")";
                }
            }
            return value?.ToDisplayString() ?? string.Empty;
        }
    }
}
