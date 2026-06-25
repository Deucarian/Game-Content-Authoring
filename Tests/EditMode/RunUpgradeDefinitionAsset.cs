using UnityEngine;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class RunUpgradeDefinitionAsset : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public ScriptableObject Target;
    }
}
