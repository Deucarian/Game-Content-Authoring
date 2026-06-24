using System;
using System.Collections.Generic;
using Deucarian.Common;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentAuthoringEditorAssets
    {
        public static bool ConfirmExistingFolder(string folder, string contentName)
        {
            return EditorUtility.DisplayDialog(
                "Use Existing " + contentName + " Folder?",
                "The folder already contains assets:\n\n" + folder + "\n\nCreate this " + contentName.ToLowerInvariant() + " root asset in that folder?\n\nNo existing assets will be overwritten.",
                "Create Here",
                "Cancel");
        }

        public static bool HasDuplicateId<TAsset>(string id, Func<TAsset, string> getId) where TAsset : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(id) || getId == null) return false;
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(TAsset).Name);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TAsset asset = AssetDatabase.LoadAssetAtPath<TAsset>(path);
                if (asset != null && string.Equals(getId(asset), id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static void AddSubAsset(ScriptableObject subAsset, UnityEngine.Object root, string name)
        {
            if (subAsset == null) return;
            subAsset.name = name;
            string path = AssetDatabase.GetAssetPath(root);
            if (string.IsNullOrWhiteSpace(path))
                AssetDatabase.AddObjectToAsset(subAsset, root);
            else
                AssetDatabase.AddObjectToAsset(subAsset, path);
            EditorUtility.SetDirty(subAsset);
        }

        public static string[] SplitCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();
            string[] parts = csv.Split(',');
            var values = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string value = parts[i].Trim();
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
            }

            return values.ToArray();
        }

        public static void DestroyTransientObject(UnityEngine.Object target)
        {
            UnityObjectUtility.DestroySafely(target);
        }

        public static void AddPathIssues(
            List<GameContentAuthoringValidationIssue> issues,
            string outputRoot,
            string defaultRoot,
            string folder,
            string rootPath,
            string contentName,
            string pathLabel)
        {
            if (issues == null) return;
            if (!GameContentAuthoringEditorPaths.IsValidAssetFolderPath(outputRoot, defaultRoot))
            {
                issues.Add(GameContentAuthoringValidationIssue.Error(pathLabel, "Output root must be Assets or a folder below Assets, without empty or parent-directory segments."));
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(rootPath) != null)
                issues.Add(GameContentAuthoringValidationIssue.Error(pathLabel, "An asset already exists at " + rootPath + ". Rename the " + contentName.ToLowerInvariant() + " or edit the existing asset."));
            else if (AssetDatabase.IsValidFolder(folder) && GameContentAuthoringEditorPaths.FolderContainsAssets(folder))
                issues.Add(GameContentAuthoringValidationIssue.Warning(pathLabel, "The " + contentName.ToLowerInvariant() + " folder already contains assets. Creation will ask for confirmation before adding this root asset."));
        }
    }
}
