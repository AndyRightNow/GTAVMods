using System;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class CreationFailureTests
    {
        [TestMethod]
        public void MissingOptionalBlipStillConfiguresHammerCollisionEffectsAndOwnership()
        {
            var f = new HammerFixture();
            f.Pathfinder.OnTick(0);
            f.Game.Entity(20).CollisionEnabled = false;
            f.WeaponCreations.Enqueue(20);
            f.Game.Entities.CreatedBlips.Enqueue(0);
            f.Game.Calls.Clear();

            f.Hammer.Init(new Vector3(2, 3, 4));

            Assert.AreEqual(20, f.Hammer.WeaponObject);
            Assert.IsTrue(f.Game.Entity(20).CollisionEnabled);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "blip.friendly" || call.Name == "blip.name"),
                "A failed optional marker cannot receive configuration commands.");
            CollectionAssert.AreEqual(new[] { 40, 41 }, f.Game.Calls
                .Where(call => call.Name == "entity.noCollision" && (int)call.Arguments[1] == 20)
                .Select(call => (int)call.Arguments[0]).ToArray(),
                "Initialization must still exclude collisions with the pathfinder helpers.");

            f.Game.Host.GameTime = 1000;
            f.ClearTerrainRay();
            f.Hammer.OnTick();
            CollectionAssert.AreEqual(new[] { 20 }, f.Game.Native.Calls
                .Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY)
                .Select(call => (int)call.Arguments[1]).ToArray(),
                "The hammer's scheduled effect remains active without its map marker.");

            f.Hammer.Dispose();
            f.Hammer.Dispose();
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            Assert.IsFalse(f.Game.Observations[20].Exists);
            CollectionAssert.AreEqual(new[] { "entity.delete:20" }, Deletions(f));
            Assert.IsTrue(f.Game.Entity(40).Exists);
            Assert.IsTrue(f.Game.Entity(41).Exists);
        }

        [TestMethod]
        public void MissingRopeStopsBeforeAnchorCreationAndAllowsAttachmentRetry()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.UnitY);
            f.Game.AddEntity(30);
            f.WeaponCreations.Enqueue(30);
            f.Game.Ropes.Creations.Enqueue(0);
            f.Game.Ropes.Creations.Enqueue(70);
            f.Game.Calls.Clear();
            f.Game.Native.Calls.Clear();

            Assert.ThrowsExactly<InvalidOperationException>(() => f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand));

            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.CREATE_WEAPON_OBJECT),
                "A missing rope must stop setup before allocating its anchor.");
            CollectionAssert.AreEqual(new[] { "rope.create" },
                f.Game.Calls.Where(call => call.Name.StartsWith("rope.", StringComparison.Ordinal))
                    .Select(call => call.Name).ToArray());
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.AreEqual(20, f.Hammer.WeaponObject);
            Assert.IsTrue(f.Game.Entity(20).Exists);

            f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand);
            Assert.AreEqual(10, f.Game.Entity(30).AttachedTo);
            Assert.IsTrue(f.Game.Ropes.Ropes.Contains(70));
            f.Hammer.Dispose();
            f.Hammer.Dispose();

            CollectionAssert.AreEqual(new[] { "entity.delete:30", "rope.delete:70", "entity.delete:20" }, Deletions(f));
            Assert.IsTrue(f.Game.Entity(10).Exists);
            Assert.IsTrue(f.Game.Entity(40).Exists);
            Assert.IsTrue(f.Game.Entity(41).Exists);
        }

        [TestMethod]
        public void MissingAnchorRetainsItsRopeUntilCleanupSucceedsBeforeReplacement()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.UnitY);
            f.Game.AddEntity(30);
            f.WeaponCreations.Enqueue(0);
            f.WeaponCreations.Enqueue(30);
            f.Game.Ropes.Creations.Enqueue(70);
            f.Game.Ropes.Creations.Enqueue(71);
            f.Game.Calls.Clear();

            Assert.ThrowsExactly<InvalidOperationException>(() => f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand));

            Assert.IsFalse(f.Game.Calls.Any(call => call.Name.StartsWith("entity.", StringComparison.Ordinal)),
                "Anchor creation failure must stop before configuring an entity with handle zero.");
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "rope.connect" || call.Name == "rope.physics"));
            Assert.IsTrue(f.Game.Ropes.Ropes.Contains(70));
            Assert.IsEmpty(Deletions(f));

            f.Game.Ropes.SilentDeletionRefusals.Add(70);
            var cleanupFailure = Assert.ThrowsExactly<AggregateException>(
                () => f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand));
            Assert.IsInstanceOfType<InvalidOperationException>(cleanupFailure.Flatten().InnerExceptions.Single());
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "rope.create").ToArray(),
                "Failed cleanup must retain the old rope without allocating a replacement.");
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.CREATE_WEAPON_OBJECT).ToArray());
            Assert.IsTrue(f.Game.Ropes.Ropes.Contains(70));
            Assert.IsTrue(f.Game.Entity(20).Exists);

            f.Game.Ropes.SilentDeletionRefusals.Clear();
            f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand);
            Assert.IsFalse(f.Game.Ropes.Ropes.Contains(70));
            Assert.IsTrue(f.Game.Ropes.Ropes.Contains(71));
            Assert.AreEqual(10, f.Game.Entity(30).AttachedTo);
            f.Hammer.Velocity = Vector3.UnitX;
            Assert.AreEqual(Vector3.UnitX, f.Game.Entity(30).Velocity);
            f.Hammer.Dispose();
            f.Hammer.Dispose();

            CollectionAssert.AreEqual(new[] {
                "rope.delete:70", "rope.delete:70", "entity.delete:30", "rope.delete:71", "entity.delete:20"
            }, Deletions(f));
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsTrue(f.Game.Entity(10).Exists);
            Assert.IsTrue(f.Game.Entity(40).Exists);
            Assert.IsTrue(f.Game.Entity(41).Exists);
        }

        private static string[] Deletions(HammerFixture f) => f.Game.Calls
            .Where(call => call.Name == "entity.delete" || call.Name == "rope.delete")
            .Select(call => call.Name + ":" + call.Arguments[0]).ToArray();
    }
}
