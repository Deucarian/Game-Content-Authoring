using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;


namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentLibraryValidation
    {
        internal static void ValidateItems(IReadOnlyList<GameContentLibraryItem> items, List<GameContentLibraryIssue> reportIssues)
        {
            for (int i = 0; i < items.Count; i++)
            {
                GameContentLibraryItem item = items[i];
                if (string.IsNullOrWhiteSpace(item.Id))
                    item.AddIssue(GameContentLibraryIssue.Error("ID", "Stable ID is missing."));
                if (string.IsNullOrWhiteSpace(item.DisplayName))
                    item.AddIssue(GameContentLibraryIssue.Warning("Display Name", "Display name is empty."));

                GameContentLibraryDomainValidation.AddDomainValidatorIssues(item);
                AddTypeSpecificIssues(item);
            }

            AddDuplicateIdIssues(items, reportIssues);
            AddUnusedAssetIssues(items);
            AddContentSetGraphIssues(items);
        }

        internal static void AddTypeSpecificIssues(GameContentLibraryItem item)
        {
            if (item.Kind == GameContentLibraryKind.Weapon && item.DirectReferences.All(reference => reference.Target.Kind != GameContentLibraryKind.Attack))
                item.AddIssue(GameContentLibraryIssue.Error("Weapon.Attack", item.DisplayName + " does not reference a discovered attack asset."));

            if (item.Kind == GameContentLibraryKind.Wave && item.DirectReferences.All(reference => reference.Target.Kind != GameContentLibraryKind.Enemy))
                item.AddIssue(GameContentLibraryIssue.Warning("Wave.Enemies", item.DisplayName + " does not reference any discovered enemy assets."));

            if (item.Kind == GameContentLibraryKind.ContentPack)
            {
                bool hasDefaultContentSet = GameContentLibraryMemberAccess.ReadMemberValue(item.Asset, "DefaultContentSet") != null;
                if (!hasDefaultContentSet)
                    item.AddIssue(GameContentLibraryIssue.Error("ContentPack.DefaultContentSet", item.DisplayName + " is missing its default Game / Run Content Set."));

                if (CountMemberReferences(item.Asset, "ContentSets", GameContentLibraryKind.ContentSet, item) == 0)
                    item.AddIssue(GameContentLibraryIssue.Error("ContentPack.ContentSets", item.DisplayName + " must include at least one discovered Game / Run Content Set."));
                return;
            }

            if (item.Kind != GameContentLibraryKind.ContentSet) return;

            bool hasStartingWeapon = GameContentLibraryMemberAccess.ReadMemberValue(item.Asset, "StartingWeapon") != null;
            if (!hasStartingWeapon)
                item.AddIssue(GameContentLibraryIssue.Error("ContentSet.StartingWeapon", item.DisplayName + " is missing its starting weapon/tower."));

            if (CountMemberReferences(item.Asset, "AvailableWeapons", GameContentLibraryKind.Weapon, item) == 0)
                item.AddIssue(GameContentLibraryIssue.Error("ContentSet.AvailableWeapons", item.DisplayName + " has an empty available weapon/tower list."));
            if (CountMemberReferences(item.Asset, "EnemyPool", GameContentLibraryKind.Enemy, item) == 0)
                item.AddIssue(GameContentLibraryIssue.Error("ContentSet.EnemyPool", item.DisplayName + " has an empty enemy pool."));
            if (CountMemberReferences(item.Asset, "WaveSet", GameContentLibraryKind.Wave, item) == 0)
                item.AddIssue(GameContentLibraryIssue.Error("ContentSet.WaveSet", item.DisplayName + " has an empty wave/spawn set list."));
            if (CountMemberReferences(item.Asset, "UpgradePool", GameContentLibraryKind.Upgrade, item) == 0)
                item.AddIssue(GameContentLibraryIssue.Warning("ContentSet.UpgradePool", item.DisplayName + " has an empty upgrade pool. The content set can still be valid, but progression will be limited."));
        }

        internal static int CountMemberReferences(UnityEngine.Object asset, string memberName, GameContentLibraryKind expectedKind, GameContentLibraryItem item)
        {
            object value = GameContentLibraryMemberAccess.ReadMemberValue(asset, memberName);
            if (value == null)
                return item.DirectReferences.Count(reference => reference.Target.Kind == expectedKind);

            if (value is UnityEngine.Object single)
                return single == null ? 0 : 1;

            if (!(value is IEnumerable enumerable))
                return 0;

            int count = 0;
            foreach (object element in enumerable)
            {
                if (element is UnityEngine.Object unityObject && unityObject != null)
                    count++;
            }

            return count;
        }

        internal static void AddDuplicateIdIssues(IReadOnlyList<GameContentLibraryItem> items, List<GameContentLibraryIssue> reportIssues)
        {
            var duplicates = items
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Category + "::" + item.Id, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToArray();

            for (int i = 0; i < duplicates.Length; i++)
            {
                string id = duplicates[i].First().Id;
                string category = duplicates[i].First().Category;
                string paths = string.Join(", ", duplicates[i].Select(item => item.Path).OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
                string message = "Duplicate " + category + " ID '" + id + "' appears in " + duplicates[i].Count().ToString(CultureInfo.InvariantCulture) + " assets: " + paths + ".";
                reportIssues.Add(GameContentLibraryIssue.Error("Duplicate IDs", message));
                foreach (GameContentLibraryItem item in duplicates[i])
                    item.AddIssue(GameContentLibraryIssue.Error("ID", message));
            }
        }

        internal static void AddUnusedAssetIssues(IReadOnlyList<GameContentLibraryItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                GameContentLibraryItem item = items[i];
                if (item.Kind == GameContentLibraryKind.ContentSet || item.Kind == GameContentLibraryKind.ContentPack) continue;
                if (item.ReverseReferences.Count == 0)
                    item.AddIssue(GameContentLibraryIssue.Info("References", "No authored assets currently reference this asset."));
            }
        }

        internal static void AddContentSetGraphIssues(IReadOnlyList<GameContentLibraryItem> items)
        {
            foreach (GameContentLibraryItem contentSet in items.Where(item => item.Kind == GameContentLibraryKind.ContentSet))
            {
                HashSet<GameContentLibraryItem> membership = GameContentLibraryReachability.GetContentSetMembership(contentSet);
                foreach (GameContentLibraryItem weapon in membership.Where(item => item.Kind == GameContentLibraryKind.Weapon))
                {
                    if (weapon.DirectReferences.All(reference => reference.Target.Kind != GameContentLibraryKind.Attack))
                        contentSet.AddIssue(GameContentLibraryIssue.Error("ContentSet.Weapons", weapon.DisplayName + " has no discovered attack reference."));
                }

                foreach (GameContentLibraryItem wave in membership.Where(item => item.Kind == GameContentLibraryKind.Wave))
                {
                    if (wave.DirectReferences.All(reference => reference.Target.Kind != GameContentLibraryKind.Enemy))
                        contentSet.AddIssue(GameContentLibraryIssue.Warning("ContentSet.Waves", wave.DisplayName + " has no discovered enemy references."));
                }

                foreach (GameContentLibraryItem upgrade in membership.Where(item => item.Kind == GameContentLibraryKind.Upgrade))
                {
                    for (int i = 0; i < upgrade.DirectReferences.Count; i++)
                    {
                        GameContentLibraryItem target = upgrade.DirectReferences[i].Target;
                        if (!membership.Contains(target) && target.Kind != GameContentLibraryKind.ContentSet)
                            contentSet.AddIssue(GameContentLibraryIssue.Warning("ContentSet.Upgrades", upgrade.DisplayName + " targets " + target.DisplayName + ", which is outside this content set."));
                    }
                }
            }

            AddContentPackGraphIssues(items);
        }

        internal static void AddContentPackGraphIssues(IReadOnlyList<GameContentLibraryItem> items)
        {
            foreach (GameContentLibraryItem contentPack in items.Where(item => item.Kind == GameContentLibraryKind.ContentPack))
            {
                HashSet<GameContentLibraryItem> membership = GameContentLibraryReachability.GetContentPackMembership(contentPack);
                if (membership.All(item => item.Kind != GameContentLibraryKind.ContentSet))
                {
                    contentPack.AddIssue(GameContentLibraryIssue.Error("ContentPack.ContentSets", "Pack does not reference any discovered Game / Run Content Sets."));
                    continue;
                }

                foreach (GameContentLibraryItem contentSet in membership.Where(item => item.Kind == GameContentLibraryKind.ContentSet))
                {
                    if (contentSet.ErrorCount > 0)
                        contentPack.AddIssue(GameContentLibraryIssue.Error("ContentPack.ContentSets", contentSet.DisplayName + " has blocking validation issues."));
                    else if (contentSet.WarningCount > 0)
                        contentPack.AddIssue(GameContentLibraryIssue.Warning("ContentPack.ContentSets", contentSet.DisplayName + " has validation warnings."));
                }
            }
        }
    }
}
