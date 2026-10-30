using System;
using System.Drawing;
using System.IO;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GTAVMods.Tests.Support.AgentScene;

namespace GTAVMods.Tests.PlayerIntegration.AgentOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class TurnBasedPlayTests
    {
        [TestMethod]
        public void AgentMovesForHalfASecondPausesAndInspectsObservedWorldWithoutReplayingInput()
        {
            using (var s = new AgentScene())
            {
                Assert.AreEqual(true, s.Batch(Step("resume"), Step("controls", ("ms", 500), ("inputs", new[] {
                    Step("", ("control", "MoveUpDown"), ("value", -0.75)), Step("", ("control", (int)Control.Sprint), ("group", 0))
                })), Step("pause"), Step("observe", ("limit", 1)), Step("capture"))["ok"]);
                s.Frame(); s.Frame();
                s.Frame(499, 499);
                Assert.AreEqual(0, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_GAME_PAUSED && (bool)c.Arguments[0]));
                var moves = s.Game.Native.Calls.Where(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME).ToArray();
                Assert.HasCount(4, moves);
                CollectionAssert.AreEqual(new object[] { 0, (int)Control.MoveUpDown, -.75f }, moves[0].Arguments);
                CollectionAssert.AreEqual(new object[] { 0, (int)Control.Sprint, 1f }, moves[1].Arguments);
                // Engine observation, not fake physics: the engine now reports a different player position.
                s.Game.Entity(10).Position = new Vector3(11, 12, 13);
                s.Frame(1, 1); s.Frame(); s.Frame(); s.Frame();
                StringAssert.Contains(s.Status, "\"state\":\"completed\"");
                StringAssert.Contains(s.Status, "\"paused\":true");
                string observation = Protocol.Encode(s.Results[3]);
                StringAssert.Contains(observation, "\"x\":11");
                StringAssert.Contains(observation, "\"nearbyTotal\":2");
                StringAssert.Contains(observation, "\"wantedLevel\":2");
                StringAssert.Contains(observation, "\"missionActive\":true");
                Assert.IsFalse(observation.Contains("\"handle\":20"), "Limit selects the nearer observed pedestrian.");
                using (var image = Image.FromFile(Directory.GetFiles(s.Root, "frame-*.png").Single()))
                { Assert.AreEqual(48, image.Width); Assert.AreEqual(24, image.Height); }
                s.Request("observe"); s.Request("status"); s.Frame();
                Assert.AreEqual(4, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                StringAssert.Contains(s.Events, "agent.step.completed");
                Assert.AreEqual(true, s.Request("cancel")["ok"]);
                Assert.AreEqual(false, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
            }
        }

        [TestMethod]
        public void BatchesRejectInvalidLaterCommandsBeforeAnyWorldChangesAndDiscoverFutureOperations()
        {
            using (var s = new AgentScene())
            {
                Assert.AreEqual(false, s.Batch(Step("pause"), Step("misspelled"))["ok"]);
                s.Frame();
                Assert.IsFalse(s.Game.Native.Calls.Any());
                s.Bridge.Commands.Register(new CommandDefinition("test.inspect-health", "Read observed health", new { },
                    args => new ImmediateStep(() => s.Game.Services.Entities.Health(10))));
                StringAssert.Contains(Protocol.Encode(s.Request("describe")), "test.inspect-health");
                Assert.AreEqual(true, s.Batch(Step("test.inspect-health"))["ok"]);
                s.Frame();
                Assert.AreEqual(200, s.Results.Single()["result"]);
                Assert.ThrowsExactly<ArgumentException>(() => s.Bridge.Commands.Register(new CommandDefinition("pause", "duplicate", null, args => null)));
            }
        }

        [TestMethod]
        public void MenuControlsCanUseWallTimeWhileGameTimeIsFrozenAndCancelReleasesOnlyAgentPause()
        {
            using (var s = new AgentScene())
            {
                s.Batch(Step("pause"), Step("controls", ("ms", 50), ("clock", "wall"), ("inputs", new[] { Step("", ("control", "FrontendAccept"), ("group", 2)) })));
                s.Frame(); s.Frame(); s.Frame(0, 49);
                Assert.AreEqual(false, s.Batch(Step("resume"))["ok"], "Concurrent batches must not replace current inputs.");
                s.Frame(0, 1);
                StringAssert.Contains(s.Status, "\"state\":\"completed\"");
                Assert.AreEqual(0, s.Game.Host.GameTime);
                var presses = s.Game.Native.Calls.Where(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME).ToArray();
                Assert.HasCount(2, presses);
                CollectionAssert.AreEqual(new object[] { 2, (int)Control.FrontendAccept, 1f }, presses[0].Arguments);
                s.Request("cancel");
                Assert.AreEqual(false, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
            }
        }

        [TestMethod]
        public void SimulationWaitHonorsClockRolloverAndStalledSimulationFailsWithoutContinuingTheBatch()
        {
            using (var s = new AgentScene())
            {
                s.Game.Host.GameTime = int.MaxValue - 100;
                s.Batch(Step("wait", ("ms", 500), ("clock", "game")), Step("pause"));
                s.Frame(); s.Frame(499); s.Frame(1); s.Frame();
                StringAssert.Contains(s.Status, "\"state\":\"completed\"");
                s.Request("cancel");
                s.Batch(Step("wait", ("ms", 1)), Step("pause"));
                s.Frame(); s.Frame(0, 30002);
                StringAssert.Contains(s.Status, "\"state\":\"failed\"");
                StringAssert.Contains(s.Status, "Simulation did not advance");
                StringAssert.Contains(s.Status, "\"paused\":true");
            }
        }

        [TestMethod]
        public void AgentCannotAccidentallyAdvanceWhilePausedAndCanRecoverWithANewBatch()
        {
            using (var s = new AgentScene())
            {
                s.Batch(Step("pause"), Step("wait")); s.Frame(); s.Frame();
                StringAssert.Contains(s.Status, "Resume before advancing");
                StringAssert.Contains(s.Status, "\"paused\":true");
                s.Batch(Step("resume"), Step("wait")); s.Frame();
                Assert.AreEqual(true, s.Request("cancel")["ok"]);
                StringAssert.Contains(s.Status, "\"state\":\"cancelled\"");
                s.Frame(1000, 1000);
                StringAssert.Contains(s.Status, "\"state\":\"cancelled\"");
            }
        }

        [TestMethod]
        [DataRow(0)] [DataRow(10)]
        public void ObservationReportsAnAbsentPlayerAndOmitsEntitiesTheEngineHasRemoved(int player)
        {
            using (var s = new AgentScene())
            {
                s.Game.Host.PlayerPed = player; s.Game.Observations[10].Exists = false; s.Game.Observations[20].Exists = false;
                s.Blips = Array.Empty<Blip>();
                var response = Protocol.Encode(s.Request("observe", ("radius", 5)));
                StringAssert.Contains(response, "\"player\":null");
                StringAssert.Contains(response, "\"weapon\":null");
                StringAssert.Contains(response, "\"nearbyTotal\":1");
                StringAssert.Contains(response, "\"blips\":[]");
            }
        }
    }
}
