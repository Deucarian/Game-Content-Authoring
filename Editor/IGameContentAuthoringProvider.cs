namespace Deucarian.GameContentAuthoring.Editor
{
    public interface IGameContentAuthoringProvider
    {
        string ProviderId { get; }
        string DisplayName { get; }
        string Description { get; }
        int SortOrder { get; }
        bool Enabled { get; }
        void OnSelected();
        void Draw(GameContentAuthoringContext context);
    }
}
