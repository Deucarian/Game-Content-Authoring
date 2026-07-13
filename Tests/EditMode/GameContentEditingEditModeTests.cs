using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.GameContentAuthoring.Editor;
using NUnit.Framework;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentEditingEditModeTests
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
            _coordinator?.Dispose();
            GameContentEditSessionCoordinator.ResetSharedForTests();
        }

        [Test]
        public void OptionalEditProvider_UsesExistingProviderRegistryAndDuplicateIdsRemainRejected()
        {
            string providerId = "com.deucarian.tests.edit." + Guid.NewGuid().ToString("N");
            var first = new InMemoryEditPackProvider(providerId);
            var duplicate = new InMemoryEditPackProvider(providerId);

            GameContentAuthoringProviderRegistry.Register(first);
            GameContentAuthoringProviderRegistry.Register(duplicate);

            Assert.That(GameContentAuthoringProviderRegistry.FindProvider(providerId), Is.SameAs(first));
            Assert.That(GameContentAuthoringProviderRegistry.Providers.Count(value => value.ProviderId == providerId), Is.EqualTo(1));
            Assert.That(GameContentAuthoringProviderRegistry.FindProvider(providerId), Is.InstanceOf<IGameContentPackEditProvider>());
            Assert.That(typeof(GameContentEditSessionCoordinator).Assembly.GetTypes()
                .Any(type => type.Name == "GameContentEditBackendRegistry"), Is.False);
        }

        [Test]
        public void ProviderWithoutEditInterface_RemainsReadOnly()
        {
            var provider = new ReadOnlyPackProvider("com.deucarian.tests.readonly." + Guid.NewGuid().ToString("N"));
            GameContentPackContext context = Select(provider);

            GameContentEditAvailability availability = _coordinator.GetAvailability(context, provider.Record, "attack");

            Assert.That(availability.IsEditable, Is.False);
            Assert.That(availability.DisabledReason, Does.Contain("no safe editing backend"));
        }

        [Test]
        public void Availability_GatesAllPacksConflictsUnsupportedAndMissingRecords()
        {
            var provider = NewProvider();
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new[] { provider });
            var selection = new GameContentPackSelectionState();
            GameContentPackContext selected = selection.Select(catalog, provider.Pack.StableKey);
            GameContentPackContext all = selection.Select(catalog, GameContentPackContext.AllPacksSelectionKey);

            Assert.That(_coordinator.GetAvailability(selected, provider.Record).IsEditable, Is.True);
            Assert.That(_coordinator.GetAvailability(all, provider.Record).DisabledReason, Does.Contain("All Packs"));
            Assert.That(_coordinator.BeginEdit(all, provider.Record).Succeeded, Is.False);
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);
            Assert.That(_coordinator.GetAvailability(selected, provider.SecondRecord).DisabledReason, Does.Contain("not supported"));
            Assert.That(_coordinator.GetAvailability(selected, Record("missing", provider.ProviderId)).DisabledReason, Does.Contain("Select a record"));

            var duplicate = new InMemoryEditPackProvider(
                "com.deucarian.tests.edit.duplicate." + Guid.NewGuid().ToString("N"),
                provider.Pack.PackId);
            GameContentPackCatalog conflictCatalog = GameContentPackCatalog.Build(
                new IGameContentAuthoringProvider[] { provider, duplicate });
            GameContentPackContext conflict = new GameContentPackSelectionState().Select(conflictCatalog, provider.Pack.StableKey);
            Assert.That(_coordinator.GetAvailability(conflict, provider.Record).DisabledReason, Does.Contain("conflict"));
        }

        [Test]
        public void BeginEdit_CapturesCleanDeterministicSnapshotAndSourceLock()
        {
            var provider = NewProvider();
            GameContentEditBeginResult begin = Begin(provider);

            Assert.That(begin.Succeeded, Is.True);
            Assert.That(begin.AttachedExisting, Is.False);
            Assert.That(begin.Session.State, Is.EqualTo(GameContentEditSessionState.Clean));
            Assert.That(begin.Session.RecordKey, Is.EqualTo(provider.Record.CanonicalKey));
            Assert.That(begin.Session.OriginalRevision.Token, Is.EqualTo("revision-0"));
            Assert.That(begin.Session.Snapshot.FieldValues["name"].StringValue, Is.EqualTo("Fixture Record"));
            Assert.That(begin.Session.Fields.Select(field => field.FieldId),
                Is.EqualTo(new[] { "id", "name", "count", "power", "enabled", "mode" }));
            Assert.That(_coordinator.ActiveSourceCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_StagesEverySupportedScalarAndRejectsUnknownTypeAndReadOnlyFields()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;

            Assert.That(_coordinator.Apply(active, "name", GameContentFieldValue.FromString("Edited")).Succeeded, Is.True);
            Assert.That(_coordinator.Apply(active, "count", GameContentFieldValue.FromInteger(7)).Succeeded, Is.True);
            Assert.That(_coordinator.Apply(active, "power", GameContentFieldValue.FromNumber(2.75d)).Succeeded, Is.True);
            Assert.That(_coordinator.Apply(active, "enabled", GameContentFieldValue.FromBoolean(false)).Succeeded, Is.True);
            Assert.That(_coordinator.Apply(active, "mode", GameContentFieldValue.FromEnum("advanced")).Succeeded, Is.True);
            Assert.That(_coordinator.Apply(active, "count", GameContentFieldValue.FromString("wrong")).Succeeded, Is.False);
            Assert.That(_coordinator.Apply(active, "unknown", GameContentFieldValue.FromString("nope")).Succeeded, Is.False);
            Assert.That(_coordinator.Apply(active, "id", GameContentFieldValue.FromString("changed-id")).Succeeded, Is.False);

            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Dirty));
            Assert.That(active.Changes.Select(change => change.FieldId),
                Is.EqualTo(new[] { "name", "count", "power", "enabled", "mode" }));
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
        }

        [Test]
        public void UndoRedo_PreservesOrderAndApplyingAfterUndoClearsRedoBranch()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("First"));
            _coordinator.Apply(active, "count", GameContentFieldValue.FromInteger(8));

            Assert.That(active.Changes.Select(change => change.FieldId), Is.EqualTo(new[] { "name", "count" }));
            Assert.That(_coordinator.Undo(active).Succeeded, Is.True);
            Assert.That(active.Changes.Select(change => change.FieldId), Is.EqualTo(new[] { "name" }));
            Assert.That(active.CanRedo, Is.True);
            Assert.That(_coordinator.Redo(active).Succeeded, Is.True);
            Assert.That(active.Changes.Select(change => change.FieldId), Is.EqualTo(new[] { "name", "count" }));
            _coordinator.Undo(active);
            _coordinator.Apply(active, "enabled", GameContentFieldValue.FromBoolean(false));

            Assert.That(active.CanRedo, Is.False);
            Assert.That(active.Changes.Select(change => change.FieldId), Is.EqualTo(new[] { "name", "enabled" }));
        }

        [Test]
        public void ApplyingOriginalValue_ClearsEffectiveChange()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Changed"));

            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Fixture Record"));

            Assert.That(active.Changes, Is.Empty);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Clean));
        }

        [Test]
        public void Cancel_DiscardsChangesLeavesSourceUntouchedAndReleasesLock()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Changed"));

            GameContentRollbackResult result = _coordinator.Cancel(active);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.RolledBack));
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);
            Assert.That(_coordinator.TryGetSession(provider.Record.CanonicalKey, out _), Is.False);
        }

        [Test]
        public void ValidationErrors_BlockCommitButRetainInvalidStagedValues()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "count", GameContentFieldValue.FromInteger(-4));

            GameContentValidationPreview preview = _coordinator.Preview(active);
            GameContentCommitResult commit = _coordinator.Commit(active, true);

            Assert.That(preview.State, Is.EqualTo(GameContentEditValidationState.Invalid));
            Assert.That(preview.CanCommit, Is.False);
            Assert.That(active.GetEffectiveValue("count").IntegerValue, Is.EqualTo(-4));
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Dirty));
            Assert.That(commit.Succeeded, Is.False);
            Assert.That(provider.Source.Count, Is.EqualTo(3));
        }

        [Test]
        public void WarningsRequireExplicitConfirmationBeforeCommit()
        {
            var provider = NewProvider();
            provider.EmitWarning = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Warning Value"));

            GameContentValidationPreview preview = _coordinator.Preview(active);
            GameContentCommitResult unconfirmed = _coordinator.Commit(active, false);
            GameContentCommitResult confirmed = _coordinator.Commit(active, true);

            Assert.That(preview.State, Is.EqualTo(GameContentEditValidationState.Warning));
            Assert.That(preview.RequiresWarningConfirmation, Is.True);
            Assert.That(unconfirmed.Succeeded, Is.False);
            Assert.That(confirmed.Succeeded, Is.True);
        }

        [Test]
        public void PreviewException_IsContainedAsInvalidValidation()
        {
            var provider = NewProvider();
            provider.ThrowPreview = true;
            GameContentActiveEditSession active = Begin(provider).Session;

            GameContentValidationPreview preview = _coordinator.Preview(active);

            Assert.That(preview.State, Is.EqualTo(GameContentEditValidationState.Invalid));
            Assert.That(preview.Issues.Single().Message, Does.Contain("preview exploded"));
        }

        [Test]
        public void FieldChangeAndRevisionOrdering_AreStableAcrossSessions()
        {
            var provider = NewProvider();
            GameContentActiveEditSession first = Begin(provider).Session;
            _coordinator.Apply(first, "mode", GameContentFieldValue.FromEnum("advanced"));
            _coordinator.Apply(first, "name", GameContentFieldValue.FromString("Stable"));
            string[] firstOrder = first.Changes.Select(change => change.FieldId).ToArray();
            _coordinator.Cancel(first);

            GameContentActiveEditSession second = Begin(provider).Session;
            _coordinator.Apply(second, "name", GameContentFieldValue.FromString("Stable"));
            _coordinator.Apply(second, "mode", GameContentFieldValue.FromEnum("advanced"));

            Assert.That(second.Fields.Select(field => field.FieldId),
                Is.EqualTo(new[] { "id", "name", "count", "power", "enabled", "mode" }));
            Assert.That(second.Changes.Select(change => change.FieldId), Is.EqualTo(firstOrder));
            Assert.That(second.OriginalRevision, Is.EqualTo(new GameContentSourceRevision("revision-0")));
        }

        [Test]
        public void ExternalRevisionChange_MarksSessionStaleAndBlocksCommit()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Changed"));
            provider.MutateRevisionExternally();

            GameContentStaleCheckResult stale = _coordinator.CheckStale(active);
            GameContentEditAvailability availability = _coordinator.GetAvailability(Select(provider), provider.Record);
            GameContentCommitResult commit = _coordinator.Commit(active, true);

            Assert.That(stale.IsStale, Is.True);
            Assert.That(availability.IsEditable, Is.False);
            Assert.That(availability.DisabledReason, Does.Contain("stale"));
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Stale));
            Assert.That(commit.Succeeded, Is.False);
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
        }

        [Test]
        public void SourceLock_AttachesSameCanonicalRecordAcrossLensesAndBlocksAnotherRecord()
        {
            var provider = NewProvider();
            provider.AllowSecondRecord = true;
            GameContentPackContext context = Select(provider);

            GameContentEditBeginResult attack = _coordinator.BeginEdit(context, provider.Record, "attack");
            GameContentEditBeginResult weapon = _coordinator.BeginEdit(context, provider.Record, "weapon");
            GameContentEditAvailability second = _coordinator.GetAvailability(context, provider.SecondRecord, "attack");

            Assert.That(weapon.Succeeded, Is.True);
            Assert.That(weapon.AttachedExisting, Is.True);
            Assert.That(weapon.Session, Is.SameAs(attack.Session));
            Assert.That(_coordinator.ActiveSourceCount, Is.EqualTo(1));
            Assert.That(second.IsEditable, Is.False);
            Assert.That(second.DisabledReason, Does.Contain("Finish or cancel"));
        }

        [Test]
        public void CommitSuccess_UpdatesSourceRevisionStateAndRefreshNotification()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            int refreshes = 0;
            _coordinator.RefreshRequested += () => refreshes++;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Committed"));

            GameContentCommitResult result = _coordinator.Commit(active, true);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.NewRevision.Token, Is.EqualTo("revision-1"));
            Assert.That(result.RequiresRefresh, Is.True);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Committed));
            Assert.That(provider.Source.Name, Is.EqualTo("Committed"));
            Assert.That(refreshes, Is.EqualTo(1));
        }

        [Test]
        public void CommitFailure_PreservesOriginalSourceAndDirtySession()
        {
            var provider = NewProvider();
            provider.FailCommit = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Not Saved"));

            GameContentCommitResult result = _coordinator.Commit(active, true);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Recovery, Is.Null);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Dirty));
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
        }

        [Test]
        public void CommitException_IsContainedAndProducesRecoveryRequired()
        {
            var provider = NewProvider();
            provider.ThrowCommit = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Not Saved"));

            GameContentCommitResult result = _coordinator.Commit(active, true);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Recovery, Is.Not.Null);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.RecoveryRequired));
            Assert.That(active.Message, Does.Contain("commit exploded"));
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
        }

        [Test]
        public void CommitStateRefreshException_RequiresRecoveryWithoutHidingCommittedSource()
        {
            var provider = NewProvider();
            provider.ThrowStateReadAfterCommit = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Committed"));

            GameContentCommitResult result = _coordinator.Commit(active, true);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.NewRevision.Token, Is.EqualTo("revision-1"));
            Assert.That(result.Recovery, Is.Not.Null);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.RecoveryRequired));
            Assert.That(provider.Source.Name, Is.EqualTo("Committed"));
            Assert.That(_coordinator.ActiveSourceCount, Is.EqualTo(1));
        }

        [Test]
        public void BackendRecoveryResult_IsRepresentedWithoutSourceLoss()
        {
            var provider = NewProvider();
            provider.FailCommit = true;
            provider.RequireRecoveryOnCommitFailure = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Not Saved"));

            GameContentCommitResult result = _coordinator.Commit(active, true);

            Assert.That(result.Recovery, Is.Not.Null);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.RecoveryRequired));
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
        }

        [Test]
        public void RollbackAfterCommit_RestoresOriginalFixtureSourceAndReleasesLock()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Committed"));
            _coordinator.Commit(active, true);

            GameContentRollbackResult rollback = _coordinator.Rollback(active);

            Assert.That(rollback.Succeeded, Is.True);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.RolledBack));
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);
        }

        [Test]
        public void RollbackFailure_ProducesRecoveryRequiredAndKeepsLock()
        {
            var provider = NewProvider();
            provider.FailRollback = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Changed"));

            GameContentRollbackResult rollback = _coordinator.Rollback(active);

            Assert.That(rollback.Succeeded, Is.False);
            Assert.That(rollback.Recovery, Is.Not.Null);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.RecoveryRequired));
            Assert.That(_coordinator.ActiveSourceCount, Is.EqualTo(1));
        }

        [Test]
        public void RollbackStateRefreshException_RequiresRecoveryAndKeepsLock()
        {
            var provider = NewProvider();
            provider.ThrowStateReadAfterRollback = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Changed"));

            GameContentRollbackResult rollback = _coordinator.Rollback(active);

            Assert.That(rollback.Succeeded, Is.False);
            Assert.That(rollback.Recovery, Is.Not.Null);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.RecoveryRequired));
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
            Assert.That(_coordinator.ActiveSourceCount, Is.EqualTo(1));
        }

        [Test]
        public void CoordinatorExposesCommittingStateAndBlocksMutationDuringCommit()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            GameContentEditOperationResult duringCommit = null;
            provider.CommitObserver = () =>
                duringCommit = _coordinator.Apply(active, "count", GameContentFieldValue.FromInteger(9));
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Committed"));

            GameContentCommitResult result = _coordinator.Commit(active, true);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(provider.StateObservedDuringCommit, Is.EqualTo(GameContentEditSessionState.Committing));
            Assert.That(duringCommit.Succeeded, Is.False);
        }

        [Test]
        public void DisposeAndReset_DoNotCommitAndReleaseAllTestLocks()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Never Commit"));

            _coordinator.Reset();

            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
            Assert.That(provider.CommitCount, Is.Zero);
            Assert.That(provider.RollbackCount, Is.EqualTo(1));
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);
        }

        [Test]
        public void ReconcileMissingProvider_DiscardsUncommittedSessionSafely()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "name", GameContentFieldValue.FromString("Never Commit"));

            _coordinator.Reconcile(GameContentPackCatalog.Build(Array.Empty<IGameContentAuthoringProvider>()));

            Assert.That(provider.Source.Name, Is.EqualTo("Fixture Record"));
            Assert.That(provider.RollbackCount, Is.EqualTo(1));
            Assert.That(_coordinator.TrackedSessionCount, Is.Zero);
        }

        [Test]
        public void ProviderAndSessionContractExceptions_AreContained()
        {
            var availabilityProvider = NewProvider();
            availabilityProvider.ThrowAvailability = true;
            GameContentEditAvailability availability = _coordinator.GetAvailability(
                Select(availabilityProvider),
                availabilityProvider.Record);

            var beginProvider = NewProvider();
            beginProvider.ThrowBegin = true;
            GameContentEditBeginResult begin = _coordinator.BeginEdit(
                Select(beginProvider),
                beginProvider.Record);

            var mismatchProvider = NewProvider();
            mismatchProvider.SessionBackendIdOverride = "wrong-backend";
            GameContentEditBeginResult mismatch = _coordinator.BeginEdit(
                Select(mismatchProvider),
                mismatchProvider.Record);

            Assert.That(availability.IsEditable, Is.False);
            Assert.That(availability.DisabledReason, Does.Contain("availability exploded"));
            Assert.That(begin.Succeeded, Is.False);
            Assert.That(begin.Message, Does.Contain("begin exploded"));
            Assert.That(mismatch.Succeeded, Is.False);
            Assert.That(mismatch.Message, Does.Contain("does not match"));
        }

        [Test]
        public void GenericSourceContract_ContainsNoWritableAbsolutePath()
        {
            string[] propertyNames = typeof(GameContentSourceTarget).GetProperties().Select(property => property.Name).ToArray();

            Assert.That(propertyNames, Does.Contain("LockKey"));
            Assert.That(propertyNames, Does.Contain("ProjectRelativeDescription"));
            Assert.That(propertyNames.Any(name => name.IndexOf("Absolute", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
            Assert.That(propertyNames.Any(name => name.Equals("Path", StringComparison.OrdinalIgnoreCase)), Is.False);
        }

        private GameContentEditBeginResult Begin(InMemoryEditPackProvider provider, string lensId = "attack")
        {
            return _coordinator.BeginEdit(Select(provider), provider.Record, lensId);
        }

        private static InMemoryEditPackProvider NewProvider()
        {
            return new InMemoryEditPackProvider("com.deucarian.tests.edit." + Guid.NewGuid().ToString("N"));
        }

        private static GameContentPackContext Select(IGameContentAuthoringProvider provider)
        {
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new[] { provider });
            GameContentPackDescriptor pack = ((IGameContentPackProvider)provider).GetContentPacks().Single();
            return new GameContentPackSelectionState().Select(catalog, pack.StableKey);
        }

        private static GameContentRecordDescriptor Record(string id, string providerId)
        {
            return InMemoryEditPackProvider.CreateRecord("fixture-pack", id, providerId);
        }

        private sealed class FixtureSource
        {
            public string Id = "fixture.record";
            public string Name = "Fixture Record";
            public long Count = 3;
            public double Power = 1.5d;
            public bool Enabled = true;
            public string Mode = "basic";
            public int Revision;

            public FixtureSource Clone()
            {
                return new FixtureSource
                {
                    Id = Id,
                    Name = Name,
                    Count = Count,
                    Power = Power,
                    Enabled = Enabled,
                    Mode = Mode,
                    Revision = Revision
                };
            }

            public void CopyFrom(FixtureSource source)
            {
                Id = source.Id;
                Name = source.Name;
                Count = source.Count;
                Power = source.Power;
                Enabled = source.Enabled;
                Mode = source.Mode;
                Revision = source.Revision;
            }
        }

        private sealed class InMemoryEditPackProvider :
            IGameContentAuthoringProvider,
            IGameContentPackProvider,
            IGameContentPackEditProvider
        {
            private readonly GameContentSourceTarget _sourceTarget;

            public InMemoryEditPackProvider(string providerId, string packId = "fixture-pack")
            {
                ProviderId = providerId;
                _sourceTarget = new GameContentSourceTarget(
                    "memory-source::" + providerId,
                    "In-memory fixture source",
                    "Test memory only",
                    "fixture-source");
                var access = new GameContentPackAccessDescriptor(
                    GameContentPackBackendCapability.Read |
                    GameContentPackBackendCapability.Validate |
                    GameContentPackBackendCapability.RevealSource |
                    GameContentPackBackendCapability.EditExisting,
                    "Test-only in-memory source");
                Pack = new GameContentPackDescriptor(
                    packId,
                    "com.deucarian.tests",
                    ProviderId,
                    "Editing Fixture",
                    string.Empty,
                    "1",
                    Array.Empty<string>(),
                    GameContentPackSourceKind.Project,
                    GameContentPackSourceState.Available,
                    "InMemory/Fixture",
                    null,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<GameContentCategoryDescriptor>(),
                    Array.Empty<GameContentActionDescriptor>(),
                    GameContentAuthoringValidationResult.Valid,
                    2,
                    access);
                Record = CreateRecord(packId, "fixture.record", ProviderId);
                SecondRecord = CreateRecord(packId, "fixture.second", ProviderId);
            }

            public string ProviderId { get; }
            public string DisplayName => "Test Editing Fixture";
            public string Description => string.Empty;
            public int SortOrder => 0;
            public bool Enabled => true;
            public GameContentPackDescriptor Pack { get; }
            public GameContentRecordDescriptor Record { get; }
            public GameContentRecordDescriptor SecondRecord { get; }
            public FixtureSource Source { get; } = new FixtureSource();
            public bool AllowSecondRecord { get; set; }
            public bool EmitWarning { get; set; }
            public bool EmitValidationError { get; set; }
            public bool ThrowAvailability { get; set; }
            public bool ThrowBegin { get; set; }
            public bool ThrowPreview { get; set; }
            public bool ThrowCommit { get; set; }
            public bool ThrowStateReadAfterCommit { get; set; }
            public bool ThrowStateReadAfterRollback { get; set; }
            public bool FailCommit { get; set; }
            public bool RequireRecoveryOnCommitFailure { get; set; }
            public bool FailRollback { get; set; }
            public string SessionBackendIdOverride { get; set; }
            public Action CommitObserver { get; set; }
            public GameContentEditSessionState StateObservedDuringCommit { get; set; }
            public int CommitCount { get; set; }
            public int RollbackCount { get; set; }

            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }
            public IReadOnlyList<GameContentPackDescriptor> GetContentPacks() => new[] { Pack };
            public IReadOnlyList<GameContentRecordDescriptor> GetRecords(string packId) => new[] { Record, SecondRecord };
            public GameContentAuthoringValidationResult ValidatePack(string packId) => GameContentAuthoringValidationResult.Valid;
            public GameContentActionResult ExecuteAction(string packId, string actionId) => GameContentActionResult.Success("ok");

            public GameContentEditAvailability CanEdit(GameContentEditRequest request)
            {
                if (ThrowAvailability) throw new InvalidOperationException("availability exploded");
                bool supported = request.RecordKey.Equals(Record.CanonicalKey) ||
                                 (AllowSecondRecord && request.RecordKey.Equals(SecondRecord.CanonicalKey));
                return supported
                    ? GameContentEditAvailability.Editable(ProviderId, 5, _sourceTarget)
                    : GameContentEditAvailability.ReadOnly("This fixture record is not supported by the editing backend.", ProviderId);
            }

            public IGameContentEditSession BeginEdit(GameContentEditRequest request)
            {
                if (ThrowBegin) throw new InvalidOperationException("begin exploded");
                return new InMemoryEditSession(this, request.RecordKey, _sourceTarget);
            }

            public void MutateRevisionExternally()
            {
                Source.Revision++;
            }

            public static GameContentRecordDescriptor CreateRecord(string packId, string id, string providerId)
            {
                return new GameContentRecordDescriptor(
                    packId + "::content::" + id,
                    id,
                    "content",
                    null,
                    id,
                    string.Empty,
                    string.Empty,
                    Array.Empty<GameContentMetadataDescriptor>(),
                    null,
                    "InMemory/Fixture",
                    "fixture-record",
                    Array.Empty<GameContentRecordReferenceDescriptor>(),
                    Array.Empty<GameContentRecordReferenceDescriptor>(),
                    GameContentAuthoringValidationResult.Valid,
                    0,
                    null,
                    string.Empty,
                    new GameContentRecordKey("com.deucarian.tests", packId, id, "fixture-source", "fixture-record"),
                    new[] { GameContentRecordCapabilities.Attack, GameContentRecordCapabilities.Weapon });
            }
        }

        private sealed class InMemoryEditSession : IGameContentEditSession
        {
            private readonly InMemoryEditPackProvider _provider;
            private readonly FixtureSource _original;
            private readonly List<Dictionary<string, GameContentFieldValue>> _history =
                new List<Dictionary<string, GameContentFieldValue>>();
            private int _historyIndex;
            private bool _disposed;
            private GameContentEditSessionState _state;

            public InMemoryEditSession(
                InMemoryEditPackProvider provider,
                GameContentRecordKey recordKey,
                GameContentSourceTarget sourceTarget)
            {
                _provider = provider;
                _original = provider.Source.Clone();
                RecordKey = recordKey;
                SourceTarget = sourceTarget;
                BackendId = string.IsNullOrWhiteSpace(provider.SessionBackendIdOverride)
                    ? provider.ProviderId
                    : provider.SessionBackendIdOverride;
                OriginalRevision = Revision(_original.Revision);
                Fields = new[]
                {
                    new GameContentFieldDescriptor("id", "content.id", "Stable ID", string.Empty, GameContentFieldType.String, true, "Stable IDs are read-only.", 0, "Identity"),
                    new GameContentFieldDescriptor("name", "content.display-name", "Display Name", "Player-facing name.", GameContentFieldType.String, order: 10, required: true, minimumLength: 1, maximumLength: 40),
                    new GameContentFieldDescriptor("count", "content.count", "Count", "Fixture count.", GameContentFieldType.Integer, order: 20, minimumNumber: 0, maximumNumber: 10),
                    new GameContentFieldDescriptor("power", "content.power", "Power", "Fixture power.", GameContentFieldType.Number, order: 25, minimumNumber: 0d, maximumNumber: 5d),
                    new GameContentFieldDescriptor("enabled", "content.enabled", "Enabled", string.Empty, GameContentFieldType.Boolean, order: 30),
                    new GameContentFieldDescriptor(
                        "mode",
                        "content.mode",
                        "Mode",
                        string.Empty,
                        GameContentFieldType.Enum,
                        order: 40,
                        required: true,
                        enumOptions: new[]
                        {
                            new GameContentEnumOption("basic", "Basic"),
                            new GameContentEnumOption("advanced", "Advanced")
                        })
                };
                var baseline = Values(_original);
                _history.Add(baseline);
                Snapshot = new GameContentEditSnapshot(
                    RecordKey,
                    SourceTarget,
                    OriginalRevision,
                    baseline,
                    DateTime.UtcNow,
                    "fixture-v1");
                State = GameContentEditSessionState.Clean;
            }

            public string BackendId { get; }
            public GameContentRecordKey RecordKey { get; }
            public GameContentSourceTarget SourceTarget { get; }
            public GameContentSourceRevision OriginalRevision { get; }
            public GameContentEditSessionState State
            {
                get
                {
                    if ((_state == GameContentEditSessionState.Committed && _provider.ThrowStateReadAfterCommit) ||
                        (_state == GameContentEditSessionState.RolledBack && _provider.ThrowStateReadAfterRollback))
                        throw new InvalidOperationException("state read exploded");
                    return _state;
                }
                private set => _state = value;
            }
            public GameContentEditSnapshot Snapshot { get; }
            public IReadOnlyList<GameContentFieldDescriptor> Fields { get; }
            public IReadOnlyList<GameContentProposedChange> Changes => BuildChanges();
            public bool CanUndo => !_disposed && _historyIndex > 0;
            public bool CanRedo => !_disposed && _historyIndex < _history.Count - 1;

            public GameContentEditOperationResult Apply(string fieldId, GameContentFieldValue value)
            {
                if (_disposed) return GameContentEditOperationResult.Failure("Session disposed.");
                GameContentFieldDescriptor field = Fields.FirstOrDefault(candidate => candidate.FieldId == fieldId);
                if (field == null) return GameContentEditOperationResult.Failure("Unknown field.");
                if (field.IsReadOnly) return GameContentEditOperationResult.Failure(field.ReadOnlyReason);
                if (value == null || value.FieldType != field.FieldType)
                    return GameContentEditOperationResult.Failure("Wrong field type.");
                if (Current[fieldId].Equals(value)) return GameContentEditOperationResult.Success("No change.");

                if (_historyIndex < _history.Count - 1)
                    _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                var next = Copy(Current);
                next[fieldId] = value;
                _history.Add(next);
                _historyIndex++;
                RefreshDirtyState();
                return GameContentEditOperationResult.Success("Staged " + field.DisplayName + ".");
            }

            public GameContentEditOperationResult Undo()
            {
                if (!CanUndo) return GameContentEditOperationResult.Failure("Nothing to undo.");
                _historyIndex--;
                RefreshDirtyState();
                return GameContentEditOperationResult.Success("Undid staged change.");
            }

            public GameContentEditOperationResult Redo()
            {
                if (!CanRedo) return GameContentEditOperationResult.Failure("Nothing to redo.");
                _historyIndex++;
                RefreshDirtyState();
                return GameContentEditOperationResult.Success("Redid staged change.");
            }

            public GameContentValidationPreview Preview()
            {
                if (_provider.ThrowPreview) throw new InvalidOperationException("preview exploded");
                var issues = new List<GameContentAuthoringValidationIssue>();
                foreach (GameContentFieldDescriptor field in Fields.Where(value => !value.IsReadOnly))
                {
                    if (!field.Accepts(Current[field.FieldId], out string reason))
                        issues.Add(GameContentAuthoringValidationIssue.Error(field.FieldId, reason));
                }
                if (_provider.EmitValidationError)
                    issues.Add(GameContentAuthoringValidationIssue.Error("fixture", "Configured fixture validation error."));
                if (_provider.EmitWarning)
                    issues.Add(GameContentAuthoringValidationIssue.Warning("fixture", "Configured fixture warning."));
                return new GameContentValidationPreview(issues);
            }

            public GameContentStaleCheckResult CheckStale()
            {
                GameContentSourceRevision current = Revision(_provider.Source.Revision);
                if (!current.Equals(OriginalRevision))
                {
                    State = GameContentEditSessionState.Stale;
                    return GameContentStaleCheckResult.Stale("Fixture source revision changed.", current);
                }
                return GameContentStaleCheckResult.Current(current);
            }

            public GameContentCommitResult Commit(bool confirmWarnings)
            {
                State = GameContentEditSessionState.Committing;
                _provider.StateObservedDuringCommit = State;
                _provider.CommitObserver?.Invoke();
                _provider.CommitCount++;
                if (_provider.ThrowCommit) throw new InvalidOperationException("commit exploded");
                GameContentValidationPreview preview = Preview();
                if (!preview.CanCommit)
                {
                    State = GameContentEditSessionState.Dirty;
                    return GameContentCommitResult.Failure("Validation failed.", OriginalRevision);
                }
                if (preview.RequiresWarningConfirmation && !confirmWarnings)
                {
                    State = GameContentEditSessionState.Dirty;
                    return GameContentCommitResult.Failure("Warnings require confirmation.", OriginalRevision);
                }
                if (_provider.FailCommit)
                {
                    if (_provider.RequireRecoveryOnCommitFailure)
                    {
                        State = GameContentEditSessionState.RecoveryRequired;
                        GameContentRecoveryRecord recovery = Recovery("Fixture commit failure", "Review the fixture source.");
                        return GameContentCommitResult.Failure("Fixture commit requires recovery.", OriginalRevision, recovery);
                    }
                    State = GameContentEditSessionState.Dirty;
                    return GameContentCommitResult.Failure("Fixture commit failed before source mutation.", OriginalRevision);
                }

                _provider.Source.Id = Current["id"].StringValue;
                _provider.Source.Name = Current["name"].StringValue;
                _provider.Source.Count = Current["count"].IntegerValue;
                _provider.Source.Power = Current["power"].NumberValue;
                _provider.Source.Enabled = Current["enabled"].BooleanValue;
                _provider.Source.Mode = Current["mode"].StringValue;
                _provider.Source.Revision++;
                State = GameContentEditSessionState.Committed;
                return new GameContentCommitResult(
                    true,
                    "Fixture source committed.",
                    OriginalRevision,
                    Revision(_provider.Source.Revision),
                    true,
                    true,
                    false);
            }

            public GameContentRollbackResult Rollback()
            {
                _provider.RollbackCount++;
                if (_provider.FailRollback)
                {
                    State = GameContentEditSessionState.RecoveryRequired;
                    GameContentRecoveryRecord recovery = Recovery("Fixture rollback failure", "Review the fixture source.");
                    return GameContentRollbackResult.Failure("Fixture rollback requires recovery.", Revision(_provider.Source.Revision), recovery);
                }
                _provider.Source.CopyFrom(_original);
                State = GameContentEditSessionState.RolledBack;
                return new GameContentRollbackResult(true, "Fixture source restored.", OriginalRevision);
            }

            public void Dispose()
            {
                _disposed = true;
            }

            private Dictionary<string, GameContentFieldValue> Current => _history[_historyIndex];

            private IReadOnlyList<GameContentProposedChange> BuildChanges()
            {
                var changes = new List<GameContentProposedChange>();
                foreach (GameContentFieldDescriptor field in Fields.OrderBy(value => value.Order).ThenBy(value => value.FieldId, StringComparer.Ordinal))
                {
                    GameContentFieldValue oldValue = Snapshot.FieldValues[field.FieldId];
                    GameContentFieldValue newValue = Current[field.FieldId];
                    if (oldValue.Equals(newValue)) continue;
                    changes.Add(new GameContentProposedChange(
                        field.FieldId,
                        oldValue,
                        newValue,
                        field.DisplayName,
                        field.Group,
                        field.Order));
                }
                return changes;
            }

            private void RefreshDirtyState()
            {
                State = BuildChanges().Count == 0
                    ? GameContentEditSessionState.Clean
                    : GameContentEditSessionState.Dirty;
            }

            private GameContentRecoveryRecord Recovery(string phase, string message)
            {
                return new GameContentRecoveryRecord(
                    BackendId,
                    SourceTarget.LockKey,
                    SourceTarget.SourceLabel,
                    OriginalRevision,
                    Revision(_provider.Source.Revision),
                    DateTime.UtcNow,
                    phase,
                    message);
            }

            private static Dictionary<string, GameContentFieldValue> Values(FixtureSource source)
            {
                return new Dictionary<string, GameContentFieldValue>(StringComparer.Ordinal)
                {
                    ["id"] = GameContentFieldValue.FromString(source.Id),
                    ["name"] = GameContentFieldValue.FromString(source.Name),
                    ["count"] = GameContentFieldValue.FromInteger(source.Count),
                    ["power"] = GameContentFieldValue.FromNumber(source.Power),
                    ["enabled"] = GameContentFieldValue.FromBoolean(source.Enabled),
                    ["mode"] = GameContentFieldValue.FromEnum(source.Mode)
                };
            }

            private static Dictionary<string, GameContentFieldValue> Copy(
                IReadOnlyDictionary<string, GameContentFieldValue> values)
            {
                return values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }

            private static GameContentSourceRevision Revision(int revision)
            {
                return new GameContentSourceRevision("revision-" + revision);
            }
        }

        private sealed class ReadOnlyPackProvider : IGameContentAuthoringProvider, IGameContentPackProvider
        {
            public ReadOnlyPackProvider(string providerId)
            {
                ProviderId = providerId;
                var access = new GameContentPackAccessDescriptor(
                    GameContentPackBackendCapability.Read |
                    GameContentPackBackendCapability.EditExisting,
                    "Legacy writable source");
                Pack = new GameContentPackDescriptor(
                    "readonly-pack",
                    "com.deucarian.tests",
                    ProviderId,
                    "Read-only Fixture",
                    string.Empty,
                    "1",
                    Array.Empty<string>(),
                    GameContentPackSourceKind.Project,
                    GameContentPackSourceState.Available,
                    "InMemory/ReadOnly",
                    null,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<GameContentCategoryDescriptor>(),
                    Array.Empty<GameContentActionDescriptor>(),
                    GameContentAuthoringValidationResult.Valid,
                    1,
                    access);
                Record = InMemoryEditPackProvider.CreateRecord(Pack.PackId, "readonly.record", ProviderId);
            }

            public string ProviderId { get; }
            public string DisplayName => "Read-only Fixture";
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
    }
}
