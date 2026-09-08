using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringObjectPreviewOptions
    {
        public float MinimumHeight { get; set; } = 184f;
        public GameContentAuthoringActionPreview ActionPreview { get; set; }
    }
}
