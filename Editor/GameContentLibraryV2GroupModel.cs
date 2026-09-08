using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentLibraryV2GroupModel
    {
        public GameContentLibraryV2GroupModel(GameContentLibraryKind kind, string name, int totalCount, IReadOnlyList<GameContentLibraryV2ItemModel> items)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            TotalCount = totalCount;
            Items = items == null ? Array.Empty<GameContentLibraryV2ItemModel>() : items.ToArray();
            BlockerCount = Items.Count(item => item.Source.ErrorCount > 0);
            WarningCount = Items.Count(item => item.Source.ErrorCount == 0 && item.Source.WarningCount > 0);
            ReadyCount = Items.Count(item => item.Source.ErrorCount == 0 && item.Source.WarningCount == 0);
        }

        public GameContentLibraryKind Kind { get; }
        public string Name { get; }
        public int TotalCount { get; }
        public IReadOnlyList<GameContentLibraryV2ItemModel> Items { get; }
        public int BlockerCount { get; }
        public int WarningCount { get; }
        public int ReadyCount { get; }
    }
}
