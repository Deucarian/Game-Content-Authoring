using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.GameContentAuthoring.Editor;
using NUnit.Framework;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentReferenceEditingEditModeTests
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
        public void ReferenceValue_RepresentsNoneResolvedAndBrokenWithIdentitySemantics()
        {
            GameContentRecordKey key = Key("fixture-pack", "target.a");
            GameContentRecordReferenceValue none = GameContentRecordReferenceValue.None();
            GameContentRecordReferenceValue resolved = GameContentRecordReferenceValue.Resolved(key, "Target A", "Fixture");
            GameContentRecordReferenceValue renamed = GameContentRecordReferenceValue.Resolved(key, "Renamed", "Other Label");
            GameContentRecordReferenceValue broken = GameContentRecordReferenceValue.Broken(
                "missing.passive",
                "The passive no longer exists.",
                Key("fixture-pack", "missing.passive"));

            Assert.That(none.State, Is.EqualTo(GameContentRecordReferenceState.None));
            Assert.That(resolved.State, Is.EqualTo(GameContentRecordReferenceState.Resolved));
            Assert.That(broken.State, Is.EqualTo(GameContentRecordReferenceState.Broken));
            Assert.That(resolved, Is.EqualTo(renamed), "Display metadata must not replace canonical identity.");
            Assert.That(resolved.GetHashCode(), Is.EqualTo(renamed.GetHashCode()));
            Assert.That(broken.ToDisplayString(), Does.Contain("missing.passive"));
            Assert.That(GameContentFieldValue.FromRecordReference(resolved).FieldType,
                Is.EqualTo(GameContentFieldType.RecordReference));
        }

        [Test]
        public void ReferenceDescriptor_EnforcesRequiredNullableCapabilityPackAndRuntimeMetadata()
        {
            var metadata = new GameContentRecordReferenceFieldDescriptor(
                "Passive Upgrade",
                new[] { GameContentRecordCapabilities.Upgrade, GameContentRecordCapabilities.Passive },
                runtimeImpact: GameContentReferenceRuntimeImpact.Refresh | GameContentReferenceRuntimeImpact.Rebind,
                allowClear: true);
            var required = new GameContentFieldDescriptor(
                "passive",
                "fixture.passive",
                "Passive",
                string.Empty,
                GameContentFieldType.RecordReference,
                required: true,
                recordReference: metadata);
            var nullable = new GameContentFieldDescriptor(
                "optional",
                "fixture.optional",
                "Optional",
                string.Empty,
                GameContentFieldType.RecordReference,
                required: false,
                recordReference: metadata);

            Assert.That(required.IsValid, Is.True);
            Assert.That(metadata.RequiredCapabilities,
                Is.EqualTo(new[] { GameContentRecordCapabilities.Upgrade, GameContentRecordCapabilities.Passive }));
            Assert.That(metadata.PackPolicy, Is.EqualTo(GameContentReferencePackPolicy.SameSelectedPack));
            Assert.That((metadata.RuntimeImpact & GameContentReferenceRuntimeImpact.Refresh) != 0, Is.True);
            Assert.That(required.Accepts(GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.None()), out _), Is.False);
            Assert.That(nullable.Accepts(GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.None()), out _), Is.True);
            Assert.That(required.Accepts(
                GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.Broken("missing", "Missing target.")),
                out string brokenReason), Is.False);
            Assert.That(brokenReason, Does.Contain("Missing target"));
        }

        [Test]
        public void CandidateSelection_IsDeterministicPackSafeCapabilityFilteredAndProviderApproved()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;

            GameContentReferenceCandidateSet candidates = _coordinator.GetReferenceCandidates(active, "passive");

            Assert.That(candidates.Candidates.Select(value => value.Record.SourceRecordId),
                Is.EqualTo(new[] { "target.a", "target.b" }));
            Assert.That(candidates.Candidates.All(value => value.Evaluation.IsValid), Is.True);
            Assert.That(candidates.Rejections.Select(value => value.TargetKey.SourceRecordId),
                Does.Contain("target.wrong-capability"));
            Assert.That(candidates.Rejections.Select(value => value.TargetKey.SourceRecordId),
                Does.Contain("target.invalid"));
            Assert.That(candidates.Rejections.Select(value => value.TargetKey.SourceRecordId),
                Does.Contain("target.broken"));

            provider.RejectTargetB = true;
            candidates = _coordinator.GetReferenceCandidates(active, "passive");
            Assert.That(candidates.Candidates.Select(value => value.Record.SourceRecordId),
                Is.EqualTo(new[] { "target.a" }));
            Assert.That(candidates.Rejections.Single(value => value.TargetKey.SourceRecordId == "target.b").Reason,
                Does.Contain("domain compatibility"));
        }

        [Test]
        public void Evaluation_RejectsCraftedCrossPackMissingInvalidClaimAndProviderException()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;

            GameContentReferenceEvaluation crossPack = _coordinator.EvaluateReferenceTarget(
                active,
                "passive",
                Key("other-pack", "target.a"));
            GameContentReferenceEvaluation missing = _coordinator.EvaluateReferenceTarget(
                active,
                "passive",
                Key(provider.Pack.PackId, "target.missing"));
            provider.InvalidClaimTargetId = "target.b";
            GameContentReferenceEvaluation invalidClaim = _coordinator.EvaluateReferenceTarget(
                active,
                "passive",
                provider.TargetB.CanonicalKey);
            provider.ThrowEvaluation = true;
            GameContentReferenceEvaluation exception = _coordinator.EvaluateReferenceTarget(
                active,
                "passive",
                provider.TargetA.CanonicalKey);

            Assert.That(crossPack.IsValid, Is.False);
            Assert.That(crossPack.SamePackPolicySatisfied, Is.False);
            Assert.That(missing.IsValid, Is.False);
            Assert.That(invalidClaim.SourceClaimValid, Is.False);
            Assert.That(exception.IsValid, Is.False);
            Assert.That(exception.Reason, Does.Contain("evaluation exploded"));
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Clean));
        }

        [Test]
        public void ReferenceSession_StagesUndoRedoPreviewsCommitsAndRollsBackWithoutEarlyMutation()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            GameContentFieldValue targetB = Reference(provider.TargetB);

            Assert.That(_coordinator.Apply(active, "passive", targetB).Succeeded, Is.True);
            Assert.That(provider.CurrentTargetKey, Is.EqualTo(provider.TargetA.CanonicalKey));
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Dirty));
            Assert.That(_coordinator.Undo(active).Succeeded, Is.True);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Clean));
            Assert.That(_coordinator.Redo(active).Succeeded, Is.True);
            Assert.That(_coordinator.Preview(active).CanCommit, Is.True);

            GameContentCommitResult commit = _coordinator.Commit(active, true);
            Assert.That(commit.Succeeded, Is.True);
            Assert.That(provider.CurrentTargetKey, Is.EqualTo(provider.TargetB.CanonicalKey));
            Assert.That(commit.RequiresRefresh, Is.True);
            Assert.That(commit.RequiresRebind, Is.True);
            Assert.That(provider.EvaluationCount, Is.GreaterThanOrEqualTo(4));

            GameContentRollbackResult rollback = _coordinator.Rollback(active);
            Assert.That(rollback.Succeeded, Is.True);
            Assert.That(provider.CurrentTargetKey, Is.EqualTo(provider.TargetA.CanonicalKey));
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);
        }

        [Test]
        public void RequiredReferenceNoneAndBrokenValuesNeverReachProviderApply()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;

            GameContentEditOperationResult none = _coordinator.Apply(
                active,
                "passive",
                GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.None()));
            GameContentEditOperationResult broken = _coordinator.Apply(
                active,
                "passive",
                GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.Broken("missing", "Missing target.")));

            Assert.That(none.Succeeded, Is.False);
            Assert.That(broken.Succeeded, Is.False);
            Assert.That(provider.SessionApplyCount, Is.Zero);
            Assert.That(active.State, Is.EqualTo(GameContentEditSessionState.Clean));
        }

        [Test]
        public void ReferenceSession_CancelDiscardsStagingAndReleasesSourceLock()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "passive", Reference(provider.TargetB));

            GameContentRollbackResult cancel = _coordinator.Cancel(active);

            Assert.That(cancel.Succeeded, Is.True);
            Assert.That(provider.CurrentTargetKey, Is.EqualTo(provider.TargetA.CanonicalKey));
            Assert.That(_coordinator.ActiveSourceCount, Is.Zero);
            Assert.That(_coordinator.TryGetSession(provider.SourceRecord.CanonicalKey, out _), Is.False);
        }

        [Test]
        public void TargetDisappearanceAndCapabilityChangeBlockPreviewAndCommit()
        {
            var provider = NewProvider();
            GameContentPackContext context = Select(provider);
            GameContentActiveEditSession active = _coordinator.BeginEdit(context, provider.SourceRecord).Session;
            _coordinator.Apply(active, "passive", Reference(provider.TargetB));
            provider.RemoveTarget("target.b");

            GameContentValidationPreview disappeared = _coordinator.Preview(active);
            GameContentCommitResult disappearedCommit = _coordinator.Commit(active, true);

            Assert.That(disappeared.CanCommit, Is.False);
            Assert.That(disappeared.Issues.Any(value => value.Message.Contains("fresh target")), Is.True);
            Assert.That(disappearedCommit.Succeeded, Is.False);
            Assert.That(provider.CurrentTargetKey, Is.EqualTo(provider.TargetA.CanonicalKey));

            _coordinator.Cancel(active);
            provider = NewProvider();
            context = Select(provider);
            active = _coordinator.BeginEdit(context, provider.SourceRecord).Session;
            _coordinator.Apply(active, "passive", Reference(provider.TargetB));
            provider.RemoveCapability("target.b", GameContentRecordCapabilities.Passive);
            _coordinator.Reconcile(GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { provider }));

            GameContentValidationPreview changed = _coordinator.Preview(active);
            Assert.That(changed.CanCommit, Is.False);
            Assert.That(changed.Issues.Any(value => value.Message.Contains("capability")), Is.True);
        }

        [Test]
        public void ReferenceReview_ExposesTargetsInboundImpactSourceInboundAndRuntimeHints()
        {
            var provider = NewProvider();
            GameContentActiveEditSession active = Begin(provider).Session;
            _coordinator.Apply(active, "passive", Reference(provider.TargetB));

            GameContentReferenceChangeReview review = _coordinator.GetReferenceChangeReview(
                active,
                active.Changes.Single());

            Assert.That(review.SourceRecordKey, Is.EqualTo(provider.SourceRecord.CanonicalKey));
            Assert.That(review.OldTarget, Is.EqualTo(provider.TargetA));
            Assert.That(review.NewTarget, Is.EqualTo(provider.TargetB));
            Assert.That(review.OldTargetInboundDelta, Is.EqualTo(-1));
            Assert.That(review.NewTargetInboundDelta, Is.EqualTo(1));
            Assert.That(review.SourceInboundReferenceCount, Is.EqualTo(1));
            Assert.That((review.RuntimeImpact & GameContentReferenceRuntimeImpact.Refresh) != 0, Is.True);
            Assert.That((review.RuntimeImpact & GameContentReferenceRuntimeImpact.Rebind) != 0, Is.True);
        }

        [Test]
        public void AllPacksRemainsReadOnlyForReferenceProvider()
        {
            var provider = NewProvider();
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { provider });
            GameContentPackContext all = new GameContentPackSelectionState().Select(
                catalog,
                GameContentPackContext.AllPacksSelectionKey);

            Assert.That(_coordinator.GetAvailability(all, provider.SourceRecord).IsEditable, Is.False);
            Assert.That(_coordinator.BeginEdit(all, provider.SourceRecord).Succeeded, Is.False);
        }

        private GameContentEditBeginResult Begin(ReferenceEditPackProvider provider)
        {
            return _coordinator.BeginEdit(Select(provider), provider.SourceRecord, "upgrade");
        }

        private static GameContentPackContext Select(ReferenceEditPackProvider provider)
        {
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { provider });
            return new GameContentPackSelectionState().Select(catalog, provider.Pack.StableKey);
        }

        private static ReferenceEditPackProvider NewProvider()
        {
            return new ReferenceEditPackProvider("com.deucarian.tests.reference." + Guid.NewGuid().ToString("N"));
        }

        private static GameContentFieldValue Reference(GameContentRecordDescriptor record)
        {
            return GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.Resolved(
                record.CanonicalKey,
                record.DisplayName,
                record.SourcePath));
        }

        private static GameContentRecordKey Key(string packId, string recordId)
        {
            return new GameContentRecordKey(
                ReferenceEditPackProvider.OwnerId,
                packId,
                recordId,
                ReferenceEditPackProvider.SourceId,
                recordId);
        }

        private sealed class ReferenceEditPackProvider :
            IGameContentAuthoringProvider,
            IGameContentPackProvider,
            IGameContentPackEditProvider
        {
            public const string OwnerId = "com.deucarian.tests.references";
            public const string SourceId = "fixture-upgrades";
            private readonly List<GameContentRecordDescriptor> _records;
            private readonly GameContentSourceTarget _sourceTarget;

            public ReferenceEditPackProvider(string providerId)
            {
                ProviderId = providerId;
                var access = new GameContentPackAccessDescriptor(
                    GameContentPackBackendCapability.Read |
                    GameContentPackBackendCapability.Validate |
                    GameContentPackBackendCapability.RevealSource |
                    GameContentPackBackendCapability.EditExisting,
                    "Reference fixture");
                Pack = new GameContentPackDescriptor(
                    "fixture-pack",
                    OwnerId,
                    ProviderId,
                    "Reference Fixture",
                    string.Empty,
                    "1",
                    Array.Empty<string>(),
                    GameContentPackSourceKind.Project,
                    GameContentPackSourceState.Available,
                    "InMemory/References",
                    null,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<GameContentCategoryDescriptor>(),
                    Array.Empty<GameContentActionDescriptor>(),
                    GameContentAuthoringValidationResult.Valid,
                    6,
                    access);
                _sourceTarget = new GameContentSourceTarget(
                    "reference-source::" + providerId,
                    "Reference fixture source",
                    "Test memory only",
                    SourceId);
                SourceRecord = CreateRecord(
                    Pack.PackId,
                    "source.evolution",
                    new[] { GameContentRecordCapabilities.Upgrade, GameContentRecordCapabilities.Evolution },
                    GameContentAuthoringValidationResult.Valid,
                    inboundCount: 1,
                    outboundTarget: "target.a");
                TargetA = CreateRecord(
                    Pack.PackId,
                    "target.a",
                    new[] { GameContentRecordCapabilities.Upgrade, GameContentRecordCapabilities.Passive },
                    GameContentAuthoringValidationResult.Valid,
                    inboundCount: 1);
                TargetB = CreateRecord(
                    Pack.PackId,
                    "target.b",
                    new[] { GameContentRecordCapabilities.Upgrade, GameContentRecordCapabilities.Passive },
                    GameContentAuthoringValidationResult.Valid);
                GameContentRecordDescriptor wrong = CreateRecord(
                    Pack.PackId,
                    "target.wrong-capability",
                    new[] { GameContentRecordCapabilities.Upgrade },
                    GameContentAuthoringValidationResult.Valid);
                GameContentRecordDescriptor invalid = CreateRecord(
                    Pack.PackId,
                    "target.invalid",
                    new[] { GameContentRecordCapabilities.Upgrade, GameContentRecordCapabilities.Passive },
                    new GameContentAuthoringValidationResult(new[]
                    {
                        GameContentAuthoringValidationIssue.Error("target.invalid", "Fixture validation error.")
                    }));
                GameContentRecordDescriptor broken = CreateRecord(
                    Pack.PackId,
                    "target.broken",
                    new[] { GameContentRecordCapabilities.Upgrade, GameContentRecordCapabilities.Passive },
                    GameContentAuthoringValidationResult.Valid,
                    outboundTarget: "target.missing",
                    brokenOutbound: true);
                _records = new List<GameContentRecordDescriptor> { SourceRecord, TargetB, invalid, wrong, broken, TargetA };
                CurrentTargetKey = TargetA.CanonicalKey;
            }

            public string ProviderId { get; }
            public string DisplayName => "Reference Fixture";
            public string Description => string.Empty;
            public int SortOrder => 0;
            public bool Enabled => true;
            public GameContentPackDescriptor Pack { get; }
            public GameContentRecordDescriptor SourceRecord { get; }
            public GameContentRecordDescriptor TargetA { get; }
            public GameContentRecordDescriptor TargetB { get; private set; }
            public GameContentRecordKey CurrentTargetKey { get; set; }
            public int Revision { get; set; }
            public int EvaluationCount { get; set; }
            public int SessionApplyCount { get; set; }
            public bool RejectTargetB { get; set; }
            public bool ThrowEvaluation { get; set; }
            public string InvalidClaimTargetId { get; set; }

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
                return request.RecordKey.Equals(SourceRecord.CanonicalKey)
                    ? GameContentEditAvailability.Editable(ProviderId, 1, _sourceTarget)
                    : GameContentEditAvailability.ReadOnly("Only the source evolution is editable.", ProviderId);
            }

            public IGameContentEditSession BeginEdit(GameContentEditRequest request)
            {
                return new ReferenceEditSession(this, request.RecordKey, _sourceTarget);
            }

            public GameContentRecordDescriptor ResolveFresh(GameContentRecordKey key)
            {
                return key == null ? null : _records.FirstOrDefault(value => value.CanonicalKey.Equals(key));
            }

            public void RemoveTarget(string id)
            {
                _records.RemoveAll(value => string.Equals(value.SourceRecordId, id, StringComparison.Ordinal));
            }

            public void RemoveCapability(string id, GameContentRecordCapability capability)
            {
                int index = _records.FindIndex(value => string.Equals(value.SourceRecordId, id, StringComparison.Ordinal));
                GameContentRecordDescriptor current = index < 0 ? null : _records[index];
                if (current == null) return;
                GameContentRecordDescriptor replacement = CreateRecord(
                    Pack.PackId,
                    id,
                    current.Capabilities.Where(value => value != capability),
                    current.Validation);
                _records[index] = replacement;
                if (string.Equals(id, "target.b", StringComparison.Ordinal)) TargetB = replacement;
            }

            private static GameContentRecordDescriptor CreateRecord(
                string packId,
                string id,
                IEnumerable<GameContentRecordCapability> capabilities,
                GameContentAuthoringValidationResult validation,
                int inboundCount = 0,
                string outboundTarget = null,
                bool brokenOutbound = false)
            {
                GameContentRecordReferenceDescriptor[] inbound = Enumerable.Range(0, inboundCount)
                    .Select(index => new GameContentRecordReferenceDescriptor(
                        "inbound." + index,
                        "content",
                        packId,
                        "Inbound",
                        true,
                        true,
                        OwnerId,
                        Key(packId, "inbound." + index)))
                    .ToArray();
                GameContentRecordReferenceDescriptor[] outbound = string.IsNullOrWhiteSpace(outboundTarget)
                    ? Array.Empty<GameContentRecordReferenceDescriptor>()
                    : new[]
                    {
                        new GameContentRecordReferenceDescriptor(
                            outboundTarget,
                            "passives",
                            packId,
                            "Passive Requirement",
                            true,
                            !brokenOutbound,
                            OwnerId,
                            Key(packId, outboundTarget))
                    };
                return new GameContentRecordDescriptor(
                    packId + "::" + id,
                    id,
                    "content",
                    null,
                    id,
                    string.Empty,
                    string.Empty,
                    Array.Empty<GameContentMetadataDescriptor>(),
                    null,
                    "InMemory/References/" + id,
                    id,
                    outbound,
                    inbound,
                    validation,
                    0,
                    null,
                    string.Empty,
                    Key(packId, id),
                    capabilities);
            }
        }

        private sealed class ReferenceEditSession :
            IGameContentEditSession,
            IGameContentRecordReferenceEditSession
        {
            private readonly ReferenceEditPackProvider _provider;
            private readonly GameContentRecordKey _originalTarget;
            private readonly List<GameContentRecordReferenceValue> _history = new List<GameContentRecordReferenceValue>();
            private int _historyIndex;

            public ReferenceEditSession(
                ReferenceEditPackProvider provider,
                GameContentRecordKey recordKey,
                GameContentSourceTarget sourceTarget)
            {
                _provider = provider;
                RecordKey = recordKey;
                SourceTarget = sourceTarget;
                _originalTarget = provider.CurrentTargetKey;
                BackendId = provider.ProviderId;
                OriginalRevision = Revision(provider.Revision);
                Fields = new[]
                {
                    new GameContentFieldDescriptor(
                        "passive",
                        "fixture.required-passive",
                        "Required Passive",
                        "Evolution prerequisite.",
                        GameContentFieldType.RecordReference,
                        order: 10,
                        group: "Evolution",
                        required: true,
                        recordReference: new GameContentRecordReferenceFieldDescriptor(
                            "Passive Upgrade",
                            new[] { GameContentRecordCapabilities.Upgrade, GameContentRecordCapabilities.Passive },
                            runtimeImpact: GameContentReferenceRuntimeImpact.Refresh |
                                           GameContentReferenceRuntimeImpact.Rebind,
                            allowClear: false))
                };
                GameContentRecordDescriptor current = provider.ResolveFresh(provider.CurrentTargetKey);
                GameContentRecordReferenceValue baseline = GameContentRecordReferenceValue.Resolved(
                    provider.CurrentTargetKey,
                    current?.DisplayName,
                    current?.SourcePath);
                _history.Add(baseline);
                Snapshot = new GameContentEditSnapshot(
                    RecordKey,
                    SourceTarget,
                    OriginalRevision,
                    new Dictionary<string, GameContentFieldValue>(StringComparer.Ordinal)
                    {
                        ["passive"] = GameContentFieldValue.FromRecordReference(baseline)
                    },
                    DateTime.UtcNow,
                    "reference-fixture-v1");
                State = GameContentEditSessionState.Clean;
            }

            public string BackendId { get; }
            public GameContentRecordKey RecordKey { get; }
            public GameContentSourceTarget SourceTarget { get; }
            public GameContentSourceRevision OriginalRevision { get; }
            public GameContentEditSessionState State { get; private set; }
            public GameContentEditSnapshot Snapshot { get; }
            public IReadOnlyList<GameContentFieldDescriptor> Fields { get; }
            public IReadOnlyList<GameContentProposedChange> Changes
            {
                get
                {
                    GameContentFieldValue oldValue = Snapshot.FieldValues["passive"];
                    GameContentFieldValue currentValue = GameContentFieldValue.FromRecordReference(Current);
                    if (oldValue.Equals(currentValue)) return Array.Empty<GameContentProposedChange>();
                    return new[]
                    {
                        new GameContentProposedChange(
                            "passive",
                            oldValue,
                            currentValue,
                            "Required Passive",
                            "Evolution",
                            10)
                    };
                }
            }
            public bool CanUndo => _historyIndex > 0;
            public bool CanRedo => _historyIndex < _history.Count - 1;
            private GameContentRecordReferenceValue Current => _history[_historyIndex];

            public GameContentEditOperationResult Apply(string fieldId, GameContentFieldValue value)
            {
                _provider.SessionApplyCount++;
                if (!string.Equals(fieldId, "passive", StringComparison.Ordinal) ||
                    value?.FieldType != GameContentFieldType.RecordReference)
                    return GameContentEditOperationResult.Failure("Unsupported reference field.");
                if (!Fields[0].Accepts(value, out string reason))
                    return GameContentEditOperationResult.Failure(reason);
                GameContentReferenceEvaluation evaluation = EvaluateReferenceTarget(fieldId, value.RecordReferenceValue.TargetKey);
                if (!evaluation.IsValid) return GameContentEditOperationResult.Failure(evaluation.Reason);
                if (Current.Equals(value.RecordReferenceValue)) return GameContentEditOperationResult.Success("No change.");
                if (CanRedo) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add(value.RecordReferenceValue);
                _historyIndex++;
                RefreshState();
                return GameContentEditOperationResult.Success("Reference staged.");
            }

            public GameContentEditOperationResult Undo()
            {
                if (!CanUndo) return GameContentEditOperationResult.Failure("Nothing to undo.");
                _historyIndex--;
                RefreshState();
                return GameContentEditOperationResult.Success("Reference change undone.");
            }

            public GameContentEditOperationResult Redo()
            {
                if (!CanRedo) return GameContentEditOperationResult.Failure("Nothing to redo.");
                GameContentRecordReferenceValue value = _history[_historyIndex + 1];
                GameContentReferenceEvaluation evaluation = EvaluateReferenceTarget("passive", value.TargetKey);
                if (!evaluation.IsValid) return GameContentEditOperationResult.Failure(evaluation.Reason);
                _historyIndex++;
                RefreshState();
                return GameContentEditOperationResult.Success("Reference change redone.");
            }

            public GameContentValidationPreview Preview()
            {
                var issues = new List<GameContentAuthoringValidationIssue>();
                GameContentFieldValue value = GameContentFieldValue.FromRecordReference(Current);
                if (!Fields[0].Accepts(value, out string reason))
                    issues.Add(GameContentAuthoringValidationIssue.Error("passive", reason));
                else
                {
                    GameContentReferenceEvaluation evaluation = EvaluateReferenceTarget("passive", Current.TargetKey);
                    if (!evaluation.IsValid)
                        issues.Add(GameContentAuthoringValidationIssue.Error("passive", evaluation.Reason));
                }
                return new GameContentValidationPreview(issues);
            }

            public GameContentStaleCheckResult CheckStale()
            {
                GameContentSourceRevision current = Revision(_provider.Revision);
                return current.Equals(OriginalRevision)
                    ? GameContentStaleCheckResult.Current(current)
                    : GameContentStaleCheckResult.Stale("Fixture source changed.", current);
            }

            public GameContentCommitResult Commit(bool confirmWarnings)
            {
                GameContentStaleCheckResult stale = CheckStale();
                if (stale.IsStale) return GameContentCommitResult.Failure(stale.Message, OriginalRevision);
                GameContentValidationPreview preview = Preview();
                if (!preview.CanCommit) return GameContentCommitResult.Failure("Validation failed.", OriginalRevision);
                _provider.CurrentTargetKey = Current.TargetKey;
                _provider.Revision++;
                State = GameContentEditSessionState.Committed;
                return new GameContentCommitResult(
                    true,
                    "Reference committed.",
                    OriginalRevision,
                    Revision(_provider.Revision),
                    true,
                    true,
                    false);
            }

            public GameContentRollbackResult Rollback()
            {
                _provider.CurrentTargetKey = _originalTarget;
                _provider.Revision = 0;
                State = GameContentEditSessionState.RolledBack;
                return new GameContentRollbackResult(true, "Reference restored.", OriginalRevision);
            }

            public GameContentReferenceEvaluation EvaluateReferenceTarget(
                string fieldId,
                GameContentRecordKey targetKey)
            {
                _provider.EvaluationCount++;
                if (_provider.ThrowEvaluation) throw new InvalidOperationException("evaluation exploded");
                if (!string.Equals(fieldId, "passive", StringComparison.Ordinal))
                    return GameContentReferenceEvaluation.Rejected(targetKey, "Unknown reference field.");
                GameContentRecordDescriptor target = _provider.ResolveFresh(targetKey);
                if (target == null)
                    return GameContentReferenceEvaluation.Rejected(targetKey, "The fresh target no longer exists.");
                if (!target.HasCapability(GameContentRecordCapabilities.Upgrade) ||
                    !target.HasCapability(GameContentRecordCapabilities.Passive))
                {
                    return GameContentReferenceEvaluation.Rejected(
                        targetKey,
                        "The fresh target lost a required capability.",
                        requiredCapabilitiesSatisfied: false);
                }
                if (string.Equals(_provider.InvalidClaimTargetId, target.SourceRecordId, StringComparison.Ordinal))
                {
                    return GameContentReferenceEvaluation.Rejected(
                        targetKey,
                        "The target has an invalid source claim.",
                        sourceClaimValid: false,
                        providerCompatibilitySatisfied: true);
                }
                if (_provider.RejectTargetB && string.Equals(target.SourceRecordId, "target.b", StringComparison.Ordinal))
                    return GameContentReferenceEvaluation.Rejected(targetKey, "Fixture domain compatibility rejected the target.");
                return GameContentReferenceEvaluation.Approved(
                    targetKey,
                    GameContentReferenceRuntimeImpact.Rebind);
            }

            public void Dispose() { }

            private void RefreshState()
            {
                State = Changes.Count == 0 ? GameContentEditSessionState.Clean : GameContentEditSessionState.Dirty;
            }

            private static GameContentSourceRevision Revision(int revision)
            {
                return new GameContentSourceRevision("reference-revision-" + revision);
            }
        }
    }
}
