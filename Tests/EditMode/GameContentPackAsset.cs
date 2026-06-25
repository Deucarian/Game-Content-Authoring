using UnityEngine;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentPackAsset : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public GameContentSetAsset[] ContentSets;
        public GameContentSetAsset DefaultContentSet;
    }
}
