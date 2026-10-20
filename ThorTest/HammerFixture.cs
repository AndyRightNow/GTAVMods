using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Thor;

namespace ModUtilsTest
{
    // Ordinary game observations for a stationary player. Tests supply ray/capsule results and
    // entity movement explicitly; this fixture never runs physics or any ability decisions.
    internal sealed class HammerFixture
    {
        public readonly FakeGame Game = new FakeGame();
        public readonly RecordingLog Log = new RecordingLog();
        public readonly SilentAudio Audio = new SilentAudio();
        public readonly HashSet<int> Peds = new HashSet<int> { 10 }, Vehicles = new HashSet<int>();
        public readonly Queue<int> WeaponCreations = new Queue<int>();
        public readonly Thor.NativeHelper Effects;
        public readonly Thunder Thunder;
        public readonly Thor.PathFinder.PathFinder Pathfinder;
        public readonly WeaponCamera Camera;
        public readonly Mjolnir Hammer;
        public readonly MjolnirWorthyAbility Ability;
        public EntityObservation Player => Game.Entity(10);
        public HammerFixture(Random random = null)
        {
            random = random ?? new Random(1729);
            Game.AddEntity(10).Bones[Bone.PHRightHand] = new Vector3(0, 0, 1);
            Game.AddEntity(20);
            Game.Host.OnWait = milliseconds => Game.Host.GameTime += milliseconds;
            Game.World.Nearby = (position, radius) => Array.Empty<int>();
            Game.Native.Queries[Hash.IS_ENTITY_A_PED] = args => Peds.Contains((int)args[0]);
            Game.Native.Queries[Hash.IS_ENTITY_A_VEHICLE] = args => Vehicles.Contains((int)args[0]);
            Game.Native.Queries[Hash.GET_ENTITY_COORDS] = args => Game.Entity((int)args[0]).Position;
            Game.Native.Queries[Hash.GET_ENTITY_SPEED] = args => Game.Entity((int)args[0]).Velocity.Length();
            Game.Native.Queries[Hash.CREATE_WEAPON_OBJECT] = args => WeaponCreations.Dequeue();
            Game.Native.Returns(Hash.GET_WEAPON_OBJECT_FROM_PED, 20);
            Game.Native.Returns(Hash.IS_ENTITY_PLAYING_ANIM, false);
            Game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, true);
            Game.Native.Returns(Hash.HAS_NAMED_PTFX_ASSET_LOADED, true);
            Game.Weapons.AssetResults[WeaponHash.Hammer] = true;
            Game.Weapons.AssetResults[WeaponHash.Grenade] = true;
            Game.World.ModelResults[(int)VehicleHash.Thruster] = true;
            Game.AddEntity(40); Game.AddEntity(41);
            Game.World.Vehicles.Enqueue(40); Game.World.Drivers.Enqueue(41);
            Effects = new Thor.NativeHelper(Game.Services);
            Thunder = new Thunder(Game.Services, Effects, random);
            Pathfinder = new Thor.PathFinder.PathFinder(Game.Services);
            Camera = new WeaponCamera(Game.Services, random);
            Hammer = new Mjolnir(Game.Services, Effects, random, Pathfinder, Camera, Thunder, Audio, Log);
            Ability = new MjolnirWorthyAbility(Game.Services, Effects, random, Hammer, Thunder, Audio, Log);
        }
        public void LooseHammer(Vector3 position)
        {
            Game.Entity(20).Position = position;
            Game.Entities.CreatedBlips.Enqueue(50);
            Hammer.WeaponObject = 20;
        }
        public void HoldHammer()
        {
            Player.Weapons.Add(WeaponHash.Hammer);
            Player.CurrentWeapon = WeaponHash.Hammer;
            Player.CurrentWeaponObject = 20;
        }
        public void PrepareHoverObjects()
        {
            foreach (int id in new[] { 21, 22 })
            {
                Game.AddEntity(id);
                WeaponCreations.Enqueue(id);
            }
        }
        public void ClearTerrainRay() => Game.World.Rays.Enqueue(new RayHit(false, 0, Vector3.Zero));
        public void ClearPath() => Game.World.Capsules.Enqueue(new RayHit(false, 0, Vector3.Zero));
        public object[] LastNative(Hash hash) => Game.Native.Calls.Last(call => call.Hash == hash).Arguments;
    }
}
