using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentLibraryV2UiContract
    {
        public static readonly string[] DetailPages =
        {
            "Overview",
            "Dependencies",
            "Used By",
            "Validation",
            "Readiness",
            "Advanced"
        };

        public static readonly string[] MainRowActionLabels =
        {
            "Ping",
            "Open"
        };

        public static readonly string[] GraphRelations =
        {
            "Content Pack -> Content Sets",
            "Content Set -> Weapons",
            "Weapon -> Attack",
            "Content Set -> Waves",
            "Wave -> Enemies",
            "Content Set -> Upgrades",
            "Upgrade -> Target"
        };
    }
}
