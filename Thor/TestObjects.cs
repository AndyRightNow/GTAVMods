

using GTA.Math;
using GTA;
using System.Runtime;
using ADModUtils;
using System;
using System.Drawing;
using GTA.Native;

namespace Thor
{

    internal class TestMjolnir
    {
        private Prop mjolnir;
        private bool Initialized;
        private ScriptSettings Settings;
        private Vector3 DefaultSpawnPos;
        private static TestMjolnir _instance;

        private TestMjolnir()
        {
            mjolnir = null;
            Initialized = false;
            DefaultSpawnPos = new Vector3(-75.0f, -818.0f, 327.0f);
        }

        public static TestMjolnir Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TestMjolnir();
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

            Settings = ScriptSettings.Load("test-mjolnir-settings.ini");
            var savedHandle = Settings.GetValue("Weapon", "Handle", -1);

            if (savedHandle != -1)
            {
                mjolnir = (Prop)Entity.FromHandle(savedHandle);
            }

            if (mjolnir == null)
            {
                mjolnir = ADModUtils.NativeHelper.CreateWeaponObject(
                    WeaponHash.Hammer,
                    1,
                    DefaultSpawnPos
                );
            }

            Settings.SetValue("Weapon", "Handle", mjolnir.Handle);
            Settings.Save();

            Initialized = true;
        }

        public void OnTick()
        {
            if (!Initialized)
            {
                Init();
            }

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
        private void DoRopeTestStuff()
        {
            if (ropeCreated)
            {
                return;
            }
            mjolnir.Position = DefaultSpawnPos + Vector3.UnitY;

            Function.Call(Hash.ACTIVATE_PHYSICS, mjolnir);
            mjolnir.IsCollisionEnabled = true;
            ADModUtils.NativeHelper.SetObjectPhysicsParams(mjolnir, 1000000.0f);

            var hammerRope = World.AddRope(RopeType.ThickRope, DefaultSpawnPos + Vector3.UnitX, Vector3.Zero, 3.19f, 0.0f, false);

            hammerRope.ActivatePhysics();

            var hammerRopeAttachedIntermediateEnt = ADModUtils.NativeHelper.CreateWeaponObject(WeaponHash.Grenade, 1, mjolnir.Position);
            hammerRopeAttachedIntermediateEnt.IsCollisionEnabled = false;
            hammerRopeAttachedIntermediateEnt.IsVisible = true;
            hammerRope.Connect(hammerRopeAttachedIntermediateEnt, mjolnir, 5.19f);

            ropeCreated = true;
        }
    }

    internal class TestChasingPlane
    {
        private static TestChasingPlane _instance;
        private bool Initialized;
        private Vehicle plane;
        private ScriptSettings Settings;
        private Vector3 DefaultSpawnPos;

        private TestChasingPlane() 
        {
            Initialized = false;
            DefaultSpawnPos = new Vector3(-75.0f, -818.0f, 347.0f);
        }
        private void Init()
        {
            if (Initialized)
            {
                return;
            }

            Settings = ScriptSettings.Load("test-chasing-plane-settings.ini");
            var savedHandle = Settings.GetValue("Plane", "Handle", -1);

            if (savedHandle != -1)
            {
                plane = (Vehicle)Entity.FromHandle(savedHandle);
            }

            if (plane == null || !plane.Exists() || plane.IsConsideredDestroyed)
            {
                var planeModel = new Model(VehicleHash.Akula);
                planeModel.Request();
                plane = World.CreateVehicle(planeModel, DefaultSpawnPos);
                plane.CreateRandomPedOnSeat(VehicleSeat.Driver);
                var driver = plane.GetPedOnSeat(VehicleSeat.Driver);
                driver.Task.ChaseWithHelicopter(Game.Player.Character, Vector3.Zero);
            }
            //driver.Task.ChaseWithGroundVehicle(Game.Player.Character);
            //driver.Task.ChaseWithPlane(Game.Player.Character, Vector3.Zero);
            //driver.Task.VehicleChase(Game.Player.Character);

            Settings.SetValue("Plane", "Handle", plane.Handle);
            Settings.Save();

            Initialized = true;
        }

        public void OnTick()
        {
            if (!Initialized)
            {
                Init();
                return;
            }

            //plane.IsCollisionProof = true;
            //plane.IsExplosionProof = true;
            //plane.Repair();
            //plane.IsInvincible = true;
            //plane.EngineHealth = 1000.0f;
            plane.MaxSpeed = 100000.0f;
        }

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
    }

    internal static class TestObjects
    {
        public static void OnTick()
        {
            DrawTestMjolnir();
        }

        private static void DrawTestMjolnir()
        {
            //TestMjolnir.Instance.OnTick();
            TestChasingPlane.Instance.OnTick();
        }
    }
}
