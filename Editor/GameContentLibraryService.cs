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
            new GameContentLibraryTypeInfo("RunContentSetAsset", GameContentLibraryKind.ContentSet, "Game / Run Content Sets")
        };

        public static GameContentLibraryReport Scan(string rootPath)
        {
            string normalizedRoot = GameContentAuthoringEditorPaths.NormalizeAssetFolderPath(rootPath, GameContentLibraryProvider.DefaultRoot);
            List<GameContentLibraryItem> items = new List<GameContentLibraryItem>();
            List<GameContentLibraryIssue> reportIssues = new List<GameContentLibraryIssue>();

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
            }

            for (int i = 0; i < items.Count; i++)
            {
                GameContentLibraryItem source = items[i];
                for (int j = 0; j < source.DirectReferences.Count; j++)
                    source.DirectReferences[j].Target.AddReverseReference(new GameContentLibraryReference(source, source.DirectReferences[j].PropertyPath));
            }
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
                            source.AddIssue(GameContentLibraryIssue.Error(iterator.propertyPath, "Broken object reference on " + serializedTarget.name + "."));
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
                assetType.Namespace + ".GameContentSetValidator"
            };

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < validatorNames.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(validatorNames[i])) continue;
                for (int j = 0; j < assemblies.Length; j++)
                {
                    Type type = assemblies[j].GetType(validatorNames[i], false);
                    if (type != null) return type;
                }
            }

            return null;
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
                item.AddIssue(GameContentLibraryIssue.Error("Weapon.Attack", "Weapon does not reference a discovered attack asset."));

            if (item.Kind == GameContentLibraryKind.Wave && item.DirectReferences.All(reference => reference.Target.Kind != GameContentLibraryKind.Enemy))
                item.AddIssue(GameContentLibraryIssue.Warning("Wave.Enemies", "Wave does not reference any discovered enemy assets."));

            if (item.Kind != GameContentLibraryKind.ContentSet) return;

            bool hasStartingWeapon = ReadMemberValue(item.Asset, "StartingWeapon") != null;
            if (!hasStartingWeapon)
                item.AddIssue(GameContentLibraryIssue.Error("ContentSet.StartingWeapon", "Starting weapon/tower is missing."));

            if (CountMemberReferences(item.Asset, "AvailableWeapons", GameContentLibraryKind.Weapon, item) == 0)
                item.AddIssue(GameContentLibraryIssue.Error("ContentSet.AvailableWeapons", "Available weapon/tower list is empty."));
            if (CountMemberReferences(item.Asset, "EnemyPool", GameContentLibraryKind.Enemy, item) == 0)
                item.AddIssue(GameContentLibraryIssue.Error("ContentSet.EnemyPool", "Enemy pool is empty."));
            if (CountMemberReferences(item.Asset, "WaveSet", GameContentLibraryKind.Wave, item) == 0)
                item.AddIssue(GameContentLibraryIssue.Error("ContentSet.WaveSet", "Wave/spawn set list is empty."));
            if (CountMemberReferences(item.Asset, "UpgradePool", GameContentLibraryKind.Upgrade, item) == 0)
                item.AddIssue(GameContentLibraryIssue.Warning("ContentSet.UpgradePool", "Upgrade pool is empty. The content set can still be valid, but progression will be limited."));
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
                string message = "Duplicate " + category + " ID '" + id + "' appears in " + duplicates[i].Count().ToString(CultureInfo.InvariantCulture) + " assets.";
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
                if (item.Kind == GameContentLibraryKind.ContentSet) continue;
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
