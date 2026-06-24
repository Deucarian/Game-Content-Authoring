using System;
using UnityEditor;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentAuthoringEditorPaths
    {
        public static string NormalizeAssetFolderPath(string path, string defaultRoot)
        {
            string normalized = string.IsNullOrWhiteSpace(path)
                ? defaultRoot
                : path.Trim().Replace("\\", "/");
            while (normalized.Contains("//")) normalized = normalized.Replace("//", "/");
            return normalized.TrimEnd('/');
        }

        public static bool IsValidAssetFolderPath(string path, string defaultRoot)
        {
            string normalized = NormalizeAssetFolderPath(path, defaultRoot);
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            if (!string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase) && !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] parts = normalized.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrWhiteSpace(part) || part == "." || part == ".." || HasInvalidAssetPathChars(part))
                    return false;
            }

            return true;
        }

        public static string EnsureFolder(string folder, string defaultRoot)
        {
            folder = NormalizeAssetFolderPath(folder, defaultRoot);
            if (string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase)) return "Assets";
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }

            return folder;
        }

        public static bool FolderContainsAssets(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return false;
            return AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length > 0;
        }

        public static string SanitizePathSegment(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            char[] chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool valid = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
                chars[i] = valid ? c : '-';
            }

            return new string(chars);
        }

        private static bool HasInvalidAssetPathChars(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '<' || c == '>' || c == ':' || c == '"' || c == '|' || c == '?' || c == '*')
                    return true;
            }

            return false;
        }
    }
}
