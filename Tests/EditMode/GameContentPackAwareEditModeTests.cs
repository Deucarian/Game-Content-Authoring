using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.GameContentAuthoring.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentPackAwareEditModeTests
    {
        [Test]
        public void CanonicalRecordKey_IncludesOwnerPackAndSourceRecord()
        {
            var key = new GameContentRecordKey(
                "com.deucarian.template.game.survivors",
                "basic-survivors",
                "weapon.survivors.arcane-wand",
                "weapons",
                "weapons[0]");

            Assert.That(key.StableKey, Is.EqualTo(
                "com.deucarian.template.game.survivors::basic-survivors::weapons::weapon.survivors.arcane-wand"));
            Assert.That(key.SourceId, Is.EqualTo("weapons"));
            Assert.That(key.LogicalLocator, Is.EqualTo("weapons[0]"));
        }

        [Test]
        public void OneRecord_CanMatchMultipleLensesWithoutChangingIdentity()
        {
            GameContentRecordDescriptor record = Record(
                "basic",
                "weapon.arcane-wand",
                GameContentRecordCapabilities.Weapon,
                GameContentRecordCapabilities.Attack);
            var attack = Lens("attack", GameContentRecordCapabilities.Attack);
            var weapon = Lens("weapon", GameContentRecordCapabilities.Weapon);

            Assert.That(attack.Matches(record), Is.True);
            Assert.That(weapon.Matches(record), Is.True);
            Assert.That(record.CanonicalKey.StableKey, Is.EqualTo(
                "com.deucarian.tests::basic::content::weapon.arcane-wand"));
        }

        [Test]
        public void SameSourceIdInDifferentPacks_RemainsDistinctAndResolvesPackSafely()
        {
            GameContentRecordDescriptor basic = Record("basic", "weapon.arc", GameContentRecordCapabilities.Weapon);
            GameContentRecordDescriptor neon = Record("neon", "weapon.arc", GameContentRecordCapabilities.Weapon);
            var provider = new TestPackProvider(
                "com.deucarian.tests.packs." + Guid.NewGuid().ToString("N"),
                new[]
                {
                    Pack("basic", "Basic", GameContentPackAccessDescriptor.ReadOnlyJson),
                    Pack("neon", "Neon", GameContentPackAccessDescriptor.ReadOnlyJson)
                },
                new Dictionary<string, IReadOnlyList<GameContentRecordDescriptor>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["basic"] = new[] { basic },
                    ["neon"] = new[] { neon }
                });
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new[] { provider });
            var selection = new GameContentPackSelectionState();
            GameContentPackContext basicContext = selection.Select(catalog, Pack("basic", "Basic", GameContentPackAccessDescriptor.ReadOnlyJson).StableKey);

            Assert.That(basic.CanonicalKey, Is.Not.EqualTo(neon.CanonicalKey));
            Assert.That(basicContext.ResolveRecord(basic.CanonicalKey), Is.SameAs(basic));
            Assert.That(basicContext.ResolveRecord(neon.CanonicalKey), Is.Null);

            var explicitNeonReference = new GameContentRecordReferenceDescriptor(
                neon.SourceRecordId,
                "weapons",
                "neon",
                "compares to",
                false,
                true,
                "com.deucarian.tests",
                neon.CanonicalKey);
            Assert.That(basicContext.ResolveReference(basic, explicitNeonReference), Is.Null);
            GameContentPackContext allContext = selection.Select(catalog, GameContentPackContext.AllPacksSelectionKey);
            Assert.That(allContext.ResolveReference(basic, explicitNeonReference), Is.SameAs(neon));
        }

        [Test]
        public void PackSelection_PersistsAcrossLensChangesAndFallsBackWhenUnavailable()
        {
            var provider = new TestPackProvider(
                "com.deucarian.tests.selection." + Guid.NewGuid().ToString("N"),
                new[]
                {
                    Pack("basic", "Basic", GameContentPackAccessDescriptor.ReadOnlyJson),
                    Pack("neon", "Neon", GameContentPackAccessDescriptor.ReadOnlyJson)
                });
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new[] { provider });
            var state = new GameContentPackSelectionState();
            string neonKey = provider.Packs.Single(pack => pack.PackId == "neon").StableKey;

            GameContentPackContext selected = state.Select(catalog, neonKey);
            Assert.That(selected.Pack.PackId, Is.EqualTo("neon"));
            Assert.That(state.Refresh(catalog).Pack.PackId, Is.EqualTo("neon"));

            var basicOnly = new TestPackProvider(
                "com.deucarian.tests.selection.basic." + Guid.NewGuid().ToString("N"),
                new[] { Pack("basic", "Basic", GameContentPackAccessDescriptor.ReadOnlyJson) });
            GameContentPackContext fallback = state.Refresh(GameContentPackCatalog.Build(new[] { basicOnly }));
            Assert.That(fallback.Pack.PackId, Is.EqualTo("basic"));
        }

        [Test]
        public void AllPacksAndConflictedPacks_AreNeverWritable()
        {
            GameContentPackDescriptor duplicate = Pack(
                "project",
                "Project",
                GameContentPackAccessDescriptor.WritableProjectContent);
            var first = new TestPackProvider("com.deucarian.tests.first." + Guid.NewGuid().ToString("N"), new[] { duplicate });
            var second = new TestPackProvider("com.deucarian.tests.second." + Guid.NewGuid().ToString("N"), new[] { duplicate });
            GameContentPackCatalog catalog = GameContentPackCatalog.Build(new[] { first, second });
            var state = new GameContentPackSelectionState();

            GameContentPackContext conflict = state.Select(catalog, duplicate.StableKey);
            GameContentPackContext all = state.Select(catalog, GameContentPackContext.AllPacksSelectionKey);

            Assert.That(conflict.SelectedEntry.IsConflict, Is.True);
            Assert.That(conflict.Access.IsWritable, Is.False);
            Assert.That(conflict.SourceStatusLabel, Is.EqualTo("Conflict"));
            Assert.That(all.IsAllPacks, Is.True);
            Assert.That(all.Access.IsWritable, Is.False);
            Assert.That(all.AccessStatusLabel, Is.EqualTo("Read-only"));
        }

        [Test]
        public void PackContext_ReportsSourceAndAccessStatusIndependently()
        {
            GameContentPackDescriptor writable = Pack(
                "project",
                "Project",
                GameContentPackAccessDescriptor.WritableProjectContent);
            var provider = new TestPackProvider(
                "com.deucarian.tests.status." + Guid.NewGuid().ToString("N"),
                new[] { writable });
            GameContentPackContext context = new GameContentPackSelectionState().Select(
                GameContentPackCatalog.Build(new[] { provider }),
                writable.StableKey);

            Assert.That(context.SourceStatusLabel, Is.EqualTo("Available"));
            Assert.That(context.AccessStatusLabel, Is.EqualTo("Writable"));
        }

        [Test]
        public void RecordSelection_MovesBetweenCompatibleLensesWithoutChangingIdentity()
        {
            GameContentRecordDescriptor record = Record(
                "basic",
                "weapon.arcane-wand",
                GameContentRecordCapabilities.Attack,
                GameContentRecordCapabilities.Weapon);
            var provider = new TestPackProvider(
                "com.deucarian.tests.record-selection." + Guid.NewGuid().ToString("N"),
                new[] { Pack("basic", "Basic", GameContentPackAccessDescriptor.ReadOnlyJson) },
                new Dictionary<string, IReadOnlyList<GameContentRecordDescriptor>>
                {
                    ["basic"] = new[] { record }
                });
            GameContentPackContext context = new GameContentPackSelectionState().Refresh(
                GameContentPackCatalog.Build(new[] { provider }));
            var selection = new GameContentRecordSelectionState();
            selection.Select(record);

            Assert.That(Lens("attack", GameContentRecordCapabilities.Attack).Matches(selection.Resolve(context)), Is.True);
            Assert.That(Lens("weapon", GameContentRecordCapabilities.Weapon).Matches(selection.Resolve(context)), Is.True);
            Assert.That(selection.Resolve(context).CanonicalKey, Is.EqualTo(record.CanonicalKey));
        }

        [Test]
        public void PackValidationDispatcher_ConvertsProviderExceptionsToValidationErrors()
        {
            GameContentPackDescriptor pack = Pack("basic", "Basic", GameContentPackAccessDescriptor.ReadOnlyJson);
            var provider = new ThrowingValidationPackProvider(pack);

            GameContentAuthoringValidationResult result = GameContentPackActionDispatcher.Validate(provider, pack);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.Issues[0].Message, Does.Contain("validation exploded"));
        }

        [Test]
        public void ProjectContent_ProjectsExistingRecordsAndIsTheOnlyWritableBuiltInBackend()
        {
            var item = new GameContentLibraryItem(
                "attack-key",
                null,
                GameContentLibraryKind.Attack,
                "Attacks",
                "Assets/GameContent/Attacks/Arc.asset",
                "attack.arc",
                "Arc");
            var report = new GameContentLibraryReport(
                GameContentLibraryProvider.DefaultRoot,
                new[] { item },
                Array.Empty<GameContentLibraryIssue>());

            GameContentPackDescriptor pack = GameContentProjectPackProjection.BuildPack(
                GameContentLibraryProvider.ContentLibraryProviderId,
                report);
            GameContentRecordDescriptor record = GameContentProjectPackProjection.BuildRecords(report).Single();

            Assert.That(pack.DisplayName, Is.EqualTo("Project Content"));
            Assert.That(pack.Access.CanCreate, Is.True);
            Assert.That(pack.Access.CanEditExisting, Is.True);
            Assert.That(record.HasCapability(GameContentRecordCapabilities.Attack), Is.True);
            Assert.That(record.CanonicalKey.OwningPackageId, Is.EqualTo(GameContentProjectPackProjection.OwningPackageId));
            Assert.That(record.CanonicalKey.PackId, Is.EqualTo(GameContentProjectPackProjection.PackId));
            Assert.That(GameContentPackAccessDescriptor.ReadOnlyJson.CanCreate, Is.False);
            Assert.That(GameContentPackAccessDescriptor.ReadOnlyAggregate.CanCreate, Is.False);
        }

        [Test]
        public void NamedPackClaim_RemovesOnlyClaimedAssetFromProjectContentAndAllPacks()
        {
            string root = CreateClaimTestRoot();
            try
            {
                AttackDefinitionAsset claimedAsset = CreateClaimTestAsset(root, "Claimed", "attack.claimed");
                AttackDefinitionAsset unclaimedAsset = CreateClaimTestAsset(root, "Unclaimed", "attack.unclaimed");
                TestPackProvider projectProvider = ProjectProvider(claimedAsset, unclaimedAsset);
                GameContentPackDescriptor namedPack = PackWithState("named", "Named", GameContentPackSourceState.Available);
                GameContentRecordDescriptor namedRecord = SourceRecord(namedPack, claimedAsset, "attack.claimed");
                var namedProvider = new ClaimingTestPackProvider(
                    "com.deucarian.tests.claims.named",
                    namedPack,
                    new[] { namedRecord },
                    new[] { GameContentSourceClaim.ForAsset(claimedAsset) });

                GameContentPackCatalog first = GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { projectProvider, namedProvider });
                GameContentPackCatalog second = GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { projectProvider, namedProvider });
                GameContentPackCatalogEntry project = first.Find(ProjectStableKey());
                GameContentPackContext namedContext = new GameContentPackSelectionState().Select(first, namedPack.StableKey);

                Assert.That(project, Is.Not.Null);
                Assert.That(namedContext.IsProjectContent, Is.False);
                Assert.That(namedContext.Access.IsWritable, Is.False);
                Assert.That(project.Records.Select(record => record.SourceRecordId), Is.EqualTo(new[] { "attack.unclaimed" }));
                Assert.That(first.AllRecords.Count(record => record.SourceAsset == claimedAsset), Is.EqualTo(1));
                Assert.That(first.AllRecords.Single(record => record.SourceAsset == claimedAsset).CanonicalKey, Is.EqualTo(namedRecord.CanonicalKey));
                Assert.That(first.AllRecords.Count, Is.EqualTo(2));
                Assert.That(first.SourceClaimConflicts, Is.Empty);
                GameContentLibraryReport filteredReport = GameContentLibraryService.Scan(
                    root,
                    first.ClaimedSourceIdentities);
                Assert.That(filteredReport.Items.Select(item => item.Id), Is.EqualTo(new[] { "attack.unclaimed" }));
                Assert.That(second.AllRecords.Select(record => record.CanonicalKey.StableKey),
                    Is.EqualTo(first.AllRecords.Select(record => record.CanonicalKey.StableKey)));
                GameContentSourceIdentity.TryCreate(claimedAsset, AssetDatabase.GetAssetPath(claimedAsset), out GameContentSourceIdentity beforeRename);
                Assert.That(AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(claimedAsset), "ClaimedRenamed"), Is.Empty);
                GameContentSourceIdentity.TryCreate(claimedAsset, AssetDatabase.GetAssetPath(claimedAsset), out GameContentSourceIdentity afterRename);
                Assert.That(afterRename, Is.EqualTo(beforeRename));
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
            }
        }

        [Test]
        public void DuplicateNamedPackClaims_CreateVisibleConflictAndNeverExposeProjectRecord()
        {
            string root = CreateClaimTestRoot();
            try
            {
                AttackDefinitionAsset asset = CreateClaimTestAsset(root, "Shared", "attack.shared");
                TestPackProvider projectProvider = ProjectProvider(asset);
                GameContentPackDescriptor firstPack = PackWithState("first", "First", GameContentPackSourceState.Available);
                GameContentPackDescriptor secondPack = PackWithState("second", "Second", GameContentPackSourceState.Available);
                GameContentSourceClaim claim = GameContentSourceClaim.ForAsset(asset);
                var first = new ClaimingTestPackProvider(
                    "com.deucarian.tests.claims.first",
                    firstPack,
                    new[] { SourceRecord(firstPack, asset, "attack.shared") },
                    new[] { claim });
                var second = new ClaimingTestPackProvider(
                    "com.deucarian.tests.claims.second",
                    secondPack,
                    new[] { SourceRecord(secondPack, asset, "attack.shared") },
                    new[] { claim });

                GameContentPackCatalog catalog = GameContentPackCatalog.Build(
                    new IGameContentAuthoringProvider[] { projectProvider, first, second });

                Assert.That(catalog.SourceClaimConflicts.Count, Is.EqualTo(1));
                Assert.That(catalog.SourceClaimConflicts[0].ClaimantPackKeys,
                    Is.EquivalentTo(new[] { firstPack.StableKey, secondPack.StableKey }));
                Assert.That(catalog.Find(firstPack.StableKey).IsConflict, Is.True);
                Assert.That(catalog.Find(secondPack.StableKey).IsConflict, Is.True);
                Assert.That(catalog.Find(ProjectStableKey()).Records, Is.Empty);
                Assert.That(catalog.Find(ProjectStableKey()).Pack.Validation.ErrorCount, Is.EqualTo(1));
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
            }
        }

        [Test]
        public void MissingNamedPack_DoesNotHideOtherwiseDiscoverableProjectContent()
        {
            string root = CreateClaimTestRoot();
            try
            {
                AttackDefinitionAsset asset = CreateClaimTestAsset(root, "Available", "attack.available");
                TestPackProvider projectProvider = ProjectProvider(asset);
                GameContentPackDescriptor missingPack = PackWithState("missing", "Missing", GameContentPackSourceState.MissingSource);
                var missing = new ClaimingTestPackProvider(
                    "com.deucarian.tests.claims.missing",
                    missingPack,
                    Array.Empty<GameContentRecordDescriptor>(),
                    new[] { GameContentSourceClaim.ForAsset(asset) });

                GameContentPackCatalog catalog = GameContentPackCatalog.Build(
                    new IGameContentAuthoringProvider[] { projectProvider, missing });

                Assert.That(catalog.Find(ProjectStableKey()).Records.Single().SourceAsset, Is.SameAs(asset));
                Assert.That(catalog.SourceClaimConflicts, Is.Empty);
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
            }
        }

        [Test]
        public void ClaimedSourceValidation_DoesNotBleedIntoProjectContentPack()
        {
            string root = CreateClaimTestRoot();
            try
            {
                AttackDefinitionAsset asset = CreateClaimTestAsset(root, "InvalidClaimed", "attack.invalid-claimed");
                var item = new GameContentLibraryItem(
                    "claimed-error",
                    asset,
                    GameContentLibraryKind.Attack,
                    "Attacks",
                    AssetDatabase.GetAssetPath(asset),
                    asset.Id,
                    asset.DisplayName);
                item.AddIssue(GameContentLibraryIssue.Error("Attack", "Claimed source error."));
                var report = new GameContentLibraryReport(
                    GameContentLibraryProvider.DefaultRoot,
                    new[] { item },
                    Array.Empty<GameContentLibraryIssue>());
                GameContentPackDescriptor projectPack = GameContentProjectPackProjection.BuildPack(
                    GameContentLibraryProvider.ContentLibraryProviderId,
                    report);
                var projectProvider = new TestPackProvider(
                    GameContentLibraryProvider.ContentLibraryProviderId,
                    new[] { projectPack },
                    new Dictionary<string, IReadOnlyList<GameContentRecordDescriptor>>
                    {
                        [GameContentProjectPackProjection.PackId] = GameContentProjectPackProjection.BuildRecords(report)
                    });
                GameContentPackDescriptor namedPack = PackWithState("validation-owner", "Validation Owner", GameContentPackSourceState.Available);
                var named = new ClaimingTestPackProvider(
                    "com.deucarian.tests.claims.validation-owner",
                    namedPack,
                    new[] { SourceRecord(namedPack, asset, asset.Id) },
                    new[] { GameContentSourceClaim.ForAsset(asset) });

                GameContentPackCatalog catalog = GameContentPackCatalog.Build(
                    new IGameContentAuthoringProvider[] { projectProvider, named });

                Assert.That(catalog.Find(ProjectStableKey()).Records, Is.Empty);
                Assert.That(catalog.Find(ProjectStableKey()).Pack.Validation.ErrorCount, Is.Zero);
                Assert.That(catalog.Find(namedPack.StableKey).Records.Single().SourceAsset, Is.SameAs(asset));
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
            }
        }

        [Test]
        public void AuthoringContext_RequiresAnExplicitWritablePackForCreation()
        {
            var noPack = new GameContentAuthoringContext(null, "test", null, null, null);
            GameContentPackDescriptor writablePack = Pack(
                "project",
                "Project",
                GameContentPackAccessDescriptor.WritableProjectContent);
            var provider = new TestPackProvider(
                "com.deucarian.tests.creation." + Guid.NewGuid().ToString("N"),
                new[] { writablePack });
            GameContentPackContext writable = new GameContentPackSelectionState().Select(
                GameContentPackCatalog.Build(new[] { provider }),
                writablePack.StableKey);
            var withPack = new GameContentAuthoringContext(null, "test", null, null, null, writable);

            Assert.That(noPack.CanCreate, Is.False);
            Assert.That(withPack.CanCreate, Is.True);
        }

        [Test]
        public void AllContentModels_FilterSearchAndSortPackRecords()
        {
            GameContentRecordDescriptor attack = Record(
                "basic",
                "weapon.arc",
                GameContentRecordCapabilities.Attack,
                GameContentRecordCapabilities.Weapon);
            GameContentRecordDescriptor enemy = Record(
                "neon",
                "enemy.rusher",
                GameContentRecordCapabilities.Enemy);
            var attackFilter = new GameContentAllContentBrowserState
            {
                CapabilityId = GameContentRecordCapabilities.Attack.Id,
                SourceId = "content",
                SortMode = GameContentRecordSortMode.DisplayName
            };

            Assert.That(GameContentAllContentBrowser.Matches(attackFilter, attack), Is.True);
            Assert.That(GameContentAllContentBrowser.Matches(attackFilter, enemy), Is.False);
            Assert.That(GameContentRecordLensBrowser.MatchesSearch(attack, "Basic Survivors", "survivors"), Is.True);
            Assert.That(GameContentRecordLensBrowser.MatchesSearch(enemy, "Neon Arcana", "weapon"), Is.False);
            Assert.That(
                GameContentAllContentBrowser.ApplySort(
                        new[] { enemy, attack },
                        GameContentRecordSortMode.DisplayName)
                    .Select(record => record.DisplayName),
                Is.EqualTo(new[] { "enemy.rusher", "weapon.arc" }));
        }

        [Test]
        public void ProjectionAdapters_RegisterDeterministicallyAndRejectDuplicateIds()
        {
            string prefix = "adapter." + Guid.NewGuid().ToString("N");
            var late = new TestProjectionAdapter(prefix + ".late", 20, "late");
            var early = new TestProjectionAdapter(prefix + ".early", 10, "early");
            try
            {
                Assert.That(GameContentRecordProjectionRegistry<TestProjection>.Register(late), Is.True);
                Assert.That(GameContentRecordProjectionRegistry<TestProjection>.Register(early), Is.True);
                Assert.That(GameContentRecordProjectionRegistry<TestProjection>.Register(
                    new TestProjectionAdapter(prefix + ".early", 0, "duplicate")), Is.False);
                Assert.That(GameContentRecordProjectionRegistry<TestProjection>.Adapters
                    .Where(adapter => adapter.AdapterId.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(adapter => adapter.AdapterId),
                    Is.EqualTo(new[] { prefix + ".early", prefix + ".late" }));
                Assert.That(GameContentRecordProjectionRegistry<TestProjection>.TryProject(
                    Record("basic", "record", GameContentRecordCapabilities.Attack),
                    out TestProjection projection), Is.True);
                Assert.That(projection.Value, Is.EqualTo("early"));
            }
            finally
            {
                GameContentRecordProjectionRegistry<TestProjection>.Unregister(early.AdapterId);
                GameContentRecordProjectionRegistry<TestProjection>.Unregister(late.AdapterId);
            }
        }

        [Test]
        public void ExistingRegistry_RejectsDuplicateLensIdsWithoutASecondRegistry()
        {
            string lensId = "test-lens-" + Guid.NewGuid().ToString("N");
            string firstId = "com.deucarian.tests.lens.first." + Guid.NewGuid().ToString("N");
            string secondId = "com.deucarian.tests.lens.second." + Guid.NewGuid().ToString("N");
            GameContentAuthoringProviderRegistry.Register(new TestLensProvider(firstId, lensId));
            GameContentAuthoringProviderRegistry.Register(new TestLensProvider(secondId, lensId));

            Assert.That(GameContentAuthoringProviderRegistry.Providers.Count(provider =>
                provider is IGameContentAuthoringLensProvider lens && lens.Lens.LensId == lensId), Is.EqualTo(1));
            Assert.That(GameContentAuthoringProviderRegistry.IsProviderRegistered(firstId), Is.True);
            Assert.That(GameContentAuthoringProviderRegistry.IsProviderRegistered(secondId), Is.False);
        }

        private static GameContentLensDescriptor Lens(string id, params GameContentRecordCapability[] capabilities)
        {
            return new GameContentLensDescriptor(id, id, "Tests", id, 0, capabilities);
        }

        private static GameContentPackDescriptor Pack(
            string packId,
            string displayName,
            GameContentPackAccessDescriptor access)
        {
            return new GameContentPackDescriptor(
                packId,
                "com.deucarian.tests",
                "com.deucarian.tests.provider",
                displayName,
                string.Empty,
                "1",
                Array.Empty<string>(),
                GameContentPackSourceKind.ImportedSample,
                GameContentPackSourceState.Available,
                "Assets/" + packId,
                null,
                null,
                null,
                null,
                null,
                Array.Empty<GameContentCategoryDescriptor>(),
                Array.Empty<GameContentActionDescriptor>(),
                GameContentAuthoringValidationResult.Valid,
                0,
                access);
        }

        private static GameContentPackDescriptor PackWithState(
            string packId,
            string displayName,
            GameContentPackSourceState state)
        {
            return new GameContentPackDescriptor(
                packId,
                "com.deucarian.tests",
                "com.deucarian.tests.claim-provider",
                displayName,
                string.Empty,
                "1",
                Array.Empty<string>(),
                GameContentPackSourceKind.Project,
                state,
                "Assets/GameContent/" + packId,
                null,
                null,
                null,
                null,
                null,
                new[] { new GameContentCategoryDescriptor("attacks", "Attacks", string.Empty, "attacks", 0, 1) },
                Array.Empty<GameContentActionDescriptor>(),
                GameContentAuthoringValidationResult.Valid,
                1,
                GameContentPackAccessDescriptor.ReadOnlyJson);
        }

        private static string CreateClaimTestRoot()
        {
            string root = "Assets/GameContent/SourceClaimTests_" + Guid.NewGuid().ToString("N");
            if (!AssetDatabase.IsValidFolder("Assets/GameContent")) AssetDatabase.CreateFolder("Assets", "GameContent");
            AssetDatabase.CreateFolder("Assets/GameContent", System.IO.Path.GetFileName(root));
            return root;
        }

        private static AttackDefinitionAsset CreateClaimTestAsset(string root, string name, string id)
        {
            var asset = ScriptableObject.CreateInstance<AttackDefinitionAsset>();
            asset.Id = id;
            asset.DisplayName = name;
            AssetDatabase.CreateAsset(asset, root + "/" + name + ".asset");
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static TestPackProvider ProjectProvider(params AttackDefinitionAsset[] assets)
        {
            GameContentLibraryItem[] items = assets.Select(asset => new GameContentLibraryItem(
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)),
                asset,
                GameContentLibraryKind.Attack,
                "Attacks",
                AssetDatabase.GetAssetPath(asset),
                asset.Id,
                asset.DisplayName)).ToArray();
            var report = new GameContentLibraryReport(
                GameContentLibraryProvider.DefaultRoot,
                items,
                Array.Empty<GameContentLibraryIssue>());
            GameContentPackDescriptor pack = GameContentProjectPackProjection.BuildPack(
                GameContentLibraryProvider.ContentLibraryProviderId,
                report);
            return new TestPackProvider(
                GameContentLibraryProvider.ContentLibraryProviderId,
                new[] { pack },
                new Dictionary<string, IReadOnlyList<GameContentRecordDescriptor>>
                {
                    [GameContentProjectPackProjection.PackId] = GameContentProjectPackProjection.BuildRecords(report)
                });
        }

        private static GameContentRecordDescriptor SourceRecord(
            GameContentPackDescriptor pack,
            AttackDefinitionAsset asset,
            string sourceRecordId)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            GameContentSourceIdentity.TryCreate(asset, path, out GameContentSourceIdentity identity);
            var key = new GameContentRecordKey(
                pack.OwningPackageId,
                pack.PackId,
                sourceRecordId,
                identity.StableKey,
                path);
            return new GameContentRecordDescriptor(
                pack.PackId + "::attacks::" + sourceRecordId,
                sourceRecordId,
                "attacks",
                null,
                asset.DisplayName,
                string.Empty,
                string.Empty,
                Array.Empty<GameContentMetadataDescriptor>(),
                asset,
                path,
                identity.StableKey,
                Array.Empty<GameContentRecordReferenceDescriptor>(),
                Array.Empty<GameContentRecordReferenceDescriptor>(),
                GameContentAuthoringValidationResult.Valid,
                0,
                asset,
                "attacks",
                key,
                new[] { GameContentRecordCapabilities.Attack });
        }

        private static string ProjectStableKey()
        {
            return GameContentPackDescriptor.BuildStableKey(
                GameContentProjectPackProjection.OwningPackageId,
                GameContentProjectPackProjection.PackId);
        }

        private static GameContentRecordDescriptor Record(
            string packId,
            string sourceRecordId,
            params GameContentRecordCapability[] capabilities)
        {
            return new GameContentRecordDescriptor(
                packId + "::content::" + sourceRecordId,
                sourceRecordId,
                "content",
                null,
                sourceRecordId,
                string.Empty,
                string.Empty,
                Array.Empty<GameContentMetadataDescriptor>(),
                null,
                "Assets/" + packId + "/content.json",
                "records[0]",
                Array.Empty<GameContentRecordReferenceDescriptor>(),
                Array.Empty<GameContentRecordReferenceDescriptor>(),
                GameContentAuthoringValidationResult.Valid,
                0,
                null,
                string.Empty,
                new GameContentRecordKey("com.deucarian.tests", packId, sourceRecordId, "content", "records[0]"),
                capabilities);
        }

        private sealed class TestPackProvider : IGameContentAuthoringProvider, IGameContentPackProvider
        {
            private readonly IReadOnlyDictionary<string, IReadOnlyList<GameContentRecordDescriptor>> _records;

            public TestPackProvider(
                string providerId,
                IReadOnlyList<GameContentPackDescriptor> packs,
                IReadOnlyDictionary<string, IReadOnlyList<GameContentRecordDescriptor>> records = null)
            {
                ProviderId = providerId;
                Packs = packs;
                _records = records ?? new Dictionary<string, IReadOnlyList<GameContentRecordDescriptor>>();
            }

            public IReadOnlyList<GameContentPackDescriptor> Packs { get; }
            public string ProviderId { get; }
            public string DisplayName => "Test Packs";
            public string Description => string.Empty;
            public int SortOrder => 0;
            public bool Enabled => true;
            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }
            public IReadOnlyList<GameContentPackDescriptor> GetContentPacks() => Packs;
            public IReadOnlyList<GameContentRecordDescriptor> GetRecords(string packId) =>
                _records.TryGetValue(packId ?? string.Empty, out IReadOnlyList<GameContentRecordDescriptor> records)
                    ? records
                    : Array.Empty<GameContentRecordDescriptor>();
            public GameContentAuthoringValidationResult ValidatePack(string packId) => GameContentAuthoringValidationResult.Valid;
            public GameContentActionResult ExecuteAction(string packId, string actionId) => GameContentActionResult.Success("ok");
        }

        private sealed class ClaimingTestPackProvider :
            IGameContentAuthoringProvider,
            IGameContentPackProvider,
            IGameContentSourceClaimProvider
        {
            private readonly GameContentPackDescriptor _pack;
            private readonly IReadOnlyList<GameContentRecordDescriptor> _records;
            private readonly IReadOnlyList<GameContentSourceClaim> _claims;

            public ClaimingTestPackProvider(
                string providerId,
                GameContentPackDescriptor pack,
                IReadOnlyList<GameContentRecordDescriptor> records,
                IReadOnlyList<GameContentSourceClaim> claims)
            {
                ProviderId = providerId;
                _pack = pack;
                _records = records;
                _claims = claims;
            }

            public string ProviderId { get; }
            public string DisplayName => "Claiming Test Pack";
            public string Description => string.Empty;
            public int SortOrder => 0;
            public bool Enabled => true;
            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }
            public IReadOnlyList<GameContentPackDescriptor> GetContentPacks() => new[] { _pack };
            public IReadOnlyList<GameContentRecordDescriptor> GetRecords(string packId) => _records;
            public IReadOnlyList<GameContentSourceClaim> GetSourceClaims(string packId) => _claims;
            public GameContentAuthoringValidationResult ValidatePack(string packId) => _pack.Validation;
            public GameContentActionResult ExecuteAction(string packId, string actionId) => GameContentActionResult.Success("ok");
        }

        private sealed class TestProjection
        {
            public TestProjection(string value) { Value = value; }
            public string Value { get; }
        }

        private sealed class ThrowingValidationPackProvider : IGameContentPackProvider
        {
            private readonly GameContentPackDescriptor _pack;
            public ThrowingValidationPackProvider(GameContentPackDescriptor pack) { _pack = pack; }
            public IReadOnlyList<GameContentPackDescriptor> GetContentPacks() => new[] { _pack };
            public IReadOnlyList<GameContentRecordDescriptor> GetRecords(string packId) => Array.Empty<GameContentRecordDescriptor>();
            public GameContentAuthoringValidationResult ValidatePack(string packId) =>
                throw new InvalidOperationException("validation exploded");
            public GameContentActionResult ExecuteAction(string packId, string actionId) =>
                GameContentActionResult.Failure("unsupported");
        }

        private sealed class TestProjectionAdapter : IGameContentRecordProjectionAdapter<TestProjection>
        {
            private readonly string _value;
            public TestProjectionAdapter(string adapterId, int sortOrder, string value)
            {
                AdapterId = adapterId;
                SortOrder = sortOrder;
                _value = value;
            }
            public string AdapterId { get; }
            public int SortOrder { get; }
            public bool TryProject(GameContentRecordDescriptor record, out TestProjection projection)
            {
                projection = new TestProjection(_value);
                return true;
            }
        }

        private sealed class TestLensProvider : IGameContentAuthoringProvider, IGameContentAuthoringLensProvider
        {
            public TestLensProvider(string providerId, string lensId)
            {
                ProviderId = providerId;
                Lens = LensDescriptor(lensId);
            }
            public string ProviderId { get; }
            public string DisplayName => "Test Lens";
            public string Description => string.Empty;
            public int SortOrder => 0;
            public bool Enabled => true;
            public GameContentLensDescriptor Lens { get; }
            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }

            private static GameContentLensDescriptor LensDescriptor(string id)
            {
                return new GameContentLensDescriptor(id, id, "Tests", id, 0, new[] { GameContentRecordCapabilities.Attack });
            }
        }
    }
}
