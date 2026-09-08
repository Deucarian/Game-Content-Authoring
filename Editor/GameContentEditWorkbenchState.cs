using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentEditWorkbenchState
    {
        private ConditionalWeakTable<GameContentActiveEditSession, GameContentEditDrafts> _sessions =
            new ConditionalWeakTable<GameContentActiveEditSession, GameContentEditDrafts>();

        internal GameContentEditDrafts ForSession(GameContentActiveEditSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            return _sessions.GetValue(session, _ => new GameContentEditDrafts());
        }

        public void Clear()
        {
            _sessions = new ConditionalWeakTable<GameContentActiveEditSession, GameContentEditDrafts>();
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
