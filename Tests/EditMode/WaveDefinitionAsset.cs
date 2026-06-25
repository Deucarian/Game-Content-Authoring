using UnityEngine;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class WaveDefinitionAsset : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public EnemyDefinitionAsset Enemy;
    }
}
