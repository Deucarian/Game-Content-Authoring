using System;
using System.Linq;
using Deucarian.Editor;
using Deucarian.GameContentAuthoring.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentToolkitDraftTests
    {
        private sealed class State { internal string Text; }
        private sealed class Provider : IGameContentAuthoringProvider
        {
            public string ProviderId => "native-draft-test";
            public string DisplayName => "Draft";
            public string Description => string.Empty;
            public int SortOrder => 0;
            public bool Enabled => true;
            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }
        }
        private sealed class Host : EditorWindow { }
        private Host host;
        private TextAsset source;
        private string revision;
        private int saves, validations;
        private GameContentEditWorkbenchState state;

        [SetUp] public void SetUp()
        {
            host = ScriptableObject.CreateInstance<Host>(); host.Show();
            source = new TextAsset("Test source"); revision = "original";
            saves = validations = 0; state = new GameContentEditWorkbenchState();
        }
        [TearDown] public void TearDown()
        {
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
        }

        [Test] public void ConstructAndEditOnlyChangeTheWindowDraft()
        {
            var root = Create(); root.Q<TextField>("test-value").value = "edited";
            Assert.That(revision, Is.EqualTo("original")); Assert.That(saves, Is.Zero); Assert.That(validations, Is.Zero);
        }

        [Test] public void MainWorkspaceKeepsContentSearchAndCompactFiltersInTheCollection()
        {
            using var page = GameContentAuthoringWindow.CreatePage();
            var list = page.Root.Q<ScrollView>("workspace-collection");
            var search = page.Root.Q<TextField>("authoring-search");
            Assert.That(list.Contains(search), Is.True);
            Assert.That(list.Contains(page.Root.Q("authoring-filters")), Is.True);
            Assert.That(page.Root.Q("workspace-tabs").childCount, Is.Zero);
            Assert.That(page.Root.Q("authoring-pack").tooltip, Is.EqualTo("Pack"));
            search.value = "No matching fixture record";
            Assert.That(page.Root.Query<Label>().ToList().Any(label => label.text ==
                "No matching content. Choose a pack or create project content."), Is.True);
        }
        [Test] public void RebuildingPreservesTheDraftButAnotherWindowDoesNotShareIt()
        {
            Create().Q<TextField>("test-value").value = "draft";
            Assert.That(Create().Q<TextField>("test-value").value, Is.EqualTo("draft"));
            state = new GameContentEditWorkbenchState();
            Assert.That(Create().Q<TextField>("test-value").value, Is.EqualTo("original"));
        }
        [Test] public void StaleSourcePreventsSaveAndRetainsDraft()
        {
            var root = Create(); root.Q<TextField>("test-value").value = "draft"; revision = "external edit";
            Submit(root.Q<Button>("content-save-draft"));
            Assert.That(saves, Is.Zero); Assert.That(validations, Is.Zero);
            Assert.That(root.Query<Label>().ToList().Any(label => label.text.Contains("Source changed")), Is.True);
            Assert.That(Create().Q<TextField>("test-value").value, Is.EqualTo("draft"));
        }
        [Test] public void ExplicitSaveValidatesThenWritesAndReloadsTheSavedRevision()
        {
            var root = Create(); root.Q<TextField>("test-value").value = "saved";
            Submit(root.Q<Button>("content-save-draft"));
            Assert.That(validations, Is.EqualTo(1)); Assert.That(saves, Is.EqualTo(1));
            Assert.That(revision, Is.EqualTo("saved"));
            Assert.That(Create().Q<TextField>("test-value").value, Is.EqualTo("saved"));
        }
        [Test] public void ReadOnlyContextDisablesEditingAndSaving()
        {
            var root = Create(readOnly: true);
            Assert.That(root.Q<TextField>("test-value").enabledInHierarchy, Is.False);
            Assert.That(root.Q<Button>("content-save-draft").enabledInHierarchy, Is.False);
            Submit(root.Q<Button>("content-save-draft")); Assert.That(saves, Is.Zero);
        }
        [Test] public void StaticPreviewDoesNotExposeMeaninglessPlaybackControls()
        {
            var root = new VisualElement();
            GameContentToolkitPreview.Add(root, () => source, null);
            Assert.That(root.Q<Slider>("preview-position"), Is.Null);
            Assert.That(root.Q<Toggle>("preview-loop"), Is.Null);
        }
        private VisualElement Create(bool readOnly = false)
        {
            var catalog = GameContentPackCatalog.Build(new IGameContentAuthoringProvider[] { new GameContentLibraryProvider() });
            var pack = readOnly ? new GameContentPackContext(catalog, null) : new GameContentPackContext(catalog, catalog.Entries.First());
            var provider = new Provider();
            var item = new GameContentLibraryItem("test-record", source, GameContentLibraryKind.Enemy, "Enemy", "Assets/Test.asset", "test", "Test");
            var context = new GameContentAuthoringSurfaceContext(host, provider, default, null, null, item,
                new GameContentAuthoringContext(host, provider.ProviderId, null, null, null, pack), null, pack, null, null, null,
                null, null, null, null, null, null, state);
            var root = GameContentToolkitDraftEditor.Create(context, () => new State { Text = revision }, value => value.Text,
                value => { validations++; return new GameContentAuthoringValidationResult(Array.Empty<GameContentAuthoringValidationIssue>()); },
                value => { saves++; revision = value.Text; return new GameContentCreationResult(true, "Saved", null); },
                (parent, value) => new DeucarianEditorWorkspaceForm(parent).Text("test-value", "Value", () => value.Text, next => value.Text = next));
            host.rootVisualElement.Clear(); host.rootVisualElement.Add(root); return root;
        }
        private static void Submit(Button button)
        {
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
        }
    }
}
