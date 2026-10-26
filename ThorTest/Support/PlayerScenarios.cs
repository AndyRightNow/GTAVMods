using Control = GTA.Control;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Forms;

namespace GTAVMods.Tests.Support
{
    internal static class PlayerScenarios
    {
        internal static PlayerSessionFixture Whirling(bool grab = false)
        {
            var s = new PlayerSessionFixture();
            s.Player.MaxHealth = 250;
            s.Frame(0);
            if (grab)
            {
                s.Game.AddEntity(60);
                s.Game.World.Nearby = (position, radius) => new[] { 60 };
                s.Game.Input.Pressed.Add(Control.ThrowGrenade);
                s.Tick();
                s.Game.Input.Pressed.Clear();
                Assert.AreEqual(10, s.Game.Entity(60).AttachedTo);
            }
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(0, Keys.Z);
            s.Game.Cameras.Creations.Enqueue(80);
            s.Game.Input.Keys.Add(Keys.R);
            s.Tick();
            s.Game.Cameras.RenderingCamera = 80;
            s.Player.Health = 5000;
            Assert.IsTrue(s.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            s.Game.Calls.Clear();
            return s;
        }
    }
}
