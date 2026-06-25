using UnityEngine;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class WeaponDefinitionAsset : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public AttackDefinitionAsset Attack;
    }
}
