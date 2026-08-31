using System.Linq;
using Deucarian.Editor;
using NUnit.Framework;

namespace Deucarian.GameContentAuthoring.Tests
{
    public sealed class GameContentAuthoringControlCenterTests
    {
        [Test]
        public void ContributionRegistersStableAuthoringToolAndCard()
        {
            DeucarianControlCenterSnapshot snapshot =
                DeucarianControlCenterSnapshotBuilder.Capture();
            DeucarianToolDescriptor tool = snapshot.Tools.Single(candidate =>
                candidate.Id == DeucarianToolIds.GameContentAuthoring);
            DeucarianControlCenterCard card = snapshot.Cards.Single(candidate =>
                candidate.Id == "com.deucarian.game-content-authoring.authoring");

            Assert.That(tool.Area, Is.EqualTo(DeucarianControlCenterArea.Authoring));
            Assert.That(card.Area, Is.EqualTo(DeucarianControlCenterArea.Authoring));
            CollectionAssert.AreEqual(
                new[] { "open" },
                card.Actions.Select(action => action.Id).ToArray());
        }
    }
}