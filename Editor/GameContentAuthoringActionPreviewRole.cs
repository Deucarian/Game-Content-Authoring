using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringActionPreviewRole
    {
        public GameContentAuthoringActionPreviewRole(string role, string label, UnityEngine.Object asset = null, string tooltip = null)
        {
            Role = role ?? string.Empty;
            Label = label ?? string.Empty;
            Asset = asset;
            Tooltip = tooltip ?? string.Empty;
        }

        public string Role { get; }
        public string Label { get; }
        public UnityEngine.Object Asset { get; }
        public string Tooltip { get; }
    }
}
