using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Deucarian.GameContentAuthoring.Editor.Tests
{
    public sealed class GameContentCollectionEditingEditModeTests
    {
        private GameContentEditSessionCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            GameContentEditSessionCoordinator.ResetSharedForTests();
            _coordinator = new GameContentEditSessionCoordinator();
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator.Dispose();
            GameContentEditSessionCoordinator.ResetSharedForTests();
        }

        [Test]
        public void CollectionDescriptors_ValidateKindsCountsDuplicatesAndImmutableValueIdentity()
        {
            GameContentFieldDescriptor field = ScalarCollectionField(minimum: 1, maximum: 2, allowDuplicates: false);
            GameContentOrderedCollectionValue first = ScalarCollection("alpha", "beta");
            GameContentOrderedCollectionValue sameContentWithFreshKeys = ScalarCollection("alpha", "beta");
            GameContentOrderedCollectionValue duplicate = ScalarCollection("alpha", "alpha");
            GameContentOrderedCollectionValue empty = ScalarCollection();

            Assert.That(field.IsValid, Is.True);
            Assert.That(field.Collection.IsValidFor(GameContentFieldType.OrderedScalarCollection), Is.True);
            Assert.That(first.Equals(sameContentWithFreshKeys), Is.True, "Session keys must not affect persisted equality.");
            Assert.That(first.GetHashCode(), Is.EqualTo(sameContentWithFreshKeys.GetHashCode()));
            Assert.That(first.Items[0].ItemKey, Is.Not.EqualTo(sameContentWithFreshKeys.Items[0].ItemKey));
            Assert.That(field.Accepts(GameContentFieldValue.FromOrderedScalarCollection(first), out _), Is.True);
            Assert.That(field.Accepts(GameContentFieldValue.FromOrderedScalarCollection(duplicate), out string duplicateReason), Is.False);
            Assert.That(duplicateReason, Does.Contain("Duplicate"));
            Assert.That(field.Accepts(GameContentFieldValue.FromOrderedScalarCollection(empty), out string countReason), Is.False);
            Assert.That(countReason, Does.Contain("at least"));
            Assert.That(first.Items, Is.InstanceOf<IReadOnlyList<GameContentCollectionItem>>());

            GameContentFieldDescriptor duplicatesAllowed = ScalarCollectionField(
                minimum: 0,
                maximum: null,
                allowDuplicates: true);
            GameContentOrderedCollectionValue independentlyAddressable = ScalarCollection("same", "same");
            GameContentCollectionItemKey firstDuplicateKey = independentlyAddressable.Items[0].ItemKey;
            GameContentCollectionItemKey secondDuplicateKey = independentlyAddressable.Items[1].ItemKey;
            Assert.That(firstDuplicateKey, Is.Not.EqualTo(secondDuplicateKey));
            Assert.That(Apply(
                duplicatesAllowed,
                independentlyAddressable,
                GameContentCollectionOperation.Remove(firstDuplicateKey),
                out independentlyAddressable), Is.True);
            Assert.That(independentlyAddressable.Items.Single().ItemKey, Is.EqualTo(secondDuplicateKey));
            Assert.That(duplicatesAllowed.Accepts(
                GameContentFieldValue.FromOrderedScalarCollection(
                    new GameContentOrderedCollectionValue(GameContentFieldType.String, Array.Empty<GameContentCollectionItem>())),
                out _), Is.True, "Minimum count zero must allow an empty, non-null collection.");

            GameContentFieldDescriptor referenceField = ReferenceCollectionField();
            GameContentOrderedCollectionValue none = new GameContentOrderedCollectionValue(
                GameContentFieldType.RecordReference,
                new[]
                {
                    OriginalItem(0, GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.None()))
                });
            Assert.That(referenceField.IsValid, Is.True);
            Assert.That(referenceField.Accepts(
                GameContentFieldValue.FromOrderedRecordReferenceCollection(none), out string noneReason), Is.False);
            Assert.That(noneReason, Does.Contain("required"));

            Assert.Throws<ArgumentException>(() => new GameContentCollectionItem(
                GameContentCollectionItemKey.Create(),
                0,
                GameContentFieldValue.FromOrderedScalarCollection(first)));
        }

        [Test]
        public void CollectionMutation_AddRemoveMoveReplaceRejectsCraftedKeysAndRestoresOriginalOrder()
        {
            GameContentFieldDescriptor field = ScalarCollectionField(minimum: 1, maximum: 4, allowDuplicates: false);
            GameContentOrderedCollectionValue current = ScalarCollection("alpha", "beta", "gamma");
            GameContentCollectionItemKey alphaKey = current.Items[0].ItemKey;
            GameContentCollectionItemKey betaKey = current.Items[1].ItemKey;
            GameContentCollectionItemKey gammaKey = current.Items[2].ItemKey;

            GameContentCollectionOperation add = GameContentCollectionOperation.Add(GameContentFieldValue.FromString("delta"));
            Assert.That(add.ItemKey, Is.Null, "Add must not accept or expose a caller-created key.");
            Assert.That(Apply(field, current, add, out current), Is.True);
            GameContentCollectionItem added = current.Items[3];
            Assert.That(added.IsAdded, Is.True);
            Assert.That(added.ItemKey, Is.Not.Null);

            Assert.That(Apply(field, current, GameContentCollectionOperation.Move(gammaKey, 0), out current), Is.True);
            Assert.That(Values(current), Is.EqualTo(new[] { "gamma", "alpha", "beta", "delta" }));
            Assert.That(current.Items[0].ItemKey, Is.EqualTo(gammaKey));

            Assert.That(Apply(field, current, GameContentCollectionOperation.Replace(
                alphaKey,
                GameContentFieldValue.FromString("renamed")), out current), Is.True);
            Assert.That(current.Items[1].ItemKey, Is.EqualTo(alphaKey));
            Assert.That(current.Items[1].OriginalIndex, Is.Zero);
            Assert.That(Apply(field, current, GameContentCollectionOperation.Remove(betaKey), out current), Is.True);
            Assert.That(Values(current), Is.EqualTo(new[] { "gamma", "renamed", "delta" }));

            GameContentCollectionItemKey crafted = GameContentCollectionItemKey.Create();
            Assert.That(GameContentCollectionMutation.TryApply(
                field,
                current,
                GameContentCollectionOperation.Remove(crafted),
                out _,
                out string craftedReason), Is.False);
            Assert.That(craftedReason, Does.Contain("unknown"));

            IReadOnlyList<GameContentCollectionOperation> restore =
                GameContentCollectionMutation.BuildRestoreOriginalOrderOperations(current);
            Assert.That(restore, Is.Not.Empty);
            for (int i = 0; i < restore.Count; i++)
                Assert.That(Apply(field, current, restore[i], out current), Is.True);
            Assert.That(Values(current), Is.EqualTo(new[] { "renamed", "gamma", "delta" }));
            Assert.That(current.Items[0].ItemKey, Is.EqualTo(alphaKey));
            Assert.That(current.Items[1].ItemKey, Is.EqualTo(gammaKey));
        }

        [Test]
        public void CollectionReview_DerivesRealOperationsWithoutTreatingRemovalShiftsAsMoves()
        {
            GameContentFieldDescriptor field = ScalarCollectionField(minimum: 0, maximum: 5, allowDuplicates: false);
            GameContentOrderedCollectionValue original = ScalarCollection("alpha", "beta", "gamma");
            GameContentOrderedCollectionValue proposed = original;
            GameContentCollectionItemKey alpha = original.Items[0].ItemKey;
            GameContentCollectionItemKey beta = original.Items[1].ItemKey;
            GameContentCollectionItemKey gamma = original.Items[2].ItemKey;

            Apply(field, proposed, GameContentCollectionOperation.Remove(alpha), out proposed);
            Apply(field, proposed, GameContentCollectionOperation.Replace(
                beta,
                GameContentFieldValue.FromString("beta-2")), out proposed);
            Apply(field, proposed, GameContentCollectionOperation.Move(gamma, 0), out proposed);
            Apply(field, proposed, GameContentCollectionOperation.Add(
                GameContentFieldValue.FromString("delta")), out proposed);

            GameContentCollectionChangeReview review = GameContentCollectionChangeReview.Create(
                Key(CollectionFixtureProvider.PackId, "source"),
                "tags",
                original,
                proposed,
                GameContentReferenceRuntimeImpact.Refresh);

            Assert.That(review, Is.Not.Null);
            Assert.That(review.Changes.Count(change => change.Operation == GameContentCollectionOperationKind.Remove), Is.EqualTo(1));
            Assert.That(review.Changes.Count(change => change.Operation == GameContentCollectionOperationKind.Add), Is.EqualTo(1));
            Assert.That(review.Changes.Count(change => change.Operation == GameContentCollectionOperationKind.Replace), Is.EqualTo(1));
            Assert.That(review.Changes.Count(change => change.Operation == GameContentCollectionOperationKind.Move), Is.EqualTo(1));
            Assert.That(review.Changes.Single(change => change.Operation == GameContentCollectionOperationKind.Move).ItemKey,
                Is.EqualTo(gamma));
            Assert.That(review.Changes.Any(change => change.Operation == GameContentCollectionOperationKind.Move &&
                                                     change.ItemKey.Equals(beta)), Is.False);
            Assert.That(review.RuntimeImpact, Is.EqualTo(GameContentReferenceRuntimeImpact.Refresh));
        }

        [Test]
        public void Coordinator_MixesScalarReferenceAndCollectionHistoryThenCommitsAndRollsBack()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            GameContentCollectionItemKey originalTargetKey = Collection(active, "targets").Items[0].ItemKey;

            Assert.That(_coordinator.Apply(active, "name", GameContentFieldValue.FromString("Edited")).Succeeded, Is.True);
            Assert.That(_coordinator.Apply(active, "primary", Reference(provider.TargetB)).Succeeded, Is.True);
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("beta"))).Succeeded, Is.True);
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "targets",
                GameContentCollectionOperation.Add(Reference(provider.TargetB))).Succeeded, Is.True);
            GameContentCollectionItemKey addedTargetKey = Collection(active, "targets").Items[1].ItemKey;

            Assert.That(provider.Source.Name, Is.EqualTo("Fixture"));
            Assert.That(provider.Source.Tags, Is.EqualTo(new[] { "alpha" }));
            Assert.That(active.Changes.Select(change => change.FieldId),
                Is.EqualTo(new[] { "name", "primary", "tags", "targets" }));
            Assert.That(Collection(active, "targets").Items[0].ItemKey, Is.EqualTo(originalTargetKey));
            Assert.That(active.Validation.CanCommit, Is.True);
            Assert.That(provider.PreviewCount, Is.GreaterThanOrEqualTo(2));

            Assert.That(_coordinator.Undo(active).Succeeded, Is.True);
            Assert.That(Collection(active, "targets").Count, Is.EqualTo(1));
            Assert.That(_coordinator.Redo(active).Succeeded, Is.True);
            Assert.That(Collection(active, "targets").Count, Is.EqualTo(2));
            Assert.That(Collection(active, "targets").Items[0].ItemKey, Is.EqualTo(originalTargetKey));
            Assert.That(Collection(active, "targets").Items[1].ItemKey, Is.EqualTo(addedTargetKey));

            GameContentCommitResult commit = _coordinator.Commit(active, true);
            Assert.That(commit.Succeeded, Is.True);
            Assert.That(provider.Source.Name, Is.EqualTo("Edited"));
            Assert.That(provider.Source.PrimaryTarget, Is.EqualTo(provider.TargetB.CanonicalKey));
            Assert.That(provider.Source.Tags, Is.EqualTo(new[] { "alpha", "beta" }));
            Assert.That(provider.Source.Targets, Is.EqualTo(new[]
            {
                provider.TargetA.CanonicalKey,
                provider.TargetB.CanonicalKey
            }));

            GameContentRollbackResult rollback = _coordinator.Rollback(active);
            Assert.That(rollback.Succeeded, Is.True);
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture"));
            Assert.That(provider.Source.Tags, Is.EqualTo(new[] { "alpha" }));
            Assert.That(provider.Source.Targets, Is.EqualTo(new[] { provider.TargetA.CanonicalKey }));
            Assert.That(provider.PostCommitValidationCount, Is.EqualTo(1));
            Assert.That(provider.PostRollbackValidationCount, Is.EqualTo(1));
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);

            active = Begin(provider).Session;
            Assert.That(Collection(active, "targets").Items[0].ItemKey, Is.Not.EqualTo(originalTargetKey),
                "Collection keys must be regenerated when a source session is reloaded.");
            Assert.That(_coordinator.Cancel(active).Succeeded, Is.True);
        }

        [Test]
        public void Coordinator_RejectsLimitsDuplicatesWrongTypesTargetsAndCraftedKeysBeforeProvider()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            int initialApplyCount = provider.CollectionApplyCount;

            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("alpha"))).Succeeded, Is.False);
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromInteger(2))).Succeeded, Is.False);
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Remove(GameContentCollectionItemKey.Create())).Succeeded, Is.False);
            Assert.That(provider.CollectionApplyCount, Is.EqualTo(initialApplyCount));

            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("beta"))).Succeeded, Is.True);
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("gamma"))).Succeeded, Is.True);
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("delta"))).Succeeded, Is.False);

            while (Collection(active, "tags").Count > 1)
            {
                GameContentCollectionItemKey key = Collection(active, "tags").Items[0].ItemKey;
                Assert.That(_coordinator.ApplyCollectionOperation(
                    active,
                    "tags",
                    GameContentCollectionOperation.Remove(key)).Succeeded, Is.True);
            }
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Remove(Collection(active, "tags").Items[0].ItemKey)).Succeeded, Is.False);

            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "targets",
                GameContentCollectionOperation.Add(Reference(provider.TargetA))).Succeeded, Is.False);
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "targets",
                GameContentCollectionOperation.Add(Reference(provider.WrongCapability))).Succeeded, Is.False);
            GameContentRecordKey crossPack = new GameContentRecordKey(
                CollectionFixtureProvider.OwnerId,
                "another-pack",
                "target.cross-pack",
                CollectionFixtureProvider.SourceId,
                "target.cross-pack");
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "targets",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromRecordReference(
                    GameContentRecordReferenceValue.Resolved(crossPack)))).Succeeded, Is.False);
        }

        [Test]
        public void ReferenceCollectionCandidates_FilterPackCapabilitiesDuplicatesAndReplacementContext()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            GameContentCollectionItemKey currentKey = Collection(active, "targets").Items[0].ItemKey;

            GameContentReferenceCandidateSet addCandidates = _coordinator.GetReferenceCandidates(active, "targets");
            Assert.That(addCandidates.Candidates.Select(candidate => candidate.Record.SourceRecordId),
                Is.EqualTo(new[] { "target.b" }));
            Assert.That(addCandidates.Rejections.Single(rejection =>
                    rejection.TargetKey.SourceRecordId == "target.a").Reason,
                Does.Contain("already present"));
            Assert.That(addCandidates.Rejections.Select(rejection => rejection.TargetKey.SourceRecordId),
                Does.Contain("target.wrong"));
            Assert.That(addCandidates.Rejections.Select(rejection => rejection.TargetKey.SourceRecordId),
                Does.Contain("target.invalid"));

            GameContentReferenceCandidateSet replacementCandidates = _coordinator.GetReferenceCandidates(
                active,
                "targets",
                currentKey);
            Assert.That(replacementCandidates.Candidates.Select(candidate => candidate.Record.SourceRecordId),
                Is.EqualTo(new[] { "target.a", "target.b" }));
        }

        [Test]
        public void TargetDisappearanceCapabilityChangeAndBrokenItemsBlockPreviewAndCommit()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "targets",
                GameContentCollectionOperation.Add(Reference(provider.TargetB))).Succeeded, Is.True);
            provider.RemoveTarget("target.b");
            _coordinator.Reconcile(GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { provider }));

            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
            Assert.That(_coordinator.Commit(active, true).Succeeded, Is.False);
            Assert.That(provider.Source.Targets, Is.EqualTo(new[] { provider.TargetA.CanonicalKey }));
            _coordinator.Cancel(active);

            provider = NewProvider();
            active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "targets",
                GameContentCollectionOperation.Add(Reference(provider.TargetB))).Succeeded, Is.True);
            provider.RemoveCapability("target.b", GameContentRecordCapabilities.Weapon);
            _coordinator.Reconcile(GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { provider }));
            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
            _coordinator.Cancel(active);

            provider = NewProvider();
            provider.Source.Targets = new List<GameContentRecordKey>
            {
                new GameContentRecordKey(
                    CollectionFixtureProvider.OwnerId,
                    CollectionFixtureProvider.PackId,
                    "target.missing",
                    CollectionFixtureProvider.SourceId,
                    "target.missing")
            };
            active = Begin(provider).Session;
            GameContentFieldValue brokenValue = Collection(active, "targets").Items[0].Value;
            Assert.That(brokenValue.RecordReferenceValue.IsBroken, Is.True);
            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
        }

        [Test]
        public void StaleSourceBlocksCollectionMutationUndoAndCommit()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("beta"))).Succeeded, Is.True);
            provider.Source.Revision++;

            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("gamma"))).Succeeded, Is.False);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Stale));
            Assert.That(_coordinator.Undo(active).Succeeded, Is.False);
            Assert.That(_coordinator.Commit(active, true).Succeeded, Is.False);
            Assert.That(provider.Source.Tags, Is.EqualTo(new[] { "alpha" }));
        }

        [Test]
        public void CancelSourceLockAndProviderExceptionsRemainContained()
        {
            var provider = NewProvider();
            GameContentPackContext context = Select(provider);
            GameContentEditBeginResult first = _coordinator.BeginEdit(context, provider.SourceRecord, "weapon");
            Assert.That(first.Succeeded, Is.True);
            Assert.That(_coordinator.ApplyCollectionOperation(
                first.Session,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("beta"))).Succeeded, Is.True);

            GameContentEditBeginResult attached = _coordinator.BeginEdit(context, provider.SourceRecord, "upgrade");
            GameContentEditBeginResult blocked = _coordinator.BeginEdit(context, provider.SecondRecord, "weapon");
            Assert.That(attached.AttachedExisting, Is.True);
            Assert.That(attached.Session, Is.SameAs(first.Session));
            Assert.That(blocked.Succeeded, Is.False);
            Assert.That(blocked.Message, Does.Contain("physical source"));
            Assert.That(_coordinator.Cancel(first.Session).Succeeded, Is.True);
            Assert.That(provider.Source.Tags, Is.EqualTo(new[] { "alpha" }));
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);

            provider = NewProvider();
            provider.ThrowCollectionOperation = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            GameContentEditOperationResult exploded = _coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("beta")));
            Assert.That(exploded.Succeeded, Is.False);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Conflict));
            Assert.That(provider.Source.Tags, Is.EqualTo(new[] { "alpha" }));
            _coordinator.Cancel(active);

            provider = NewProvider();
            provider.ThrowPreview = true;
            active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("beta"))).Succeeded, Is.True);
            Assert.That(active.Validation.CanCommit, Is.False);
            Assert.That(active.Validation.Issues.Single().Message, Does.Contain("preview failed"));
            _coordinator.Cancel(active);

            provider = NewProvider();
            provider.EmitDomainError = true;
            active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("beta"))).Succeeded, Is.True);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Dirty));
            Assert.That(active.Validation.CanCommit, Is.False);
            Assert.That(active.Validation.Issues.Any(issue => issue.Message.Contains("provider-domain error")), Is.True);
            Assert.That(_coordinator.Commit(active, true).Succeeded, Is.False);
            Assert.That(provider.Source.Tags, Is.EqualTo(new[] { "alpha" }));
            provider.EmitDomainError = false;
            _coordinator.Cancel(active);

            provider = NewProvider();
            active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("beta"))).Succeeded, Is.True);
            _coordinator.Dispose();
            Assert.That(provider.Source.Tags, Is.EqualTo(new[] { "alpha" }));
            Assert.That(provider.RollbackCount, Is.EqualTo(1));
            _coordinator = new GameContentEditSessionCoordinator();
        }

        [Test]
        public void OptionalContractAllPacksAndProjectContentBoundariesRemainUnchanged()
        {
            var provider = NewProvider();
            provider.OmitCollectionContract = true;
            GameContentEditBeginResult missingContract = _coordinator.BeginEdit(
                Select(provider),
                provider.SourceRecord);
            Assert.That(missingContract.Succeeded, Is.False);
            Assert.That(missingContract.Message, Does.Contain("collection-session contract"));

            provider = NewProvider();
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { provider });
            GameContentPackContext all = new GameContentPackSelectionState().Select(
                catalog,
                GameContentPackContext.AllPacksSelectionKey);
            Assert.That(_coordinator.GetAvailability(all, provider.SourceRecord).IsEditable, Is.False);
            Assert.That(_coordinator.BeginEdit(all, provider.SourceRecord).Succeeded, Is.False);

            var project = new ProjectContentFixtureProvider();
            GameContentPackContext projectContext = Select(project);
            GameContentEditAvailability availability = _coordinator.GetAvailability(
                projectContext,
                project.Record);
            Assert.That(projectContext.IsProjectContent, Is.True);
            Assert.That(availability.IsEditable, Is.False);
            Assert.That(availability.DisabledReason, Does.Contain("existing provider-owned editing surface"));
        }

        private GameContentEditBeginResult Begin(CollectionFixtureProvider provider)
        {
            return _coordinator.BeginEdit(Select(provider), provider.SourceRecord, "collection-fixture");
        }

        private static CollectionFixtureProvider NewProvider()
        {
            return new CollectionFixtureProvider(
                "com.deucarian.tests.collections." + Guid.NewGuid().ToString("N"));
        }

        private static GameContentPackContext Select(IGameContentAuthoringProvider provider)
        {
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new[] { provider });
            GameContentPackDescriptor pack = ((IGameContentPackProvider)provider).GetContentPacks().Single();
            return new GameContentPackSelectionState().Select(catalog, pack.StableKey);
        }

        private static GameContentOrderedCollectionValue Collection(
            GameContentActiveEditSession active,
            string fieldId)
        {
            return active.GetEffectiveValue(fieldId).OrderedCollectionValue;
        }

        private static GameContentFieldValue Reference(GameContentRecordDescriptor record)
        {
            return GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.Resolved(
                record.CanonicalKey,
                record.DisplayName,
                record.SourcePath));
        }

        private static GameContentFieldDescriptor ScalarCollectionField(
            int minimum,
            int? maximum,
            bool allowDuplicates)
        {
            return new GameContentFieldDescriptor(
                "tags",
                "fixture.tags",
                "Tags",
                string.Empty,
                GameContentFieldType.OrderedScalarCollection,
                order: 20,
                group: "Collections",
                required: minimum > 0,
                collection: new GameContentCollectionFieldDescriptor(
                    new GameContentFieldDescriptor(
                        "tags.item",
                        "fixture.tags.item",
                        "Tag",
                        string.Empty,
                        GameContentFieldType.String,
                        required: true,
                        minimumLength: 1,
                        maximumLength: 24),
                    minimum,
                    maximum,
                    allowDuplicates,
                    "Tag order is significant.",
                    GameContentReferenceRuntimeImpact.Refresh));
        }

        private static GameContentFieldDescriptor ReferenceCollectionField()
        {
            return new GameContentFieldDescriptor(
                "targets",
                "fixture.targets",
                "Targets",
                string.Empty,
                GameContentFieldType.OrderedRecordReferenceCollection,
                order: 30,
                group: "Collections",
                required: true,
                collection: new GameContentCollectionFieldDescriptor(
                    new GameContentFieldDescriptor(
                        "targets.item",
                        "fixture.targets.item",
                        "Target",
                        string.Empty,
                        GameContentFieldType.RecordReference,
                        required: true,
                        recordReference: new GameContentRecordReferenceFieldDescriptor(
                            "Weapon",
                            new[] { GameContentRecordCapabilities.Weapon },
                            runtimeImpact: GameContentReferenceRuntimeImpact.Rebind,
                            allowClear: false)),
                    1,
                    3,
                    false,
                    "Target order is significant.",
                    GameContentReferenceRuntimeImpact.Refresh));
        }

        private static GameContentOrderedCollectionValue ScalarCollection(params string[] values)
        {
            return new GameContentOrderedCollectionValue(
                GameContentFieldType.String,
                (values ?? Array.Empty<string>())
                    .Select((value, index) => OriginalItem(index, GameContentFieldValue.FromString(value)))
                    .ToArray());
        }

        private static GameContentCollectionItem OriginalItem(int index, GameContentFieldValue value)
        {
            return new GameContentCollectionItem(GameContentCollectionItemKey.Create(), index, value);
        }

        private static bool Apply(
            GameContentFieldDescriptor field,
            GameContentOrderedCollectionValue current,
            GameContentCollectionOperation operation,
            out GameContentOrderedCollectionValue proposed)
        {
            return GameContentCollectionMutation.TryApply(field, current, operation, out proposed, out _);
        }

        private static string[] Values(GameContentOrderedCollectionValue value)
        {
            return value.Items.Select(item => item.Value.StringValue).ToArray();
        }

        private static GameContentRecordKey Key(string packId, string id)
        {
            return new GameContentRecordKey(
                CollectionFixtureProvider.OwnerId,
                packId,
                id,
                CollectionFixtureProvider.SourceId,
                id);
        }

        private sealed class FixtureSource
        {
            public string Name = "Fixture";
            public GameContentRecordKey PrimaryTarget;
            public List<string> Tags = new List<string> { "alpha" };
            public List<GameContentRecordKey> Targets = new List<GameContentRecordKey>();
            public int Revision;

            public FixtureSource Clone()
            {
                return new FixtureSource
                {
                    Name = Name,
                    PrimaryTarget = PrimaryTarget,
                    Tags = new List<string>(Tags),
                    Targets = new List<GameContentRecordKey>(Targets),
                    Revision = Revision
                };
            }

            public void CopyFrom(FixtureSource source)
            {
                Name = source.Name;
                PrimaryTarget = source.PrimaryTarget;
                Tags = new List<string>(source.Tags);
                Targets = new List<GameContentRecordKey>(source.Targets);
                Revision = source.Revision;
            }
        }

        private sealed class CollectionFixtureProvider :
            IGameContentAuthoringProvider,
            IGameContentPackProvider,
            IGameContentPackEditProvider
        {
            public const string OwnerId = "com.deucarian.tests.collections";
            public const string PackId = "collection-fixture";
            public const string SourceId = "fixture-source";
            private readonly List<GameContentRecordDescriptor> _records;
            private readonly GameContentSourceTarget _sourceTarget;

            public CollectionFixtureProvider(string providerId)
            {
                ProviderId = providerId;
                _sourceTarget = new GameContentSourceTarget(
                    "collection-source::" + providerId,
                    "Collection fixture source",
                    "Test memory only",
                    SourceId);
                Pack = CreatePack(
                    PackId,
                    OwnerId,
                    ProviderId,
                    "Collection Fixture",
                    GameContentPackAccessDescriptor.WritableProjectContent,
                    6);
                SourceRecord = CreateRecord("source", new[] { GameContentRecordCapabilities.Upgrade });
                SecondRecord = CreateRecord("source.second", new[] { GameContentRecordCapabilities.Upgrade });
                TargetA = CreateRecord("target.a", new[] { GameContentRecordCapabilities.Weapon });
                TargetB = CreateRecord("target.b", new[] { GameContentRecordCapabilities.Weapon });
                WrongCapability = CreateRecord("target.wrong", new[] { GameContentRecordCapabilities.Upgrade });
                InvalidTarget = CreateRecord(
                    "target.invalid",
                    new[] { GameContentRecordCapabilities.Weapon },
                    new GameContentAuthoringValidationResult(new[]
                    {
                        GameContentAuthoringValidationIssue.Error("target.invalid", "Invalid target fixture.")
                    }));
                _records = new List<GameContentRecordDescriptor>
                {
                    SourceRecord,
                    TargetB,
                    WrongCapability,
                    InvalidTarget,
                    TargetA,
                    SecondRecord
                };
                Source.PrimaryTarget = TargetA.CanonicalKey;
                Source.Targets.Add(TargetA.CanonicalKey);
            }

            public string ProviderId { get; }
            public string DisplayName => "Collection Fixture";
            public string Description => string.Empty;
            public int SortOrder => 0;
            public bool Enabled => true;
            public GameContentPackDescriptor Pack { get; }
            public GameContentRecordDescriptor SourceRecord { get; }
            public GameContentRecordDescriptor SecondRecord { get; }
            public GameContentRecordDescriptor TargetA { get; private set; }
            public GameContentRecordDescriptor TargetB { get; private set; }
            public GameContentRecordDescriptor WrongCapability { get; }
            public GameContentRecordDescriptor InvalidTarget { get; }
            public FixtureSource Source { get; } = new FixtureSource();
            public int CollectionApplyCount { get; set; }
            public int PreviewCount { get; set; }
            public bool ThrowCollectionOperation { get; set; }
            public bool ThrowPreview { get; set; }
            public bool EmitDomainError { get; set; }
            public int PostCommitValidationCount { get; set; }
            public int PostRollbackValidationCount { get; set; }
            public int RollbackCount { get; set; }
            public bool OmitCollectionContract { get; set; }

            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }
            public IReadOnlyList<GameContentPackDescriptor> GetContentPacks() => new[] { Pack };
            public IReadOnlyList<GameContentRecordDescriptor> GetRecords(string packId) =>
                string.Equals(packId, Pack.PackId, StringComparison.OrdinalIgnoreCase)
                    ? _records.ToArray()
                    : Array.Empty<GameContentRecordDescriptor>();
            public GameContentAuthoringValidationResult ValidatePack(string packId) => GameContentAuthoringValidationResult.Valid;
            public GameContentActionResult ExecuteAction(string packId, string actionId) => GameContentActionResult.Success("ok");

            public GameContentEditAvailability CanEdit(GameContentEditRequest request)
            {
                bool supported = request.RecordKey.Equals(SourceRecord.CanonicalKey) ||
                                 request.RecordKey.Equals(SecondRecord.CanonicalKey);
                return supported
                    ? GameContentEditAvailability.Editable(ProviderId, 4, _sourceTarget)
                    : GameContentEditAvailability.ReadOnly("Unsupported fixture record.", ProviderId);
            }

            public IGameContentEditSession BeginEdit(GameContentEditRequest request)
            {
                var session = new CollectionFixtureSession(this, request.RecordKey, _sourceTarget);
                return OmitCollectionContract
                    ? new CollectionContractOmittingSession(session)
                    : session;
            }

            public GameContentRecordDescriptor ResolveFresh(GameContentRecordKey key)
            {
                return key == null ? null : _records.FirstOrDefault(record => record.CanonicalKey.Equals(key));
            }

            public void RemoveTarget(string id)
            {
                _records.RemoveAll(record => string.Equals(record.SourceRecordId, id, StringComparison.Ordinal));
            }

            public void RemoveCapability(string id, GameContentRecordCapability capability)
            {
                int index = _records.FindIndex(record => string.Equals(record.SourceRecordId, id, StringComparison.Ordinal));
                if (index < 0) return;
                GameContentRecordDescriptor current = _records[index];
                GameContentRecordDescriptor replacement = CreateRecord(
                    id,
                    current.Capabilities.Where(value => value != capability),
                    current.Validation);
                _records[index] = replacement;
                if (string.Equals(id, "target.a", StringComparison.Ordinal)) TargetA = replacement;
                if (string.Equals(id, "target.b", StringComparison.Ordinal)) TargetB = replacement;
            }

            private static GameContentRecordDescriptor CreateRecord(
                string id,
                IEnumerable<GameContentRecordCapability> capabilities,
                GameContentAuthoringValidationResult validation = null)
            {
                return new GameContentRecordDescriptor(
                    PackId + "::" + id,
                    id,
                    "content",
                    null,
                    id,
                    string.Empty,
                    string.Empty,
                    Array.Empty<GameContentMetadataDescriptor>(),
                    null,
                    "InMemory/Collections/" + id,
                    id,
                    Array.Empty<GameContentRecordReferenceDescriptor>(),
                    Array.Empty<GameContentRecordReferenceDescriptor>(),
                    validation ?? GameContentAuthoringValidationResult.Valid,
                    0,
                    null,
                    string.Empty,
                    Key(PackId, id),
                    capabilities);
            }
        }

        private sealed class CollectionFixtureSession :
            IGameContentEditSession,
            IGameContentOrderedCollectionEditSession,
            IGameContentRecordReferenceEditSession
        {
            private readonly CollectionFixtureProvider _provider;
            private readonly FixtureSource _original;
            private readonly List<Dictionary<string, GameContentFieldValue>> _history =
                new List<Dictionary<string, GameContentFieldValue>>();
            private int _historyIndex;

            public CollectionFixtureSession(
                CollectionFixtureProvider provider,
                GameContentRecordKey recordKey,
                GameContentSourceTarget sourceTarget)
            {
                _provider = provider;
                _original = provider.Source.Clone();
                BackendId = provider.ProviderId;
                RecordKey = recordKey;
                SourceTarget = sourceTarget;
                OriginalRevision = Revision(provider.Source.Revision);
                Fields = new[]
                {
                    new GameContentFieldDescriptor(
                        "name",
                        "fixture.name",
                        "Name",
                        string.Empty,
                        GameContentFieldType.String,
                        order: 0,
                        required: true,
                        minimumLength: 1),
                    new GameContentFieldDescriptor(
                        "primary",
                        "fixture.primary",
                        "Primary Weapon",
                        string.Empty,
                        GameContentFieldType.RecordReference,
                        order: 10,
                        required: true,
                        recordReference: ReferenceDescriptor()),
                    ScalarCollectionField(1, 3, false),
                    ReferenceCollectionField()
                };
                var baseline = BuildValues(provider.Source);
                _history.Add(baseline);
                Snapshot = new GameContentEditSnapshot(
                    RecordKey,
                    SourceTarget,
                    OriginalRevision,
                    baseline,
                    DateTime.UtcNow,
                    "collection-fixture-v1");
                State = GameContentEditSessionState.Clean;
            }

            public string BackendId { get; }
            public GameContentRecordKey RecordKey { get; }
            public GameContentSourceTarget SourceTarget { get; }
            public GameContentSourceRevision OriginalRevision { get; }
            public GameContentEditSessionState State { get; private set; }
            public GameContentEditSnapshot Snapshot { get; }
            public IReadOnlyList<GameContentFieldDescriptor> Fields { get; }
            public IReadOnlyList<GameContentProposedChange> Changes => BuildChanges();
            public bool CanUndo => _historyIndex > 0;
            public bool CanRedo => _historyIndex < _history.Count - 1;
            private Dictionary<string, GameContentFieldValue> Current => _history[_historyIndex];

            public GameContentEditOperationResult Apply(string fieldId, GameContentFieldValue value)
            {
                GameContentFieldDescriptor field = Fields.FirstOrDefault(candidate => candidate.FieldId == fieldId);
                if (field == null || field.FieldType.IsOrderedCollection())
                    return GameContentEditOperationResult.Failure("Unsupported scalar or reference field.");
                if (value == null || value.FieldType != field.FieldType)
                    return GameContentEditOperationResult.Failure("Wrong field type.");
                if (field.FieldType == GameContentFieldType.RecordReference)
                {
                    if (!field.Accepts(value, out string reason)) return GameContentEditOperationResult.Failure(reason);
                    GameContentReferenceEvaluation evaluation = EvaluateReferenceTarget(
                        fieldId,
                        value.RecordReferenceValue.TargetKey);
                    if (!evaluation.IsValid) return GameContentEditOperationResult.Failure(evaluation.Reason);
                }
                return Stage(field, value);
            }

            public GameContentEditOperationResult ApplyCollectionOperation(
                string fieldId,
                GameContentCollectionOperation operation)
            {
                _provider.CollectionApplyCount++;
                if (_provider.ThrowCollectionOperation) throw new InvalidOperationException("collection operation exploded");
                GameContentFieldDescriptor field = Fields.FirstOrDefault(candidate => candidate.FieldId == fieldId);
                if (field == null || !field.FieldType.IsOrderedCollection())
                    return GameContentEditOperationResult.Failure("Unsupported collection field.");
                if (!GameContentCollectionMutation.TryApply(
                        field,
                        Current[fieldId].OrderedCollectionValue,
                        operation,
                        out GameContentOrderedCollectionValue proposed,
                        out string reason))
                    return GameContentEditOperationResult.Failure(reason);

                if (field.FieldType == GameContentFieldType.OrderedRecordReferenceCollection &&
                    (operation.Kind == GameContentCollectionOperationKind.Add ||
                     operation.Kind == GameContentCollectionOperationKind.Replace))
                {
                    GameContentReferenceEvaluation evaluation = EvaluateReferenceTarget(
                        fieldId,
                        operation.Value.RecordReferenceValue.TargetKey);
                    if (!evaluation.IsValid) return GameContentEditOperationResult.Failure(evaluation.Reason);
                }
                return Stage(field, GameContentFieldValue.FromOrderedCollection(proposed));
            }

            public GameContentEditOperationResult Undo()
            {
                if (!CanUndo) return GameContentEditOperationResult.Failure("Nothing to undo.");
                _historyIndex--;
                RefreshState();
                return GameContentEditOperationResult.Success("Collection fixture change undone.");
            }

            public GameContentEditOperationResult Redo()
            {
                if (!CanRedo) return GameContentEditOperationResult.Failure("Nothing to redo.");
                Dictionary<string, GameContentFieldValue> proposed = _history[_historyIndex + 1];
                GameContentValidationPreview preview = Validate(proposed);
                if (!preview.CanCommit) return GameContentEditOperationResult.Failure("Redo target validation failed.");
                _historyIndex++;
                RefreshState();
                return GameContentEditOperationResult.Success("Collection fixture change redone.");
            }

            public GameContentValidationPreview Preview()
            {
                _provider.PreviewCount++;
                if (_provider.ThrowPreview) throw new InvalidOperationException("preview exploded");
                return Validate(Current);
            }

            public GameContentStaleCheckResult CheckStale()
            {
                GameContentSourceRevision current = Revision(_provider.Source.Revision);
                if (current.Equals(OriginalRevision)) return GameContentStaleCheckResult.Current(current);
                State = GameContentEditSessionState.Stale;
                return GameContentStaleCheckResult.Stale("Collection fixture source changed.", current);
            }

            public GameContentCommitResult Commit(bool confirmWarnings)
            {
                GameContentStaleCheckResult stale = CheckStale();
                if (stale.IsStale) return GameContentCommitResult.Failure(stale.Message, OriginalRevision);
                GameContentValidationPreview preview = Preview();
                if (!preview.CanCommit) return GameContentCommitResult.Failure("Validation failed.", OriginalRevision);

                _provider.Source.Name = Current["name"].StringValue;
                _provider.Source.PrimaryTarget = Current["primary"].RecordReferenceValue.TargetKey;
                _provider.Source.Tags = Current["tags"].OrderedCollectionValue.Items
                    .Select(item => item.Value.StringValue)
                    .ToList();
                _provider.Source.Targets = Current["targets"].OrderedCollectionValue.Items
                    .Select(item => item.Value.RecordReferenceValue.TargetKey)
                    .ToList();
                _provider.Source.Revision++;
                _provider.PostCommitValidationCount++;
                if (!Validate(BuildValues(_provider.Source)).CanCommit)
                    return GameContentCommitResult.Failure("Persisted fixture validation failed.", OriginalRevision);
                State = GameContentEditSessionState.Committed;
                return new GameContentCommitResult(
                    true,
                    "Collection fixture committed.",
                    OriginalRevision,
                    Revision(_provider.Source.Revision),
                    true,
                    true,
                    false);
            }

            public GameContentRollbackResult Rollback()
            {
                _provider.RollbackCount++;
                _provider.Source.CopyFrom(_original);
                _provider.PostRollbackValidationCount++;
                if (!Validate(BuildValues(_provider.Source)).CanCommit)
                    return GameContentRollbackResult.Failure("Restored fixture validation failed.", OriginalRevision);
                State = GameContentEditSessionState.RolledBack;
                return new GameContentRollbackResult(true, "Collection fixture restored.", OriginalRevision);
            }

            public GameContentReferenceEvaluation EvaluateReferenceTarget(
                string fieldId,
                GameContentRecordKey targetKey)
            {
                if (!string.Equals(fieldId, "primary", StringComparison.Ordinal) &&
                    !string.Equals(fieldId, "targets", StringComparison.Ordinal))
                    return GameContentReferenceEvaluation.Rejected(targetKey, "Unknown reference field.");
                GameContentRecordDescriptor target = _provider.ResolveFresh(targetKey);
                if (target == null)
                    return GameContentReferenceEvaluation.Rejected(
                        targetKey,
                        "The fresh target no longer exists.",
                        sourceClaimValid: false);
                if (!target.HasCapability(GameContentRecordCapabilities.Weapon))
                {
                    return GameContentReferenceEvaluation.Rejected(
                        targetKey,
                        "The fresh target lacks the Weapon capability.",
                        requiredCapabilitiesSatisfied: false);
                }
                if (!target.Validation.IsValid || target.HasBrokenReferences)
                {
                    return GameContentReferenceEvaluation.Rejected(
                        targetKey,
                        "The fresh target is invalid.",
                        validationState: GameContentEditValidationState.Invalid);
                }
                return GameContentReferenceEvaluation.Approved(
                    targetKey,
                    GameContentReferenceRuntimeImpact.Rebind);
            }

            public void Dispose() { }

            private GameContentEditOperationResult Stage(
                GameContentFieldDescriptor field,
                GameContentFieldValue value)
            {
                if (Current[field.FieldId].Equals(value)) return GameContentEditOperationResult.Success("No change.");
                if (CanRedo) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                var next = Current.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                next[field.FieldId] = value;
                _history.Add(next);
                _historyIndex++;
                RefreshState();
                return GameContentEditOperationResult.Success("Staged " + field.DisplayName + ".");
            }

            private GameContentValidationPreview Validate(IReadOnlyDictionary<string, GameContentFieldValue> values)
            {
                var issues = new List<GameContentAuthoringValidationIssue>();
                foreach (GameContentFieldDescriptor field in Fields)
                {
                    if (!field.Accepts(values[field.FieldId], out string reason))
                    {
                        issues.Add(GameContentAuthoringValidationIssue.Error(field.FieldId, reason));
                        continue;
                    }
                    if (field.FieldType == GameContentFieldType.RecordReference)
                        ValidateReference(field, values[field.FieldId].RecordReferenceValue, issues);
                    else if (field.FieldType == GameContentFieldType.OrderedRecordReferenceCollection)
                    {
                        foreach (GameContentCollectionItem item in values[field.FieldId].OrderedCollectionValue.Items)
                            ValidateReference(field, item.Value.RecordReferenceValue, issues);
                    }
                }
                if (_provider.EmitDomainError)
                    issues.Add(GameContentAuthoringValidationIssue.Error("fixture", "Configured provider-domain error."));
                return new GameContentValidationPreview(issues);
            }

            private void ValidateReference(
                GameContentFieldDescriptor field,
                GameContentRecordReferenceValue reference,
                ICollection<GameContentAuthoringValidationIssue> issues)
            {
                if (reference == null || !reference.IsResolved || reference.TargetKey == null)
                {
                    issues.Add(GameContentAuthoringValidationIssue.Error(field.FieldId, "A resolved target is required."));
                    return;
                }
                GameContentReferenceEvaluation evaluation = EvaluateReferenceTarget(field.FieldId, reference.TargetKey);
                if (!evaluation.IsValid)
                    issues.Add(GameContentAuthoringValidationIssue.Error(field.FieldId, evaluation.Reason));
            }

            private Dictionary<string, GameContentFieldValue> BuildValues(FixtureSource source)
            {
                GameContentRecordDescriptor primary = _provider.ResolveFresh(source.PrimaryTarget);
                GameContentRecordReferenceValue primaryValue = primary == null
                    ? GameContentRecordReferenceValue.Broken(
                        source.PrimaryTarget?.SourceRecordId ?? "missing",
                        "The primary target is missing.",
                        source.PrimaryTarget)
                    : GameContentRecordReferenceValue.Resolved(
                        source.PrimaryTarget,
                        primary.DisplayName,
                        primary.SourcePath);
                return new Dictionary<string, GameContentFieldValue>(StringComparer.Ordinal)
                {
                    ["name"] = GameContentFieldValue.FromString(source.Name),
                    ["primary"] = GameContentFieldValue.FromRecordReference(primaryValue),
                    ["tags"] = GameContentFieldValue.FromOrderedScalarCollection(
                        new GameContentOrderedCollectionValue(
                            GameContentFieldType.String,
                            source.Tags.Select((value, index) => OriginalItem(
                                index,
                                GameContentFieldValue.FromString(value))))),
                    ["targets"] = GameContentFieldValue.FromOrderedRecordReferenceCollection(
                        new GameContentOrderedCollectionValue(
                            GameContentFieldType.RecordReference,
                            source.Targets.Select((key, index) => OriginalItem(
                                index,
                                BuildReferenceValue(key)))))
                };
            }

            private GameContentFieldValue BuildReferenceValue(GameContentRecordKey key)
            {
                GameContentRecordDescriptor target = _provider.ResolveFresh(key);
                return GameContentFieldValue.FromRecordReference(target == null
                    ? GameContentRecordReferenceValue.Broken(
                        key?.SourceRecordId ?? "missing",
                        "The collection target is missing.",
                        key)
                    : GameContentRecordReferenceValue.Resolved(
                        key,
                        target.DisplayName,
                        target.SourcePath));
            }

            private IReadOnlyList<GameContentProposedChange> BuildChanges()
            {
                var changes = new List<GameContentProposedChange>();
                foreach (GameContentFieldDescriptor field in Fields.OrderBy(value => value.Order).ThenBy(value => value.FieldId))
                {
                    GameContentFieldValue oldValue = Snapshot.FieldValues[field.FieldId];
                    GameContentFieldValue proposedValue = Current[field.FieldId];
                    if (oldValue.Equals(proposedValue)) continue;
                    changes.Add(new GameContentProposedChange(
                        field.FieldId,
                        oldValue,
                        proposedValue,
                        field.DisplayName,
                        field.Group,
                        field.Order));
                }
                return changes;
            }

            private void RefreshState()
            {
                State = Changes.Count == 0 ? GameContentEditSessionState.Clean : GameContentEditSessionState.Dirty;
            }

            private static GameContentRecordReferenceFieldDescriptor ReferenceDescriptor()
            {
                return new GameContentRecordReferenceFieldDescriptor(
                    "Weapon",
                    new[] { GameContentRecordCapabilities.Weapon },
                    runtimeImpact: GameContentReferenceRuntimeImpact.Refresh |
                                   GameContentReferenceRuntimeImpact.Rebind,
                    allowClear: false);
            }

            private static GameContentSourceRevision Revision(int revision)
            {
                return new GameContentSourceRevision("collection-revision-" + revision);
            }
        }

        private sealed class CollectionContractOmittingSession :
            IGameContentEditSession,
            IGameContentRecordReferenceEditSession
        {
            private readonly CollectionFixtureSession _inner;

            public CollectionContractOmittingSession(CollectionFixtureSession inner)
            {
                _inner = inner;
            }

            public string BackendId => _inner.BackendId;
            public GameContentRecordKey RecordKey => _inner.RecordKey;
            public GameContentSourceTarget SourceTarget => _inner.SourceTarget;
            public GameContentSourceRevision OriginalRevision => _inner.OriginalRevision;
            public GameContentEditSessionState State => _inner.State;
            public GameContentEditSnapshot Snapshot => _inner.Snapshot;
            public IReadOnlyList<GameContentFieldDescriptor> Fields => _inner.Fields;
            public IReadOnlyList<GameContentProposedChange> Changes => _inner.Changes;
            public bool CanUndo => _inner.CanUndo;
            public bool CanRedo => _inner.CanRedo;
            public GameContentEditOperationResult Apply(string fieldId, GameContentFieldValue value) => _inner.Apply(fieldId, value);
            public GameContentEditOperationResult Undo() => _inner.Undo();
            public GameContentEditOperationResult Redo() => _inner.Redo();
            public GameContentValidationPreview Preview() => _inner.Preview();
            public GameContentStaleCheckResult CheckStale() => _inner.CheckStale();
            public GameContentCommitResult Commit(bool confirmWarnings) => _inner.Commit(confirmWarnings);
            public GameContentRollbackResult Rollback() => _inner.Rollback();
            public GameContentReferenceEvaluation EvaluateReferenceTarget(string fieldId, GameContentRecordKey targetKey) =>
                _inner.EvaluateReferenceTarget(fieldId, targetKey);
            public void Dispose() => _inner.Dispose();
        }

        private sealed class ProjectContentFixtureProvider :
            IGameContentAuthoringProvider,
            IGameContentPackProvider
        {
            public ProjectContentFixtureProvider()
            {
                Pack = CreatePack(
                    GameContentProjectPackProjection.PackId,
                    GameContentProjectPackProjection.OwningPackageId,
                    ProviderId,
                    "Project Content",
                    GameContentPackAccessDescriptor.WritableProjectContent,
                    1);
                Record = new GameContentRecordDescriptor(
                    Pack.PackId + "::project.record",
                    "project.record",
                    "content",
                    null,
                    "Project Record",
                    string.Empty,
                    string.Empty,
                    Array.Empty<GameContentMetadataDescriptor>(),
                    null,
                    "Assets/GameContent/Project.asset",
                    "project.record",
                    Array.Empty<GameContentRecordReferenceDescriptor>(),
                    Array.Empty<GameContentRecordReferenceDescriptor>(),
                    GameContentAuthoringValidationResult.Valid,
                    0,
                    null,
                    string.Empty,
                    new GameContentRecordKey(
                        Pack.OwningPackageId,
                        Pack.PackId,
                        "project.record",
                        "project-source",
                        "project.record"),
                    Array.Empty<GameContentRecordCapability>());
            }

            public string ProviderId => "com.deucarian.tests.project-content";
            public string DisplayName => "Project Content Fixture";
            public string Description => string.Empty;
            public int SortOrder => 0;
            public bool Enabled => true;
            public GameContentPackDescriptor Pack { get; }
            public GameContentRecordDescriptor Record { get; }
            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }
            public IReadOnlyList<GameContentPackDescriptor> GetContentPacks() => new[] { Pack };
            public IReadOnlyList<GameContentRecordDescriptor> GetRecords(string packId) => new[] { Record };
            public GameContentAuthoringValidationResult ValidatePack(string packId) => GameContentAuthoringValidationResult.Valid;
            public GameContentActionResult ExecuteAction(string packId, string actionId) => GameContentActionResult.Success("ok");
        }

        private static GameContentPackDescriptor CreatePack(
            string packId,
            string ownerId,
            string providerId,
            string displayName,
            GameContentPackAccessDescriptor access,
            int recordCount)
        {
            return new GameContentPackDescriptor(
                packId,
                ownerId,
                providerId,
                displayName,
                string.Empty,
                "1",
                Array.Empty<string>(),
                GameContentPackSourceKind.Project,
                GameContentPackSourceState.Available,
                "InMemory/Collections",
                null,
                null,
                null,
                null,
                null,
                Array.Empty<GameContentCategoryDescriptor>(),
                Array.Empty<GameContentActionDescriptor>(),
                GameContentAuthoringValidationResult.Valid,
                recordCount,
                access);
        }
    }
}
