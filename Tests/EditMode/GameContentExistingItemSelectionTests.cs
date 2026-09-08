using System;
using Deucarian.GameContentAuthoring.Editor;
using NUnit.Framework;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentExistingItemSelectionTests
    {
        [Test]
        public void SelectionsAreOwnedPerWindowAndProvider()
        {
            var first = new GameContentExistingItemSelection();
            var second = new GameContentExistingItemSelection();
            var provider = new Provider("example.attack");
            var otherProvider = new Provider("another.attack");
            var item = Item("attack", GameContentLibraryKind.Attack);
            var report = Report(item);
            first.Select(provider, item);
            Assert.That(first.Resolve(provider, report), Is.SameAs(item));
            Assert.That(first.Resolve(otherProvider, report), Is.Null);
            Assert.That(second.Resolve(provider, report), Is.Null);
            Assert.That(first.IsSelected(provider, item), Is.True);
            first.Clear();
            Assert.That(first.HasSelection(provider), Is.False);
        }

        [Test]
        public void RefreshPrunesMissingItemsButKeepsOtherSelections()
        {
            var selection = new GameContentExistingItemSelection();
            var provider = new Provider("example.attack");
            var other = new Provider("example.enemy");
            var attack = Item("attack", GameContentLibraryKind.Attack);
            var enemy = Item("enemy", GameContentLibraryKind.Enemy);
            selection.Select(provider, attack);
            selection.Select(other, enemy);
            selection.Prune(null);
            Assert.That(selection.HasSelection(provider), Is.True);
            selection.Prune(Report(enemy));
            Assert.That(selection.HasSelection(provider), Is.False);
            Assert.That(selection.Resolve(other, Report(enemy)), Is.SameAs(enemy));
            Assert.That(selection.Remove(other), Is.True);
            Assert.That(selection.Remove(other), Is.False);
        }

        [Test]
        public void ResolutionRequiresProviderKindAndOrdinalKeyMatch()
        {
            var selection = new GameContentExistingItemSelection();
            var provider = new Provider("example.attack");
            selection.Select(provider, Item("KEY", GameContentLibraryKind.Attack));
            Assert.That(selection.Resolve(provider, Report(Item("key", GameContentLibraryKind.Attack))), Is.Null);
            Assert.That(selection.Resolve(provider, Report(Item("KEY", GameContentLibraryKind.Enemy))), Is.Null);
        }

        private static GameContentLibraryItem Item(string key, GameContentLibraryKind kind)
            => new GameContentLibraryItem(key, null, kind, "Test", "", key, key);

        private static GameContentLibraryReport Report(params GameContentLibraryItem[] items)
            => new GameContentLibraryReport("Assets", items, Array.Empty<GameContentLibraryIssue>());

        private sealed class Provider : IGameContentAuthoringProvider
        {
            internal Provider(string id) { ProviderId = id; }
            public string ProviderId { get; }
            public string DisplayName => ProviderId;
            public string Description => "Test provider";
            public int SortOrder => 0;
            public bool Enabled => true;
            public void OnSelected() { }
            public void Draw(GameContentAuthoringContext context) { }
            public void DrawPreview(GameContentAuthoringPreviewContext context) { }
            public void StopPreview() { }
        }
    }
}
