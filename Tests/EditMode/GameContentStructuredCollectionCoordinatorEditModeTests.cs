using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Deucarian.GameContentAuthoring.Editor.Tests
{
    public sealed class GameContentStructuredCollectionCoordinatorEditModeTests
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
        public void Coordinator_MixedHistoryUndoRedoBranchCommitRollbackAndFreshKeys()
        {
            StructuredFixtureProvider provider = NewProvider();
            int refreshCount = 0;
            _coordinator.RefreshRequested += () => refreshCount++;
            GameContentActiveEditSession active = Begin(provider).Session;
            GameContentStructuredRowKey firstOriginalKey = Rows(active).Rows[0].RowKey;

            Assert.That(_coordinator.Apply(active, "name", GameContentFieldValue.FromString("Edited")).Succeeded, Is.True);
            GameContentStructuredCollectionOperationResult add = _coordinator.ApplyStructuredOperation(
                active,
                "rows",
                GameContentStructuredCollectionOperation.AddRow(
                    AddedRowValues("Gamma", 4, 3.5, true, "burst", provider.TargetC.CanonicalKey)));
            Assert.That(add.Succeeded, Is.True);
            GameContentStructuredRowKey addedKey = add.RowKey;
            Assert.That(_coordinator.ApplyStructuredOperation(
                active,
                "rows",
                GameContentStructuredCollectionOperation.ReplaceRowField(
                    addedKey, "title", GameContentFieldValue.FromString("Gamma Prime"))).Succeeded, Is.True);
            Assert.That(_coordinator.ApplyStructuredOperation(
                active,
                "rows",
                GameContentStructuredCollectionOperation.MoveRow(addedKey, 0)).Succeeded, Is.True);
            Assert.That(_coordinator.Apply(active, "primary", Reference(provider.TargetB)).Succeeded, Is.True);
            Assert.That(_coordinator.ApplyCollectionOperation(
                active,
                "tags",
                GameContentCollectionOperation.Add(GameContentFieldValue.FromString("beta"))).Succeeded, Is.True);

            Assert.That(provider.Source.Name, Is.EqualTo("Fixture"));
            Assert.That(provider.Source.Rows.Count, Is.EqualTo(2));
            Assert.That(active.Changes.Select(change => change.FieldId),
                Is.EqualTo(new[] { "name", "primary", "tags", "rows" }));
            Assert.That(Rows(active).Rows[0].RowKey, Is.EqualTo(addedKey));
            Assert.That(active.Validation.CanCommit, Is.True);

            for (int i = 0; i < 6; i++) Assert.That(_coordinator.Undo(active).Succeeded, Is.True);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Clean));
            Assert.That(Rows(active).Count, Is.EqualTo(2));
            Assert.That(Rows(active).Rows[0].RowKey, Is.EqualTo(firstOriginalKey));
            for (int i = 0; i < 6; i++) Assert.That(_coordinator.Redo(active).Succeeded, Is.True);
            Assert.That(Rows(active).Rows[0].RowKey, Is.EqualTo(addedKey),
                "Generated row identity must survive in-session Undo/Redo.");

            Assert.That(_coordinator.Undo(active).Succeeded, Is.True);
            Assert.That(_coordinator.Apply(active, "name", GameContentFieldValue.FromString("Branched")).Succeeded, Is.True);
            Assert.That(active.CanRedo, Is.False, "A new operation after Undo must clear the Redo branch.");
            Assert.That(_coordinator.Preview(active).CanCommit, Is.True);

            GameContentCommitResult commit = _coordinator.Commit(active, true);
            Assert.That(commit.Succeeded, Is.True);
            Assert.That(provider.Source.Name, Is.EqualTo("Branched"));
            Assert.That(provider.Source.PrimaryTarget, Is.EqualTo(provider.TargetB.CanonicalKey));
            Assert.That(provider.Source.Tags, Is.EqualTo(new[] { "alpha" }),
                "The branched history excluded the undone flat-collection operation.");
            Assert.That(provider.Source.Rows.Select(row => row.Title),
                Is.EqualTo(new[] { "Gamma Prime", "Alpha", "Beta" }));
            Assert.That(provider.Source.Rows[0].Target, Is.EqualTo(provider.TargetC.CanonicalKey));
            Assert.That(provider.PostCommitValidationCount, Is.EqualTo(1));
            Assert.That(refreshCount, Is.EqualTo(1));

            GameContentRollbackResult rollback = _coordinator.Rollback(active);
            Assert.That(rollback.Succeeded, Is.True);
            Assert.That(provider.Source.Name, Is.EqualTo("Fixture"));
            Assert.That(provider.Source.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Alpha", "Beta" }));
            Assert.That(provider.PostRollbackValidationCount, Is.EqualTo(1));
            Assert.That(refreshCount, Is.EqualTo(2));
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);

            GameContentActiveEditSession fresh = Begin(provider).Session;
            Assert.That(Rows(fresh).Rows[0].RowKey, Is.Not.EqualTo(firstOriginalKey),
                "A new source session must receive fresh row keys.");
            Assert.That(_coordinator.Cancel(fresh).Succeeded, Is.True);
        }

        [Test]
        public void Coordinator_ValidatesRowsReferencesCandidatesAndDoesNotDeleteTargets()
        {
            StructuredFixtureProvider provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            int recordCount = provider.Records.Count;
            int backendCalls = provider.StructuredApplyCount;

            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.AddRow(
                    AddedRowValues(null, 2, 1.5, true, "burst", provider.TargetA.CanonicalKey))).Succeeded, Is.False);
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.AddRow(
                    AddedRowValues("Wrong", 2, 1.5, true, "burst", provider.WrongCapability.CanonicalKey))).Succeeded, Is.False);
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.AddRow(
                    AddedRowValues("Cross", 2, 1.5, true, "burst", CrossPackTarget()))).Succeeded, Is.False);
            Assert.That(provider.StructuredApplyCount, Is.EqualTo(backendCalls));

            GameContentReferenceCandidateSet candidates = _coordinator.GetStructuredReferenceCandidates(
                active, "rows", null, "target");
            Assert.That(candidates.Candidates.Select(candidate => candidate.Record.SourceRecordId),
                Is.EqualTo(new[] { "target.a", "target.b", "target.c" }));
            Assert.That(candidates.Rejections.Select(rejection => rejection.TargetKey.SourceRecordId),
                Does.Contain("target.wrong"));
            Assert.That(candidates.Rejections.Select(rejection => rejection.TargetKey.SourceRecordId),
                Does.Contain("target.invalid"));

            GameContentStructuredCollectionOperationResult add = _coordinator.ApplyStructuredOperation(
                active, "rows", GameContentStructuredCollectionOperation.AddRow(
                    AddedRowValues("Gamma", 4, 3.5, true, "steady", provider.TargetC.CanonicalKey)));
            Assert.That(add.Succeeded, Is.True);
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.ReplaceRowField(
                    add.RowKey, "target", Reference(provider.WrongCapability))).Succeeded, Is.False);
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.ReplaceRowField(
                    add.RowKey,
                    "target",
                    GameContentFieldValue.FromRecordReference(
                        GameContentRecordReferenceValue.Resolved(CrossPackTarget())))).Succeeded, Is.False);
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.ReplaceRowField(
                    add.RowKey, "target", Reference(provider.TargetB))).Succeeded, Is.True);
            Assert.That(Rows(active).Rows.Single(row => row.RowKey.Equals(add.RowKey)).RowKey, Is.EqualTo(add.RowKey));
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.RemoveRow(
                    GameContentStructuredRowKey.CreateSessionKey())).Succeeded, Is.False);

            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.RemoveRow(add.RowKey)).Succeeded, Is.True);
            Assert.That(provider.Records.Count, Is.EqualTo(recordCount));
            Assert.That(provider.ResolveFresh(provider.TargetB.CanonicalKey), Is.Not.Null,
                "Removing an embedded row must not delete its referenced target record.");
            Assert.That(_coordinator.Cancel(active).Succeeded, Is.True);
            Assert.That(provider.Source.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Alpha", "Beta" }));
        }

        [Test]
        public void RestoreOriginalOrder_KeepsAddedRowsAfterSurvivorsAndUndoRestoresRemovedIdentity()
        {
            StructuredFixtureProvider provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            GameContentStructuredRowKey alpha = Rows(active).Rows[0].RowKey;
            GameContentStructuredRowKey beta = Rows(active).Rows[1].RowKey;
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.MoveRow(beta, 0)).Succeeded, Is.True);
            GameContentStructuredRowKey added = _coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.AddRow(
                    AddedRowValues("Gamma", 4, 3.5, true, "burst", provider.TargetC.CanonicalKey))).RowKey;
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.RemoveRow(alpha)).Succeeded, Is.True);
            Assert.That(_coordinator.RestoreOriginalStructuredOrder(active, "rows").Succeeded, Is.True);
            Assert.That(Rows(active).Rows.Select(row => row.RowKey), Is.EqualTo(new[] { beta, added }));
            Assert.That(_coordinator.Undo(active).Succeeded, Is.True, "Restore order is one deterministic history item.");
            Assert.That(_coordinator.Undo(active).Succeeded, Is.True, "Undo must restore a removed row.");
            Assert.That(Rows(active).Rows.Any(row => row.RowKey.Equals(alpha)), Is.True);
            Assert.That(_coordinator.Cancel(active).Succeeded, Is.True);
        }

        [Test]
        public void References_AreReevaluatedForWarningsDisappearanceCapabilitiesAndProviderRules()
        {
            StructuredFixtureProvider provider = NewProvider();
            provider.StructuredReferenceWarning = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.ReplaceRowField(
                    Rows(active).Rows[0].RowKey, "target", Reference(provider.TargetC))).Succeeded, Is.True);
            GameContentValidationPreview warning = _coordinator.Preview(active);
            Assert.That(warning.State, Is.EqualTo(GameContentEditValidationState.Warning));
            Assert.That(warning.RequiresWarningConfirmation, Is.True);
            Assert.That(_coordinator.Commit(active, false).Succeeded, Is.False);
            Assert.That(_coordinator.Commit(active, true).Succeeded, Is.True);
            Assert.That(_coordinator.Rollback(active).Succeeded, Is.True);

            provider = NewProvider();
            active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.ReplaceRowField(
                    Rows(active).Rows[0].RowKey, "target", Reference(provider.TargetC))).Succeeded, Is.True);
            provider.RemoveTarget("target.c");
            _coordinator.Reconcile(GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { provider }));
            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
            Assert.That(_coordinator.Commit(active, true).Succeeded, Is.False);
            _coordinator.Cancel(active);

            provider = NewProvider();
            active = Begin(provider).Session;
            provider.RemoveCapability("target.a", GameContentRecordCapabilities.Weapon);
            _coordinator.Reconcile(GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { provider }));
            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
            _coordinator.Cancel(active);

            provider = NewProvider();
            active = Begin(provider).Session;
            provider.RejectStructuredReference = true;
            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
            Assert.That(active.Validation.Issues.Any(issue =>
                issue.Severity == GameContentAuthoringValidationSeverity.Error &&
                issue.Path.StartsWith("rows", StringComparison.Ordinal)), Is.True);
            _coordinator.Cancel(active);
        }

        [Test]
        public void ValidationAndProviderExceptions_AreContainedAndBlockCommit()
        {
            StructuredFixtureProvider provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.ReplaceRowField(
                    Rows(active).Rows[0].RowKey, "count", GameContentFieldValue.FromInteger(8))).Succeeded, Is.True);
            provider.EmitDomainError = true;
            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
            Assert.That(_coordinator.Commit(active, true).Succeeded, Is.False);
            Assert.That(provider.Source.Rows[0].Count, Is.EqualTo(2));
            provider.EmitDomainError = false;
            _coordinator.Cancel(active);

            provider = NewProvider();
            provider.ThrowStructuredOperation = true;
            active = Begin(provider).Session;
            GameContentStructuredCollectionOperationResult operation = _coordinator.ApplyStructuredOperation(
                active, "rows", GameContentStructuredCollectionOperation.MoveRow(Rows(active).Rows[1].RowKey, 0));
            Assert.That(operation.Succeeded, Is.False);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Conflict));
            Assert.That(provider.Source.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Alpha", "Beta" }));
            _coordinator.Cancel(active);

            provider = NewProvider();
            provider.ThrowStructuredEvaluation = true;
            active = Begin(provider).Session;
            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
            Assert.That(active.Validation.Issues.Any(issue =>
                issue.Message.Contains("preview failed") || issue.Message.Contains("could not evaluate")), Is.True);
            _coordinator.Cancel(active);

            provider = NewProvider();
            provider.ThrowPreview = true;
            active = Begin(provider).Session;
            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
            Assert.That(active.Validation.Issues.Single().Message, Does.Contain("preview failed"));
            _coordinator.Cancel(active);
        }

        [Test]
        public void SourceStaleLocksLensesAndProviderDisappearancePreserveCoordinatorRules()
        {
            StructuredFixtureProvider provider = NewProvider();
            GameContentPackContext context = Select(provider);
            GameContentEditBeginResult first = _coordinator.BeginEdit(context, provider.SourceRecord, "weapon");
            GameContentEditBeginResult attached = _coordinator.BeginEdit(context, provider.SourceRecord, "upgrade");
            GameContentEditBeginResult blocked = _coordinator.BeginEdit(context, provider.SecondRecord, "weapon");
            Assert.That(first.Succeeded, Is.True);
            Assert.That(attached.AttachedExisting, Is.True);
            Assert.That(attached.Session, Is.SameAs(first.Session));
            Assert.That(blocked.Succeeded, Is.False);
            Assert.That(blocked.Message, Does.Contain("physical source"));

            Assert.That(_coordinator.ApplyStructuredOperation(first.Session, "rows",
                GameContentStructuredCollectionOperation.MoveRow(Rows(first.Session).Rows[1].RowKey, 0)).Succeeded, Is.True);
            provider.ForceStale = true;
            Assert.That(_coordinator.ApplyStructuredOperation(first.Session, "rows",
                GameContentStructuredCollectionOperation.MoveRow(Rows(first.Session).Rows[0].RowKey, 1)).Succeeded, Is.False);
            Assert.That(first.Session.State, Is.EqualTo(GameContentEditSessionState.Stale));
            Assert.That(_coordinator.Undo(first.Session).Succeeded, Is.False);
            Assert.That(_coordinator.Commit(first.Session, true).Succeeded, Is.False);
            Assert.That(_coordinator.Cancel(first.Session).Succeeded, Is.True);
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);

            provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Reconcile(GameContentPackCatalog.Build(Array.Empty<IGameContentAuthoringProvider>()));
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero,
                "A disappeared provider/pack must invalidate and release the complete source session.");
            Assert.That(provider.RollbackCount, Is.EqualTo(1));
            Assert.That(provider.Source.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Alpha", "Beta" }));
        }

        [Test]
        public void CommitAndRollbackFailures_PreserveSourceAndExposeRecovery()
        {
            StructuredFixtureProvider provider = NewProvider();
            provider.FailCommit = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.ReplaceRowField(
                    Rows(active).Rows[0].RowKey, "title", GameContentFieldValue.FromString("Changed"))).Succeeded, Is.True);
            GameContentCommitResult failed = _coordinator.Commit(active, true);
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(provider.Source.Rows[0].Title, Is.EqualTo("Alpha"));
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Dirty));
            provider.FailCommit = false;
            Assert.That(_coordinator.Cancel(active).Succeeded, Is.True);

            provider = NewProvider();
            provider.FailRollback = true;
            active = Begin(provider).Session;
            Assert.That(_coordinator.ApplyStructuredOperation(active, "rows",
                GameContentStructuredCollectionOperation.MoveRow(Rows(active).Rows[1].RowKey, 0)).Succeeded, Is.True);
            GameContentRollbackResult rollback = _coordinator.Cancel(active);
            Assert.That(rollback.Succeeded, Is.False);
            Assert.That(rollback.Recovery, Is.Not.Null);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.RecoveryRequired));
            Assert.That(_coordinator.ActiveSourceCount, Is.EqualTo(1));
        }

        [Test]
        public void OptionalContractCrudBoundaryAllPacksAndProjectContentRemainGuarded()
        {
            StructuredFixtureProvider provider = NewProvider();
            provider.OmitStructuredContract = true;
            GameContentEditBeginResult missingContract = _coordinator.BeginEdit(
                Select(provider), provider.SourceRecord);
            Assert.That(missingContract.Succeeded, Is.False);
            Assert.That(missingContract.Message, Does.Contain("structured-session contract"));

            provider = NewProvider();
            provider.ExposeCanonicalRows = true;
            GameContentEditBeginResult canonical = _coordinator.BeginEdit(Select(provider), provider.SourceRecord);
            Assert.That(canonical.Succeeded, Is.False);
            Assert.That(canonical.Message, Does.Contain("canonical"));
            Assert.That(canonical.Message, Does.Contain("CRUD"));

            provider = NewProvider();
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { provider });
            GameContentPackContext all = new GameContentPackSelectionState().Select(
                catalog, GameContentPackContext.AllPacksSelectionKey);
            Assert.That(_coordinator.GetAvailability(all, provider.SourceRecord).IsEditable, Is.False);
            Assert.That(_coordinator.BeginEdit(all, provider.SourceRecord).Succeeded, Is.False);

            var project = new ProjectContentFixtureProvider();
            GameContentPackContext projectContext = Select(project);
            GameContentEditAvailability availability = _coordinator.GetAvailability(projectContext, project.Record);
            Assert.That(projectContext.IsProjectContent, Is.True);
            Assert.That(availability.IsEditable, Is.False);
            Assert.That(availability.DisabledReason, Does.Contain("existing provider-owned editing surface"));

            Assert.That(typeof(StructuredFixtureProvider).IsNestedPrivate, Is.True);
            Assert.That(typeof(StructuredFixtureProvider).Assembly.GetName().Name,
                Is.EqualTo("Deucarian.GameContentAuthoring.Tests"));
        }

        [Test]
        public void NativeKeyDuplicatesAndCollectionErrorsRemainVisibleAndBlockCommit()
        {
            StructuredFixtureProvider provider = NewProvider();
            provider.DuplicateNativeKeys = true;
            GameContentActiveEditSession active = Begin(provider).Session;
            GameContentValidationPreview preview = _coordinator.Preview(active);
            Assert.That(preview.CanCommit, Is.False);
            Assert.That(preview.Issues.Any(issue => issue.Message.Contains("native")), Is.True);
            Assert.That(provider.Source.Rows.Count, Is.EqualTo(2));

            provider = NewProvider();
            active = Begin(provider).Session;
            provider.EmitCollectionError = true;
            Assert.That(_coordinator.Preview(active).CanCommit, Is.False);
            Assert.That(active.Validation.Issues.Any(issue => issue.Path == "rows"), Is.True);
            _coordinator.Cancel(active);
        }

        private GameContentEditBeginResult Begin(StructuredFixtureProvider provider)
        {
            return _coordinator.BeginEdit(Select(provider), provider.SourceRecord, "structured-fixture");
        }

        private static StructuredFixtureProvider NewProvider()
        {
            return new StructuredFixtureProvider(
                "com.deucarian.tests.structured." + Guid.NewGuid().ToString("N"));
        }

        private static GameContentPackContext Select(IGameContentAuthoringProvider provider)
        {
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new[] { provider });
            GameContentPackDescriptor pack = ((IGameContentPackProvider)provider).GetContentPacks().Single();
            return new GameContentPackSelectionState().Select(catalog, pack.StableKey);
        }

        private static GameContentOrderedStructuredCollectionValue Rows(GameContentActiveEditSession active)
        {
            return active.GetEffectiveValue("rows").OrderedStructuredCollectionValue;
        }

        private static GameContentFieldValue Reference(GameContentRecordDescriptor record)
        {
            return GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.Resolved(
                record.CanonicalKey, record.DisplayName, record.SourcePath));
        }

        private static IReadOnlyList<GameContentStructuredRowFieldValue> AddedRowValues(
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
            values.Add(new GameContentStructuredRowFieldValue(
                "target",
                GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.Resolved(target))));
            return values;
        }

        private static GameContentRecordKey CrossPackTarget()
        {
            return new GameContentRecordKey(
                StructuredFixtureProvider.OwnerId,
                "another-pack",
                "target.cross",
                StructuredFixtureProvider.SourceId,
                "target.cross");
        }

        private static GameContentFieldDescriptor StructuredField(bool canonicalRows = false)
        {
            var rowDescriptor = new GameContentStructuredRowDescriptor(
                "fixture-row-v1",
                "Fixture Row",
                "Embedded child values owned by the fixture source record.",
                new[]
                {
                    new GameContentFieldDescriptor(
                        "nativeLabel", "fixture.rows.native-label", "Native Label", string.Empty,
                        GameContentFieldType.String, true, "Provider native identity is read-only.", 0),
                    new GameContentFieldDescriptor(
                        "title", "fixture.rows.title", "Title", string.Empty,
                        GameContentFieldType.String, order: 10, required: true, minimumLength: 1, maximumLength: 32),
                    new GameContentFieldDescriptor(
                        "count", "fixture.rows.count", "Count", string.Empty,
                        GameContentFieldType.Integer, order: 20, required: true, minimumNumber: 0, maximumNumber: 100),
                    new GameContentFieldDescriptor(
                        "weight", "fixture.rows.weight", "Weight", string.Empty,
                        GameContentFieldType.Number, order: 30, required: true, minimumNumber: 0, maximumNumber: 100),
                    new GameContentFieldDescriptor(
                        "enabled", "fixture.rows.enabled", "Enabled", string.Empty,
                        GameContentFieldType.Boolean, order: 40, required: true),
                    new GameContentFieldDescriptor(
                        "mode", "fixture.rows.mode", "Mode", string.Empty,
                        GameContentFieldType.Enum, order: 50, required: true,
                        enumOptions: new[]
                        {
                            new GameContentEnumOption("burst", "Burst"),
                            new GameContentEnumOption("steady", "Steady")
                        }),
                    new GameContentFieldDescriptor(
                        "target", "fixture.rows.target", "Target", string.Empty,
                        GameContentFieldType.RecordReference, order: 60, required: true,
                        recordReference: ReferenceDescriptor())
                },
                new[] { "title", "mode" },
                new GameContentStructuredRowNativeKeyDescriptor(
                    "Provider Native Key",
                    "Read-only provider metadata, not canonical content identity."),
                representsIndependentCanonicalRecord: canonicalRows);
            return GameContentFieldDescriptor.FromStructuredCollection(
                new GameContentStructuredCollectionFieldDescriptor(
                    "rows",
                    "fixture.rows",
                    "Rows",
                    "Ordered embedded fixture rows.",
                    rowDescriptor,
                    1,
                    4,
                    "Priority order is significant.",
                    GameContentStructuredRowDuplicatePolicy.Allow,
                    GameContentStructuredCollectionPermittedOperations.All,
                    GameContentReferenceRuntimeImpact.Refresh | GameContentReferenceRuntimeImpact.Rebind),
                30,
                "Structured");
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

        private sealed class FixtureRow
        {
            public string NativeKey;
            public string Title;
            public long Count;
            public double Weight;
            public bool Enabled;
            public string Mode;
            public GameContentRecordKey Target;

            public FixtureRow Clone()
            {
                return new FixtureRow
                {
                    NativeKey = NativeKey,
                    Title = Title,
                    Count = Count,
                    Weight = Weight,
                    Enabled = Enabled,
                    Mode = Mode,
                    Target = Target
                };
            }
        }

        private sealed class FixtureSource
        {
            public string Name = "Fixture";
            public GameContentRecordKey PrimaryTarget;
            public List<string> Tags = new List<string> { "alpha" };
            public List<FixtureRow> Rows = new List<FixtureRow>();
            public int Revision;

            public FixtureSource Clone()
            {
                return new FixtureSource
                {
                    Name = Name,
                    PrimaryTarget = PrimaryTarget,
                    Tags = new List<string>(Tags),
                    Rows = Rows.Select(row => row.Clone()).ToList(),
                    Revision = Revision
                };
            }

            public void CopyFrom(FixtureSource source)
            {
                Name = source.Name;
                PrimaryTarget = source.PrimaryTarget;
                Tags = new List<string>(source.Tags);
                Rows = source.Rows.Select(row => row.Clone()).ToList();
                Revision = source.Revision;
            }
        }

        private sealed class StructuredFixtureProvider :
            IGameContentAuthoringProvider,
            IGameContentPackProvider,
            IGameContentPackEditProvider
        {
            public const string OwnerId = "com.deucarian.tests.structured";
            public const string PackId = "structured-fixture";
            public const string SourceId = "structured-source";
            private readonly List<GameContentRecordDescriptor> _records;
            private readonly GameContentSourceTarget _sourceTarget;

            public StructuredFixtureProvider(string providerId)
            {
                ProviderId = providerId;
                _sourceTarget = new GameContentSourceTarget(
                    "structured-source::" + providerId,
                    "Structured fixture source",
                    "Test memory only",
                    SourceId);
                Pack = CreatePack(PackId, OwnerId, ProviderId, "Structured Fixture", 7);
                SourceRecord = CreateRecord("source", new[] { GameContentRecordCapabilities.Upgrade });
                SecondRecord = CreateRecord("source.second", new[] { GameContentRecordCapabilities.Upgrade });
                TargetA = CreateRecord("target.a", new[] { GameContentRecordCapabilities.Weapon });
                TargetB = CreateRecord("target.b", new[] { GameContentRecordCapabilities.Weapon });
                TargetC = CreateRecord("target.c", new[] { GameContentRecordCapabilities.Weapon });
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
                    SourceRecord, TargetC, WrongCapability, TargetA, InvalidTarget, SecondRecord, TargetB
                };
                Source.PrimaryTarget = TargetA.CanonicalKey;
                Source.Rows.Add(NewRow("native-a", "Alpha", 2, 1.5, true, "burst", TargetA.CanonicalKey));
                Source.Rows.Add(NewRow("native-b", "Beta", 3, 2.5, false, "steady", TargetB.CanonicalKey));
            }

            public string ProviderId { get; }
            public string DisplayName => "Structured Fixture";
            public string Description => string.Empty;
            public int SortOrder => 0;
            public bool Enabled => true;
            public GameContentPackDescriptor Pack { get; }
            public GameContentRecordDescriptor SourceRecord { get; }
            public GameContentRecordDescriptor SecondRecord { get; }
            public GameContentRecordDescriptor TargetA { get; private set; }
            public GameContentRecordDescriptor TargetB { get; private set; }
            public GameContentRecordDescriptor TargetC { get; private set; }
            public GameContentRecordDescriptor WrongCapability { get; }
            public GameContentRecordDescriptor InvalidTarget { get; }
            public IReadOnlyList<GameContentRecordDescriptor> Records => _records;
            public FixtureSource Source { get; } = new FixtureSource();
            public int StructuredApplyCount { get; set; }
            public int PreviewCount { get; set; }
            public int RollbackCount { get; set; }
            public int PostCommitValidationCount { get; set; }
            public int PostRollbackValidationCount { get; set; }
            public bool StructuredReferenceWarning { get; set; }
            public bool RejectStructuredReference { get; set; }
            public bool ThrowStructuredOperation { get; set; }
            public bool ThrowStructuredEvaluation { get; set; }
            public bool ThrowPreview { get; set; }
            public bool EmitDomainError { get; set; }
            public bool EmitCollectionError { get; set; }
            public bool ForceStale { get; set; }
            public bool FailCommit { get; set; }
            public bool FailRollback { get; set; }
            public bool OmitStructuredContract { get; set; }
            public bool ExposeCanonicalRows { get; set; }
            public bool DuplicateNativeKeys { get; set; }

            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }
            public IReadOnlyList<GameContentPackDescriptor> GetContentPacks() => new[] { Pack };
            public IReadOnlyList<GameContentRecordDescriptor> GetRecords(string packId) =>
                string.Equals(packId, Pack.PackId, StringComparison.OrdinalIgnoreCase)
                    ? _records.ToArray()
                    : Array.Empty<GameContentRecordDescriptor>();
            public GameContentAuthoringValidationResult ValidatePack(string packId) =>
                GameContentAuthoringValidationResult.Valid;
            public GameContentActionResult ExecuteAction(string packId, string actionId) =>
                GameContentActionResult.Success("ok");

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
                var session = new StructuredFixtureSession(this, request.RecordKey, _sourceTarget);
                return OmitStructuredContract
                    ? new StructuredContractOmittingSession(session)
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
                if (id == "target.a") TargetA = replacement;
                if (id == "target.b") TargetB = replacement;
                if (id == "target.c") TargetC = replacement;
            }

            private static FixtureRow NewRow(
                string nativeKey,
                string title,
                long count,
                double weight,
                bool enabled,
                string mode,
                GameContentRecordKey target)
            {
                return new FixtureRow
                {
                    NativeKey = nativeKey,
                    Title = title,
                    Count = count,
                    Weight = weight,
                    Enabled = enabled,
                    Mode = mode,
                    Target = target
                };
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
                    "InMemory/Structured/" + id,
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

        private sealed class StructuredFixtureSession :
            IGameContentEditSession,
            IGameContentOrderedCollectionEditSession,
            IGameContentRecordReferenceEditSession,
            IGameContentStructuredCollectionEditSession
        {
            private readonly StructuredFixtureProvider _provider;
            private readonly FixtureSource _original;
            private readonly List<Dictionary<string, GameContentFieldValue>> _history =
                new List<Dictionary<string, GameContentFieldValue>>();
            private int _historyIndex;

            public StructuredFixtureSession(
                StructuredFixtureProvider provider,
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
                        "name", "fixture.name", "Name", string.Empty,
                        GameContentFieldType.String, order: 0, required: true, minimumLength: 1),
                    new GameContentFieldDescriptor(
                        "primary", "fixture.primary", "Primary", string.Empty,
                        GameContentFieldType.RecordReference, order: 10, required: true,
                        recordReference: ReferenceDescriptor()),
                    new GameContentFieldDescriptor(
                        "tags", "fixture.tags", "Tags", string.Empty,
                        GameContentFieldType.OrderedScalarCollection, order: 20, required: true,
                        collection: new GameContentCollectionFieldDescriptor(
                            new GameContentFieldDescriptor(
                                "tags.item", "fixture.tags.item", "Tag", string.Empty,
                                GameContentFieldType.String, required: true, minimumLength: 1),
                            1, 4, false)),
                    StructuredField(provider.ExposeCanonicalRows)
                };
                Dictionary<string, GameContentFieldValue> baseline = BuildValues(provider.Source);
                _history.Add(baseline);
                Snapshot = new GameContentEditSnapshot(
                    RecordKey,
                    SourceTarget,
                    OriginalRevision,
                    baseline,
                    DateTime.UtcNow,
                    "structured-fixture-v1");
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
                GameContentFieldDescriptor field = FindField(fieldId);
                if (field == null || field.FieldType.IsOrderedCollection() ||
                    field.FieldType == GameContentFieldType.OrderedStructuredCollection)
                    return GameContentEditOperationResult.Failure("Unsupported scalar/reference field.");
                if (!field.Accepts(value, out string reason)) return GameContentEditOperationResult.Failure(reason);
                if (field.FieldType == GameContentFieldType.RecordReference)
                {
                    GameContentReferenceEvaluation evaluation = EvaluateReferenceTarget(
                        fieldId, value.RecordReferenceValue.TargetKey);
                    if (!evaluation.IsValid) return GameContentEditOperationResult.Failure(evaluation.Reason);
                }
                return Stage(field, value);
            }

            public GameContentEditOperationResult ApplyCollectionOperation(
                string fieldId,
                GameContentCollectionOperation operation)
            {
                GameContentFieldDescriptor field = FindField(fieldId);
                if (field == null || !field.FieldType.IsOrderedCollection())
                    return GameContentEditOperationResult.Failure("Unsupported flat collection field.");
                if (!GameContentCollectionMutation.TryApply(
                        field,
                        Current[fieldId].OrderedCollectionValue,
                        operation,
                        out GameContentOrderedCollectionValue proposed,
                        out string reason))
                    return GameContentEditOperationResult.Failure(reason);
                return Stage(field, GameContentFieldValue.FromOrderedCollection(proposed));
            }

            public GameContentStructuredCollectionOperationResult ApplyStructuredOperation(
                string fieldId,
                GameContentStructuredCollectionOperation operation)
            {
                _provider.StructuredApplyCount++;
                if (_provider.ThrowStructuredOperation)
                    throw new InvalidOperationException("structured operation exploded");
                GameContentFieldDescriptor field = FindField(fieldId);
                if (!GameContentStructuredCollectionMutation.TryApply(
                        field,
                        Current[fieldId].OrderedStructuredCollectionValue,
                        operation,
                        out GameContentOrderedStructuredCollectionValue proposed,
                        out GameContentStructuredRowKey rowKey,
                        out string reason))
                    return GameContentStructuredCollectionOperationResult.Failure(reason);
                GameContentEditOperationResult staged = Stage(
                    field,
                    GameContentFieldValue.FromOrderedStructuredCollection(proposed));
                return staged.Succeeded
                    ? GameContentStructuredCollectionOperationResult.Success(staged.Message, rowKey)
                    : GameContentStructuredCollectionOperationResult.Failure(staged.Message);
            }

            public GameContentReferenceEvaluation EvaluateReferenceTarget(
                string fieldId,
                GameContentRecordKey targetKey)
            {
                return EvaluateTarget(fieldId, targetKey, false);
            }

            public GameContentReferenceEvaluation EvaluateStructuredRowReference(
                string fieldId,
                GameContentStructuredRowKey rowKey,
                string rowFieldId,
                GameContentRecordKey targetKey)
            {
                if (_provider.ThrowStructuredEvaluation)
                    throw new InvalidOperationException("structured reference evaluation exploded");
                if (fieldId != "rows" || rowFieldId != "target")
                    return GameContentReferenceEvaluation.Rejected(targetKey, "Unknown structured reference field.");
                if (_provider.RejectStructuredReference)
                    return GameContentReferenceEvaluation.Rejected(targetKey, "The provider rejected this row target.");
                return EvaluateTarget(rowFieldId, targetKey, _provider.StructuredReferenceWarning);
            }

            public GameContentEditOperationResult Undo()
            {
                if (!CanUndo) return GameContentEditOperationResult.Failure("Nothing to undo.");
                _historyIndex--;
                RefreshState();
                return GameContentEditOperationResult.Success("Fixture operation undone.");
            }

            public GameContentEditOperationResult Redo()
            {
                if (!CanRedo) return GameContentEditOperationResult.Failure("Nothing to redo.");
                _historyIndex++;
                RefreshState();
                return GameContentEditOperationResult.Success("Fixture operation redone.");
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
                if (!_provider.ForceStale && current.Equals(OriginalRevision))
                    return GameContentStaleCheckResult.Current(current);
                State = GameContentEditSessionState.Stale;
                return GameContentStaleCheckResult.Stale("Structured fixture source changed.", current);
            }

            public GameContentCommitResult Commit(bool confirmWarnings)
            {
                GameContentStaleCheckResult stale = CheckStale();
                if (stale.IsStale) return GameContentCommitResult.Failure(stale.Message, OriginalRevision);
                GameContentValidationPreview preview = Preview();
                if (!preview.CanCommit) return GameContentCommitResult.Failure("Validation failed.", OriginalRevision);
                if (preview.RequiresWarningConfirmation && !confirmWarnings)
                    return GameContentCommitResult.Failure("Confirm warnings.", OriginalRevision);
                if (_provider.FailCommit)
                    return GameContentCommitResult.Failure("Configured commit failure.", OriginalRevision);

                _provider.Source.Name = Current["name"].StringValue;
                _provider.Source.PrimaryTarget = Current["primary"].RecordReferenceValue.TargetKey;
                _provider.Source.Tags = Current["tags"].OrderedCollectionValue.Items
                    .Select(item => item.Value.StringValue).ToList();
                _provider.Source.Rows = Current["rows"].OrderedStructuredCollectionValue.Rows
                    .Select(ToFixtureRow).ToList();
                _provider.Source.Revision++;
                _provider.PostCommitValidationCount++;
                if (!Validate(BuildValues(_provider.Source)).CanCommit)
                    return GameContentCommitResult.Failure("Persisted fixture validation failed.", OriginalRevision);
                State = GameContentEditSessionState.Committed;
                return new GameContentCommitResult(
                    true,
                    "Structured fixture committed.",
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
                    var recovery = new GameContentRecoveryRecord(
                        BackendId,
                        SourceTarget.LockKey,
                        SourceTarget.SourceLabel,
                        OriginalRevision,
                        Revision(_provider.Source.Revision),
                        DateTime.UtcNow,
                        "Fixture rollback",
                        "Configured rollback failure requires recovery.");
                    State = GameContentEditSessionState.RecoveryRequired;
                    return GameContentRollbackResult.Failure("Configured rollback failure.", OriginalRevision, recovery);
                }
                _provider.Source.CopyFrom(_original);
                _provider.PostRollbackValidationCount++;
                if (!Validate(BuildValues(_provider.Source)).CanCommit)
                    return GameContentRollbackResult.Failure("Restored fixture validation failed.", OriginalRevision);
                State = GameContentEditSessionState.RolledBack;
                return new GameContentRollbackResult(true, "Structured fixture restored.", OriginalRevision);
            }

            public void Dispose() { }

            private GameContentReferenceEvaluation EvaluateTarget(
                string fieldId,
                GameContentRecordKey targetKey,
                bool warning)
            {
                if (fieldId != "primary" && fieldId != "target")
                    return GameContentReferenceEvaluation.Rejected(targetKey, "Unknown reference field.");
                GameContentRecordDescriptor target = _provider.ResolveFresh(targetKey);
                if (target == null)
                {
                    return GameContentReferenceEvaluation.Rejected(
                        targetKey, "The fresh target no longer exists.", sourceClaimValid: false);
                }
                if (!target.HasCapability(GameContentRecordCapabilities.Weapon))
                {
                    return GameContentReferenceEvaluation.Rejected(
                        targetKey, "The fresh target lacks the Weapon capability.",
                        requiredCapabilitiesSatisfied: false);
                }
                if (!target.Validation.IsValid || target.HasBrokenReferences)
                {
                    return GameContentReferenceEvaluation.Rejected(
                        targetKey, "The fresh target is invalid.",
                        validationState: GameContentEditValidationState.Invalid);
                }
                return GameContentReferenceEvaluation.Approved(
                    targetKey,
                    GameContentReferenceRuntimeImpact.Rebind,
                    warning ? GameContentEditValidationState.Warning : GameContentEditValidationState.Valid,
                    warning ? "Configured structured-reference warning." : null);
            }

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
                        ValidateReference(field.FieldId, values[field.FieldId].RecordReferenceValue, issues);
                    else if (field.FieldType == GameContentFieldType.OrderedStructuredCollection)
                    {
                        GameContentOrderedStructuredCollectionValue rows =
                            values[field.FieldId].OrderedStructuredCollectionValue;
                        for (int rowIndex = 0; rowIndex < rows.Rows.Count; rowIndex++)
                        {
                            GameContentStructuredRowValue row = rows.Rows[rowIndex];
                            row.TryGetFieldValue("target", out GameContentFieldValue target);
                            ValidateStructuredReference(row, target?.RecordReferenceValue, rowIndex, issues);
                        }
                    }
                }
                if (_provider.EmitDomainError)
                    issues.Add(GameContentAuthoringValidationIssue.Error("fixture", "Configured provider-domain error."));
                if (_provider.EmitCollectionError)
                    issues.Add(GameContentAuthoringValidationIssue.Error("rows", "Configured collection-level error."));
                if (_provider.StructuredReferenceWarning)
                    issues.Add(GameContentAuthoringValidationIssue.Warning("rows", "Configured provider warning."));
                return new GameContentValidationPreview(issues);
            }

            private void ValidateReference(
                string fieldId,
                GameContentRecordReferenceValue reference,
                ICollection<GameContentAuthoringValidationIssue> issues)
            {
                if (reference == null || !reference.IsResolved || reference.TargetKey == null)
                {
                    issues.Add(GameContentAuthoringValidationIssue.Error(fieldId, "A resolved target is required."));
                    return;
                }
                GameContentReferenceEvaluation evaluation = EvaluateReferenceTarget(fieldId, reference.TargetKey);
                AddEvaluationIssue(fieldId, evaluation, issues);
            }

            private void ValidateStructuredReference(
                GameContentStructuredRowValue row,
                GameContentRecordReferenceValue reference,
                int index,
                ICollection<GameContentAuthoringValidationIssue> issues)
            {
                string path = "rows[" + (index + 1) + "].target";
                if (reference == null || !reference.IsResolved || reference.TargetKey == null)
                {
                    issues.Add(GameContentAuthoringValidationIssue.Error(path, "A resolved row target is required."));
                    return;
                }
                GameContentReferenceEvaluation evaluation = EvaluateStructuredRowReference(
                    "rows", row.RowKey, "target", reference.TargetKey);
                AddEvaluationIssue(path, evaluation, issues);
            }

            private static void AddEvaluationIssue(
                string path,
                GameContentReferenceEvaluation evaluation,
                ICollection<GameContentAuthoringValidationIssue> issues)
            {
                if (!evaluation.IsValid)
                    issues.Add(GameContentAuthoringValidationIssue.Error(path, evaluation.Reason));
                else if (evaluation.ValidationState == GameContentEditValidationState.Warning)
                    issues.Add(GameContentAuthoringValidationIssue.Warning(path, evaluation.Reason));
            }

            private Dictionary<string, GameContentFieldValue> BuildValues(FixtureSource source)
            {
                return new Dictionary<string, GameContentFieldValue>(StringComparer.Ordinal)
                {
                    ["name"] = GameContentFieldValue.FromString(source.Name),
                    ["primary"] = BuildReferenceValue(source.PrimaryTarget),
                    ["tags"] = GameContentFieldValue.FromOrderedScalarCollection(
                        new GameContentOrderedCollectionValue(
                            GameContentFieldType.String,
                            source.Tags.Select((value, index) => new GameContentCollectionItem(
                                GameContentCollectionItemKey.Create(),
                                index,
                                GameContentFieldValue.FromString(value))))),
                    ["rows"] = GameContentFieldValue.FromOrderedStructuredCollection(
                        new GameContentOrderedStructuredCollectionValue(
                            "fixture-row-v1",
                            source.Rows.Select((row, index) => BuildRow(row, index))))
                };
            }

            private GameContentStructuredRowValue BuildRow(FixtureRow row, int index)
            {
                string nativeKey = _provider.DuplicateNativeKeys && index > 0
                    ? _provider.Source.Rows[0].NativeKey
                    : row.NativeKey;
                var values = new[]
                {
                    new GameContentStructuredRowFieldValue(
                        "nativeLabel", GameContentFieldValue.FromString(nativeKey ?? string.Empty)),
                    new GameContentStructuredRowFieldValue("title", GameContentFieldValue.FromString(row.Title)),
                    new GameContentStructuredRowFieldValue("count", GameContentFieldValue.FromInteger(row.Count)),
                    new GameContentStructuredRowFieldValue("weight", GameContentFieldValue.FromNumber(row.Weight)),
                    new GameContentStructuredRowFieldValue("enabled", GameContentFieldValue.FromBoolean(row.Enabled)),
                    new GameContentStructuredRowFieldValue("mode", GameContentFieldValue.FromEnum(row.Mode)),
                    new GameContentStructuredRowFieldValue("target", BuildReferenceValue(row.Target))
                };
                return new GameContentStructuredRowValue(
                    GameContentStructuredRowKey.CreateSessionKey(),
                    index,
                    "fixture-row-v1",
                    values,
                    GameContentEditValidationState.Valid,
                    row.Title + " | " + row.Mode,
                    nativeKey);
            }

            private GameContentFieldValue BuildReferenceValue(GameContentRecordKey key)
            {
                GameContentRecordDescriptor target = _provider.ResolveFresh(key);
                return GameContentFieldValue.FromRecordReference(target == null
                    ? GameContentRecordReferenceValue.Broken(
                        key?.SourceRecordId ?? "missing", "The fixture target is missing.", key)
                    : GameContentRecordReferenceValue.Resolved(
                        key, target.DisplayName, target.SourcePath));
            }

            private static FixtureRow ToFixtureRow(GameContentStructuredRowValue row)
            {
                row.TryGetFieldValue("title", out GameContentFieldValue title);
                row.TryGetFieldValue("count", out GameContentFieldValue count);
                row.TryGetFieldValue("weight", out GameContentFieldValue weight);
                row.TryGetFieldValue("enabled", out GameContentFieldValue enabled);
                row.TryGetFieldValue("mode", out GameContentFieldValue mode);
                row.TryGetFieldValue("target", out GameContentFieldValue target);
                return new FixtureRow
                {
                    NativeKey = row.NativeKeyDisplayMetadata,
                    Title = title.StringValue,
                    Count = count.IntegerValue,
                    Weight = weight.NumberValue,
                    Enabled = enabled.BooleanValue,
                    Mode = mode.StringValue,
                    Target = target.RecordReferenceValue.TargetKey
                };
            }

            private IReadOnlyList<GameContentProposedChange> BuildChanges()
            {
                var changes = new List<GameContentProposedChange>();
                foreach (GameContentFieldDescriptor field in Fields.OrderBy(value => value.Order))
                {
                    GameContentFieldValue oldValue = Snapshot.FieldValues[field.FieldId];
                    GameContentFieldValue proposed = Current[field.FieldId];
                    if (oldValue.Equals(proposed)) continue;
                    changes.Add(new GameContentProposedChange(
                        field.FieldId,
                        oldValue,
                        proposed,
                        field.DisplayName,
                        field.Group,
                        field.Order));
                }
                return changes;
            }

            private GameContentFieldDescriptor FindField(string fieldId)
            {
                return Fields.FirstOrDefault(field => field.FieldId == fieldId);
            }

            private void RefreshState()
            {
                State = Changes.Count == 0 ? GameContentEditSessionState.Clean : GameContentEditSessionState.Dirty;
            }

            private static GameContentSourceRevision Revision(int revision)
            {
                return new GameContentSourceRevision("structured-revision-" + revision);
            }
        }

        private sealed class StructuredContractOmittingSession :
            IGameContentEditSession,
            IGameContentOrderedCollectionEditSession,
            IGameContentRecordReferenceEditSession
        {
            private readonly StructuredFixtureSession _inner;

            public StructuredContractOmittingSession(StructuredFixtureSession inner) { _inner = inner; }
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
            public GameContentEditOperationResult Apply(string fieldId, GameContentFieldValue value) =>
                _inner.Apply(fieldId, value);
            public GameContentEditOperationResult ApplyCollectionOperation(
                string fieldId, GameContentCollectionOperation operation) =>
                _inner.ApplyCollectionOperation(fieldId, operation);
            public GameContentReferenceEvaluation EvaluateReferenceTarget(
                string fieldId, GameContentRecordKey targetKey) =>
                _inner.EvaluateReferenceTarget(fieldId, targetKey);
            public GameContentEditOperationResult Undo() => _inner.Undo();
            public GameContentEditOperationResult Redo() => _inner.Redo();
            public GameContentValidationPreview Preview() => _inner.Preview();
            public GameContentStaleCheckResult CheckStale() => _inner.CheckStale();
            public GameContentCommitResult Commit(bool confirmWarnings) => _inner.Commit(confirmWarnings);
            public GameContentRollbackResult Rollback() => _inner.Rollback();
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
                    Key(Pack.PackId, "project.record"),
                    Array.Empty<GameContentRecordCapability>());
            }

            public string ProviderId => "com.deucarian.tests.project-content.structured";
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
                "InMemory/Structured",
                null,
                null,
                null,
                null,
                null,
                Array.Empty<GameContentCategoryDescriptor>(),
                Array.Empty<GameContentActionDescriptor>(),
                GameContentAuthoringValidationResult.Valid,
                recordCount,
                GameContentPackAccessDescriptor.WritableProjectContent);
        }

        private static GameContentRecordKey Key(string packId, string id)
        {
            return new GameContentRecordKey(
                StructuredFixtureProvider.OwnerId,
                packId,
                id,
                StructuredFixtureProvider.SourceId,
                id);
        }
    }
}
