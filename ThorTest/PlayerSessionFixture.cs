using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;
using Control = GTA.Control;
using ADModUtils;

namespace ModUtilsTest
{
    // A scene supplies engine observations and successful allocations, never
    // ability transitions or simulated motion. All behavior uses the shipped composition.
    internal sealed class PlayerSessionFixture
    {
        public readonly FakeGame Game = new FakeGame();
        public readonly SessionObservationLog Log;
        public readonly SilentAudio Audio;
        public readonly ThorController Controller;
        public readonly List<int> CreatedWeapons = new List<int>();
        public readonly HashSet<int> Peds = new HashSet<int> { 10, 11, 60 };
        public readonly HashSet<int> Vehicles = new HashSet<int>();
        public readonly Queue<int> WeaponCreations = new Queue<int>();
        public EntityObservation Player => Game.Entity(Game.Host.PlayerPed);
        private readonly bool initiallyHolding;
        private int nextWeapon = 21;

        public PlayerSessionFixture(int seed = 11, bool holding = true, SessionObservationLog log = null,
            IAudioPlaybackFactory audio = null, Random random = null, Action<FakeGame> configure = null,
            Func<long> milliseconds = null, Func<long> timestamp = null, long frequency = 0,
            bool useProductionDiagnostics = false)
        {
            Log = log ?? new SessionObservationLog(useDefaultLogger: useProductionDiagnostics);
            Audio = audio as SilentAudio ?? new SilentAudio();
            initiallyHolding = holding;
            AddPlayer(10, 200);
            AddPlayer(11, 250);
            Game.AddEntity(20);
            Game.AddEntity(99).Position = new Vector3(90, 80, 70);
            Game.AddEntity(40);
            Game.AddEntity(41);
            Game.World.Vehicles.Enqueue(40);
            Game.World.Drivers.Enqueue(41);
            Game.World.ModelResults[(int)VehicleHash.Thruster] = true;
            Game.Weapons.AssetResults[WeaponHash.Hammer] = true;
            Game.Weapons.AssetResults[WeaponHash.Grenade] = true;
            Game.Entities.CreatedBlips.Enqueue(2000);
            Game.World.Nearby = (position, radius) => Array.Empty<int>();
            Game.World.RaycastObservation = (from, to, flags, ignore) => new RayHit(false, 0, Vector3.Zero);
            Game.World.CapsuleObservation = (from, to, radius, flags) => new RayHit(false, 0, Vector3.Zero);
            Game.Native.Queries[Hash.IS_ENTITY_A_PED] = args => Peds.Contains((int)args[0]);
            Game.Native.Queries[Hash.IS_ENTITY_A_VEHICLE] = args => Vehicles.Contains((int)args[0]);
            Game.Native.Queries[Hash.GET_ENTITY_COORDS] = args => Game.Entity((int)args[0]).Position;
            Game.Native.Queries[Hash.GET_ENTITY_SPEED] = args => Game.Entity((int)args[0]).Velocity.Length();
            Game.Native.Queries[Hash.GET_WEAPON_OBJECT_FROM_PED] = args => Game.Entity((int)args[0]).CurrentWeaponObject;
            Game.Native.Queries[Hash.CREATE_WEAPON_OBJECT] = args =>
            {
                while (Game.Observations.ContainsKey(nextWeapon)) nextWeapon++;
                int handle = WeaponCreations.Count > 0 ? WeaponCreations.Dequeue() : nextWeapon++;
                if (handle == 0) return 0;
                if (!Game.Observations.ContainsKey(handle)) Game.AddEntity(handle);
                else if (!Game.Observations[handle].Exists) Game.Observations[handle] = new EntityObservation();
                Game.Entity(handle).Position = new Vector3((float)args[2], (float)args[3], (float)args[4]);
                CreatedWeapons.Add(handle);
                return handle;
            };
            Game.Native.Returns(Hash.IS_ENTITY_PLAYING_ANIM, false);
            Game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, true);
            Game.Native.Returns(Hash.HAS_NAMED_PTFX_ASSET_LOADED, true);
            Game.Host.OnWait = duration => Game.Host.GameTime = unchecked(Game.Host.GameTime + duration);
            if (holding) Game.ApplyWeaponObservation(10, WeaponHash.Hammer, 20);
            configure?.Invoke(Game);
            Controller = ThorComposition.Create(Game.Services, audio ?? Audio, Log.Writer, random ?? new Random(seed),
                Thor.Thor.ReadConsoleOpen,
                useProductionDiagnostics ? new Func<long>(Thor.Thor.ReadDroppedRecords) : () => Log.Writer.DroppedRecords,
                ThorSessionInfo.Capture(Log.Writer.FilePath),
                useProductionDiagnostics ? null : milliseconds ?? (() => Log.Clock.ElapsedMilliseconds), timestamp, frequency);
            Controller.Start();
        }

        private void AddPlayer(int handle, int originalHealth)
        {
            var player = Game.AddEntity(handle);
            player.MaxHealth = player.Health = originalHealth;
            player.Bones[Bone.PHRightHand] = new Vector3(0, 0, 1);
        }

        public void Frame(int elapsed = 16, Keys key = Keys.None, bool aim = false, bool expectFailure = false)
        {
            Game.Input.Keys.Clear();
            Game.Input.Pressed.Clear();
            if (key != Keys.None) Game.Input.Keys.Add(key);
            if (aim) Game.Input.Pressed.Add(Control.Aim);
            Game.AdvanceFrame(elapsed);
            Log.Clock.ElapsedMilliseconds += elapsed;
            if (key != Keys.None) Controller.KeyDown(new KeyEventArgs(key));
            Controller.Tick();
            if (!expectFailure) AssertNoErrors();
        }

        // An explicit multi-key/controller observation persists until the next sample.
        public void Tick()
        {
            Controller.Tick();
            AssertNoErrors();
        }
        public void ObserveHeldHammer(int handle = 20) => Game.ApplyWeaponObservation(Game.Host.PlayerPed, WeaponHash.Hammer, handle);
        public void DropHammer(Vector3 position)
        {
            Frame(0);
            Frame(0, Keys.Y);
            Game.Entity(20).Position = position;
            Game.Input.Keys.Clear();
        }
        public void Recall()
        {
            Game.Input.Keys.Add(Keys.H);
            Controller.Tick();
            Game.Input.Keys.Remove(Keys.H);
            AssertNoErrors();
        }
        public void Throw(bool fly = false)
        {
            Game.Input.Pressed.Add(Control.Aim);
            Game.Input.Keys.Add(fly ? Keys.U : Keys.T);
            Controller.Tick();
            Game.Input.Keys.Remove(fly ? Keys.U : Keys.T);
            Game.Input.Pressed.Remove(Control.Aim);
            AssertNoErrors();
        }
        public void Disable() => Chord(Keys.F);
        public void Reload() => Controller.Abort();
        public object[] LastNative(Hash hash) => Game.Native.Calls.Last(call => call.Hash == hash).Arguments;

        public void Chord(Keys key)
        {
            Game.Input.Keys.Clear();
            Game.Input.Keys.Add(Keys.A);
            Controller.KeyDown(new KeyEventArgs(key | Keys.Control));
        }

        public string State()
        {
            Controller.KeyDown(new KeyEventArgs(Keys.F9));
            return (string)Log.Last("gameplay.snapshot").Data["state"];
        }

        public void AssertNoErrors()
        {
            var errors = Log.Records.Where(record => record.Level == "ERROR" || record.Name == "diagnostics.exception").ToArray();
            Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors.Select(record =>
                record.Name + ": " + string.Join(", ", record.Data.Values))));
        }

        public void AssertReleased()
        {
            Assert.IsFalse(Game.Observations[40].Exists);
            Assert.IsFalse(Game.Observations[41].Exists);
            Assert.AreEqual(!initiallyHolding, Game.Observations[20].Exists);
            Assert.IsTrue(CreatedWeapons.All(handle => !Game.Observations[handle].Exists));
            Assert.IsEmpty(Game.Ropes.Ropes);
            Assert.IsEmpty(Game.Cameras.Cameras);
            Assert.AreEqual(200, Game.Entity(10).MaxHealth);
            Assert.AreEqual(250, Game.Entity(11).MaxHealth);
            Assert.AreEqual(new Vector3(90, 80, 70), Game.Entity(99).Position);
            Assert.IsFalse(Game.Calls.Any(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 99));
            Assert.IsTrue(Audio.Clips.Values.All(clip => clip.Disposals == 1 && !clip.IsPlaying));
        }
    }

}
