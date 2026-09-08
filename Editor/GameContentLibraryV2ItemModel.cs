using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentLibraryV2ItemModel
    {
        public GameContentLibraryV2ItemModel(GameContentLibraryItem source)
        {
            Source = source;
            DisplayName = source == null ? string.Empty : source.DisplayName;
            StableId = source == null || string.IsNullOrWhiteSpace(source.Id) ? "(missing id)" : source.Id;
            TypeLabel = source == null ? string.Empty : GameContentLibraryV2Model.GetKindChipLabel(source.Kind);
            DirectDependencyCount = source == null ? 0 : source.DirectReferences.Count;
            ReverseReferenceCount = source == null ? 0 : source.ReverseReferences.Count;
            ReadinessLabel = GameContentLibraryV2Model.GetReadinessLabel(source);
            ReadinessStatus = GameContentLibraryV2Model.GetStatus(source);
        }

        public GameContentLibraryItem Source { get; }
        public string DisplayName { get; }
        public string StableId { get; }
        public string TypeLabel { get; }
        public int DirectDependencyCount { get; }
        public int ReverseReferenceCount { get; }
        public string ReadinessLabel { get; }
        public DeucarianEditorStatus ReadinessStatus { get; }
    }
}
