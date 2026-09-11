using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentEditWorkbenchState
    {
        private ConditionalWeakTable<GameContentActiveEditSession, GameContentEditDrafts> _sessions =
            new ConditionalWeakTable<GameContentActiveEditSession, GameContentEditDrafts>();
        private readonly Dictionary<string, object> _viewDrafts = new Dictionary<string, object>(StringComparer.Ordinal);

        internal T GetViewDraft<T>(string key, Func<T> create) where T : class
        {
            if (!_viewDrafts.TryGetValue(key, out var value)) _viewDrafts.Add(key, value = create());
            return value as T ?? throw new InvalidOperationException("An authoring draft key was reused for another state type.");
        }
        internal void RemoveViewDraft(string key) => _viewDrafts.Remove(key);

        internal GameContentEditDrafts ForSession(GameContentActiveEditSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            return _sessions.GetValue(session, _ => new GameContentEditDrafts());
        }

        public void Clear()
        {
            _sessions = new ConditionalWeakTable<GameContentActiveEditSession, GameContentEditDrafts>();
            _viewDrafts.Clear();
        }
    }

    internal sealed class GameContentEditDrafts
    {
        internal readonly Dictionary<string, GameContentFieldValue> CollectionAddDrafts =
            new Dictionary<string, GameContentFieldValue>(StringComparer.Ordinal);
        internal readonly Dictionary<string, GameContentStructuredRowKey> StructuredSelections =
            new Dictionary<string, GameContentStructuredRowKey>(StringComparer.Ordinal);
        internal readonly Dictionary<string, IReadOnlyList<GameContentStructuredRowFieldValue>> StructuredAddDrafts =
            new Dictionary<string, IReadOnlyList<GameContentStructuredRowFieldValue>>(StringComparer.Ordinal);
    }
}
