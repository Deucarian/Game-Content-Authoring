using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;

namespace Deucarian.GameContentAuthoring.Editor
{
    [InitializeOnLoad]
    internal static class GameContentAuthoringControlCenter
    {
        private const string PackageId = "com.deucarian.game-content-authoring";

        static GameContentAuthoringControlCenter()
        {
            DeucarianToolRegistry.Register(new DeucarianToolDescriptor(
                DeucarianToolIds.GameContentAuthoring,
                "Game Content Authoring",
                "Create and edit package-owned game content.",
                DeucarianControlCenterArea.Authoring,
                GameContentAuthoringWindow.Open,
                PackageId,
                searchTerms: new[] { "content", "authoring", "assets" },
                order: 100));
            DeucarianControlCenterRegistry.RegisterCardProvider(new Provider());
        }

        private sealed class Provider : IDeucarianControlCenterCardProvider
        {
            public string Id => PackageId + ".control-center";

            public IEnumerable<DeucarianControlCenterCard> Capture(
                DeucarianControlCenterContext context)
            {
                int count = GameContentAuthoringProviderRegistry.VisibleProviders.Count;
                yield return new DeucarianControlCenterCard(
                    PackageId + ".authoring",
                    DeucarianControlCenterArea.Authoring,
                    "Game Content Authoring",
                    "Open the shared authoring surface contributed to by installed domain packages.",
                    PackageId,
                    count > 0
                        ? DeucarianControlCenterStatus.Success
                        : DeucarianControlCenterStatus.Info,
                    count > 0
                        ? count + " authoring provider(s) available"
                        : "No authoring providers are currently available",
                    order: 100,
                    details: new[]
                    {
                        "Provider count is local project metadata; content payloads are not exposed."
                    },
                    actions: new[]
                    {
                        new DeucarianControlCenterAction(
                            "open",
                            "Open Authoring",
                            GameContentAuthoringWindow.Open)
                    },
                    searchTerms: new[] { "content", "assets", "providers" });
            }
        }
    }
}
