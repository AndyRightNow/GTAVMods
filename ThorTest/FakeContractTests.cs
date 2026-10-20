using System;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class FakeContractTests
    {
        [TestMethod]
        public void CommandsDoNotInventMotionOrCompleteWeaponTransfers()
        {
            var game = new FakeGame();
            game.AddEntity(10);
            var hammer = game.AddEntity(20);
            hammer.Position = new Vector3(5, 7, 9);
            game.Entities.SetVelocity(20, Vector3.UnitY * 100);
            game.Entities.ApplyForce(20, Vector3.UnitZ * 50);
            game.Native.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, 20, 10);

            game.AdvanceFrame(100);

            Assert.AreEqual(new Vector3(5, 7, 9), game.Entities.Position(20),
                "Elapsed time cannot manufacture a physics trajectory.");
            Assert.AreEqual(WeaponHash.Unarmed, game.Weapons.Current(10));
            Assert.AreEqual(0, game.Weapons.CurrentObject(10));

            game.ObserveNextFrame(world => world.ApplyWeaponObservation(10, WeaponHash.Hammer, 20));
            Assert.AreEqual(0, game.Weapons.CurrentObject(10), "Submission is distinct from observation.");
            game.AdvanceFrame(33);
            Assert.AreEqual(20, game.Weapons.CurrentObject(10));
            Assert.AreEqual(WeaponHash.Hammer, game.Weapons.Current(10));
        }

        [TestMethod]
        public void FramePlaybackPreservesHeldInputExpiresEdgesAndWrapsMilliseconds()
        {
            var game = new FakeGame();
            game.Host.GameTime = int.MaxValue - 4;
            game.Input.Pressed.Add(Control.Attack);
            game.Input.Released.Add(Control.Aim);
            game.ObserveNextFrame(world =>
            {
                world.Input.Released.Add(Control.Reload);
                world.ObserveNextFrame(next => next.Input.Pressed.Clear());
            });

            game.AdvanceFrame(10);

            Assert.AreEqual(int.MinValue + 5, game.Host.GameTime);
            Assert.AreEqual(0.010f, game.Host.LastFrameTime);
            Assert.IsTrue(game.Input.IsControlPressed(Control.Attack));
            Assert.IsFalse(game.Input.IsControlJustReleased(Control.Aim));
            Assert.IsTrue(game.Input.IsControlJustReleased(Control.Reload));

            game.AdvanceFrame(0);

            Assert.IsFalse(game.Input.IsControlPressed(Control.Attack));
            Assert.IsFalse(game.Input.IsControlJustReleased(Control.Reload));
            Assert.AreEqual(int.MinValue + 5, game.Host.GameTime);
        }

        [TestMethod]
        public void WeaponRequestsPreserveOwnershipAndDistinguishGivingFromImmediateSelection()
        {
            var game = new FakeGame();
            game.AddEntity(10);
            game.AddEntity(20);
            Assert.IsFalse(game.Weapons.Select(10, WeaponHash.Hammer, true));
            Assert.AreEqual(WeaponHash.Unarmed, game.Weapons.Current(10));
            game.Weapons.Give(10, WeaponHash.Hammer, 1, true, true);
            Assert.IsTrue(game.Weapons.HasWeapon(10, WeaponHash.Hammer));
            Assert.AreEqual(WeaponHash.Unarmed, game.Weapons.Current(10),
                "Give's equipNow flag permits a later equip; it does not promise an immediate switch.");
            Assert.IsTrue(game.Weapons.Select(10, WeaponHash.Hammer, false));
            Assert.AreEqual(WeaponHash.Unarmed, game.Weapons.Current(10));
            game.ApplyWeaponObservation(10, WeaponHash.Hammer, 20);
            game.Weapons.Remove(10, WeaponHash.Parachute);
            Assert.AreEqual(20, game.Weapons.CurrentObject(10), "Removing another weapon must not detach the held one.");
            game.Weapons.Remove(10, WeaponHash.Hammer);
            Assert.AreEqual(0, game.Weapons.CurrentObject(10));
            Assert.AreEqual(WeaponHash.Unarmed, game.Weapons.Current(10));
            Assert.IsTrue(game.Observations[20].Exists, "Inventory removal does not grant this fake ownership to delete world entities.");
        }

        [TestMethod]
        public void FailedCommandHasNoFakeAssignmentAndExternalDeletionRevokesAnEarlierObservation()
        {
            var game = new FakeGame();
            var entity = game.AddEntity(20);
            entity.Health = 90;
            var failure = new InvalidOperationException("operation failed before submission");
            game.BeforeCall = call => { if (call.Name == "entity.health") throw failure; };
            Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() => game.Entities.SetHealth(20, 200)));
            Assert.AreEqual(90, entity.Health);
            game.BeforeCall = null;
            Assert.IsTrue(game.Entities.Exists(20));
            game.ObserveNextFrame(world => world.Observations[20].Exists = false);

            game.AdvanceFrame(16);

            Assert.IsFalse(game.Entities.Exists(20), "An existence observation is not a lifetime lease.");
            Assert.ThrowsExactly<InvalidOperationException>(() => game.Entities.Position(20));
            Assert.IsFalse(game.Entities.Exists(0));
        }

        [TestMethod]
        public void ExplicitWorldObservationsPreserveTerrainHitsAndDoNotHideUnscriptedQueries()
        {
            var game = new FakeGame();
            Assert.ThrowsExactly<InvalidOperationException>(() => game.World.Raycast(Vector3.Zero, Vector3.UnitZ, IntersectFlags.Map));
            Assert.ThrowsExactly<InvalidOperationException>(() => game.World.Capsule(Vector3.Zero, Vector3.UnitY, 1, IntersectFlags.Objects));
            game.World.RaycastObservation = (from, to, flags, ignore) =>
            {
                Assert.AreEqual(new Vector3(2, 3, 4), from);
                Assert.AreEqual(new Vector3(2, 3, -8), to);
                Assert.AreEqual(IntersectFlags.Map, flags);
                Assert.AreEqual(20, ignore);
                return new RayHit(true, 0, new Vector3(2, 3, 0));
            };
            game.World.Rays.Enqueue(new RayHit(false, 0, Vector3.Zero));
            Assert.IsFalse(game.World.Raycast(Vector3.Zero, Vector3.Zero, IntersectFlags.Map).DidHit);
            var terrain = game.World.Raycast(new Vector3(2, 3, 4), new Vector3(2, 3, -8), IntersectFlags.Map, 20);
            Assert.IsTrue(terrain.DidHit);
            Assert.AreEqual(0, terrain.Entity, "Terrain can be hit without an entity handle.");
            Assert.AreEqual(new Vector3(2, 3, 0), terrain.Position);
        }
    }
}
