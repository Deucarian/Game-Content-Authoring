using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentLibraryV2GraphEdge
    {
        public GameContentLibraryV2GraphEdge(GameContentLibraryItem from, GameContentLibraryItem to, string relation, string context)
        {
            From = from;
            To = to;
            Relation = relation ?? string.Empty;
            Context = context ?? string.Empty;
        }

        public GameContentLibraryItem From { get; }
        public GameContentLibraryItem To { get; }
        public string Relation { get; }
        public string Context { get; }
    }
}
