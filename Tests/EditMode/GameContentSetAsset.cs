using UnityEngine;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentSetAsset : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public WeaponDefinitionAsset StartingWeapon;
        public WeaponDefinitionAsset[] AvailableWeapons;
        public EnemyDefinitionAsset[] EnemyPool;
        public WaveDefinitionAsset[] WaveSet;
        public RunUpgradeDefinitionAsset[] UpgradePool;
    }
}
