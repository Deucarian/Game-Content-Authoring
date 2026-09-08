using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentExistingItemPresentation
    {
        internal static string GetIdLabel(GameContentLibraryItem item)
        {
            return item == null || string.IsNullOrWhiteSpace(item.Id) ? "(missing id)" : item.Id;
        }

        internal static DeucarianEditorStatus GetItemStatus(GameContentLibraryItem item)
        {
            if (item == null) return DeucarianEditorStatus.Disabled;
            if (item.ErrorCount > 0) return DeucarianEditorStatus.Error;
            if (item.WarningCount > 0) return DeucarianEditorStatus.Warning;
            return DeucarianEditorStatus.Success;
        }

        internal static GameContentLibraryKind? GetProviderKind(IGameContentAuthoringProvider provider)
        {
            if (provider == null) return null;
            string id = provider.ProviderId ?? string.Empty;
            if (id.EndsWith(".attack", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Attack;
            if (id.EndsWith(".enemy", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Enemy;
            if (id.EndsWith(".wave", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Wave;
            if (id.EndsWith(".weapon", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Weapon;
            if (id.EndsWith(".upgrade", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Upgrade;
            if (id.Contains("game-content-set")) return GameContentLibraryKind.ContentSet;
            if (id.Contains("content-pack")) return GameContentLibraryKind.ContentPack;
            return null;
        }
    }
}
