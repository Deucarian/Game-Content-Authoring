using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    [InitializeOnLoad]
    internal static class GameContentLibraryProviderRegistration
    {
        static GameContentLibraryProviderRegistration()
        {
            GameContentAuthoringProviderRegistry.Register(new GameContentLibraryProvider());
        }
    }

    public sealed class GameContentLibraryProvider : IGameContentAuthoringProvider
    {
        public const string ContentLibraryProviderId = "com.deucarian.game-content-authoring.content-library";
        public const string DefaultRoot = "Assets/GameContent";

        private string _rootPath = DefaultRoot;
        private GameContentLibraryReport _report;
        private string _selectedKey;

        public string ProviderId => ContentLibraryProviderId;
        public string DisplayName => "Content Library";
        public string Description => "Browse, inspect, validate, and understand authored game content.";
        public int SortOrder => 1000;
        public bool Enabled => true;

        public void OnSelected()
        {
            Refresh(false);
        }

        public void Draw(GameContentAuthoringContext context)
        {
            if (_report == null) Refresh(false);

            context.DrawSection("Library", () =>
            {
                EditorGUILayout.LabelField("Browse authored assets under Assets/GameContent and validate their references as a playable recipe.", context.MutedStyle);
                GUILayout.Space(DeucarianEditorSpacing.Small);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _rootPath = EditorGUILayout.TextField("Content Root", _rootPath);
                    if (context.DrawSecondaryButton("Validate All Game Content", true, GUILayout.Width(168f)))
                        Refresh(true);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (context.DrawSecondaryButton("Ping Root", AssetDatabase.IsValidFolder(NormalizedRoot), GUILayout.Width(92f)))
                    {
                        UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(NormalizedRoot);
                        if (folder != null) EditorGUIUtility.PingObject(folder);
                    }

                    if (context.DrawSecondaryButton("Copy Summary", _report != null, GUILayout.Width(112f)))
                        EditorGUIUtility.systemCopyBuffer = GameContentLibraryReportWriter.ToMarkdown(_report);

                    GUILayout.FlexibleSpace();
                }
            });

            DrawReportSummary(context);
            DrawGroups(context);
        }

        public void DrawPreview(GameContentAuthoringPreviewContext context)
        {
            if (_report == null) Refresh(false);

            GameContentLibraryItem selected = SelectedItem;
            if (selected == null)
            {
                context.DrawCard("Selection", () =>
                {
                    EditorGUILayout.LabelField("Select a content asset from the library to inspect validation, dependencies, and reverse references.", context.MutedStyle);
                });
                return;
            }

            context.DrawCard("Selected Asset", () =>
            {
                context.DrawSummaryRows(new[]
                {
                    new GameContentAuthoringPreviewRow("ID", string.IsNullOrWhiteSpace(selected.Id) ? "(missing)" : selected.Id),
                    new GameContentAuthoringPreviewRow("Name", selected.DisplayName),
                    new GameContentAuthoringPreviewRow("Type", selected.Category),
                    new GameContentAuthoringPreviewRow("Path", selected.Path),
                    new GameContentAuthoringPreviewRow("State", selected.ValidationLabel)
                });

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (context.DrawSecondaryButton("Ping", selected.Asset != null, GUILayout.Width(56f)) && selected.Asset != null)
                        EditorGUIUtility.PingObject(selected.Asset);
                    if (context.DrawSecondaryButton("Select", selected.Asset != null, GUILayout.Width(64f)) && selected.Asset != null)
                        Selection.activeObject = selected.Asset;
                    if (context.DrawSecondaryButton("Open Folder", AssetDatabase.IsValidFolder(selected.Folder), GUILayout.Width(92f)))
                    {
                        UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(selected.Folder);
                        if (folder != null)
                        {
                            Selection.activeObject = folder;
                            EditorGUIUtility.PingObject(folder);
                        }
                    }

                    if (context.DrawSecondaryButton("Revalidate", true, GUILayout.Width(88f)))
                        Refresh(true);
                }
            });

            DrawSelectedValidation(context, selected);
            DrawReferenceList(context, "Direct References", selected.DirectReferences, "No authored direct references found.");
            DrawReferenceList(context, "Referenced By", selected.ReverseReferences, "No authored assets reference this asset.");
            DrawDependencyGraph(context, selected);

            if (selected.Kind == GameContentLibraryKind.ContentSet)
            {
                GameContentLibraryContentSetSummary summary = _report.GetContentSetSummary(selected);
                if (summary != null)
                {
                    context.DrawCard("Ready To Play", () =>
                    {
                        DeucarianEditorStatusPanel.DrawStatusCard(summary.Message, summary.Ready ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning);
                        context.DrawSummaryRows(new[]
                        {
                            new GameContentAuthoringPreviewRow("Weapons", summary.WeaponCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Enemies", summary.EnemyCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Waves", summary.WaveCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Upgrades", summary.UpgradeCount.ToString(CultureInfo.InvariantCulture))
                        });

                        if (context.DrawSecondaryButton("Copy Content Set Summary", true, GUILayout.Width(172f)))
                            EditorGUIUtility.systemCopyBuffer = GameContentLibraryReportWriter.ToContentSetMarkdown(_report, selected);
                    });
                }
            }
        }

        public void StopPreview()
        {
        }

        private string NormalizedRoot => GameContentAuthoringEditorPaths.NormalizeAssetFolderPath(_rootPath, DefaultRoot);

        private GameContentLibraryItem SelectedItem
        {
            get
            {
                if (_report == null || string.IsNullOrWhiteSpace(_selectedKey)) return null;
                return _report.Items.FirstOrDefault(item => string.Equals(item.Key, _selectedKey, StringComparison.Ordinal));
            }
        }

        private void Refresh(bool forceSelection)
        {
            string previous = _selectedKey;
            _report = GameContentLibraryService.Scan(NormalizedRoot);
            if (forceSelection || string.IsNullOrWhiteSpace(previous) || _report.Items.All(item => !string.Equals(item.Key, previous, StringComparison.Ordinal)))
                _selectedKey = _report.Items.Count > 0 ? _report.Items[0].Key : string.Empty;
        }

        private void DrawReportSummary(GameContentAuthoringContext context)
        {
            if (_report == null) return;

            GameContentAuthoringValidationResult validation = _report.ToValidationResult();
            string ready = _report.Items.Count == 0
                ? "No authored game content was found under " + _report.RootPath + "."
                : _report.ReadyContentSetCount.ToString(CultureInfo.InvariantCulture) + " ready content set(s), " + _report.Items.Count.ToString(CultureInfo.InvariantCulture) + " authored asset(s).";
            context.DrawValidation(validation, ready);

            context.DrawInlineCard(() =>
            {
                EditorGUILayout.LabelField("Batch Validation", context.SectionTitleStyle);
                EditorGUILayout.LabelField(_report.BlockerCount.ToString(CultureInfo.InvariantCulture) + " blocker(s), " + _report.WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s), " + _report.InfoCount.ToString(CultureInfo.InvariantCulture) + " info item(s).", context.MutedStyle);
                DrawSeverityRows(_report);
            });
        }

        private static void DrawSeverityRows(GameContentLibraryReport report)
        {
            DrawIssueBucket("Blockers", report.BlockerCount, report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error));
            DrawIssueBucket("Warnings", report.WarningCount, report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning));
            DrawIssueBucket("Info", report.InfoCount, report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Info));
        }

        private static void DrawIssueBucket(string label, int count, IEnumerable<GameContentLibraryIssue> issues)
        {
            if (count == 0) return;
            EditorGUILayout.LabelField(label, DeucarianEditorStyles.SectionTitle);
            foreach (GameContentLibraryIssue issue in issues.Take(6))
                EditorGUILayout.LabelField(issue.Path + ": " + issue.Message, DeucarianEditorStyles.MutedLabel);
        }

        private void DrawGroups(GameContentAuthoringContext context)
        {
            if (_report == null) return;

            foreach (GameContentLibraryGroup group in _report.Groups)
            {
                context.DrawSection(group.Name + " (" + group.Items.Count.ToString(CultureInfo.InvariantCulture) + ")", () =>
                {
                    if (group.Items.Count == 0)
                    {
                        EditorGUILayout.LabelField("No authored assets found.", context.MutedStyle);
                        return;
                    }

                    for (int i = 0; i < group.Items.Count; i++)
                    {
                        GameContentLibraryItem item = group.Items[i];
                        bool selected = string.Equals(item.Key, _selectedKey, StringComparison.Ordinal);
                        Rect rect = EditorGUILayout.BeginHorizontal(GUIStyle.none, GUILayout.MinHeight(40f));
                        if (Event.current.type == EventType.Repaint)
                        {
                            Color background = selected
                                ? new Color(0.07f, 0.30f, 0.32f, 0.82f)
                                : new Color(0.07f, 0.13f, 0.17f, 0.58f);
                            Color border = selected ? DeucarianEditorTheme.Accent : DeucarianEditorTheme.BorderSubtle;
                            DeucarianEditorVisualShell.DrawInsetSurface(rect, background, border, 5f);
                        }

                        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                        {
                            _selectedKey = item.Key;
                            GUI.FocusControl(null);
                            Event.current.Use();
                        }

                        GUILayout.Space(8f);
                        EditorGUILayout.BeginVertical();
                        EditorGUILayout.LabelField(item.DisplayName, DeucarianEditorStyles.SectionTitle);
                        EditorGUILayout.LabelField(item.IdAndPathLabel, DeucarianEditorStyles.MutedLabel);
                        EditorGUILayout.EndVertical();
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField(item.ValidationLabel, ValidationMiniStyle(item), GUILayout.Width(84f));
                        GUILayout.Space(8f);
                        EditorGUILayout.EndHorizontal();
                    }
                });
            }
        }

        private static GUIStyle ValidationMiniStyle(GameContentLibraryItem item)
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap = false
            };
            if (item.ErrorCount > 0) style.normal.textColor = DeucarianEditorTheme.Error;
            else if (item.WarningCount > 0) style.normal.textColor = DeucarianEditorTheme.Warning;
            else style.normal.textColor = DeucarianEditorTheme.Success;
            return style;
        }

        private static void DrawSelectedValidation(GameContentAuthoringPreviewContext context, GameContentLibraryItem selected)
        {
            context.DrawCard("Validation", () =>
            {
                if (selected.Issues.Count == 0)
                {
                    DeucarianEditorStatusPanel.DrawStatusCard("No validation issues found for this asset.", DeucarianEditorStatus.Success);
                    return;
                }

                List<string> messages = new List<string>();
                for (int i = 0; i < selected.Issues.Count; i++)
                    messages.Add(selected.Issues[i].Path + ": " + selected.Issues[i].Message);
                DeucarianEditorStatus status = selected.ErrorCount > 0 ? DeucarianEditorStatus.Error : DeucarianEditorStatus.Warning;
                DeucarianEditorStatusPanel.DrawValidationCard(selected.ValidationLabel, messages, status);
            });
        }

        private static void DrawReferenceList(GameContentAuthoringPreviewContext context, string title, IReadOnlyList<GameContentLibraryReference> references, string emptyText)
        {
            context.DrawCard(title, () =>
            {
                if (references == null || references.Count == 0)
                {
                    EditorGUILayout.LabelField(emptyText, context.MutedStyle);
                    return;
                }

                for (int i = 0; i < references.Count; i++)
                {
                    GameContentLibraryReference reference = references[i];
                    context.DrawInlineCard(() =>
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(reference.Target.DisplayName, DeucarianEditorStyles.SectionTitle);
                            GUILayout.FlexibleSpace();
                            EditorGUILayout.LabelField(reference.Target.Category, DeucarianEditorStyles.MutedLabel, GUILayout.Width(112f));
                        }

                        EditorGUILayout.LabelField(reference.PropertyPath, context.MutedStyle);
                    });
                }
            });
        }

        private static void DrawDependencyGraph(GameContentAuthoringPreviewContext context, GameContentLibraryItem selected)
        {
            context.DrawCard("Dependency Graph", () =>
            {
                List<string> lines = GameContentLibraryReportWriter.BuildDependencyLines(selected, 3);
                if (lines.Count == 0)
                {
                    EditorGUILayout.LabelField("No authored dependency edges found.", context.MutedStyle);
                    return;
                }

                for (int i = 0; i < lines.Count; i++)
                    EditorGUILayout.LabelField(lines[i], i == 0 ? DeucarianEditorStyles.SectionTitle : context.MutedStyle);
            });
        }
    }

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

    public sealed class GameContentLibraryReport
    {
        private readonly List<GameContentLibraryGroup> _groups = new List<GameContentLibraryGroup>();
        private readonly List<GameContentLibraryContentSetSummary> _contentSetSummaries = new List<GameContentLibraryContentSetSummary>();

        internal GameContentLibraryReport(string rootPath, IReadOnlyList<GameContentLibraryItem> items, IReadOnlyList<GameContentLibraryIssue> reportIssues)
        {
            RootPath = rootPath ?? string.Empty;
            Items = items == null ? Array.Empty<GameContentLibraryItem>() : items.ToArray();
            ReportIssues = reportIssues == null ? Array.Empty<GameContentLibraryIssue>() : reportIssues.ToArray();
        }

        public string RootPath { get; }
        public IReadOnlyList<GameContentLibraryItem> Items { get; }
        public IReadOnlyList<GameContentLibraryIssue> ReportIssues { get; }
        public IReadOnlyList<GameContentLibraryGroup> Groups => _groups;
        public IReadOnlyList<GameContentLibraryContentSetSummary> ContentSetSummaries => _contentSetSummaries;
        public IEnumerable<GameContentLibraryIssue> AllIssues => ReportIssues.Concat(Items.SelectMany(item => item.Issues));
        public int BlockerCount => AllIssues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error);
        public int WarningCount => AllIssues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning);
        public int InfoCount => AllIssues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Info);
        public int ReadyContentSetCount => _contentSetSummaries.Count(summary => summary.Ready);

        public GameContentAuthoringValidationResult ToValidationResult()
        {
            return new GameContentAuthoringValidationResult(AllIssues
                .Select(issue => new GameContentAuthoringValidationIssue(issue.Severity, issue.Path, issue.Message))
                .ToArray());
        }

        public GameContentLibraryContentSetSummary GetContentSetSummary(GameContentLibraryItem item)
        {
            if (item == null) return null;
            return _contentSetSummaries.FirstOrDefault(summary => ReferenceEquals(summary.Item, item));
        }

        internal void RebuildGroups(IReadOnlyList<GameContentLibraryTypeInfo> knownTypes)
        {
            _groups.Clear();
            HashSet<GameContentLibraryKind> added = new HashSet<GameContentLibraryKind>();
            for (int i = 0; i < knownTypes.Count; i++)
            {
                GameContentLibraryTypeInfo typeInfo = knownTypes[i];
                if (!added.Add(typeInfo.Kind)) continue;
                _groups.Add(new GameContentLibraryGroup(typeInfo.Category, Items.Where(item => item.Kind == typeInfo.Kind).ToArray()));
            }
        }

        internal void RebuildContentSetSummaries()
        {
            _contentSetSummaries.Clear();
            foreach (GameContentLibraryItem contentSet in Items.Where(item => item.Kind == GameContentLibraryKind.ContentSet))
            {
                HashSet<GameContentLibraryItem> membership = GameContentLibraryService.GetContentSetMembership(contentSet);
                int weaponCount = membership.Count(item => item.Kind == GameContentLibraryKind.Weapon);
                int enemyCount = membership.Count(item => item.Kind == GameContentLibraryKind.Enemy);
                int waveCount = membership.Count(item => item.Kind == GameContentLibraryKind.Wave);
                int upgradeCount = membership.Count(item => item.Kind == GameContentLibraryKind.Upgrade);
                bool ready = contentSet.ErrorCount == 0 && weaponCount > 0 && enemyCount > 0 && waveCount > 0;
                string message = ready
                    ? "Ready to play: all required authored content is connected."
                    : "Not ready: resolve blocker issues or add required authored content.";
                _contentSetSummaries.Add(new GameContentLibraryContentSetSummary(contentSet, ready, message, weaponCount, enemyCount, waveCount, upgradeCount));
            }
        }
    }

    public sealed class GameContentLibraryItem
    {
        private readonly List<GameContentLibraryIssue> _issues = new List<GameContentLibraryIssue>();
        private readonly List<GameContentLibraryReference> _directReferences = new List<GameContentLibraryReference>();
        private readonly List<GameContentLibraryReference> _reverseReferences = new List<GameContentLibraryReference>();

        internal GameContentLibraryItem(string key, UnityEngine.Object asset, GameContentLibraryKind kind, string category, string path, string id, string displayName)
        {
            Key = key ?? string.Empty;
            Asset = asset;
            Kind = kind;
            Category = category ?? string.Empty;
            Path = path ?? string.Empty;
            string folder = string.IsNullOrWhiteSpace(Path) ? string.Empty : System.IO.Path.GetDirectoryName(Path);
            Folder = string.IsNullOrWhiteSpace(folder) ? "Assets" : folder.Replace("\\", "/");
            Id = id ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? asset != null ? asset.name : "(unnamed)" : displayName;
        }

        public string Key { get; }
        public UnityEngine.Object Asset { get; }
        public GameContentLibraryKind Kind { get; }
        public string Category { get; }
        public string Path { get; }
        public string Folder { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<GameContentLibraryIssue> Issues => _issues;
        public IReadOnlyList<GameContentLibraryReference> DirectReferences => _directReferences;
        public IReadOnlyList<GameContentLibraryReference> ReverseReferences => _reverseReferences;
        public int ErrorCount => _issues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error);
        public int WarningCount => _issues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning);
        public string IdAndPathLabel => (string.IsNullOrWhiteSpace(Id) ? "(missing id)" : Id) + " - " + Path;
        public string ValidationLabel => ErrorCount > 0 ? ErrorCount.ToString(CultureInfo.InvariantCulture) + " blocker(s)" : WarningCount > 0 ? WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s)" : "Ready";

        internal void AddIssue(GameContentLibraryIssue issue)
        {
            if (issue != null) _issues.Add(issue);
        }

        internal void AddDirectReference(GameContentLibraryReference reference)
        {
            if (reference == null || reference.Target == null) return;
            if (_directReferences.Any(existing => ReferenceEquals(existing.Target, reference.Target) && string.Equals(existing.PropertyPath, reference.PropertyPath, StringComparison.Ordinal)))
                return;
            _directReferences.Add(reference);
        }

        internal void AddReverseReference(GameContentLibraryReference reference)
        {
            if (reference == null || reference.Target == null) return;
            if (_reverseReferences.Any(existing => ReferenceEquals(existing.Target, reference.Target) && string.Equals(existing.PropertyPath, reference.PropertyPath, StringComparison.Ordinal)))
                return;
            _reverseReferences.Add(reference);
        }
    }

    public sealed class GameContentLibraryReference
    {
        public GameContentLibraryReference(GameContentLibraryItem target, string propertyPath)
        {
            Target = target;
            PropertyPath = propertyPath ?? string.Empty;
        }

        public GameContentLibraryItem Target { get; }
        public string PropertyPath { get; }
    }

    public sealed class GameContentLibraryIssue
    {
        public GameContentLibraryIssue(GameContentAuthoringValidationSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public GameContentAuthoringValidationSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public static GameContentLibraryIssue Info(string path, string message)
        {
            return new GameContentLibraryIssue(GameContentAuthoringValidationSeverity.Info, path, message);
        }

        public static GameContentLibraryIssue Warning(string path, string message)
        {
            return new GameContentLibraryIssue(GameContentAuthoringValidationSeverity.Warning, path, message);
        }

        public static GameContentLibraryIssue Error(string path, string message)
        {
            return new GameContentLibraryIssue(GameContentAuthoringValidationSeverity.Error, path, message);
        }
    }

    public sealed class GameContentLibraryGroup
    {
        public GameContentLibraryGroup(string name, IReadOnlyList<GameContentLibraryItem> items)
        {
            Name = name ?? string.Empty;
            Items = items == null ? Array.Empty<GameContentLibraryItem>() : items.ToArray();
        }

        public string Name { get; }
        public IReadOnlyList<GameContentLibraryItem> Items { get; }
    }

    public sealed class GameContentLibraryContentSetSummary
    {
        public GameContentLibraryContentSetSummary(GameContentLibraryItem item, bool ready, string message, int weaponCount, int enemyCount, int waveCount, int upgradeCount)
        {
            Item = item;
            Ready = ready;
            Message = message ?? string.Empty;
            WeaponCount = weaponCount;
            EnemyCount = enemyCount;
            WaveCount = waveCount;
            UpgradeCount = upgradeCount;
        }

        public GameContentLibraryItem Item { get; }
        public bool Ready { get; }
        public string Message { get; }
        public int WeaponCount { get; }
        public int EnemyCount { get; }
        public int WaveCount { get; }
        public int UpgradeCount { get; }
    }

    public enum GameContentLibraryKind
    {
        Attack = 0,
        Enemy = 1,
        Wave = 2,
        Weapon = 3,
        Upgrade = 4,
        ContentSet = 5
    }

    internal sealed class GameContentLibraryTypeInfo
    {
        public GameContentLibraryTypeInfo(string typeName, GameContentLibraryKind kind, string category)
        {
            TypeName = typeName;
            Kind = kind;
            Category = category;
        }

        public string TypeName { get; }
        public GameContentLibraryKind Kind { get; }
        public string Category { get; }
    }

    public static class GameContentLibraryReportWriter
    {
        public static string ToMarkdown(GameContentLibraryReport report)
        {
            if (report == null) return string.Empty;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Game Content Validation");
            builder.AppendLine();
            builder.AppendLine("- Root: " + report.RootPath);
            builder.AppendLine("- Assets: " + report.Items.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Blockers: " + report.BlockerCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Warnings: " + report.WarningCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Info: " + report.InfoCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();

            foreach (GameContentLibraryContentSetSummary summary in report.ContentSetSummaries)
                builder.AppendLine("- Content Set: " + summary.Item.DisplayName + " - " + summary.Message);

            AppendIssues(builder, "Blockers", report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error));
            AppendIssues(builder, "Warnings", report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning));
            AppendIssues(builder, "Info", report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Info));
            return builder.ToString();
        }

        public static string ToContentSetMarkdown(GameContentLibraryReport report, GameContentLibraryItem contentSet)
        {
            if (report == null || contentSet == null) return string.Empty;
            GameContentLibraryContentSetSummary summary = report.GetContentSetSummary(contentSet);
            if (summary == null) return string.Empty;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# " + contentSet.DisplayName);
            builder.AppendLine();
            builder.AppendLine("- ID: " + contentSet.Id);
            builder.AppendLine("- Ready: " + (summary.Ready ? "Yes" : "No"));
            builder.AppendLine("- Weapons: " + summary.WeaponCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Enemies: " + summary.EnemyCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Waves: " + summary.WaveCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Upgrades: " + summary.UpgradeCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            foreach (string line in BuildDependencyLines(contentSet, 3))
                builder.AppendLine("- " + line);
            return builder.ToString();
        }

        public static List<string> BuildDependencyLines(GameContentLibraryItem item, int depth)
        {
            List<string> lines = new List<string>();
            if (item == null) return lines;
            BuildDependencyLines(item, depth, 0, new HashSet<GameContentLibraryItem>(), lines);
            return lines;
        }

        private static void BuildDependencyLines(GameContentLibraryItem item, int depth, int indent, HashSet<GameContentLibraryItem> visited, List<string> lines)
        {
            if (item == null || depth < 0) return;
            string prefix = new string(' ', indent * 2);
            lines.Add(prefix + item.Category + " -> " + item.DisplayName);
            if (!visited.Add(item))
            {
                lines.Add(prefix + "  (cycle)");
                return;
            }

            for (int i = 0; i < item.DirectReferences.Count; i++)
                BuildDependencyLines(item.DirectReferences[i].Target, depth - 1, indent + 1, visited, lines);
        }

        private static void AppendIssues(StringBuilder builder, string title, IEnumerable<GameContentLibraryIssue> issues)
        {
            GameContentLibraryIssue[] issueArray = issues.ToArray();
            if (issueArray.Length == 0) return;
            builder.AppendLine();
            builder.AppendLine("## " + title);
            for (int i = 0; i < issueArray.Length; i++)
                builder.AppendLine("- " + issueArray[i].Path + ": " + issueArray[i].Message);
        }
    }
}
