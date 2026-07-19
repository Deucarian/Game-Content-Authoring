using System;
using System.Linq;
using Deucarian.GameplayFoundation;
using Deucarian.GameContentAuthoring.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentLibraryEditModeTests
    {
        private string _root;
        private bool _createdGameContentRoot;

        [SetUp]
        public void SetUp()
        {
            _createdGameContentRoot = false;
            if (!AssetDatabase.IsValidFolder("Assets/GameContent"))
            {
                AssetDatabase.CreateFolder("Assets", "GameContent");
                _createdGameContentRoot = true;
            }

            _root = "Assets/GameContent/LibraryTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/GameContent", System.IO.Path.GetFileName(_root));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_root) && AssetDatabase.IsValidFolder(_root))
                AssetDatabase.DeleteAsset(_root);

            if (_createdGameContentRoot && AssetDatabase.IsValidFolder("Assets/GameContent") && AssetDatabase.FindAssets(string.Empty, new[] { "Assets/GameContent" }).Length == 0)
                AssetDatabase.DeleteAsset("Assets/GameContent");

            AssetDatabase.Refresh();
        }

        [Test]
        public void ContentLibraryProvider_RegistersUnderToolsMenu()
        {
            GameContentAuthoringProviderRegistry.Register(new GameContentLibraryProvider());

            Assert.That(GameContentAuthoringWindow.MenuPath, Is.EqualTo("Tools/Deucarian/Tools and Quality/Game Content Authoring"));
            Assert.That(GameContentAuthoringProviderRegistry.IsProviderRegistered(GameContentLibraryProvider.ContentLibraryProviderId), Is.True);
        }

        [Test]
        public void ContentLibraryProvider_UsesV2ManagementSurface()
        {
            var provider = new GameContentLibraryProvider();

            Assert.That(provider, Is.InstanceOf<IGameContentAuthoringSurfaceProvider>());
            Assert.That(GameContentLibraryV2UiContract.MainRowActionLabels, Does.Not.Contain("Select"));
            Assert.That(GameContentLibraryV2UiContract.MainRowActionLabels, Does.Contain("Ping"));
            Assert.That(GameContentLibraryV2UiContract.MainRowActionLabels, Does.Contain("Open"));
            Assert.That(GameContentLibraryV2UiContract.DetailPages, Does.Contain("Dependencies"));
            Assert.That(GameContentLibraryV2UiContract.DetailPages, Does.Contain("Advanced"));
        }

        [Test]
        public void ContentLibraryV2Model_GroupsFiltersAndDashboardUseManagementOrder()
        {
            GameContentLibraryReport report = BuildValidContentPack();

            GameContentLibraryV2Dashboard dashboard = GameContentLibraryV2Model.BuildDashboard(report);
            var groups = GameContentLibraryV2Model.BuildGroups(
                report,
                string.Empty,
                0,
                GameContentLibraryV2SeverityFilter.All,
                GameContentLibraryV2ReadinessFilter.All);
            var contentPackOnly = GameContentLibraryV2Model.BuildGroups(
                report,
                "Basic",
                1,
                GameContentLibraryV2SeverityFilter.All,
                GameContentLibraryV2ReadinessFilter.All);

            Assert.That(dashboard.TotalAssets, Is.EqualTo(7));
            Assert.That(dashboard.ReadyContentPacks, Is.EqualTo(1));
            Assert.That(dashboard.ReadyContentSets, Is.EqualTo(1));
            Assert.That(dashboard.Blockers, Is.EqualTo(0));
            Assert.That(groups[0].Name, Is.EqualTo("Content Packs"));
            Assert.That(groups[1].Name, Is.EqualTo("Game / Run Content Sets"));
            Assert.That(groups.Single(group => group.Kind == GameContentLibraryKind.ContentPack).Items.Count, Is.EqualTo(1));
            Assert.That(contentPackOnly.Sum(group => group.Items.Count), Is.EqualTo(1));
        }

        [Test]
        public void ContentLibraryV2State_RowClickStyleSelectionUpdatesSelectedItem()
        {
            GameContentLibraryReport report = BuildValidContentPack();
            GameContentLibraryItem contentPack = Find(report, GameContentLibraryKind.ContentPack);
            GameContentLibraryItem attack = Find(report, GameContentLibraryKind.Attack);
            var state = new GameContentLibraryV2State();

            state.EnsureSelection(report);
            Assert.That(state.GetSelected(report), Is.Not.Null);

            state.Select(contentPack);
            Assert.That(state.GetSelected(report), Is.SameAs(contentPack));

            state.Select(attack);
            Assert.That(state.GetSelected(report), Is.SameAs(attack));
        }

        [Test]
        public void ContentLibraryV2Graph_IncludesPackSetWeaponAttackWaveEnemyAndUpgradeTarget()
        {
            GameContentLibraryReport report = BuildValidContentPack();
            GameContentLibraryItem contentPack = Find(report, GameContentLibraryKind.ContentPack);

            var edges = GameContentLibraryV2Model.BuildGraphEdges(contentPack);

            Assert.That(edges.Any(edge => edge.Relation == "Content Pack -> Content Sets"), Is.True);
            Assert.That(edges.Any(edge => edge.Relation == "Content Set -> Weapons"), Is.True);
            Assert.That(edges.Any(edge => edge.Relation == "Weapon -> Attack"), Is.True);
            Assert.That(edges.Any(edge => edge.Relation == "Content Set -> Waves"), Is.True);
            Assert.That(edges.Any(edge => edge.Relation == "Wave -> Enemies"), Is.True);
            Assert.That(edges.Any(edge => edge.Relation == "Content Set -> Upgrades"), Is.True);
            Assert.That(edges.Any(edge => edge.Relation == "Upgrade -> Target"), Is.True);
        }

        [Test]
        public void ContentLibraryV2Graph_DependencyTargetsCanDriveSelection()
        {
            GameContentLibraryReport report = BuildValidContentPack();
            GameContentLibraryItem contentSet = Find(report, GameContentLibraryKind.ContentSet);
            GameContentLibraryV2GraphEdge weaponEdge = GameContentLibraryV2Model
                .BuildGraphEdges(contentSet)
                .First(edge => edge.Relation == "Content Set -> Weapons");
            var state = new GameContentLibraryV2State();

            state.Select(contentSet);
            state.Select(weaponEdge.To);

            Assert.That(state.GetSelected(report), Is.SameAs(weaponEdge.To));
        }

        [Test]
        public void ContentLibraryV2Model_NoGameContentRootBuildsEmptyManagementState()
        {
            GameContentLibraryReport report = GameContentLibraryService.Scan("Assets/GameContentMissing_" + Guid.NewGuid().ToString("N"));

            GameContentLibraryV2Dashboard dashboard = GameContentLibraryV2Model.BuildDashboard(report);
            var groups = GameContentLibraryV2Model.BuildGroups(
                report,
                string.Empty,
                0,
                GameContentLibraryV2SeverityFilter.All,
                GameContentLibraryV2ReadinessFilter.All);

            Assert.That(report.Items, Is.Empty);
            Assert.That(dashboard.TotalAssets, Is.EqualTo(0));
            Assert.That(groups.Count, Is.GreaterThanOrEqualTo(7));
            Assert.That(groups.Sum(group => group.Items.Count), Is.EqualTo(0));
        }

        [Test]
        public void V3ObjectEditorContext_TracksDirtyAndAcceptedState()
        {
            var context = new GameContentAuthoringObjectEditorContext(null, "baseline");

            Assert.That(context.IsDirty, Is.False);
            Assert.That(context.Key, Is.EqualTo(string.Empty));

            context.Capture("changed", GameContentAuthoringValidationResult.Valid);

            Assert.That(context.IsDirty, Is.True);
            Assert.That(context.Validation, Is.SameAs(GameContentAuthoringValidationResult.Valid));

            context.Accept("changed", "Saved");

            Assert.That(context.IsDirty, Is.False);
            Assert.That(context.StatusMessage, Is.EqualTo("Saved"));
        }

        [Test]
        public void ActionPreviewRoles_BuildCompactViewportLabels()
        {
            var preview = new GameContentAuthoringActionPreview
            {
                Label = "Moss Seeker",
                DeliveryTypeLabel = "Homing Projectile",
                Playing = false,
                Loop = true,
                Muted = true
            };
            preview.Roles.Add(new GameContentAuthoringActionPreviewRole("Source", "Tower Source"));
            preview.Roles.Add(new GameContentAuthoringActionPreviewRole("Projectile", "projectile-moss-seeker"));
            preview.Roles.Add(new GameContentAuthoringActionPreviewRole("Target", "Target Dummy"));

            string header = GameContentAuthoringObjectPreviewUtility.BuildViewportHeader(preview);
            GUIContent projectile = GameContentAuthoringObjectPreviewUtility.BuildRoleLabelContent(preview.Roles[1]);

            Assert.That(GameContentAuthoringObjectPreviewUtility.BuildRoleLegend(preview), Is.EqualTo("Source -> Projectile -> Target"));
            Assert.That(GameContentAuthoringObjectPreviewUtility.IsGamePreview(preview), Is.True);
            Assert.That(GameContentAuthoringObjectPreviewUtility.RequestsRoleLabels(preview), Is.False);
            Assert.That(header, Does.Contain("Moss Seeker"));
            Assert.That(header, Does.Contain("Homing Projectile"));
            Assert.That(header, Does.Contain("Paused"));
            Assert.That(header, Does.Contain("Muted"));
            Assert.That(header, Does.Contain("Loop"));
            Assert.That(projectile.text, Is.EqualTo("Projectile: projectile-moss-seeker"));
            preview.RenderMode = GameContentAuthoringActionPreviewRenderMode.Debug;
            Assert.That(GameContentAuthoringObjectPreviewUtility.RequestsRoleLabels(preview), Is.True);
            Assert.DoesNotThrow(() => GameContentAuthoringObjectPreviewUtility.BuildRoleLabelContent(preview.Roles[0]));
        }

        [Test]
        public void Scan_WhenRootMissing_ReturnsInfoWithoutThrowing()
        {
            GameContentLibraryReport report = GameContentLibraryService.Scan("Assets/GameContentMissing_" + Guid.NewGuid().ToString("N"));

            Assert.That(report.Items, Is.Empty);
            Assert.That(report.InfoCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Scan_DiscoversGroupedAuthoredAssets()
        {
            GameContentLibraryReport report = BuildValidContentSet();

            Assert.That(report.Items.Count, Is.EqualTo(6));
            Assert.That(report.Groups.Single(group => group.Name == "Attacks").Items.Count, Is.EqualTo(1));
            Assert.That(report.Groups.Single(group => group.Name == "Enemies").Items.Count, Is.EqualTo(1));
            Assert.That(report.Groups.Single(group => group.Name == "Waves").Items.Count, Is.EqualTo(1));
            Assert.That(report.Groups.Single(group => group.Name == "Tower / Weapon").Items.Count, Is.EqualTo(1));
            Assert.That(report.Groups.Single(group => group.Name == "Upgrades").Items.Count, Is.EqualTo(1));
            Assert.That(report.Groups.Single(group => group.Name == "Game / Run Content Sets").Items.Count, Is.EqualTo(1));
            Assert.That(report.Groups.Single(group => group.Name == "Content Packs").Items.Count, Is.EqualTo(0));
        }

        [Test]
        public void Scan_DetectsDuplicateIdsWithinContentType()
        {
            CreateAsset<AttackDefinitionAsset>("AttackA", asset =>
            {
                asset.Id = "attack.duplicate";
                asset.DisplayName = "Attack A";
            });
            CreateAsset<AttackDefinitionAsset>("AttackB", asset =>
            {
                asset.Id = "attack.duplicate";
                asset.DisplayName = "Attack B";
            });

            GameContentLibraryReport report = GameContentLibraryService.Scan(_root);

            Assert.That(report.BlockerCount, Is.GreaterThanOrEqualTo(1));
            GameContentLibraryIssue duplicate = report.AllIssues.FirstOrDefault(issue => issue.Message.Contains("Duplicate Attacks ID"));
            Assert.That(duplicate, Is.Not.Null);
            Assert.That(duplicate.Message, Does.Contain("AttackA.asset"));
            Assert.That(duplicate.Message, Does.Contain("AttackB.asset"));
        }

        [Test]
        public void Scan_BuildsDependencyAndReverseReferenceSummary()
        {
            GameContentLibraryReport report = BuildValidContentSet();
            GameContentLibraryItem attack = Find(report, GameContentLibraryKind.Attack);
            GameContentLibraryItem weapon = Find(report, GameContentLibraryKind.Weapon);
            GameContentLibraryItem contentSet = Find(report, GameContentLibraryKind.ContentSet);

            Assert.That(weapon.DirectReferences.Any(reference => ReferenceEquals(reference.Target, attack)), Is.True);
            Assert.That(attack.ReverseReferences.Any(reference => ReferenceEquals(reference.Target, weapon)), Is.True);
            Assert.That(GameContentLibraryReportWriter.BuildDependencyLines(contentSet, 3).Any(line => line.Contains("Tower / Weapon -> Basic Tower")), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Scan_InspectsSameFolderSectionAssetsForReferences(bool usesCanonicalRoleDirectoryNames)
        {
            AttackDefinitionAsset attack = CreateAsset<AttackDefinitionAsset>("Attack", asset =>
            {
                asset.Id = "attack.section";
                asset.DisplayName = "Section Attack";
            });
            EnemyDefinitionAsset enemy = CreateAsset<EnemyDefinitionAsset>("Enemy", asset =>
            {
                asset.Id = "enemy.section";
                asset.DisplayName = "Section Enemy";
            });
            RunUpgradeDefinitionAsset upgrade = CreateAsset<RunUpgradeDefinitionAsset>("Upgrade", asset =>
            {
                asset.Id = "upgrade.section";
                asset.DisplayName = "Section Upgrade";
            });

            string weaponFolder = _root + "/weapon.section";
            string waveFolder = _root + "/wave.section";
            AssetDatabase.CreateFolder(_root, "weapon.section");
            AssetDatabase.CreateFolder(_root, "wave.section");

            string weaponDefinitionName = usesCanonicalRoleDirectoryNames
                ? "WeaponDefinition.asset"
                : "weapon.section_WeaponDefinition.asset";
            string weaponStatsName = usesCanonicalRoleDirectoryNames
                ? "Stats.asset"
                : "weapon.section_Stats.asset";
            string waveDefinitionName = usesCanonicalRoleDirectoryNames
                ? "WaveDefinition.asset"
                : "wave.section_WaveDefinition.asset";
            string waveEntriesName = usesCanonicalRoleDirectoryNames
                ? "Entries.asset"
                : "wave.section_Entries.asset";

            WeaponDefinitionAsset weapon = CreateAssetAt<WeaponDefinitionAsset>(weaponFolder + "/" + weaponDefinitionName, asset =>
            {
                asset.Id = "weapon.section";
                asset.DisplayName = "Section Tower";
            });
            CreateAssetAt<WeaponStatsSectionAsset>(weaponFolder + "/" + weaponStatsName, asset => asset.Attack = attack);
            if (usesCanonicalRoleDirectoryNames)
            {
                CreateAssetAt<CompanionReferenceSectionAsset>(
                    weaponFolder + "/Presentation.asset",
                    asset => asset.Reference = enemy);
                CreateAssetAt<CompanionReferenceSectionAsset>(
                    weaponFolder + "/Unrelated.asset",
                    asset => asset.Reference = upgrade);
            }

            WaveDefinitionAsset wave = CreateAssetAt<WaveDefinitionAsset>(waveFolder + "/" + waveDefinitionName, asset =>
            {
                asset.Id = "wave.section";
                asset.DisplayName = "Section Wave";
            });
            CreateAssetAt<WaveEntriesSectionAsset>(waveFolder + "/" + waveEntriesName, asset => asset.Enemy = enemy);

            CreateAsset<GameContentSetAsset>("ContentSet", asset =>
            {
                asset.Id = "content.section";
                asset.DisplayName = "Section Content Set";
                asset.StartingWeapon = weapon;
                asset.AvailableWeapons = new[] { weapon };
                asset.EnemyPool = new[] { enemy };
                asset.WaveSet = new[] { wave };
                asset.UpgradePool = new[] { upgrade };
            });

            GameContentLibraryReport report = GameContentLibraryService.Scan(_root);
            GameContentLibraryItem weaponItem = report.Items.Single(item => item.Id == "weapon.section");
            GameContentLibraryItem waveItem = report.Items.Single(item => item.Id == "wave.section");
            GameContentLibraryItem attackItem = report.Items.Single(item => item.Id == "attack.section");
            GameContentLibraryItem enemyItem = report.Items.Single(item => item.Id == "enemy.section");
            GameContentLibraryItem upgradeItem = report.Items.Single(item => item.Id == "upgrade.section");

            Assert.That(weaponItem.DirectReferences.Any(reference => ReferenceEquals(reference.Target, attackItem)), Is.True);
            Assert.That(waveItem.DirectReferences.Any(reference => ReferenceEquals(reference.Target, enemyItem)), Is.True);
            Assert.That(
                weaponItem.DirectReferences.Any(reference => ReferenceEquals(reference.Target, enemyItem)),
                Is.EqualTo(usesCanonicalRoleDirectoryNames));
            Assert.That(weaponItem.DirectReferences.Any(reference => ReferenceEquals(reference.Target, upgradeItem)), Is.False);
            Assert.That(report.AllIssues.Any(issue => issue.Path == "Weapon.Attack" && issue.Message.Contains("does not reference")), Is.False);
            Assert.That(report.AllIssues.Any(issue => issue.Path == "Wave.Enemies" && issue.Message.Contains("does not reference")), Is.False);
        }

        [Test]
        public void Scan_BuildsReadyContentSetSummary()
        {
            GameContentLibraryReport report = BuildValidContentSet();
            GameContentLibraryItem contentSet = Find(report, GameContentLibraryKind.ContentSet);
            GameContentLibraryContentSetSummary summary = report.GetContentSetSummary(contentSet);

            Assert.That(summary, Is.Not.Null);
            Assert.That(summary.Ready, Is.True);
            Assert.That(summary.WeaponCount, Is.EqualTo(1));
            Assert.That(summary.EnemyCount, Is.EqualTo(1));
            Assert.That(summary.WaveCount, Is.EqualTo(1));
            Assert.That(summary.UpgradeCount, Is.EqualTo(1));
        }

        [Test]
        public void Scan_BuildsContentPackDependencyAndReadySummary()
        {
            GameContentLibraryReport report = BuildValidContentPack();
            GameContentLibraryItem contentPack = Find(report, GameContentLibraryKind.ContentPack);
            GameContentLibraryContentPackSummary summary = report.GetContentPackSummary(contentPack);
            string markdown = GameContentLibraryReportWriter.ToContentPackMarkdown(report, contentPack);

            Assert.That(report.Items.Count, Is.EqualTo(7));
            Assert.That(report.Groups.Single(group => group.Name == "Content Packs").Items.Count, Is.EqualTo(1));
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary.Ready, Is.True);
            Assert.That(summary.ContentSetCount, Is.EqualTo(1));
            Assert.That(summary.WeaponCount, Is.EqualTo(1));
            Assert.That(summary.EnemyCount, Is.EqualTo(1));
            Assert.That(summary.WaveCount, Is.EqualTo(1));
            Assert.That(summary.UpgradeCount, Is.EqualTo(1));
            Assert.That(GameContentLibraryReportWriter.BuildDependencyLines(contentPack, 4).Any(line => line.Contains("Game / Run Content Sets -> Basic Content Set")), Is.True);
            Assert.That(GameContentLibraryReportWriter.BuildDependencyLines(contentPack, 4).Any(line => line.Contains("Tower / Weapon -> Basic Tower")), Is.True);
            Assert.That(markdown, Does.Contain("Content Sets: 1"));
        }

        [Test]
        public void Scan_ReportsMissingRequiredReferences()
        {
            CreateAsset<WeaponDefinitionAsset>("Weapon", asset =>
            {
                asset.Id = "weapon.missing";
                asset.DisplayName = "Missing Attack Weapon";
            });
            CreateAsset<WaveDefinitionAsset>("Wave", asset =>
            {
                asset.Id = "wave.missing";
                asset.DisplayName = "Missing Enemy Wave";
            });
            CreateAsset<GameContentSetAsset>("ContentSet", asset =>
            {
                asset.Id = "content.missing";
                asset.DisplayName = "Missing Content Set";
                asset.AvailableWeapons = Array.Empty<WeaponDefinitionAsset>();
                asset.EnemyPool = Array.Empty<EnemyDefinitionAsset>();
                asset.WaveSet = Array.Empty<WaveDefinitionAsset>();
                asset.UpgradePool = Array.Empty<RunUpgradeDefinitionAsset>();
            });

            GameContentLibraryReport report = GameContentLibraryService.Scan(_root);

            Assert.That(report.AllIssues.Any(issue => issue.Path == "Weapon.Attack"), Is.True);
            Assert.That(report.AllIssues.Any(issue => issue.Path == "Wave.Enemies"), Is.True);
            Assert.That(report.AllIssues.Any(issue => issue.Path == "ContentSet.StartingWeapon"), Is.True);
            Assert.That(report.AllIssues.Any(issue => issue.Path == "ContentSet.StartingWeapon" && issue.Message.Contains("Missing Content Set")), Is.True);
            Assert.That(report.ContentSetSummaries.Single().Ready, Is.False);
        }

        [Test]
        public void Scan_WarnsWhenUpgradeTargetsOutsideContentSet()
        {
            AttackDefinitionAsset attack = CreateAsset<AttackDefinitionAsset>("Attack", asset =>
            {
                asset.Id = "attack.basic";
                asset.DisplayName = "Basic Attack";
            });
            WeaponDefinitionAsset startingWeapon = CreateAsset<WeaponDefinitionAsset>("StartingWeapon", asset =>
            {
                asset.Id = "weapon.start";
                asset.DisplayName = "Starting Tower";
                asset.Attack = attack;
            });
            WeaponDefinitionAsset externalWeapon = CreateAsset<WeaponDefinitionAsset>("ExternalWeapon", asset =>
            {
                asset.Id = "weapon.external";
                asset.DisplayName = "External Tower";
                asset.Attack = attack;
            });
            EnemyDefinitionAsset enemy = CreateAsset<EnemyDefinitionAsset>("Enemy", asset =>
            {
                asset.Id = "enemy.basic";
                asset.DisplayName = "Basic Enemy";
            });
            WaveDefinitionAsset wave = CreateAsset<WaveDefinitionAsset>("Wave", asset =>
            {
                asset.Id = "wave.basic";
                asset.DisplayName = "Basic Wave";
                asset.Enemy = enemy;
            });
            RunUpgradeDefinitionAsset upgrade = CreateAsset<RunUpgradeDefinitionAsset>("Upgrade", asset =>
            {
                asset.Id = "upgrade.external";
                asset.DisplayName = "External Upgrade";
                asset.Target = externalWeapon;
            });
            CreateAsset<GameContentSetAsset>("ContentSet", asset =>
            {
                asset.Id = "content.basic";
                asset.DisplayName = "Basic Content Set";
                asset.StartingWeapon = startingWeapon;
                asset.AvailableWeapons = new[] { startingWeapon };
                asset.EnemyPool = new[] { enemy };
                asset.WaveSet = new[] { wave };
                asset.UpgradePool = new[] { upgrade };
            });

            GameContentLibraryReport report = GameContentLibraryService.Scan(_root);

            Assert.That(report.AllIssues.Any(issue => issue.Path == "ContentSet.Upgrades" && issue.Message.Contains("outside this content set")), Is.True);
        }

        [Test]
        public void ReportWriter_IncludesValidationBuckets()
        {
            CreateAsset<AttackDefinitionAsset>("AttackA", asset =>
            {
                asset.Id = "attack.duplicate";
                asset.DisplayName = "Attack A";
            });
            CreateAsset<AttackDefinitionAsset>("AttackB", asset =>
            {
                asset.Id = "attack.duplicate";
                asset.DisplayName = "Attack B";
            });
            GameContentLibraryReport report = GameContentLibraryService.Scan(_root);

            string markdown = GameContentLibraryReportWriter.ToMarkdown(report);

            Assert.That(markdown, Does.Contain("## Blockers"));
            Assert.That(markdown, Does.Contain("Duplicate Attacks ID"));
        }

        [Test]
        public void GameplayFoundationReportAdapter_ConvertsCountsAndMessages()
        {
            var report = new ContentValidationReport();
            report.AddError("Missing stable id.", "Weapon.Id");
            report.AddWarning("Unused reference.", "References");
            report.AddInfo("Loaded sample content.", "Summary");

            GameContentAuthoringValidationResult result = GameContentAuthoringValidationReports.ToAuthoringResult(report);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.WarningCount, Is.EqualTo(1));
            Assert.That(result.InfoCount, Is.EqualTo(1));
            Assert.That(result.Issues.Count, Is.EqualTo(3));
            Assert.That(result.Issues[0].Path, Is.EqualTo("Weapon.Id"));
            Assert.That(result.Issues[0].Message, Is.EqualTo("Missing stable id."));
        }

        [Test]
        public void GameplayFoundationReportAdapter_WritesGroupedMarkdown()
        {
            var report = new ContentValidationReport();
            report.AddError("Missing stable id.", "Weapon.Id");
            report.AddWarning("Unused reference.", "References");
            report.AddInfo("Loaded sample content.", "Summary");

            string markdown = GameContentAuthoringValidationReports.ToMarkdown(report, "Authoring Report");

            Assert.That(markdown, Does.Contain("# Authoring Report"));
            Assert.That(markdown, Does.Contain("- Errors: 1"));
            Assert.That(markdown, Does.Contain("## Errors"));
            Assert.That(markdown, Does.Contain("- Weapon.Id: Missing stable id."));
            Assert.That(markdown, Does.Contain("## Warnings"));
            Assert.That(markdown, Does.Contain("## Info"));
        }

        [Test]
        public void ObjectPreviewUtility_FitsContentInsidePreviewRect()
        {
            Rect container = new Rect(0f, 0f, 220f, 120f);

            Rect fitted = GameContentAuthoringObjectPreviewUtility.FitRect(container, new Vector2(512f, 128f), 10f);

            Assert.That(fitted.xMin, Is.GreaterThanOrEqualTo(container.xMin + 10f));
            Assert.That(fitted.xMax, Is.LessThanOrEqualTo(container.xMax - 10f));
            Assert.That(fitted.yMin, Is.GreaterThanOrEqualTo(container.yMin + 10f));
            Assert.That(fitted.yMax, Is.LessThanOrEqualTo(container.yMax - 10f));
        }

        [Test]
        public void ObjectPreviewUtility_RejectsPrefabsWithoutVisibleRenderers()
        {
            var root = new GameObject("empty-preview-root");
            try
            {
                Bounds bounds;
                Assert.That(GameContentAuthoringObjectPreviewRenderer.TryCalculateBoundsForTests(root, out bounds), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ObjectPreviewUtility_SanitizesPathologicalPreviewBounds()
        {
            var raw = new Bounds(new Vector3(999f, 0f, 0f), new Vector3(1200f, 0.001f, float.PositiveInfinity));

            Bounds sanitized = GameContentAuthoringObjectPreviewRenderer.SanitizePreviewBoundsForTests(raw, Vector3.zero);

            Assert.That(sanitized.center.magnitude, Is.LessThanOrEqualTo(8.001f));
            Assert.That(sanitized.size.x, Is.LessThanOrEqualTo(8.001f));
            Assert.That(sanitized.size.y, Is.GreaterThanOrEqualTo(0.08f));
            Assert.That(sanitized.size.z, Is.EqualTo(1f));
        }

        [Test]
        public void ObjectPreviewUtility_ClampsHugeRenderableBounds()
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                root.name = "huge-preview-cube";
                root.transform.localScale = new Vector3(1000f, 0.001f, 1000f);

                Bounds bounds;
                Assert.That(GameContentAuthoringObjectPreviewRenderer.TryCalculateBoundsForTests(root, out bounds), Is.True);
                Assert.That(bounds.size.x, Is.LessThanOrEqualTo(8.001f));
                Assert.That(bounds.size.y, Is.GreaterThanOrEqualTo(0.08f));
                Assert.That(bounds.size.z, Is.LessThanOrEqualTo(8.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ActionPreviewTimeline_ClampsAndLabelsPlaybackPhases()
        {
            var preview = new GameContentAuthoringActionPreview
            {
                Mode = GameContentAuthoringActionPreviewMode.Projectile,
                DurationSeconds = 2f,
                Playing = true,
                Loop = false,
                StartTime = 10d,
                IncludeStatusEffect = true
            };

            Assert.That(preview.GetNormalizedTime(9d), Is.EqualTo(0f));
            Assert.That(preview.GetNormalizedTime(11d), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(preview.GetNormalizedTime(20d), Is.EqualTo(1f));
            Assert.That(preview.GetPhaseLabel(10.1d), Is.EqualTo("OnCast"));
            Assert.That(preview.GetPhaseLabel(11d), Is.EqualTo("Projectile travel"));
            Assert.That(preview.GetPhaseLabel(11.55d), Is.EqualTo("OnImpact"));
            Assert.That(preview.GetPhaseLabel(11.9d), Is.EqualTo("Status / Expire"));
        }

        [Test]
        public void ActionPreviewTimeline_AppliesPlaybackSpeed()
        {
            var preview = new GameContentAuthoringActionPreview
            {
                DurationSeconds = 4f,
                Playing = true,
                Loop = false,
                Speed = 2f,
                StartTime = 10d
            };

            Assert.That(preview.GetNormalizedTime(11d), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(preview.GetNormalizedTime(12d), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void OptionalCustomSurface_DoesNotChangeBaseProviderContract()
        {
            Assert.That(typeof(IGameContentAuthoringProvider).GetMethods().Any(method => method.Name == "DrawCustomAuthoringSurface"), Is.False);
            Assert.That(typeof(IGameContentAuthoringSurfaceProvider).IsInterface, Is.True);
            Assert.That(typeof(IGameContentAuthoringSurfaceProvider).GetMethods().Any(method => method.Name == "DrawCustomAuthoringSurface"), Is.True);
        }

        private GameContentLibraryReport BuildValidContentSet()
        {
            BuildValidContentSetAsset();
            return GameContentLibraryService.Scan(_root);
        }

        private GameContentLibraryReport BuildValidContentPack()
        {
            GameContentSetAsset contentSet = BuildValidContentSetAsset();
            CreateAsset<GameContentPackAsset>("ContentPack", asset =>
            {
                asset.Id = "contentpack.basic";
                asset.DisplayName = "Basic Content Pack";
                asset.ContentSets = new[] { contentSet };
                asset.DefaultContentSet = contentSet;
            });

            return GameContentLibraryService.Scan(_root);
        }

        private GameContentSetAsset BuildValidContentSetAsset()
        {
            AttackDefinitionAsset attack = CreateAsset<AttackDefinitionAsset>("Attack", asset =>
            {
                asset.Id = "attack.basic";
                asset.DisplayName = "Basic Attack";
            });
            EnemyDefinitionAsset enemy = CreateAsset<EnemyDefinitionAsset>("Enemy", asset =>
            {
                asset.Id = "enemy.basic";
                asset.DisplayName = "Basic Enemy";
            });
            WaveDefinitionAsset wave = CreateAsset<WaveDefinitionAsset>("Wave", asset =>
            {
                asset.Id = "wave.basic";
                asset.DisplayName = "Basic Wave";
                asset.Enemy = enemy;
            });
            WeaponDefinitionAsset weapon = CreateAsset<WeaponDefinitionAsset>("Weapon", asset =>
            {
                asset.Id = "weapon.basic";
                asset.DisplayName = "Basic Tower";
                asset.Attack = attack;
            });
            RunUpgradeDefinitionAsset upgrade = CreateAsset<RunUpgradeDefinitionAsset>("Upgrade", asset =>
            {
                asset.Id = "upgrade.basic";
                asset.DisplayName = "Basic Upgrade";
                asset.Target = weapon;
            });
            return CreateAsset<GameContentSetAsset>("ContentSet", asset =>
            {
                asset.Id = "content.basic";
                asset.DisplayName = "Basic Content Set";
                asset.StartingWeapon = weapon;
                asset.AvailableWeapons = new[] { weapon };
                asset.EnemyPool = new[] { enemy };
                asset.WaveSet = new[] { wave };
                asset.UpgradePool = new[] { upgrade };
            });
        }

        private TAsset CreateAsset<TAsset>(string name, Action<TAsset> configure) where TAsset : ScriptableObject
        {
            TAsset asset = ScriptableObject.CreateInstance<TAsset>();
            asset.name = name;
            configure?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, _root + "/" + name + ".asset");
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static TAsset CreateAssetAt<TAsset>(string assetPath, Action<TAsset> configure) where TAsset : ScriptableObject
        {
            TAsset asset = ScriptableObject.CreateInstance<TAsset>();
            asset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            configure?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static GameContentLibraryItem Find(GameContentLibraryReport report, GameContentLibraryKind kind)
        {
            return report.Items.Single(item => item.Kind == kind);
        }

    }
}
