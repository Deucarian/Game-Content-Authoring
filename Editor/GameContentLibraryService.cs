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
                    ReadStringMember(main, "Id", string.Empty),
                    ReadStringMember(main, "DisplayName", main.name));
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
            ValidateItems(items, reportIssues);
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

        private static void ValidateItems(IReadOnlyList<GameContentLibraryItem> items, List<GameContentLibraryIssue> reportIssues)
        {
            for (int i = 0; i < items.Count; i++)
            {
                GameContentLibraryItem item = items[i];
                if (string.IsNullOrWhiteSpace(item.Id))
                    item.AddIssue(GameContentLibraryIssue.Error("ID", "Stable ID is missing."));
                if (string.IsNullOrWhiteSpace(item.DisplayName))
                    item.AddIssue(GameContentLibraryIssue.Warning("Display Name", "Display name is empty."));

                AddDomainValidatorIssues(item);
                AddTypeSpecificIssues(item);
            }

            AddDuplicateIdIssues(items, reportIssues);
            AddUnusedAssetIssues(items);
            AddContentSetGraphIssues(items);
        }

        private static void AddDomainValidatorIssues(GameContentLibraryItem item)
        {
            Type validatorType = FindValidatorType(item.Asset.GetType());
            if (validatorType == null) return;

            MethodInfo validateMethod = validatorType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => string.Equals(method.Name, "Validate", StringComparison.Ordinal) && HasSingleAssignableParameter(method, item.Asset.GetType()));
            if (validateMethod == null) return;

            try
            {
                object result = validateMethod.Invoke(null, new[] { item.Asset });
                AddIssuesFromValidationResult(item, result);
            }
            catch (Exception ex)
            {
                item.AddIssue(GameContentLibraryIssue.Warning("Domain Validator", "Could not run domain validator: " + ex.GetBaseException().Message));
            }
        }

        private static Type FindValidatorType(Type assetType)
        {
            string[] validatorNames =
            {
                assetType.Namespace + ".AttackRecipeValidator",
                assetType.Namespace + ".EnemyDefinitionValidator",
                assetType.Namespace + ".WaveDefinitionValidator",
                assetType.Namespace + ".WeaponDefinitionValidator",
                assetType.Namespace + ".RunUpgradeDefinitionValidator",
                assetType.Namespace + ".GameContentSetValidator",
                assetType.Namespace + ".GameContentPackValidator"
            };

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < validatorNames.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(validatorNames[i])) continue;
                for (int j = 0; j < assemblies.Length; j++)
                {
                    Type type = assemblies[j].GetType(validatorNames[i], false);
                    if (type != null && HasApplicableValidateMethod(type, assetType)) return type;
                }
            }

            return null;
        }

        private static bool HasApplicableValidateMethod(Type validatorType, Type assetType)
        {
            MethodInfo method = validatorType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, "Validate", StringComparison.Ordinal) && HasSingleAssignableParameter(candidate, assetType));
            return method != null;
        }

        private static bool HasSingleAssignableParameter(MethodInfo method, Type assetType)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(assetType);
        }

        private static void AddIssuesFromValidationResult(GameContentLibraryItem item, object result)
        {
            if (result == null) return;
            object issues = ReadMemberValue(result, "Issues");
            if (!(issues is IEnumerable enumerable)) return;

            foreach (object issue in enumerable)
            {
                if (issue == null) continue;
                string path = ReadStringMember(issue, "Path", "Domain Validator");
                string message = ReadStringMember(issue, "Message", "Validation issue.");
                object severityValue = ReadMemberValue(issue, "Severity");
                GameContentAuthoringValidationSeverity severity = ParseSeverity(severityValue);
                item.AddIssue(new GameContentLibraryIssue(severity, path, message));
            }
        }

        private static GameContentAuthoringValidationSeverity ParseSeverity(object severityValue)
        {
            if (severityValue == null) return GameContentAuthoringValidationSeverity.Warning;
            string value = severityValue.ToString();
            if (string.Equals(value, "Error", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Blocker", StringComparison.OrdinalIgnoreCase))
                return GameContentAuthoringValidationSeverity.Error;
            if (string.Equals(value, "Info", StringComparison.OrdinalIgnoreCase))
                return GameContentAuthoringValidationSeverity.Info;
            return GameContentAuthoringValidationSeverity.Warning;
        }

        private static void AddTypeSpecificIssues(GameContentLibraryItem item)
        {
            if (item.Kind == GameContentLibraryKind.Weapon && item.DirectReferences.All(reference => reference.Target.Kind != GameContentLibraryKind.Attack))
                item.AddIssue(GameContentLibraryIssue.Error("Weapon.Attack", item.DisplayName + " does not reference a discovered attack asset."));

            if (item.Kind == GameContentLibraryKind.Wave && item.DirectReferences.All(reference => reference.Target.Kind != GameContentLibraryKind.Enemy))
                item.AddIssue(GameContentLibraryIssue.Warning("Wave.Enemies", item.DisplayName + " does not reference any discovered enemy assets."));

            if (item.Kind == GameContentLibraryKind.ContentPack)
            {
                bool hasDefaultContentSet = ReadMemberValue(item.Asset, "DefaultContentSet") != null;
                if (!hasDefaultContentSet)
                    item.AddIssue(GameContentLibraryIssue.Error("ContentPack.DefaultContentSet", item.DisplayName + " is missing its default Game / Run Content Set."));

                if (CountMemberReferences(item.Asset, "ContentSets", GameContentLibraryKind.ContentSet, item) == 0)
                    item.AddIssue(GameContentLibraryIssue.Error("ContentPack.ContentSets", item.DisplayName + " must include at least one discovered Game / Run Content Set."));
                return;
            }

            if (item.Kind != GameContentLibraryKind.ContentSet) return;

            bool hasStartingWeapon = ReadMemberValue(item.Asset, "StartingWeapon") != null;
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

        private static int CountMemberReferences(UnityEngine.Object asset, string memberName, GameContentLibraryKind expectedKind, GameContentLibraryItem item)
        {
            object value = ReadMemberValue(asset, memberName);
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

        private static void AddDuplicateIdIssues(IReadOnlyList<GameContentLibraryItem> items, List<GameContentLibraryIssue> reportIssues)
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

        private static void AddUnusedAssetIssues(IReadOnlyList<GameContentLibraryItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                GameContentLibraryItem item = items[i];
                if (item.Kind == GameContentLibraryKind.ContentSet || item.Kind == GameContentLibraryKind.ContentPack) continue;
                if (item.ReverseReferences.Count == 0)
                    item.AddIssue(GameContentLibraryIssue.Info("References", "No authored assets currently reference this asset."));
            }
        }

        private static void AddContentSetGraphIssues(IReadOnlyList<GameContentLibraryItem> items)
        {
            foreach (GameContentLibraryItem contentSet in items.Where(item => item.Kind == GameContentLibraryKind.ContentSet))
            {
                HashSet<GameContentLibraryItem> membership = GetContentSetMembership(contentSet);
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

        private static void AddContentPackGraphIssues(IReadOnlyList<GameContentLibraryItem> items)
        {
            foreach (GameContentLibraryItem contentPack in items.Where(item => item.Kind == GameContentLibraryKind.ContentPack))
            {
                HashSet<GameContentLibraryItem> membership = GetContentPackMembership(contentPack);
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

        internal static HashSet<GameContentLibraryItem> GetContentSetMembership(GameContentLibraryItem contentSet)
        {
            HashSet<GameContentLibraryItem> membership = new HashSet<GameContentLibraryItem>();
            if (contentSet == null) return membership;
            membership.Add(contentSet);
            for (int i = 0; i < contentSet.DirectReferences.Count; i++)
            {
                GameContentLibraryItem direct = contentSet.DirectReferences[i].Target;
                if (direct == null) continue;
                membership.Add(direct);
                if (direct.Kind != GameContentLibraryKind.Weapon && direct.Kind != GameContentLibraryKind.Wave)
                    continue;
                for (int j = 0; j < direct.DirectReferences.Count; j++)
                    membership.Add(direct.DirectReferences[j].Target);
            }

            return membership;
        }

        internal static HashSet<GameContentLibraryItem> GetContentPackMembership(GameContentLibraryItem contentPack)
        {
            HashSet<GameContentLibraryItem> membership = new HashSet<GameContentLibraryItem>();
            if (contentPack == null) return membership;
            membership.Add(contentPack);
            for (int i = 0; i < contentPack.DirectReferences.Count; i++)
            {
                GameContentLibraryItem direct = contentPack.DirectReferences[i].Target;
                if (direct == null) continue;
                membership.Add(direct);
                if (direct.Kind != GameContentLibraryKind.ContentSet) continue;
                membership.UnionWith(GetContentSetMembership(direct));
            }

            return membership;
        }

        internal static HashSet<GameContentLibraryItem> GetReachableItems(GameContentLibraryItem root, int depth)
        {
            HashSet<GameContentLibraryItem> visited = new HashSet<GameContentLibraryItem>();
            if (root == null) return visited;
            CollectReachable(root, depth, visited);
            return visited;
        }

        private static void CollectReachable(GameContentLibraryItem item, int depth, HashSet<GameContentLibraryItem> visited)
        {
            if (item == null || depth < 0 || !visited.Add(item)) return;
            for (int i = 0; i < item.DirectReferences.Count; i++)
                CollectReachable(item.DirectReferences[i].Target, depth - 1, visited);
        }

        private static string ReadStringMember(object target, string memberName, string fallback)
        {
            object value = ReadMemberValue(target, memberName);
            if (value is string text)
                return string.IsNullOrWhiteSpace(text) ? fallback : text;
            return fallback;
        }

        private static object ReadMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName)) return null;
            Type type = target.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(target, null);
                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (field != null)
                    return field.GetValue(target);
                type = type.BaseType;
            }

            return null;
        }
    }
}
