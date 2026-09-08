using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentExistingItemsView
    {
        private readonly Action refresh;
        private readonly Action<IGameContentAuthoringProvider, GameContentLibraryItem> select;
        private readonly Func<IGameContentAuthoringProvider, GameContentLibraryItem, bool> isSelected;

        internal GameContentExistingItemsView(Action refresh,
            Action<IGameContentAuthoringProvider, GameContentLibraryItem> select,
            Func<IGameContentAuthoringProvider, GameContentLibraryItem, bool> isSelected)
        {
            this.refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            this.select = select ?? throw new ArgumentNullException(nameof(select));
            this.isSelected = isSelected ?? throw new ArgumentNullException(nameof(isSelected));
        }

        internal void Draw(IGameContentAuthoringProvider provider, GameContentAuthoringContext context, IReadOnlyList<GameContentLibraryItem> items)
        {
            string key = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", provider.ProviderId, "existing-items");
            string summary = items.Count.ToString(CultureInfo.InvariantCulture) + " authored item(s) under Assets/GameContent.";
            context.DrawFoldoutCard(key, "Existing Authored Items", summary, () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (context.DrawSecondaryButton("Refresh Library", true, GUILayout.Width(124f), GUILayout.Height(24f)))
                        refresh();
                    GUILayout.FlexibleSpace();
                }

                if (items.Count == 0)
                {
                    DeucarianEditorStatusPanel.DrawStatusCard("No existing " + provider.DisplayName + " assets were found under Assets/GameContent.", DeucarianEditorStatus.Info);
                    return;
                }

                for (int i = 0; i < items.Count; i++)
                    DrawExistingItem(provider, context, items[i], isSelected(provider, items[i]));
            }, items.Count > 0);
        }

        private void DrawExistingItem(IGameContentAuthoringProvider provider, GameContentAuthoringContext context, GameContentLibraryItem item, bool selected)
        {
            if (item == null) return;
            string itemKey = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", provider.ProviderId, "item", item.Key);
            string summary = GameContentExistingItemPresentation.GetIdLabel(item) + " - " + item.Category + " - " + item.ValidationLabel;
            context.DrawFoldoutCard(
                itemKey,
                selected ? item.DisplayName + "  (preview)" : item.DisplayName,
                summary,
                () =>
                {
                    context.DrawInlineCard(() =>
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            DeucarianEditorStatusBadge.Draw(item.ValidationLabel, GameContentExistingItemPresentation.GetItemStatus(item), GUILayout.Width(96f));
                            GUILayout.FlexibleSpace();
                            if (context.DrawSecondaryButton(selected ? "Previewing" : "Preview", item.Asset != null, GUILayout.Width(88f), GUILayout.Height(22f)))
                                select(provider, item);
                            DeucarianEditorMiniToolbar.PingButton(item.Asset);
                            DeucarianEditorMiniToolbar.SelectButton(item.Asset);
                        }

                        DeucarianEditorFieldRow.Draw("ID", () => EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(item.Id) ? "(missing)" : item.Id, context.MutedStyle));
                        DeucarianEditorFieldRow.Draw("Type", () => EditorGUILayout.LabelField(item.Category, context.MutedStyle));
                    });

                    DrawItemIssues(context, item);
                    DrawItemReferences(context, "Direct References", item.DirectReferences);
                    DrawItemReferences(context, "Referenced By", item.ReverseReferences);
                    DrawItemAdvanced(context, item);
                },
                false,
                true,
                () =>
                {
                    DeucarianEditorMiniToolbar.PingButton(item.Asset);
                    if (DeucarianEditorMiniToolbar.SelectButton(item.Asset))
                        select(provider, item);
                });
        }

        private static void DrawItemIssues(GameContentAuthoringContext context, GameContentLibraryItem item)
        {
            if (item.Issues.Count == 0)
            {
                DeucarianEditorStatusBadge.Draw("Ready", DeucarianEditorStatus.Success, GUILayout.Width(72f));
                return;
            }

            List<string> messages = new List<string>();
            for (int i = 0; i < item.Issues.Count; i++)
                messages.Add(item.Issues[i].Path + ": " + item.Issues[i].Message);
            DeucarianEditorStatus status = item.ErrorCount > 0 ? DeucarianEditorStatus.Error : DeucarianEditorStatus.Warning;
            DeucarianEditorStatusPanel.DrawValidationCard(item.ValidationLabel, messages, status);
        }

        private static void DrawItemReferences(GameContentAuthoringContext context, string title, IReadOnlyList<GameContentLibraryReference> references)
        {
            context.DrawInlineCard(() =>
            {
                DeucarianEditorSectionHeader.Draw(title);
                if (references == null || references.Count == 0)
                {
                    EditorGUILayout.LabelField("None found.", context.MutedStyle);
                    return;
                }

                for (int i = 0; i < references.Count; i++)
                {
                    GameContentLibraryReference reference = references[i];
                    if (reference == null || reference.Target == null) continue;
                    EditorGUILayout.LabelField(reference.Target.DisplayName + " (" + reference.Target.Category + ")", context.MutedStyle);
                }
            });
        }

        private static void DrawItemAdvanced(GameContentAuthoringContext context, GameContentLibraryItem item)
        {
            string key = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", "advanced", item.Key);
            context.DrawFoldoutCard(key, "Advanced", "Raw path and serialized reference details.", () =>
            {
                context.DrawInlineCard(() =>
                {
                    DeucarianEditorFieldRow.Draw("Path", () => EditorGUILayout.LabelField(item.Path, context.MutedStyle));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (context.DrawSecondaryButton("Copy Path", !string.IsNullOrWhiteSpace(item.Path), GUILayout.Width(84f), GUILayout.Height(22f)))
                            EditorGUIUtility.systemCopyBuffer = item.Path;
                        if (context.DrawSecondaryButton("Open Folder", AssetDatabase.IsValidFolder(item.Folder), GUILayout.Width(96f), GUILayout.Height(22f)))
                        {
                            UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.Folder);
                            if (folder != null)
                            {
                                Selection.activeObject = folder;
                                EditorGUIUtility.PingObject(folder);
                            }
                        }
                    }
                });

                DrawRawReferences(context, "Direct Property Uses", item.DirectReferences);
                DrawRawReferences(context, "Referenced By Properties", item.ReverseReferences);
            }, false);
        }

        private static void DrawRawReferences(GameContentAuthoringContext context, string title, IReadOnlyList<GameContentLibraryReference> references)
        {
            context.DrawInlineCard(() =>
            {
                DeucarianEditorSectionHeader.Draw(title);
                if (references == null || references.Count == 0)
                {
                    EditorGUILayout.LabelField("None", context.MutedStyle);
                    return;
                }

                for (int i = 0; i < references.Count; i++)
                {
                    GameContentLibraryReference reference = references[i];
                    if (reference == null || reference.Target == null) continue;
                    EditorGUILayout.LabelField(reference.Target.DisplayName + " - " + reference.PropertyPath, context.MutedStyle);
                }
            });
        }
    }
}
