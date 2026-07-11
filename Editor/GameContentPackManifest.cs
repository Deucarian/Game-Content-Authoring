using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    [CreateAssetMenu(fileName = "Game Content Pack", menuName = "Deucarian/Game Content/Content Pack Manifest")]
    public sealed class GameContentPackManifest : ScriptableObject
    {
        [SerializeField] private string packId = "content-pack";
        [SerializeField] private string owningPackageId = "com.company.package";
        [SerializeField] private string providerId = "com.company.package.content-pack";
        [SerializeField] private string displayName = "Content Pack";
        [SerializeField, TextArea] private string description = string.Empty;
        [SerializeField] private string schemaVersion = "1";
        [SerializeField] private string[] tags = Array.Empty<string>();
        [SerializeField] private SceneAsset playableScene;
        [SerializeField] private Texture2D preview;
        [SerializeField] private Texture2D icon;
        [SerializeField] private UnityEngine.Object defaultTheme;
        [SerializeField] private GameContentPackSourceReference[] contentSources = Array.Empty<GameContentPackSourceReference>();

        public string PackId => packId ?? string.Empty;
        public string OwningPackageId => owningPackageId ?? string.Empty;
        public string ProviderId => providerId ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;
        public string Description => description ?? string.Empty;
        public string SchemaVersion => schemaVersion ?? string.Empty;
        public IReadOnlyList<string> Tags => tags ?? Array.Empty<string>();
        public SceneAsset PlayableScene => playableScene;
        public Texture2D Preview => preview;
        public Texture2D Icon => icon;
        public UnityEngine.Object DefaultTheme => defaultTheme;
        public IReadOnlyList<GameContentPackSourceReference> ContentSources => contentSources ?? Array.Empty<GameContentPackSourceReference>();
        public string StableKey => GameContentPackDescriptor.BuildStableKey(OwningPackageId, PackId);

        public void Configure(
            string stablePackId,
            string packageId,
            string contentProviderId,
            string name,
            string packDescription,
            string version,
            IEnumerable<string> packTags,
            SceneAsset scene,
            UnityEngine.Object defaultThemeAsset,
            IEnumerable<GameContentPackSourceReference> sources,
            Texture2D previewTexture = null,
            Texture2D iconTexture = null)
        {
            packId = Normalize(stablePackId);
            owningPackageId = Normalize(packageId);
            providerId = Normalize(contentProviderId);
            displayName = Normalize(name);
            description = Normalize(packDescription);
            schemaVersion = Normalize(version);
            tags = packTags == null
                ? Array.Empty<string>()
                : packTags.Select(Normalize).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            playableScene = scene;
            defaultTheme = defaultThemeAsset;
            contentSources = sources == null
                ? Array.Empty<GameContentPackSourceReference>()
                : sources.Where(source => source != null).ToArray();
            preview = previewTexture;
            icon = iconTexture;
            NormalizeSerializedState();
        }

        public bool TryGetSource(string sourceId, out GameContentPackSourceReference source)
        {
            source = ContentSources.FirstOrDefault(candidate =>
                candidate != null && string.Equals(candidate.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));
            return source != null;
        }

        private void OnValidate()
        {
            NormalizeSerializedState();
        }

        private void NormalizeSerializedState()
        {
            packId = Normalize(packId);
            owningPackageId = Normalize(owningPackageId);
            providerId = Normalize(providerId);
            displayName = Normalize(displayName);
            description = Normalize(description);
            schemaVersion = Normalize(schemaVersion);
            tags = tags == null
                ? Array.Empty<string>()
                : tags.Select(Normalize).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            contentSources = contentSources == null
                ? Array.Empty<GameContentPackSourceReference>()
                : contentSources.Where(source => source != null).ToArray();
            for (int i = 0; i < contentSources.Length; i++) contentSources[i].Normalize();
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    [Serializable]
    public sealed class GameContentPackSourceReference
    {
        [SerializeField] private string sourceId = "content";
        [SerializeField] private string sourceKind = "json";
        [SerializeField] private TextAsset textAsset;
        [SerializeField] private string displayLabel = "Content";
        [SerializeField] private string categoryHint = string.Empty;
        [SerializeField] private bool required = true;

        public GameContentPackSourceReference()
        {
        }

        public GameContentPackSourceReference(
            string id,
            string kind,
            TextAsset asset,
            string label,
            string category,
            bool isRequired = true)
        {
            sourceId = id;
            sourceKind = kind;
            textAsset = asset;
            displayLabel = label;
            categoryHint = category;
            required = isRequired;
            Normalize();
        }

        public string SourceId => sourceId ?? string.Empty;
        public string SourceKind => sourceKind ?? string.Empty;
        public TextAsset TextAsset => textAsset;
        public string DisplayLabel => displayLabel ?? string.Empty;
        public string CategoryHint => categoryHint ?? string.Empty;
        public bool Required => required;

        internal void Normalize()
        {
            sourceId = NormalizeValue(sourceId);
            sourceKind = NormalizeValue(sourceKind);
            displayLabel = NormalizeValue(displayLabel);
            categoryHint = NormalizeValue(categoryHint);
        }

        private static string NormalizeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
