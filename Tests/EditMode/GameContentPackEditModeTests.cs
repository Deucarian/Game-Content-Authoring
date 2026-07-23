using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.GameContentAuthoring.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentPackEditModeTests
    {
        private string _root;
        private string _providerId;
        private TestPackProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _root = "Assets/GameContentPackTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(_root));
            _providerId = "com.deucarian.tests.pack-provider." + Guid.NewGuid().ToString("N");
            _provider = new TestPackProvider(_providerId);
            GameContentAuthoringProviderRegistry.Register(_provider);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!string.IsNullOrWhiteSpace(_root) && AssetDatabase.IsValidFolder(_root))
                AssetDatabase.DeleteAsset(_root);
            AssetDatabase.Refresh();
        }

        [Test]
        public void OptionalPackProvider_UsesExistingRegistryAndRejectsDuplicateProviderIds()
        {
            int before = GameContentAuthoringProviderRegistry.Providers.Count(provider =>
                string.Equals(provider.ProviderId, _providerId, StringComparison.OrdinalIgnoreCase));

            GameContentAuthoringProviderRegistry.Register(new TestPackProvider(_providerId));

            Assert.That(_provider, Is.InstanceOf<IGameContentAuthoringProvider>());
            Assert.That(_provider, Is.InstanceOf<IGameContentAuthoringSurfaceProvider>());
            Assert.That(_provider, Is.InstanceOf<IGameContentPackProvider>());
            Assert.That(before, Is.EqualTo(1));
            Assert.That(GameContentAuthoringProviderRegistry.Providers.Count(provider =>
                string.Equals(provider.ProviderId, _providerId, StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
            Assert.That(GameContentAuthoringProviderRegistry.IsProviderRegistered(GameContentLibraryProvider.ContentLibraryProviderId), Is.True);
            Assert.That(GameContentAuthoringWindow.MenuPath, Is.EqualTo("Tools/Deucarian/Authoring/Game Content..."));
        }

        [Test]
        public void ManifestDiscovery_FindsOneValidImportedStyleManifest()
        {
            CreateManifest("Basic", "pack.basic", "Basic Pack", sourceAsset: CreateTextAsset("Basic"));

            GameContentPackDiscoveryReport report = GameContentPackDiscovery.Discover(_providerId);

            Assert.That(report.Entries.Count, Is.EqualTo(1));
            Assert.That(report.Entries[0].StableKey, Is.EqualTo("com.deucarian.tests::pack.basic"));
            Assert.That(report.Entries[0].SourceState, Is.EqualTo(GameContentPackSourceState.Available));
            Assert.That(report.Entries[0].Validation.ErrorCount, Is.Zero);
            Assert.That(report.Entries[0].ManifestPath, Does.StartWith(_root));
        }

        [Test]
        public void ManifestDiscovery_FindsTwoPacksInDeterministicDisplayOrder()
        {
            CreateManifest("Zulu", "pack.zulu", "Zulu Pack", sourceAsset: CreateTextAsset("Zulu"));
            CreateManifest("Alpha", "pack.alpha", "Alpha Pack", sourceAsset: CreateTextAsset("Alpha"));

            GameContentPackDiscoveryReport report = GameContentPackDiscovery.Discover(_providerId);

            Assert.That(report.Entries.Select(entry => entry.Manifest.DisplayName), Is.EqualTo(new[] { "Alpha Pack", "Zulu Pack" }));
        }

        [Test]
        public void ManifestDiscovery_DuplicateStableKeysBecomeBlockingConflictsWithLocations()
        {
            CreateManifest("First", "pack.shared", "First", sourceAsset: CreateTextAsset("First"));
            CreateManifest("Second", "pack.shared", "Second", sourceAsset: CreateTextAsset("Second"));

            GameContentPackDiscoveryReport report = GameContentPackDiscovery.Discover(_providerId);

            Assert.That(report.Entries.Count, Is.EqualTo(2));
            Assert.That(report.ConflictCount, Is.EqualTo(2));
            Assert.That(report.Entries.All(entry => entry.SourceState == GameContentPackSourceState.DuplicateConflict), Is.True);
            Assert.That(report.Entries.All(entry => entry.Validation.ErrorCount > 0), Is.True);
            Assert.That(report.Entries[0].Validation.Issues[0].Message, Does.Contain("First.asset"));
            Assert.That(report.Entries[0].Validation.Issues[0].Message, Does.Contain("Second.asset"));
        }

        [Test]
        public void ManifestDiscovery_ReportsMissingRequiredSourceAndInvalidMetadata()
        {
            CreateManifest("Missing", "pack.missing", "Missing Source", sourceAsset: null);
            CreateManifest("Invalid", string.Empty, string.Empty, sourceAsset: CreateTextAsset("Invalid"));

            GameContentPackDiscoveryReport report = GameContentPackDiscovery.Discover(_providerId);
            GameContentPackManifestEntry missing = report.Entries.Single(entry => entry.Manifest.name == "Missing");
            GameContentPackManifestEntry invalid = report.Entries.Single(entry => entry.Manifest.name == "Invalid");

            Assert.That(missing.SourceState, Is.EqualTo(GameContentPackSourceState.MissingSource));
            Assert.That(missing.Validation.Issues.Any(issue => issue.Message.Contains("missing its TextAsset")), Is.True);
            Assert.That(invalid.SourceState, Is.EqualTo(GameContentPackSourceState.InvalidManifest));
            Assert.That(invalid.Validation.ErrorCount, Is.GreaterThan(0));
        }

        [Test]
        public void ManifestDiscovery_ReportsProviderUnavailableWithoutInventingAProvider()
        {
            string unavailable = "com.deucarian.tests.missing-provider." + Guid.NewGuid().ToString("N");
            CreateManifest("Unavailable", "pack.unavailable", "Unavailable", unavailable, CreateTextAsset("Unavailable"));

            GameContentPackDiscoveryReport report = GameContentPackDiscovery.Discover(unavailable);

            Assert.That(report.Entries.Single().SourceState, Is.EqualTo(GameContentPackSourceState.ProviderUnavailable));
            Assert.That(report.Validation.Issues.Any(issue => issue.Message.Contains("is not registered")), Is.True);
        }

        [Test]
        public void BrowserModel_SearchCategoryValidationAndSortAreDeterministic()
        {
            GameContentRecordDescriptor readyWeapon = Record("pack::weapon.zulu", "weapon.zulu", "weapons", "Zulu Wand", 2);
            GameContentRecordDescriptor warningPassive = Record(
                "pack::passive.alpha",
                "passive.alpha",
                "passives",
                "Alpha Heart",
                1,
                validation: new GameContentAuthoringValidationResult(new[]
                {
                    GameContentAuthoringValidationIssue.Warning("description", "Short description.")
                }));
            GameContentRecordDescriptor brokenPickup = Record(
                "pack::pickup.beta",
                "pickup.beta",
                "passives",
                "Beta Magnet",
                0,
                categories: new[] { "pickup-magnet" },
                outbound: new[]
                {
                    new GameContentRecordReferenceDescriptor("missing", "weapons", "pack", "targets", true, false)
                });
            var records = new[] { readyWeapon, warningPassive, brokenPickup };

            Assert.That(GameContentPackBrowserModel.FilterRecords(
                    records, "wand", string.Empty, GameContentRecordValidationFilter.All, GameContentRecordSortMode.SourceOrder)
                .Single(), Is.SameAs(readyWeapon));
            Assert.That(GameContentPackBrowserModel.FilterRecords(
                    records, string.Empty, "pickup-magnet", GameContentRecordValidationFilter.All, GameContentRecordSortMode.SourceOrder)
                .Single(), Is.SameAs(brokenPickup));
            Assert.That(GameContentPackBrowserModel.FilterRecords(
                    records, string.Empty, string.Empty, GameContentRecordValidationFilter.Warnings, GameContentRecordSortMode.SourceOrder)
                .Single(), Is.SameAs(warningPassive));
            Assert.That(GameContentPackBrowserModel.FilterRecords(
                    records, string.Empty, string.Empty, GameContentRecordValidationFilter.BrokenReferences, GameContentRecordSortMode.SourceOrder)
                .Single(), Is.SameAs(brokenPickup));
            Assert.That(GameContentPackBrowserModel.FilterRecords(
                    records, string.Empty, string.Empty, GameContentRecordValidationFilter.All, GameContentRecordSortMode.DisplayName)
                .Select(record => record.DisplayName), Is.EqualTo(new[] { "Alpha Heart", "Beta Magnet", "Zulu Wand" }));
            Assert.That(GameContentPackBrowserModel.FilterRecords(
                    records, string.Empty, string.Empty, GameContentRecordValidationFilter.All, GameContentRecordSortMode.SourceOrder)
                .Select(record => record.DisplayName), Is.EqualTo(new[] { "Beta Magnet", "Alpha Heart", "Zulu Wand" }));
        }

        [Test]
        public void BrowserModel_ResolvesValidReferencesAndLeavesBrokenReferencesVisible()
        {
            GameContentRecordDescriptor target = Record("pack::weapon.arc", "weapon.arc", "weapons", "Arc", 0);
            var valid = new GameContentRecordReferenceDescriptor(target.PackScopedId, "weapons", "pack", "starts with", true, true);
            var broken = new GameContentRecordReferenceDescriptor("pack::weapon.missing", "weapons", "pack", "starts with", true, false);

            Assert.That(GameContentPackBrowserModel.ResolveReferenceTarget(new[] { target }, valid), Is.SameAs(target));
            Assert.That(GameContentPackBrowserModel.ResolveReferenceTarget(new[] { target }, broken), Is.Null);
            Assert.That(broken.Valid, Is.False);
        }

        [Test]
        public void BrowserState_EnumeratesPacksCategoriesRecordsAndValidation()
        {
            GameContentRecordDescriptor record = Record("pack::weapon.arc", "weapon.arc", "weapons", "Arc", 0);
            GameContentCategoryDescriptor category = new GameContentCategoryDescriptor("weapons", "Weapons", "", "weapon", 0, 1);
            GameContentAuthoringValidationResult validation = new GameContentAuthoringValidationResult(new[]
            {
                GameContentAuthoringValidationIssue.Info("pack", "Validated")
            });
            _provider.Packs = new[] { Pack("pack.basic", "Basic", new[] { category }, validation, 1) };
            _provider.Records["pack.basic"] = new[] { record };
            var state = new GameContentPackBrowserState();

            state.Refresh(_provider);

            Assert.That(state.Packs.Count, Is.EqualTo(1));
            Assert.That(state.GetSelectedPack().Categories.Single(), Is.SameAs(category));
            Assert.That(state.GetRecords(state.GetSelectedPack()).Single(), Is.SameAs(record));
            Assert.That(state.GetSelectedPack().Validation.InfoCount, Is.EqualTo(1));
        }

        [Test]
        public void BrowserState_KeepsDuplicateConflictLocationsIndividuallySelectable()
        {
            GameContentPackDescriptor first = Pack(
                "pack.shared",
                "Shared Pack",
                Array.Empty<GameContentCategoryDescriptor>(),
                GameContentAuthoringValidationResult.Valid,
                0,
                "Assets/Samples/First/Shared.asset");
            GameContentPackDescriptor second = Pack(
                "pack.shared",
                "Shared Pack",
                Array.Empty<GameContentCategoryDescriptor>(),
                GameContentAuthoringValidationResult.Valid,
                0,
                "Assets/Samples/Second/Shared.asset");
            _provider.Packs = new[] { first, second };
            var state = new GameContentPackBrowserState();

            state.Refresh(_provider);
            state.SelectPack(second);

            Assert.That(state.Packs.Count, Is.EqualTo(2));
            Assert.That(state.GetSelectedPack(), Is.SameAs(second));
        }

        [Test]
        public void ActionDispatch_HonorsEnabledStateReturnsResultAndContainsProviderExceptions()
        {
            GameContentPackDescriptor pack = Pack("pack.basic", "Basic", Array.Empty<GameContentCategoryDescriptor>(), GameContentAuthoringValidationResult.Valid, 0);
            var enabled = new GameContentActionDescriptor("validate", "Validate", "", true, "", GameContentActionKind.Validate);
            var disabled = new GameContentActionDescriptor("play", "Play", "", false, "Scene missing", GameContentActionKind.Play);
            _provider.ActionResult = GameContentActionResult.Success("Validated");

            Assert.That(GameContentPackActionDispatcher.Execute(_provider, pack, enabled).Message, Is.EqualTo("Validated"));
            Assert.That(GameContentPackActionDispatcher.Execute(_provider, pack, disabled).Message, Is.EqualTo("Scene missing"));
            _provider.ThrowOnExecute = true;
            GameContentActionResult failure = GameContentPackActionDispatcher.Execute(_provider, pack, enabled);
            Assert.That(failure.Succeeded, Is.False);
            Assert.That(failure.Message, Does.Contain("planned test failure"));
        }

        [Test]
        public void ActionDispatch_MapsMissingInputsAndNullProviderResultsToStableFailures()
        {
            GameContentPackDescriptor pack = Pack(
                "pack.basic",
                "Basic",
                Array.Empty<GameContentCategoryDescriptor>(),
                GameContentAuthoringValidationResult.Valid,
                0);
            var action = new GameContentActionDescriptor(
                "validate", "Validate", "", true, "", GameContentActionKind.Validate);

            Assert.That(GameContentPackActionDispatcher.Validate(null, pack).ErrorCount, Is.EqualTo(1));
            Assert.That(GameContentPackActionDispatcher.Validate(_provider, null).ErrorCount, Is.EqualTo(1));
            Assert.That(GameContentPackActionDispatcher.Execute(null, pack, action).Succeeded, Is.False);
            Assert.That(GameContentPackActionDispatcher.Execute(_provider, null, action).Succeeded, Is.False);
            Assert.That(GameContentPackActionDispatcher.Execute(_provider, pack, null).Succeeded, Is.False);

            _provider.ValidationResult = null;
            _provider.ActionResult = null;

            Assert.That(GameContentPackActionDispatcher.Validate(_provider, pack).Issues[0].Message,
                Does.Contain("no validation result"));
            Assert.That(GameContentPackActionDispatcher.Execute(_provider, pack, action).Message,
                Does.Contain("no action result"));
        }

        private GameContentPackManifest CreateManifest(
            string assetName,
            string packId,
            string displayName,
            string providerId = null,
            TextAsset sourceAsset = null)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string scenePath = _root + "/" + assetName + ".unity";
            Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);

            GameContentPackManifest manifest = ScriptableObject.CreateInstance<GameContentPackManifest>();
            manifest.name = assetName;
            manifest.Configure(
                packId,
                "com.deucarian.tests",
                providerId ?? _providerId,
                displayName,
                "Test content pack",
                "1",
                new[] { "test" },
                sceneAsset,
                null,
                new[]
                {
                    new GameContentPackSourceReference("content", "json", sourceAsset, "Content", "records", true)
                });
            AssetDatabase.CreateAsset(manifest, _root + "/" + assetName + ".asset");
            AssetDatabase.SaveAssets();
            return manifest;
        }

        private TextAsset CreateTextAsset(string name)
        {
            var asset = new TextAsset("{\"records\":[]}") { name = name + "Json" };
            AssetDatabase.CreateAsset(asset, _root + "/" + name + "Json.asset");
            return asset;
        }

        private GameContentPackDescriptor Pack(
            string packId,
            string displayName,
            IEnumerable<GameContentCategoryDescriptor> categories,
            GameContentAuthoringValidationResult validation,
            int recordCount,
            string sourcePath = null)
        {
            return new GameContentPackDescriptor(
                packId,
                "com.deucarian.tests",
                _providerId,
                displayName,
                "Test pack",
                "1",
                new[] { "test" },
                GameContentPackSourceKind.Project,
                GameContentPackSourceState.Available,
                sourcePath ?? _root,
                null,
                null,
                null,
                null,
                null,
                categories,
                Array.Empty<GameContentActionDescriptor>(),
                validation,
                recordCount);
        }

        private static GameContentRecordDescriptor Record(
            string scopedId,
            string sourceId,
            string category,
            string displayName,
            int order,
            IEnumerable<string> categories = null,
            IEnumerable<GameContentRecordReferenceDescriptor> outbound = null,
            GameContentAuthoringValidationResult validation = null)
        {
            return new GameContentRecordDescriptor(
                scopedId,
                sourceId,
                category,
                categories,
                displayName,
                displayName + " description",
                displayName + " summary",
                new[] { new GameContentMetadataDescriptor("Name", displayName) },
                null,
                "Assets/content.json",
                "/records/0",
                outbound,
                null,
                validation,
                order,
                null,
                category);
        }

        private sealed class TestPackProvider : IGameContentAuthoringProvider, IGameContentAuthoringSurfaceProvider, IGameContentPackProvider
        {
            public TestPackProvider(string providerId)
            {
                ProviderId = providerId;
            }

            public string ProviderId { get; }
            public string DisplayName => "Test Content Packs";
            public string Description => "Test provider";
            public int SortOrder => 900;
            public bool Enabled => true;
            public IReadOnlyList<GameContentPackDescriptor> Packs { get; set; } = Array.Empty<GameContentPackDescriptor>();
            public Dictionary<string, IReadOnlyList<GameContentRecordDescriptor>> Records { get; } =
                new Dictionary<string, IReadOnlyList<GameContentRecordDescriptor>>(StringComparer.OrdinalIgnoreCase);
            public GameContentActionResult ActionResult { get; set; } = GameContentActionResult.Success("Done");
            public GameContentAuthoringValidationResult ValidationResult { get; set; } =
                GameContentAuthoringValidationResult.Valid;
            public bool ThrowOnExecute { get; set; }

            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }
            public void DrawCustomAuthoringSurface(GameContentAuthoringSurfaceContext context) { }
            public IReadOnlyList<GameContentPackDescriptor> GetContentPacks() => Packs;
            public IReadOnlyList<GameContentRecordDescriptor> GetRecords(string packId) =>
                Records.TryGetValue(packId ?? string.Empty, out IReadOnlyList<GameContentRecordDescriptor> records)
                    ? records
                    : Array.Empty<GameContentRecordDescriptor>();
            public GameContentAuthoringValidationResult ValidatePack(string packId) => ValidationResult;

            public GameContentActionResult ExecuteAction(string packId, string actionId)
            {
                if (ThrowOnExecute) throw new InvalidOperationException("planned test failure");
                return ActionResult;
            }
        }
    }
}
