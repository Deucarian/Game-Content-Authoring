using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Deucarian.GameContentAuthoring.Editor.Tests
{
    public sealed class GameContentStructuredCollectionModelsEditModeTests
    {
        private const string OwnerId = "com.deucarian.tests.structured";
        private const string PackId = "structured-fixture";
        private const string SourceId = "structured-source";

        [Test]
        public void Models_AreImmutableDeterministicAndPersistedEqualityIgnoresSessionKeys()
        {
            GameContentStructuredRowDescriptor rowDescriptor = RowDescriptor();
            GameContentFieldDescriptor field = StructuredField(rowDescriptor);
            GameContentStructuredRowValue first = OriginalRow(0, "native-a", "Alpha", 2, 1.5, true, "burst", Target("a"));
            GameContentStructuredRowValue second = OriginalRow(1, "native-b", "Beta", 3, 2.5, false, "steady", Target("b"));
            var input = new List<GameContentStructuredRowValue> { first, second };
            var value = new GameContentOrderedStructuredCollectionValue(rowDescriptor.RowSchemaId, input);
            var equivalent = new GameContentOrderedStructuredCollectionValue(
                rowDescriptor.RowSchemaId,
                new[]
                {
                    OriginalRow(0, "native-a", "Alpha", 2, 1.5, true, "burst", Target("a")),
                    OriginalRow(1, "native-b", "Beta", 3, 2.5, false, "steady", Target("b"))
                });

            input.Clear();
            Assert.That((int)GameContentFieldType.OrderedStructuredCollection, Is.EqualTo(8));
            Assert.That(GameContentFieldType.String.IsScalarValue(), Is.True);
            Assert.That(GameContentFieldType.OrderedStructuredCollection.IsScalarValue(), Is.False);
            Assert.That(field.IsValid, Is.True);
            Assert.That(field.FieldType, Is.EqualTo(GameContentFieldType.OrderedStructuredCollection));
            Assert.That(rowDescriptor.Fields.Select(item => item.FieldId), Is.EqualTo(new[]
            {
                "nativeLabel", "title", "count", "weight", "enabled", "mode", "target"
            }));
            Assert.That(value.Count, Is.EqualTo(2));
            Assert.That(value.Rows, Is.InstanceOf<IReadOnlyList<GameContentStructuredRowValue>>());
            Assert.That(value.Equals(equivalent), Is.True, "Persisted equality must ignore fresh session keys.");
            Assert.That(value.GetHashCode(), Is.EqualTo(equivalent.GetHashCode()));
            Assert.That(value.Rows[0].RowKey, Is.Not.EqualTo(equivalent.Rows[0].RowKey));
            Assert.That(value.Rows[0].OriginalIndex, Is.Zero);
            Assert.That(field.Accepts(GameContentFieldValue.FromOrderedStructuredCollection(value), out _), Is.True);
            Assert.That(typeof(GameContentStructuredRowValue)
                .GetProperty(nameof(GameContentStructuredRowValue.NativeKeyDisplayMetadata))
                ?.CanWrite, Is.False);
            Assert.That(typeof(GameContentStructuredRowKey).GetConstructors(), Is.Empty,
                "Opaque row keys must not expose a public constructor.");
            MethodInfo add = typeof(GameContentStructuredCollectionOperation)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == nameof(GameContentStructuredCollectionOperation.AddRow));
            Assert.That(add.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(GameContentStructuredRowKey)), Is.False,
                "Callers must never supply an AddRow session key.");
        }

        [Test]
        public void Descriptor_RejectsNestedValuesAndCanonicalRecordsDisguisedAsRows()
        {
            GameContentStructuredRowDescriptor canonical = new GameContentStructuredRowDescriptor(
                "canonical-row",
                "Canonical Row",
                string.Empty,
                new[] { StringField("a", 0), StringField("b", 1) },
                representsIndependentCanonicalRecord: true);
            Assert.That(canonical.IsValid, Is.False);
            Assert.That(canonical.BoundaryViolationReason, Does.Contain("canonical"));
            Assert.That(canonical.BoundaryViolationReason, Does.Contain("CRUD"));

            var canonicalNativeKey = new GameContentStructuredRowNativeKeyDescriptor(
                "Stable ID",
                representsIndependentCanonicalRecord: true);
            GameContentStructuredRowDescriptor nativeCanonical = new GameContentStructuredRowDescriptor(
                "native-canonical",
                "Native Canonical",
                string.Empty,
                new[] { StringField("a", 0), StringField("b", 1) },
                nativeKey: canonicalNativeKey);
            Assert.That(nativeCanonical.IsValid, Is.False);
            Assert.That(nativeCanonical.BoundaryViolationReason, Does.Contain("CRUD"));

            GameContentFieldDescriptor nested = new GameContentFieldDescriptor(
                "nested",
                "fixture.nested",
                "Nested",
                string.Empty,
                GameContentFieldType.OrderedScalarCollection,
                collection: new GameContentCollectionFieldDescriptor(StringField("nested.item", 0)));
            GameContentStructuredRowDescriptor nestedSchema = new GameContentStructuredRowDescriptor(
                "nested-schema",
                "Nested Schema",
                string.Empty,
                new[] { StringField("title", 0), nested });
            Assert.That(nestedSchema.IsValid, Is.False,
                "Nested collections are outside the structured-row foundation.");
        }

        [Test]
        public void AddRow_GeneratesKeyAndRejectsMissingUnknownWrongInvalidAndMaximumValues()
        {
            GameContentFieldDescriptor field = StructuredField(RowDescriptor(), minimum: 1, maximum: 2);
            GameContentOrderedStructuredCollectionValue current = Collection(
                field,
                OriginalRow(0, "native-a", "Alpha", 2, 1.5, true, "burst", Target("a")));
            GameContentStructuredCollectionOperation add = GameContentStructuredCollectionOperation.AddRow(
                AddedValues("Beta", 3, 2.5, false, "steady", Target("b")));
            Assert.That(add.RowKey, Is.Null);
            Assert.That(Apply(field, current, add, out current, out GameContentStructuredRowKey generated, out _), Is.True);
            Assert.That(generated, Is.Not.Null);
            Assert.That(current.Rows[1].RowKey, Is.EqualTo(generated));
            Assert.That(current.Rows[1].IsAdded, Is.True);

            Assert.That(Apply(field, current, GameContentStructuredCollectionOperation.AddRow(
                AddedValues("Gamma", 4, 3.5, true, "burst", Target("c"))), out _, out _, out string maximum), Is.False);
            Assert.That(maximum, Does.Contain("maximum"));

            GameContentFieldDescriptor room = StructuredField(RowDescriptor(), minimum: 0, maximum: 4);
            GameContentOrderedStructuredCollectionValue empty = Collection(room);
            AssertRejected(room, empty, AddedValues(null, 2, 1.5, true, "burst", Target("a")), "required");
            AssertRejected(room, empty, AddedValues("Alpha", 2, 1.5, true, "unknown", Target("a")), "enum");

            var unknown = AddedValues("Alpha", 2, 1.5, true, "burst", Target("a")).ToList();
            unknown.Add(new GameContentStructuredRowFieldValue("unknown", GameContentFieldValue.FromString("x")));
            AssertRejected(room, empty, unknown, "unknown field");

            var wrong = AddedValues("Alpha", 2, 1.5, true, "burst", Target("a")).ToList();
            wrong[1] = new GameContentStructuredRowFieldValue("count", GameContentFieldValue.FromString("two"));
            AssertRejected(room, empty, wrong, "field type");

            var readOnly = AddedValues("Alpha", 2, 1.5, true, "burst", Target("a")).ToList();
            readOnly.Add(new GameContentStructuredRowFieldValue("nativeLabel", GameContentFieldValue.FromString("crafted")));
            AssertRejected(room, empty, readOnly, "read-only");
        }

        [Test]
        public void RemoveMoveReplaceAndRestore_PreserveRowIdentityAndOtherFields()
        {
            GameContentFieldDescriptor field = StructuredField(RowDescriptor(), minimum: 1, maximum: 5);
            GameContentStructuredRowValue alpha = OriginalRow(0, "native-a", "Alpha", 2, 1.5, true, "burst", Target("a"));
            GameContentStructuredRowValue beta = OriginalRow(1, "native-b", "Beta", 3, 2.5, false, "steady", Target("b"));
            GameContentStructuredRowValue gamma = OriginalRow(2, "native-c", "Gamma", 4, 3.5, true, "burst", Target("c"));
            GameContentOrderedStructuredCollectionValue current = Collection(field, alpha, beta, gamma);

            Assert.That(Apply(field, current,
                GameContentStructuredCollectionOperation.MoveRow(gamma.RowKey, 0), out current, out _, out _), Is.True);
            Assert.That(current.Rows.Select(row => row.RowKey), Is.EqualTo(new[] { gamma.RowKey, alpha.RowKey, beta.RowKey }));

            var replacements = new[]
            {
                new { Id = "title", Value = GameContentFieldValue.FromString("Renamed") },
                new { Id = "count", Value = GameContentFieldValue.FromInteger(9) },
                new { Id = "weight", Value = GameContentFieldValue.FromNumber(8.25) },
                new { Id = "enabled", Value = GameContentFieldValue.FromBoolean(false) },
                new { Id = "mode", Value = GameContentFieldValue.FromEnum("steady") },
                new { Id = "target", Value = ReferenceValue(Target("b")) }
            };
            for (int i = 0; i < replacements.Length; i++)
            {
                GameContentFieldValue untouched = current.Rows[1].FieldValues
                    .Single(value => value.FieldId == "nativeLabel").Value;
                Assert.That(Apply(
                    field,
                    current,
                    GameContentStructuredCollectionOperation.ReplaceRowField(
                        alpha.RowKey,
                        replacements[i].Id,
                        replacements[i].Value),
                    out current,
                    out _,
                    out _), Is.True);
                Assert.That(current.Rows[1].RowKey, Is.EqualTo(alpha.RowKey));
                Assert.That(current.Rows[1].FieldValues.Single(value => value.FieldId == "nativeLabel").Value,
                    Is.EqualTo(untouched));
            }

            Assert.That(Apply(field, current, GameContentStructuredCollectionOperation.ReplaceRowField(
                alpha.RowKey, "unknown", GameContentFieldValue.FromString("x")), out _, out _, out string unknown), Is.False);
            Assert.That(unknown, Does.Contain("unknown"));
            Assert.That(Apply(field, current, GameContentStructuredCollectionOperation.ReplaceRowField(
                alpha.RowKey, "nativeLabel", GameContentFieldValue.FromString("changed")), out _, out _, out string readOnly), Is.False);
            Assert.That(readOnly, Does.Contain("read-only"));
            Assert.That(Apply(field, current, GameContentStructuredCollectionOperation.RemoveRow(
                GameContentStructuredRowKey.CreateSessionKey()), out _, out _, out string crafted), Is.False);
            Assert.That(crafted, Does.Contain("unknown"));

            Assert.That(Apply(field, current,
                GameContentStructuredCollectionOperation.RemoveRow(beta.RowKey), out current, out _, out _), Is.True);
            Assert.That(current.Rows.Any(row => row.RowKey.Equals(beta.RowKey)), Is.False);
            Assert.That(Apply(field, current,
                GameContentStructuredCollectionOperation.RestoreOriginalOrder(), out current, out _, out _), Is.True);
            Assert.That(current.Rows.Select(row => row.RowKey), Is.EqualTo(new[] { alpha.RowKey, gamma.RowKey }));

            Assert.That(Apply(field, current,
                GameContentStructuredCollectionOperation.RemoveRow(gamma.RowKey), out current, out _, out _), Is.True);
            Assert.That(Apply(field, current,
                GameContentStructuredCollectionOperation.RemoveRow(alpha.RowKey), out _, out _, out string minimum), Is.False);
            Assert.That(minimum, Does.Contain("minimum"));
        }

        [Test]
        public void DuplicateRowsRemainAddressableAndNativeKeysMustBeUnique()
        {
            GameContentStructuredRowDescriptor withoutNative = RowDescriptor(includeNativeKey: false, includeReadOnly: false);
            GameContentFieldDescriptor allowDuplicates = StructuredField(
                withoutNative,
                duplicatePolicy: GameContentStructuredRowDuplicatePolicy.Allow,
                minimum: 0,
                maximum: 5);
            GameContentStructuredRowValue first = OriginalRow(
                0, null, "Same", 2, 1.5, true, "burst", Target("a"), includeReadOnly: false);
            GameContentStructuredRowValue second = OriginalRow(
                1, null, "Same", 2, 1.5, true, "burst", Target("a"), includeReadOnly: false);
            GameContentOrderedStructuredCollectionValue value = Collection(allowDuplicates, first, second);
            Assert.That(first.RowKey, Is.Not.EqualTo(second.RowKey));
            Assert.That(first.PersistedEquals(second), Is.True);
            Assert.That(Apply(allowDuplicates, value,
                GameContentStructuredCollectionOperation.ReplaceRowField(
                    first.RowKey, "title", GameContentFieldValue.FromString("First only")),
                out value, out _, out _), Is.True);
            Assert.That(value.Rows[0].TryGetFieldValue("title", out GameContentFieldValue changed), Is.True);
            Assert.That(value.Rows[1].TryGetFieldValue("title", out GameContentFieldValue unchanged), Is.True);
            Assert.That(changed.StringValue, Is.EqualTo("First only"));
            Assert.That(unchanged.StringValue, Is.EqualTo("Same"));

            GameContentFieldDescriptor rejectDuplicates = StructuredField(
                withoutNative,
                duplicatePolicy: GameContentStructuredRowDuplicatePolicy.RejectPersistedDuplicates,
                minimum: 0,
                maximum: 5);
            Assert.That(rejectDuplicates.Accepts(
                GameContentFieldValue.FromOrderedStructuredCollection(Collection(rejectDuplicates, first, second)),
                out string duplicateReason), Is.False);
            Assert.That(duplicateReason, Does.Contain("Duplicate"));

            GameContentFieldDescriptor nativeField = StructuredField(RowDescriptor(), minimum: 0, maximum: 5);
            GameContentOrderedStructuredCollectionValue duplicateNative = Collection(
                nativeField,
                OriginalRow(0, "native-a", "Alpha", 2, 1.5, true, "burst", Target("a")),
                OriginalRow(1, "native-a", "Beta", 3, 2.5, false, "steady", Target("b")));
            Assert.That(nativeField.Accepts(
                GameContentFieldValue.FromOrderedStructuredCollection(duplicateNative),
                out string nativeReason), Is.False);
            Assert.That(nativeReason, Does.Contain("native"));
            Assert.That(nativeReason, Does.Contain("unique"));
        }

        [Test]
        public void PermittedOperations_AreEnforcedAndRestoreRequiresMoveSupport()
        {
            GameContentStructuredRowDescriptor rowDescriptor = RowDescriptor();
            GameContentFieldDescriptor moveOnly = StructuredField(
                rowDescriptor,
                minimum: 1,
                maximum: 3,
                operations: GameContentStructuredCollectionPermittedOperations.MoveRow);
            GameContentStructuredRowValue alpha = OriginalRow(
                0, "native-a", "Alpha", 2, 1.5, true, "burst", Target("a"));
            GameContentStructuredRowValue beta = OriginalRow(
                1, "native-b", "Beta", 3, 2.5, false, "steady", Target("b"));
            GameContentOrderedStructuredCollectionValue current = Collection(moveOnly, alpha, beta);

            Assert.That(Apply(moveOnly, current,
                GameContentStructuredCollectionOperation.MoveRow(beta.RowKey, 0),
                out current, out _, out _), Is.True);
            Assert.That(Apply(moveOnly, current,
                GameContentStructuredCollectionOperation.MoveRow(beta.RowKey, 1),
                out current, out _, out _), Is.True);
            Assert.That(Apply(moveOnly, current,
                GameContentStructuredCollectionOperation.MoveRow(beta.RowKey, 2),
                out _, out _, out string boundsReason), Is.False);
            Assert.That(boundsReason, Does.Contain("outside"));
            Assert.That(Apply(moveOnly, current,
                GameContentStructuredCollectionOperation.RemoveRow(alpha.RowKey),
                out _, out _, out string removeReason), Is.False);
            Assert.That(removeReason, Does.Contain("does not permit"));
            Assert.That(Apply(moveOnly, current,
                GameContentStructuredCollectionOperation.AddRow(
                    AddedValues("Gamma", 4, 3.5, true, "burst", Target("c"))),
                out _, out _, out string addReason), Is.False);
            Assert.That(addReason, Does.Contain("does not permit"));

            var invalid = new GameContentStructuredCollectionFieldDescriptor(
                "rows",
                "fixture.rows",
                "Rows",
                string.Empty,
                rowDescriptor,
                permittedOperations: GameContentStructuredCollectionPermittedOperations.RestoreOriginalOrder);
            Assert.That(invalid.IsValid, Is.False,
                "Restore Original Order is invalid unless MoveRow is also permitted.");
        }

        [Test]
        public void Review_ReportsAddsRemovalsMovesFieldChangesOrdersAndRuntimeImpact()
        {
            GameContentFieldDescriptor field = StructuredField(RowDescriptor(), minimum: 0, maximum: 5);
            GameContentStructuredRowValue alpha = OriginalRow(0, "native-a", "Alpha", 2, 1.5, true, "burst", Target("a"));
            GameContentStructuredRowValue beta = OriginalRow(1, "native-b", "Beta", 3, 2.5, false, "steady", Target("b"));
            GameContentStructuredRowValue gamma = OriginalRow(2, "native-c", "Gamma", 4, 3.5, true, "burst", Target("c"));
            GameContentOrderedStructuredCollectionValue original = Collection(field, alpha, beta, gamma);
            GameContentOrderedStructuredCollectionValue proposed = original;

            Apply(field, proposed, GameContentStructuredCollectionOperation.RemoveRow(beta.RowKey), out proposed, out _, out _);
            Apply(field, proposed, GameContentStructuredCollectionOperation.MoveRow(gamma.RowKey, 0), out proposed, out _, out _);
            Apply(field, proposed, GameContentStructuredCollectionOperation.ReplaceRowField(
                alpha.RowKey, "title", GameContentFieldValue.FromString("Alpha 2")), out proposed, out _, out _);
            Apply(field, proposed, GameContentStructuredCollectionOperation.AddRow(
                AddedValues("Delta", 5, 4.5, true, "steady", Target("a"))), out proposed, out _, out _);

            GameContentStructuredCollectionChangeReview review = GameContentStructuredCollectionChangeReview.Create(
                Key(PackId, "source"),
                field.FieldId,
                original,
                proposed,
                new[] { GameContentAuthoringValidationIssue.Warning("rows[1].title", "Review title.") },
                GameContentReferenceRuntimeImpact.Refresh | GameContentReferenceRuntimeImpact.Rebind);
            Assert.That(review, Is.Not.Null);
            Assert.That(review.OriginalOrder.Count, Is.EqualTo(3));
            Assert.That(review.ProposedOrder.Count, Is.EqualTo(3));
            Assert.That(review.AddedRows.Count, Is.EqualTo(1));
            Assert.That(review.RemovedRows.Single().RowKey, Is.EqualTo(beta.RowKey));
            Assert.That(review.MovedRows.Single().RowKey, Is.EqualTo(gamma.RowKey));
            Assert.That(review.FieldChanges.Single().RowFieldId, Is.EqualTo("title"));
            Assert.That(review.ValidationFindings.Count, Is.EqualTo(1));
            Assert.That(review.RuntimeImpact,
                Is.EqualTo(GameContentReferenceRuntimeImpact.Refresh | GameContentReferenceRuntimeImpact.Rebind));
        }

        private static void AssertRejected(
            GameContentFieldDescriptor field,
            GameContentOrderedStructuredCollectionValue current,
            IEnumerable<GameContentStructuredRowFieldValue> values,
            string expectedReason)
        {
            Assert.That(Apply(field, current,
                GameContentStructuredCollectionOperation.AddRow(values), out _, out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain(expectedReason).IgnoreCase);
        }

        private static bool Apply(
            GameContentFieldDescriptor field,
            GameContentOrderedStructuredCollectionValue current,
            GameContentStructuredCollectionOperation operation,
            out GameContentOrderedStructuredCollectionValue proposed,
            out GameContentStructuredRowKey rowKey,
            out string reason)
        {
            return GameContentStructuredCollectionMutation.TryApply(
                field, current, operation, out proposed, out rowKey, out reason);
        }

        private static GameContentFieldDescriptor StructuredField(
            GameContentStructuredRowDescriptor rowDescriptor,
            int minimum = 1,
            int? maximum = 4,
            GameContentStructuredRowDuplicatePolicy duplicatePolicy = GameContentStructuredRowDuplicatePolicy.Allow,
            GameContentStructuredCollectionPermittedOperations operations =
                GameContentStructuredCollectionPermittedOperations.All)
        {
            return GameContentFieldDescriptor.FromStructuredCollection(
                new GameContentStructuredCollectionFieldDescriptor(
                    "rows",
                    "fixture.rows",
                    "Rows",
                    "Structured fixture rows.",
                    rowDescriptor,
                    minimum,
                    maximum,
                    "Priority order is significant.",
                    duplicatePolicy,
                    operations,
                    GameContentReferenceRuntimeImpact.Refresh | GameContentReferenceRuntimeImpact.Rebind),
                30,
                "Structured");
        }

        private static GameContentStructuredRowDescriptor RowDescriptor(
            bool includeNativeKey = true,
            bool includeReadOnly = true)
        {
            var fields = new List<GameContentFieldDescriptor>
            {
                ReferenceField("target", 60),
                EnumField("mode", 50),
                BooleanField("enabled", 40),
                NumberField("weight", 30),
                IntegerField("count", 20),
                StringField("title", 10, required: true)
            };
            if (includeReadOnly)
            {
                fields.Add(new GameContentFieldDescriptor(
                    "nativeLabel",
                    "fixture.rows.native-label",
                    "Native Label",
                    "Provider-owned immutable metadata.",
                    GameContentFieldType.String,
                    readOnly: true,
                    readOnlyReason: "Provider-owned identity is immutable.",
                    order: 0));
            }
            return new GameContentStructuredRowDescriptor(
                "fixture-row-v1",
                "Fixture Row",
                "One embedded child value owned by its parent source record.",
                fields,
                new[] { "title", "mode" },
                includeNativeKey
                    ? new GameContentStructuredRowNativeKeyDescriptor(
                        "Provider Native Key",
                        "Read-only display metadata; it is not a canonical record ID.")
                    : null);
        }

        private static GameContentStructuredRowValue OriginalRow(
            int index,
            string nativeKey,
            string title,
            long count,
            double weight,
            bool enabled,
            string mode,
            GameContentRecordKey target,
            bool includeReadOnly = true)
        {
            var values = AddedValues(title, count, weight, enabled, mode, target).ToList();
            if (includeReadOnly)
            {
                values.Insert(0, new GameContentStructuredRowFieldValue(
                    "nativeLabel",
                    GameContentFieldValue.FromString(nativeKey ?? string.Empty)));
            }
            return new GameContentStructuredRowValue(
                GameContentStructuredRowKey.CreateSessionKey(),
                index,
                "fixture-row-v1",
                values,
                GameContentEditValidationState.Valid,
                title + " | " + mode,
                nativeKey);
        }

        private static IReadOnlyList<GameContentStructuredRowFieldValue> AddedValues(
            string title,
            long count,
            double weight,
            bool enabled,
            string mode,
            GameContentRecordKey target)
        {
            var values = new List<GameContentStructuredRowFieldValue>();
            if (title != null)
                values.Add(new GameContentStructuredRowFieldValue("title", GameContentFieldValue.FromString(title)));
            values.Add(new GameContentStructuredRowFieldValue("count", GameContentFieldValue.FromInteger(count)));
            values.Add(new GameContentStructuredRowFieldValue("weight", GameContentFieldValue.FromNumber(weight)));
            values.Add(new GameContentStructuredRowFieldValue("enabled", GameContentFieldValue.FromBoolean(enabled)));
            values.Add(new GameContentStructuredRowFieldValue("mode", GameContentFieldValue.FromEnum(mode)));
            values.Add(new GameContentStructuredRowFieldValue("target", ReferenceValue(target)));
            return values;
        }

        private static GameContentOrderedStructuredCollectionValue Collection(
            GameContentFieldDescriptor field,
            params GameContentStructuredRowValue[] rows)
        {
            return new GameContentOrderedStructuredCollectionValue(
                field.StructuredCollection.RowDescriptor.RowSchemaId,
                rows ?? Array.Empty<GameContentStructuredRowValue>());
        }

        private static GameContentFieldValue ReferenceValue(GameContentRecordKey target)
        {
            return GameContentFieldValue.FromRecordReference(
                GameContentRecordReferenceValue.Resolved(target, target.SourceRecordId, "InMemory"));
        }

        private static GameContentRecordKey Target(string id)
        {
            return Key(PackId, "target." + id);
        }

        private static GameContentRecordKey Key(string packId, string id)
        {
            return new GameContentRecordKey(OwnerId, packId, id, SourceId, id);
        }

        private static GameContentFieldDescriptor StringField(
            string id,
            int order,
            bool required = false)
        {
            return new GameContentFieldDescriptor(
                id,
                "fixture.rows." + id,
                id,
                string.Empty,
                GameContentFieldType.String,
                order: order,
                required: required,
                minimumLength: required ? 1 : (int?)null,
                maximumLength: 32);
        }

        private static GameContentFieldDescriptor IntegerField(string id, int order)
        {
            return new GameContentFieldDescriptor(
                id, "fixture.rows." + id, id, string.Empty, GameContentFieldType.Integer,
                order: order, required: true, minimumNumber: 0, maximumNumber: 100);
        }

        private static GameContentFieldDescriptor NumberField(string id, int order)
        {
            return new GameContentFieldDescriptor(
                id, "fixture.rows." + id, id, string.Empty, GameContentFieldType.Number,
                order: order, required: true, minimumNumber: 0, maximumNumber: 100);
        }

        private static GameContentFieldDescriptor BooleanField(string id, int order)
        {
            return new GameContentFieldDescriptor(
                id, "fixture.rows." + id, id, string.Empty, GameContentFieldType.Boolean,
                order: order, required: true);
        }

        private static GameContentFieldDescriptor EnumField(string id, int order)
        {
            return new GameContentFieldDescriptor(
                id,
                "fixture.rows." + id,
                id,
                string.Empty,
                GameContentFieldType.Enum,
                order: order,
                required: true,
                enumOptions: new[]
                {
                    new GameContentEnumOption("burst", "Burst"),
                    new GameContentEnumOption("steady", "Steady")
                });
        }

        private static GameContentFieldDescriptor ReferenceField(string id, int order)
        {
            return new GameContentFieldDescriptor(
                id,
                "fixture.rows." + id,
                id,
                string.Empty,
                GameContentFieldType.RecordReference,
                order: order,
                required: true,
                recordReference: new GameContentRecordReferenceFieldDescriptor(
                    "Weapon",
                    new[] { GameContentRecordCapabilities.Weapon },
                    runtimeImpact: GameContentReferenceRuntimeImpact.Rebind,
                    allowClear: false));
        }
    }
}
