using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentSourceIdentity : IEquatable<GameContentSourceIdentity>
    {
        public const string UnityAssetGuidKind = "unity-asset-guid";

        public GameContentSourceIdentity(string kind, string value)
        {
            Kind = Normalize(kind).ToLowerInvariant();
            Value = Normalize(value).ToLowerInvariant();
        }

        public string Kind { get; }
        public string Value { get; }
        public string StableKey => Kind + "::" + Value;
        public bool IsValid => !string.IsNullOrWhiteSpace(Kind) && !string.IsNullOrWhiteSpace(Value);

        public bool Equals(GameContentSourceIdentity other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(StableKey, other.StableKey, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentSourceIdentity);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(StableKey);
        }

        public override string ToString()
        {
            return StableKey;
        }

        public static bool TryCreate(UnityEngine.Object sourceAsset, string sourcePath, out GameContentSourceIdentity identity)
        {
            string path = sourceAsset == null ? string.Empty : AssetDatabase.GetAssetPath(sourceAsset);
            if (string.IsNullOrWhiteSpace(path)) path = Normalize(sourcePath).Replace("\\", "/");
            string guid = string.IsNullOrWhiteSpace(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrWhiteSpace(guid))
            {
                identity = null;
                return false;
            }

            identity = new GameContentSourceIdentity(UnityAssetGuidKind, guid);
            return true;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class GameContentSourceClaim
    {
        public GameContentSourceClaim(GameContentSourceIdentity sourceIdentity, string sourcePath = null)
        {
            SourceIdentity = sourceIdentity;
            SourcePath = string.IsNullOrWhiteSpace(sourcePath)
                ? string.Empty
                : sourcePath.Trim().Replace("\\", "/");
        }

        public GameContentSourceIdentity SourceIdentity { get; }
        public string SourcePath { get; }
        public bool IsValid => SourceIdentity != null && SourceIdentity.IsValid;

        public static GameContentSourceClaim ForAsset(UnityEngine.Object sourceAsset)
        {
            string path = sourceAsset == null ? string.Empty : AssetDatabase.GetAssetPath(sourceAsset);
            return GameContentSourceIdentity.TryCreate(sourceAsset, path, out GameContentSourceIdentity identity)
                ? new GameContentSourceClaim(identity, path)
                : null;
        }
    }

    public interface IGameContentSourceClaimProvider
    {
        IReadOnlyList<GameContentSourceClaim> GetSourceClaims(string packId);
    }

    public sealed class GameContentSourceClaimConflict
    {
        internal GameContentSourceClaimConflict(
            GameContentSourceIdentity sourceIdentity,
            string sourcePath,
            IEnumerable<string> claimantPackKeys)
        {
            SourceIdentity = sourceIdentity;
            SourcePath = sourcePath ?? string.Empty;
            ClaimantPackKeys = claimantPackKeys == null
                ? Array.Empty<string>()
                : claimantPackKeys
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        public GameContentSourceIdentity SourceIdentity { get; }
        public string SourcePath { get; }
        public IReadOnlyList<string> ClaimantPackKeys { get; }
    }
}
