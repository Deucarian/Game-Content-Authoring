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
    public static class GameContentLibraryService
    {
        private static readonly GameContentLibraryTypeInfo[] KnownTypes =
        {
            new GameContentLibraryTypeInfo("AttackDefinitionAsset", GameContentLibraryKind.Attack, "Attacks"),
            new GameContentLibraryTypeInfo("EnemyDefinitionAsset", GameContentLibraryKind.Enemy, "Enemies"),
            new GameContentLibraryTypeInfo("WaveDefinitionAsset", GameContentLibraryKind.Wave, "Waves"),
            new GameContentLibraryTypeInfo("WeaponDefinitionAsset", GameContentLibraryKind.Weapon, "Tower / Weapon"),
            new GameContentLibraryTypeInfo("RunUpgradeDefinitionAsset", GameContentLibraryKind.Upgrade, "Upgrades"),
            new GameContentLibraryTypeInfo("GameContentSetAsset", GameContentLibraryKind.ContentSet, "Game / Run Content Sets"),
            new GameContentLibraryTypeInfo("RunContentSetAsset", GameContentLibraryKind.ContentSet, "Game / Run Content Sets"),
            new GameContentLibraryTypeInfo("GameContentPackAsset", GameContentLibraryKind.ContentPack, "Content Packs"),
            new GameContentLibraryTypeInfo("ContentPackAsset", GameContentLibraryKind.ContentPack, "Content Packs")
        };

        private static readonly IReadOnlyDictionary<string, HashSet<string>> CanonicalCompanionAssetNames =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "AttackDefinition",
                    new HashSet<string>(
                        new[] { "Delivery", "Mechanics", "Presentation", "StatusEffects", "Targeting" },
                        StringComparer.OrdinalIgnoreCase)
                },
                {
                    "EnemyDefinition",
                    new HashSet<string>(
                        new[] { "Presentation", "Stats" },
                        StringComparer.OrdinalIgnoreCase)
                },
                {
                    "WaveDefinition",
                    new HashSet<string>(
                        new[] { "Entries", "Schedule" },
                        StringComparer.OrdinalIgnoreCase)
                },
                {
                    "WeaponDefinition",
                    new HashSet<string>(
                        new[] { "Presentation", "Stats" },
                        StringComparer.OrdinalIgnoreCase)
                },
                {
                    "RunUpgradeDefinition",
                    new HashSet<string>(
                        new[] { "Economy", "Effects" },
                        StringComparer.OrdinalIgnoreCase)
                }
            };

        public static GameContentLibraryReport Scan(string rootPath)
        {
            return Scan(rootPath, null);
        }

        public static GameContentLibraryReport Scan(
            string rootPath,
            IEnumerable<GameContentSourceIdentity> excludedSources)
        {
            string normalizedRoot = GameContentAuthoringEditorPaths.NormalizeAssetFolderPath(rootPath, GameContentLibraryProvider.DefaultRoot);
            List<GameContentLibraryItem> items = new List<GameContentLibraryItem>();
            List<GameContentLibraryIssue> reportIssues = new List<GameContentLibraryIssue>();
            var excluded = new HashSet<string>(
                excludedSources == null
                    ? Array.Empty<string>()
                    : excludedSources.Where(value => value != null && value.IsValid).Select(value => value.StableKey),
                StringComparer.OrdinalIgnoreCase);

            if (!GameContentAuthoringEditorPaths.IsValidAssetFolderPath(normalizedRoot, GameContentLibraryProvider.DefaultRoot))
            {
                reportIssues.Add(GameContentLibraryIssue.Error("Content Root", "Content root must be Assets or a folder below Assets."));
                return BuildReport(normalizedRoot, items, reportIssues);
            }

            if (!AssetDatabase.IsValidFolder(normalizedRoot))
            {
                reportIssues.Add(GameContentLibraryIssue.Info(normalizedRoot, "No Assets/GameContent folder exists yet."));
                return BuildReport(normalizedRoot, items, reportIssues);
            }

            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { normalizedRoot });
            Dictionary<UnityEngine.Object, GameContentLibraryItem> objectMap = new Dictionary<UnityEngine.Object, GameContentLibraryItem>();
            HashSet<string> seenItemKeys = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < guids.Length; i++)
            {
                string sourceKey = new GameContentSourceIdentity(
                    GameContentSourceIdentity.UnityAssetGuidKind,
                    guids[i]).StableKey;
                if (excluded.Contains(sourceKey)) continue;
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main == null) continue;
                GameContentLibraryTypeInfo typeInfo = FindTypeInfo(main.GetType());
                if (typeInfo == null) continue;

                string key = AssetDatabase.AssetPathToGUID(path) + "::" + main.GetInstanceID().ToString(CultureInfo.InvariantCulture);
                if (!seenItemKeys.Add(key)) continue;

                GameContentLibraryItem item = new GameContentLibraryItem(
                    key,
                    main,
                    typeInfo.Kind,
                    typeInfo.Category,
                    path,
                    GameContentLibraryMemberAccess.ReadStringMember(main, "Id", string.Empty),
                    GameContentLibraryMemberAccess.ReadStringMember(main, "DisplayName", main.name));
                items.Add(item);

                UnityEngine.Object[] allObjects = AssetDatabase.LoadAllAssetsAtPath(path);
                for (int j = 0; j < allObjects.Length; j++)
                {
                    UnityEngine.Object assetObject = allObjects[j];
                    if (assetObject != null && !objectMap.ContainsKey(assetObject))
                        objectMap.Add(assetObject, item);
                }
            }

            BuildReferences(items, objectMap);
            GameContentLibraryValidation.ValidateItems(items, reportIssues);
            return BuildReport(normalizedRoot, items, reportIssues);
        }

        internal static GameContentLibraryReport BuildProjection(
            string rootPath,
            IEnumerable<GameContentLibraryItem> items)
        {
            return BuildReport(
                rootPath ?? string.Empty,
                items == null ? new List<GameContentLibraryItem>() : items.Where(value => value != null).ToList(),
                new List<GameContentLibraryIssue>());
        }

        private static GameContentLibraryReport BuildReport(string rootPath, List<GameContentLibraryItem> items, List<GameContentLibraryIssue> reportIssues)
        {
            items.Sort((left, right) =>
            {
                int kind = left.Kind.CompareTo(right.Kind);
                if (kind != 0) return kind;
                return string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            GameContentLibraryReport report = new GameContentLibraryReport(rootPath, items, reportIssues);
            report.RebuildGroups(KnownTypes);
            report.RebuildContentSetSummaries();
            report.RebuildContentPackSummaries();
            return report;
        }

        private static GameContentLibraryTypeInfo FindTypeInfo(Type type)
        {
            while (type != null && type != typeof(ScriptableObject))
            {
                for (int i = 0; i < KnownTypes.Length; i++)
                {
                    if (string.Equals(type.Name, KnownTypes[i].TypeName, StringComparison.Ordinal))
                        return KnownTypes[i];
                }

                type = type.BaseType;
            }

            return null;
        }

        private static void BuildReferences(IReadOnlyList<GameContentLibraryItem> items, IReadOnlyDictionary<UnityEngine.Object, GameContentLibraryItem> objectMap)
        {
            for (int i = 0; i < items.Count; i++)
            {
                GameContentLibraryItem item = items[i];
                UnityEngine.Object[] assetObjects = AssetDatabase.LoadAllAssetsAtPath(item.Path);
                for (int j = 0; j < assetObjects.Length; j++)
                    AddSerializedReferences(item, assetObjects[j], objectMap);

                UnityEngine.Object[] companionObjects = LoadCompanionAssetObjects(item);
                for (int j = 0; j < companionObjects.Length; j++)
                    AddSerializedReferences(item, companionObjects[j], objectMap);
            }

            for (int i = 0; i < items.Count; i++)
            {
                GameContentLibraryItem source = items[i];
                for (int j = 0; j < source.DirectReferences.Count; j++)
                    source.DirectReferences[j].Target.AddReverseReference(new GameContentLibraryReference(source, source.DirectReferences[j].PropertyPath));
            }
        }

        private static UnityEngine.Object[] LoadCompanionAssetObjects(GameContentLibraryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path) || string.IsNullOrWhiteSpace(item.Folder))
                return Array.Empty<UnityEngine.Object>();

            string prefix = GetRootAssetPrefix(item);
            if (string.IsNullOrWhiteSpace(prefix))
                return Array.Empty<UnityEngine.Object>();

            string rootFileName = System.IO.Path.GetFileNameWithoutExtension(item.Path);
            CanonicalCompanionAssetNames.TryGetValue(rootFileName, out HashSet<string> canonicalCompanionNames);
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { item.Folder });
            var objects = new List<UnityEngine.Object>();
            var seen = new HashSet<UnityEngine.Object>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.Equals(path, item.Path, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/"), item.Folder, StringComparison.OrdinalIgnoreCase))
                    continue;

                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                bool legacyCompanion = !string.IsNullOrWhiteSpace(fileName) &&
                                       fileName.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase);
                bool canonicalCompanion = canonicalCompanionNames != null &&
                                          !string.IsNullOrWhiteSpace(fileName) &&
                                          canonicalCompanionNames.Contains(fileName);
                if (!legacyCompanion && !canonicalCompanion)
                    continue;

                UnityEngine.Object[] assetObjects = AssetDatabase.LoadAllAssetsAtPath(path);
                for (int j = 0; j < assetObjects.Length; j++)
                {
                    UnityEngine.Object assetObject = assetObjects[j];
                    if (assetObject != null && seen.Add(assetObject))
                        objects.Add(assetObject);
                }
            }

            return objects.ToArray();
        }

        private static string GetRootAssetPrefix(GameContentLibraryItem item)
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(item.Path);
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            string[] rootSuffixes =
            {
                "_AttackDefinition",
                "_EnemyDefinition",
                "_WaveDefinition",
                "_WeaponDefinition",
                "_RunUpgradeDefinition",
                "_GameContentSet",
                "_ContentPack"
            };

            for (int i = 0; i < rootSuffixes.Length; i++)
            {
                string suffix = rootSuffixes[i];
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return fileName.Substring(0, fileName.Length - suffix.Length);
            }

            return fileName;
        }

        private static void AddSerializedReferences(
            GameContentLibraryItem source,
            UnityEngine.Object serializedTarget,
            IReadOnlyDictionary<UnityEngine.Object, GameContentLibraryItem> objectMap)
        {
            if (source == null || serializedTarget == null) return;

            try
            {
                SerializedObject serializedObject = new SerializedObject(serializedTarget);
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = true;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;

                    UnityEngine.Object reference = iterator.objectReferenceValue;
                    if (reference == null)
                    {
                        if (iterator.objectReferenceInstanceIDValue != 0)
                            source.AddIssue(GameContentLibraryIssue.Error(
                                source.DisplayName + "." + iterator.propertyPath,
                                "Broken object reference on " + source.DisplayName + " (" + serializedTarget.name + ") at " + iterator.propertyPath + "."));
                        continue;
                    }

                    if (!objectMap.TryGetValue(reference, out GameContentLibraryItem target)) continue;
                    if (ReferenceEquals(target, source)) continue;
                    source.AddDirectReference(new GameContentLibraryReference(target, serializedTarget.name + "." + iterator.propertyPath));
                }
            }
            catch (Exception ex)
            {
                source.AddIssue(GameContentLibraryIssue.Warning(serializedTarget.name, "Could not inspect serialized references: " + ex.Message));
            }
        }

        internal static HashSet<GameContentLibraryItem> GetContentSetMembership(GameContentLibraryItem contentSet)
        {
            return GameContentLibraryReachability.GetContentSetMembership(contentSet);
        }

        internal static HashSet<GameContentLibraryItem> GetContentPackMembership(GameContentLibraryItem contentPack)
        {
            return GameContentLibraryReachability.GetContentPackMembership(contentPack);
        }

        internal static HashSet<GameContentLibraryItem> GetReachableItems(GameContentLibraryItem root, int depth)
        {
            return GameContentLibraryReachability.GetReachableItems(root, depth);
        }

    }
}
