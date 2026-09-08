using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public enum GameContentLibraryV2ReadinessFilter
    {
        All = 0,
        Ready = 1,
        Warnings = 2,
        Blockers = 3
    }
}
