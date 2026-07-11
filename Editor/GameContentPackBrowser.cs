using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentPackBrowserState
    {
        private readonly Dictionary<string, IReadOnlyList<GameContentRecordDescriptor>> _recordsByPack =
            new Dictionary<string, IReadOnlyList<GameContentRecordDescriptor>>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<GameContentPackDescriptor> Packs { get; private set; } = Array.Empty<GameContentPackDescriptor>();
        public string SelectedPackKey { get; private set; } = string.Empty;
        public string SelectedCategoryId { get; private set; } = string.Empty;
        public string SelectedRecordId { get; private set; } = string.Empty;
        public string SearchText { get; set; } = string.Empty;
        public GameContentRecordValidationFilter ValidationFilter { get; set; }
        public GameContentRecordSortMode SortMode { get; set; }
        public Vector2 NavigationScroll { get; set; }
        public Vector2 RecordScroll { get; set; }
        public Vector2 DetailScroll { get; set; }
        public GameContentActionResult LastActionResult { get; set; }
        public string StatusMessage { get; private set; } = "Content packs ready";

        public void Refresh(IGameContentPackProvider provider)
        {
            _recordsByPack.Clear();
            LastActionResult = null;
            if (provider == null)
            {
                Packs = Array.Empty<GameContentPackDescriptor>();
                StatusMessage = "Content-pack provider is unavailable.";
                EnsureSelection();
                return;
            }

            try
            {
                Packs = GameContentPackBrowserModel.SortPacks(provider.GetContentPacks());
                for (int i = 0; i < Packs.Count; i++)
                {
                    GameContentPackDescriptor pack = Packs[i];
                    IReadOnlyList<GameContentRecordDescriptor> records = provider.GetRecords(pack.PackId);
                    _recordsByPack[GetSelectionKey(pack)] = records == null
                        ? Array.Empty<GameContentRecordDescriptor>()
                        : records.Where(record => record != null).ToArray();
                }

                StatusMessage = Packs.Count.ToString(CultureInfo.InvariantCulture) + " content pack(s) discovered.";
            }
            catch (Exception exception)
            {
                Packs = Array.Empty<GameContentPackDescriptor>();
                StatusMessage = "Content-pack refresh failed: " + exception.GetBaseException().Message;
            }

            EnsureSelection();
        }

        public GameContentPackDescriptor GetSelectedPack()
        {
            return Packs.FirstOrDefault(pack => string.Equals(GetSelectionKey(pack), SelectedPackKey, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<GameContentRecordDescriptor> GetRecords(GameContentPackDescriptor pack)
        {
            if (pack == null || !_recordsByPack.TryGetValue(GetSelectionKey(pack), out IReadOnlyList<GameContentRecordDescriptor> records))
                return Array.Empty<GameContentRecordDescriptor>();
            return records;
        }

        public GameContentRecordDescriptor GetSelectedRecord()
        {
            GameContentPackDescriptor pack = GetSelectedPack();
            return GetRecords(pack).FirstOrDefault(record =>
                string.Equals(record.PackScopedId, SelectedRecordId, StringComparison.OrdinalIgnoreCase));
        }

        public void SelectPack(GameContentPackDescriptor pack)
        {
            SelectedPackKey = GetSelectionKey(pack);
            SelectedCategoryId = string.Empty;
            SelectedRecordId = string.Empty;
            SearchText = string.Empty;
            RecordScroll = Vector2.zero;
            DetailScroll = Vector2.zero;
        }

        public void SelectCategory(string categoryId)
        {
            SelectedCategoryId = categoryId ?? string.Empty;
            SelectedRecordId = string.Empty;
            RecordScroll = Vector2.zero;
            DetailScroll = Vector2.zero;
        }

        public void SelectRecord(GameContentRecordDescriptor record)
        {
            SelectedRecordId = record == null ? string.Empty : record.PackScopedId;
            DetailScroll = Vector2.zero;
        }

        private void EnsureSelection()
        {
            if (Packs.Count == 0)
            {
                SelectedPackKey = string.Empty;
                SelectedRecordId = string.Empty;
                return;
            }

            if (GetSelectedPack() == null) SelectPack(Packs[0]);
            if (GetSelectedRecord() == null) SelectedRecordId = string.Empty;
        }

        private static string GetSelectionKey(GameContentPackDescriptor pack)
        {
            return pack == null ? string.Empty : pack.StableKey + "::" + pack.SourcePath;
        }
    }

    public static class GameContentPackBrowserModel
    {
        public static IReadOnlyList<GameContentPackDescriptor> SortPacks(IEnumerable<GameContentPackDescriptor> packs)
        {
            return packs == null
                ? Array.Empty<GameContentPackDescriptor>()
                : packs.Where(pack => pack != null)
                    .OrderBy(pack => pack.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(pack => pack.StableKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(pack => pack.SourcePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        public static IReadOnlyList<GameContentRecordDescriptor> FilterRecords(
            IEnumerable<GameContentRecordDescriptor> records,
            string searchText,
            string categoryId,
            GameContentRecordValidationFilter validationFilter,
            GameContentRecordSortMode sortMode)
        {
            IEnumerable<GameContentRecordDescriptor> query = records == null
                ? Enumerable.Empty<GameContentRecordDescriptor>()
                : records.Where(record => record != null);
            string search = string.IsNullOrWhiteSpace(searchText) ? string.Empty : searchText.Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(record =>
                    Contains(record.DisplayName, search) ||
                    Contains(record.SourceRecordId, search) ||
                    Contains(record.Summary, search) ||
                    Contains(record.Description, search) ||
                    Contains(record.SourcePath, search));
            }

            if (!string.IsNullOrWhiteSpace(categoryId)) query = query.Where(record => record.IsInCategory(categoryId));
            query = query.Where(record => MatchesValidation(record, validationFilter));

            switch (sortMode)
            {
                case GameContentRecordSortMode.DisplayName:
                    query = query.OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(record => record.PackScopedId, StringComparer.OrdinalIgnoreCase);
                    break;
                case GameContentRecordSortMode.Category:
                    query = query.OrderBy(record => record.CategoryId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(record => record.PackScopedId, StringComparer.OrdinalIgnoreCase);
                    break;
                default:
                    query = query.OrderBy(record => record.Order)
                        .ThenBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(record => record.PackScopedId, StringComparer.OrdinalIgnoreCase);
                    break;
            }

            return query.ToArray();
        }

        public static GameContentRecordDescriptor ResolveReferenceTarget(
            IEnumerable<GameContentRecordDescriptor> records,
            GameContentRecordReferenceDescriptor reference)
        {
            if (records == null || reference == null || string.IsNullOrWhiteSpace(reference.TargetRecordId)) return null;
            return records.FirstOrDefault(record =>
                string.Equals(record.PackScopedId, reference.TargetRecordId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.SourceRecordId, reference.TargetRecordId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesValidation(GameContentRecordDescriptor record, GameContentRecordValidationFilter filter)
        {
            switch (filter)
            {
                case GameContentRecordValidationFilter.Ready:
                    return record.Validation.ErrorCount == 0 && record.Validation.WarningCount == 0 && !record.HasBrokenReferences;
                case GameContentRecordValidationFilter.Warnings:
                    return record.Validation.WarningCount > 0 && record.Validation.ErrorCount == 0;
                case GameContentRecordValidationFilter.Errors:
                    return record.Validation.ErrorCount > 0;
                case GameContentRecordValidationFilter.BrokenReferences:
                    return record.HasBrokenReferences;
                default:
                    return true;
            }
        }

        private static bool Contains(string value, string search)
        {
            return !string.IsNullOrWhiteSpace(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public static class GameContentPackBrowser
    {
        private static readonly string[] ValidationLabels = { "All", "Ready", "Warnings", "Errors", "Broken Refs" };
        private static readonly string[] SortLabels = { "Source Order", "Name", "Category" };

        public static void Draw(
            GameContentAuthoringSurfaceContext context,
            IGameContentPackProvider provider,
            GameContentPackBrowserState state)
        {
            if (context == null || state == null) return;
            if (state.Packs.Count == 0) state.Refresh(provider);
            GameContentPackDescriptor selectedPack = state.GetSelectedPack();
            context.Authoring.SetValidation(selectedPack == null ? GameContentAuthoringValidationResult.Valid : selectedPack.Validation);

            GameContentAuthoringWorkbench.Draw(
                context,
                () => DrawNavigation(context, provider, state),
                () => DrawRecords(context, state),
                () => DrawDetail(context, provider, state));
        }

        private static void DrawNavigation(
            GameContentAuthoringSurfaceContext context,
            IGameContentPackProvider provider,
            GameContentPackBrowserState state)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Content Packs", DeucarianEditorStyles.SectionTitle);
                GUILayout.FlexibleSpace();
                if (DeucarianEditorMiniToolbar.Button("Refresh", true, GUILayout.Width(62f), GUILayout.Height(22f)))
                {
                    state.Refresh(provider);
                    context.RequestRepaint();
                }
            }

            EditorGUILayout.LabelField(state.StatusMessage, DeucarianEditorStyles.MutedLabel);
            state.NavigationScroll = EditorGUILayout.BeginScrollView(state.NavigationScroll);
            for (int i = 0; i < state.Packs.Count; i++)
            {
                GameContentPackDescriptor pack = state.Packs[i];
                bool selected = ReferenceEquals(pack, state.GetSelectedPack());
                string label = pack.DisplayName + "\n" + pack.OwningPackageId + "\n" +
                               PackStatusLabel(pack) + " - " + pack.RecordCount.ToString(CultureInfo.InvariantCulture) + " records";
                if (GUILayout.Toggle(selected, new GUIContent(label, pack.Description), "Button", GUILayout.MinHeight(58f)) && !selected)
                {
                    state.SelectPack(pack);
                    context.RequestRepaint();
                }
            }

            GameContentPackDescriptor selectedPack = state.GetSelectedPack();
            if (selectedPack != null)
            {
                GUILayout.Space(DeucarianEditorSpacing.Small);
                EditorGUILayout.LabelField("Categories", DeucarianEditorStyles.SectionTitle);
                DrawCategoryButton(context, state, string.Empty, "All Records", selectedPack.RecordCount);
                foreach (GameContentCategoryDescriptor category in selectedPack.Categories
                             .OrderBy(value => value.Order)
                             .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase))
                    DrawCategoryButton(context, state, category.CategoryId, category.DisplayName, category.RecordCount);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawCategoryButton(
            GameContentAuthoringSurfaceContext context,
            GameContentPackBrowserState state,
            string categoryId,
            string displayName,
            int count)
        {
            bool selected = string.Equals(state.SelectedCategoryId, categoryId, StringComparison.OrdinalIgnoreCase);
            string label = displayName + "  " + count.ToString(CultureInfo.InvariantCulture);
            if (GUILayout.Toggle(selected, label, "Button", GUILayout.Height(25f)) && !selected)
            {
                state.SelectCategory(categoryId);
                context.RequestRepaint();
            }
        }

        private static void DrawRecords(GameContentAuthoringSurfaceContext context, GameContentPackBrowserState state)
        {
            GameContentPackDescriptor pack = state.GetSelectedPack();
            if (pack == null)
            {
                DrawEmpty("No content packs are available. Import the provider's sample or resolve manifest conflicts.");
                return;
            }

            EditorGUILayout.LabelField(pack.DisplayName, DeucarianEditorStyles.SectionTitle);
            state.SearchText = DeucarianEditorSearchField.Draw(state.SearchText, "Search records", GUILayout.ExpandWidth(true));
            using (new EditorGUILayout.HorizontalScope())
            {
                state.ValidationFilter = (GameContentRecordValidationFilter)EditorGUILayout.Popup(
                    (int)state.ValidationFilter,
                    ValidationLabels,
                    GUILayout.MinWidth(96f));
                state.SortMode = (GameContentRecordSortMode)EditorGUILayout.Popup(
                    (int)state.SortMode,
                    SortLabels,
                    GUILayout.MinWidth(104f));
            }

            IReadOnlyList<GameContentRecordDescriptor> records = GameContentPackBrowserModel.FilterRecords(
                state.GetRecords(pack),
                state.SearchText,
                state.SelectedCategoryId,
                state.ValidationFilter,
                state.SortMode);
            EditorGUILayout.LabelField(records.Count.ToString(CultureInfo.InvariantCulture) + " record(s)", DeucarianEditorStyles.MutedLabel);
            state.RecordScroll = EditorGUILayout.BeginScrollView(state.RecordScroll);
            for (int i = 0; i < records.Count; i++)
            {
                GameContentRecordDescriptor record = records[i];
                bool selected = string.Equals(record.PackScopedId, state.SelectedRecordId, StringComparison.OrdinalIgnoreCase);
                string status = RecordStatusLabel(record);
                string label = record.DisplayName + "\n" + record.SourceRecordId + " - " + status;
                if (GUILayout.Toggle(selected, new GUIContent(label, record.Summary), "Button", GUILayout.MinHeight(42f)) && !selected)
                {
                    state.SelectRecord(record);
                    context.RequestRepaint();
                }
            }

            if (records.Count == 0) DrawEmpty("No records match the current search and filters.");
            EditorGUILayout.EndScrollView();
        }

        private static void DrawDetail(
            GameContentAuthoringSurfaceContext context,
            IGameContentPackProvider provider,
            GameContentPackBrowserState state)
        {
            GameContentPackDescriptor pack = state.GetSelectedPack();
            state.DetailScroll = EditorGUILayout.BeginScrollView(state.DetailScroll);
            if (pack == null)
            {
                DrawEmpty("Select an available content pack.");
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.LabelField(pack.DisplayName, DeucarianEditorStyles.SectionTitle);
            EditorGUILayout.LabelField(pack.Description, EditorStyles.wordWrappedLabel);
            DrawRow("Package", pack.OwningPackageId);
            DrawRow("Pack ID", pack.PackId);
            DrawRow("Schema", pack.SchemaVersion);
            DrawRow("Tags", pack.Tags.Count == 0 ? "None" : string.Join(", ", pack.Tags));
            DrawRow("Source", pack.SourcePath);
            DrawRow("State", SourceStateLabel(pack.SourceState));
            DrawPackActions(context, provider, state, pack);

            GameContentRecordDescriptor record = state.GetSelectedRecord();
            if (record == null)
            {
                DrawValidation(pack.Validation);
                if (pack.PlayableScene != null)
                    EditorGUILayout.ObjectField("Playable Scene", pack.PlayableScene, typeof(SceneAsset), false);
                EditorGUILayout.EndScrollView();
                return;
            }

            GUILayout.Space(DeucarianEditorSpacing.Small);
            EditorGUILayout.LabelField(record.DisplayName, DeucarianEditorStyles.SectionTitle);
            if (!string.IsNullOrWhiteSpace(record.Description)) EditorGUILayout.LabelField(record.Description, EditorStyles.wordWrappedLabel);
            DrawRow("Record ID", record.SourceRecordId);
            DrawRow("Category", record.CategoryId);
            DrawRow("Source", record.SourcePath);
            DrawRow("Locator", record.SourceLocator);
            for (int i = 0; i < record.PlayerFacingMetadata.Count; i++)
                DrawRow(record.PlayerFacingMetadata[i].Label, record.PlayerFacingMetadata[i].Value);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool canReveal = record.SourceAsset != null && !string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(record.SourceAsset));
                using (new EditorGUI.DisabledScope(!canReveal))
                {
                    if (GUILayout.Button(new GUIContent("Reveal Source", "Reveal the authored source file in the operating system."), GUILayout.Height(24f)))
                        Reveal(record.SourceAsset);
                }
            }

            DrawReferences(context, state, "References", record.OutboundReferences);
            DrawReferences(context, state, "Referenced By", record.InboundReferences);
            DrawValidation(record.Validation);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawPackActions(
            GameContentAuthoringSurfaceContext context,
            IGameContentPackProvider provider,
            GameContentPackBrowserState state,
            GameContentPackDescriptor pack)
        {
            if (pack.Actions.Count == 0) return;
            GUILayout.Space(DeucarianEditorSpacing.Small);
            EditorGUILayout.LabelField("Actions", DeucarianEditorStyles.SectionTitle);
            for (int i = 0; i < pack.Actions.Count; i++)
            {
                GameContentActionDescriptor action = pack.Actions[i];
                string tooltip = action.Enabled ? action.Description : action.DisabledReason;
                using (new EditorGUI.DisabledScope(!action.Enabled))
                {
                    if (GUILayout.Button(new GUIContent(action.DisplayName, tooltip), GUILayout.Height(25f)))
                    {
                        GameContentActionResult result = GameContentPackActionDispatcher.Execute(provider, pack, action);
                        if (action.ActionKind == GameContentActionKind.Validate)
                        {
                            state.Refresh(provider);
                            state.LastActionResult = result;
                        }
                        else
                        {
                            state.LastActionResult = result;
                        }
                        context.RequestRepaint();
                    }
                }
            }

            if (state.LastActionResult != null)
            {
                EditorGUILayout.HelpBox(
                    state.LastActionResult.Message,
                    state.LastActionResult.Succeeded ? MessageType.Info : MessageType.Error);
            }
        }

        private static void DrawReferences(
            GameContentAuthoringSurfaceContext context,
            GameContentPackBrowserState state,
            string title,
            IReadOnlyList<GameContentRecordReferenceDescriptor> references)
        {
            EditorGUILayout.LabelField(title, DeucarianEditorStyles.SectionTitle);
            if (references == null || references.Count == 0)
            {
                EditorGUILayout.LabelField("None", DeucarianEditorStyles.MutedLabel);
                return;
            }

            IReadOnlyList<GameContentRecordDescriptor> records = state.GetRecords(state.GetSelectedPack());
            for (int i = 0; i < references.Count; i++)
            {
                GameContentRecordReferenceDescriptor reference = references[i];
                GameContentRecordDescriptor target = GameContentPackBrowserModel.ResolveReferenceTarget(records, reference);
                string label = (reference.Valid ? string.Empty : "Broken: ") +
                               reference.RelationshipLabel + " -> " + reference.TargetRecordId;
                using (new EditorGUI.DisabledScope(target == null))
                {
                    if (GUILayout.Button(label, GUILayout.Height(22f)) && target != null)
                    {
                        state.SelectRecord(target);
                        context.RequestRepaint();
                    }
                }
            }
        }

        private static void DrawValidation(GameContentAuthoringValidationResult validation)
        {
            validation = validation ?? GameContentAuthoringValidationResult.Valid;
            EditorGUILayout.LabelField("Validation", DeucarianEditorStyles.SectionTitle);
            if (validation.Issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No validation issues.", MessageType.Info);
                return;
            }

            for (int i = 0; i < validation.Issues.Count; i++)
            {
                GameContentAuthoringValidationIssue issue = validation.Issues[i];
                MessageType type = issue.Severity == GameContentAuthoringValidationSeverity.Error
                    ? MessageType.Error
                    : issue.Severity == GameContentAuthoringValidationSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Path + ": " + issue.Message, type);
            }
        }

        private static void DrawRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, DeucarianEditorStyles.MutedLabel, GUILayout.Width(86f));
                EditorGUILayout.SelectableLabel(value ?? string.Empty, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(18f));
            }
        }

        private static void DrawEmpty(string message)
        {
            EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        private static void Reveal(UnityEngine.Object asset)
        {
            if (asset == null) return;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrWhiteSpace(path)) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            EditorUtility.RevealInFinder(path);
        }

        private static string SourceStateLabel(GameContentPackSourceState state)
        {
            switch (state)
            {
                case GameContentPackSourceState.Available: return "Available";
                case GameContentPackSourceState.MissingSource: return "Missing source";
                case GameContentPackSourceState.InvalidManifest: return "Invalid manifest";
                case GameContentPackSourceState.DuplicateConflict: return "Duplicate conflict";
                case GameContentPackSourceState.ProviderUnavailable: return "Provider unavailable";
                case GameContentPackSourceState.ValidationFailed: return "Validation failed";
                case GameContentPackSourceState.SampleNotImported: return "Sample not imported";
                default: return "Unknown";
            }
        }

        private static string RecordStatusLabel(GameContentRecordDescriptor record)
        {
            if (record == null) return "Unknown";
            if (record.Validation.ErrorCount > 0) return record.Validation.ErrorCount.ToString(CultureInfo.InvariantCulture) + " error(s)";
            if (record.HasBrokenReferences) return "Broken reference";
            if (record.Validation.WarningCount > 0) return record.Validation.WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s)";
            return "Ready";
        }

        private static string PackStatusLabel(GameContentPackDescriptor pack)
        {
            if (pack == null) return "Unknown";
            string source = SourceStateLabel(pack.SourceState);
            if (pack.Validation.ErrorCount > 0)
                return source + ", " + pack.Validation.ErrorCount.ToString(CultureInfo.InvariantCulture) + " error(s)";
            if (pack.Validation.WarningCount > 0)
                return source + ", " + pack.Validation.WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s)";
            return source + ", validated";
        }
    }
}
