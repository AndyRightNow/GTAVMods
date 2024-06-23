

using GTA.Math;
using GTA;
using System.Runtime;
using ADModUtils;
using System;
using System.Drawing;
using GTA.Native;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Thor
{

    internal class TestMjolnir
    {
        private Prop mjolnir;
        private bool Initialized;
        private ScriptSettings Settings;
        private Vector3 DefaultSpawnPos;
        private static TestMjolnir _instance;

        public TestMjolnir(Vector3? spawnPos)
        {
            mjolnir = null;
            Initialized = false;

            DefaultSpawnPos = spawnPos ?? new Vector3(-75.0f, -818.0f, 327.0f);
        }

        public static TestMjolnir Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TestMjolnir(null);
                }

                return _instance;
            }
        }

        private void Init()
        {
            if (Initialized)
            {
                return;
            }

            //Settings = ScriptSettings.Load("test-mjolnir-settings.ini");
            //var savedHandle = Settings.GetValue("Weapon", "Handle", -1);

            //if (savedHandle != -1)
            //{
            //    mjolnir = (Prop)Entity.FromHandle(savedHandle);
            //}

            //if (mjolnir == null)
            //{
            //    mjolnir = ADModUtils.NativeHelper.CreateWeaponObject(
            //        WeaponHash.Hammer,
            //        1,
            //        DefaultSpawnPos
            //    );
            //}

            //Settings.SetValue("Weapon", "Handle", mjolnir.Handle);
            //Settings.Save();
            mjolnir = ADModUtils.NativeHelper.CreateWeaponObject(
                WeaponHash.Hammer,
                1,
                DefaultSpawnPos
            );
            mjolnir.Position = DefaultSpawnPos + Vector3.UnitY;

            Function.Call(Hash.ACTIVATE_PHYSICS, mjolnir);
            mjolnir.IsCollisionEnabled = true;
            ADModUtils.NativeHelper.SetObjectPhysicsParams(mjolnir, 1000.0f);

            ADModUtils.Logger.Log("INFO", "test mjolnir initialized");
            Initialized = true;
        }

        public void OnTick()
        {
            //if (!initialized)
            //{
            //    Init();
            //}

            //SetupTestMjolnir();
            //DoPointAtTestStuff();
            DoRopeTestStuff();
        }

        private void SetupTestMjolnir()
        {
            mjolnir.Velocity = new Vector3();
            mjolnir.IsCollisionEnabled = false;
            mjolnir.Position = DefaultSpawnPos;
            mjolnir.Quaternion = Quaternion.Zero;
        }

        private Quaternion tempRot = new Quaternion(Vector3.WorldUp, 0.1f);

        private void DoPointAtTestStuff()
        {

            Vector3 weaponCurrentForwardVector = mjolnir.ForwardVector;
            Vector3 weaponPos = mjolnir.Position;
            Vector3 weaponCurrentUpVector = mjolnir.UpVector;

            var testDirection = (Game.Player.Character.Position - weaponPos).Normalized;
            World.DrawLine(mjolnir.Position, mjolnir.Position + testDirection * 100.0f, System.Drawing.Color.Blue);

            tempRot = Utilities.Math.DirectionToQuaternion(weaponCurrentForwardVector, testDirection);
            //tempRot = Quaternion.FromToRotation(weaponCurrentForwardVector, testDirection);

            World.DrawLine(mjolnir.Position, mjolnir.Position + weaponCurrentForwardVector * 20.0f, System.Drawing.Color.DarkCyan);
            Vector3 rotatedForwardVector = tempRot.RotateTransform(weaponCurrentForwardVector);
            Vector3 rotatedUpVector = tempRot.RotateTransform(weaponCurrentUpVector);
            World.DrawLine(mjolnir.Position, mjolnir.Position + rotatedUpVector * 30.0f, System.Drawing.Color.Red);
            World.DrawLine(mjolnir.Position, mjolnir.Position + rotatedForwardVector * 20.0f, System.Drawing.Color.Yellow);
            mjolnir.Quaternion = Quaternion.Normalize(tempRot * Utilities.Math.DirectionToQuaternion(new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, 1.0f, 0.0f)));
            //mjolnir.Quaternion = Quaternion.Normalize(new Quaternion(new Vector3(1.0f, 0.0f, 0.0f), Utilities.Math.DegreesToRadians(90.0f)) * new Quaternion(new Vector3(0.0f, 0.0f, 1.0f), Utilities.Math.DegreesToRadians(0.0f)));

            Logger.LogConsole("Info", "angle between", Convert.ToString(Vector3.Angle(weaponCurrentForwardVector, testDirection)), "current quaternion", Convert.ToString(tempRot), "current euler", Convert.ToString(Utilities.Math.QuaternionToEulerAngles(tempRot)));
        }


        private bool ropeCreated = false;
        private Rope HammerRope = null;
        private Entity AttachedIntermediateEnt1 = null;
        private Entity AttachedIntermediateEnt2 = null;

        private void DoRopeTestStuff()
        {
            if (HammerRope != null)
            {
                Logger.LogConsole("INFO", $"Ent1 {AttachedIntermediateEnt1}");
                Logger.LogConsole("INFO", $"Rope pos {HammerRope.GetVertexCoord(0)} - {HammerRope.GetVertexCoord(1)}");
                Logger.LogConsole("INFO", $"Rope length {HammerRope.Length}");
                return;
            }
            Logger.LogConsole("INFO", "------------------------------------------------");


            //var hammerRopeAttachedIntermediateEnt = ADModUtils.NativeHelper.CreateWeaponObject(WeaponHash.Grenade, 1, mjolnir.Position);
            //hammerRopeAttachedIntermediateEnt.IsCollisionEnabled = false;
            //hammerRopeAttachedIntermediateEnt.IsVisible = true;
            //hammerRope.Connect(hammerRopeAttachedIntermediateEnt, mjolnir, 20.0f);

            AttachedIntermediateEnt1 = ADModUtils.NativeHelper.CreateWeaponObject(WeaponHash.Widowmaker, 1, DefaultSpawnPos + Vector3.UnitY);
            AttachedIntermediateEnt2 = ADModUtils.NativeHelper.CreateWeaponObject(WeaponHash.Widowmaker, 1, DefaultSpawnPos - Vector3.UnitY);

            AttachedIntermediateEnt1.IsCollisionEnabled = true;
            AttachedIntermediateEnt1.IsVisible = true;
            AttachedIntermediateEnt2.IsCollisionEnabled = true;
            AttachedIntermediateEnt2.IsVisible = true;

            var dist = World.GetDistance(AttachedIntermediateEnt1.Position, AttachedIntermediateEnt2.Position);

            HammerRope = World.AddRope(RopeType.ThickRope, AttachedIntermediateEnt1.Position, Vector3.Zero, dist, 0.5f, false);
            HammerRope.Length = dist;

            HammerRope.Connect(AttachedIntermediateEnt1, AttachedIntermediateEnt1.Position, AttachedIntermediateEnt2, AttachedIntermediateEnt2.Position, dist);

            HammerRope.ActivatePhysics();

            Logger.LogConsole("INFO", "------------------------------------------------");
            ropeCreated = true;
        }
    }

    internal class TestChasingPlane
    {
        private static TestChasingPlane _instance;
        private bool Initialized = false;
        private Vehicle plane;
        private ScriptSettings Settings;
        private Vector3 DefaultSpawnPos;

        public static TestChasingPlane Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TestChasingPlane();
                }

                return _instance;
            }
        }

        public void Init()
        {
            if (Initialized)
            {
                return;
            }

            DefaultSpawnPos = Game.Player.Character.Position + Vector3.UnitY * 3.0f + Vector3.UnitZ;

            //Settings = ScriptSettings.Load("test-chasing-plane-settings.ini");
            //var savedHandle = Settings.GetValue("Plane", "Handle", -1);


            //plane = (Vehicle)Entity.FromHandle(savedHandle);

            var planeModel = new Model(VehicleHash.Thruster);
            planeModel.Request();
            plane = World.CreateVehicle(planeModel, DefaultSpawnPos);
            plane.Rotation = new Vector3(45.0f, 45.0f, 45.0f);
            plane.CreateRandomPedOnSeat(VehicleSeat.Driver);
            var driver = plane.GetPedOnSeat(VehicleSeat.Driver);
            driver.Task.StartHeliMission(plane, new Vector3(500.0f, 500.0f, 500.0f), VehicleMissionType.GoTo, 10000.0f, 10.0f, 5, 2, -1, -1, HeliMissionFlags.StartEngineImmediately);

            //Settings.SetValue("Plane", "Handle", plane.Handle);
            //Settings.Save();

            Initialized = true;
        }

        public void OnTick()
        {
            if (!Initialized)
            {
                return;
            }
        }
    }

    internal static class TestObjects
    {
        private static List<TestMjolnir> TestMjolnirList;
        private static bool Initialized;

        public static void OnTick()
        {
            Init();
            //HandleTestMjolnirs();
            HandleTestChasingPlane();
        }

        private static void HandleTestChasingPlane()
        {
            if (Game.IsControlPressed(GTA.Control.VehicleSubDescend) &&
               Game.IsKeyPressed(Keys.T) && Game.IsKeyPressed(Keys.D2))
            {
                TestChasingPlane.Instance.Init();

                return;
            }

            TestChasingPlane.Instance.OnTick();
        }

        private static void HandleTestMjolnirs()
        {
            if (Game.IsControlPressed(GTA.Control.VehicleSubDescend) &&
               Game.IsKeyPressed(Keys.T) && Game.IsKeyPressed(Keys.D1))
            {
                TestMjolnirList.Add(new TestMjolnir(Game.Player.Character.Position + Vector3.UnitY));
            }

            foreach (var testMjolnir in TestMjolnirList)
            {
                testMjolnir.OnTick();
            }
        }

        private static void Init()
        {
            if (!Initialized)
            {
                TestMjolnirList = new List<TestMjolnir>();
                Initialized = true;
            }
        }
    }
}
