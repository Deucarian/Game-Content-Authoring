using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentLibraryV2Dashboard
    {
        public GameContentLibraryV2Dashboard(
            int totalAssets,
            int readyContentPacks,
            int readyContentSets,
            int blockers,
            int warnings,
            int duplicateIds,
            int missingReferences)
        {
            TotalAssets = totalAssets;
            ReadyContentPacks = readyContentPacks;
            ReadyContentSets = readyContentSets;
            Blockers = blockers;
            Warnings = warnings;
            DuplicateIds = duplicateIds;
            MissingReferences = missingReferences;
        }

        public int TotalAssets { get; }
        public int ReadyContentPacks { get; }
        public int ReadyContentSets { get; }
        public int Blockers { get; }
        public int Warnings { get; }
        public int DuplicateIds { get; }
        public int MissingReferences { get; }
    }
}
