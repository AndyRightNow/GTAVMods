

using GTA.Math;
using GTA;
using System.Runtime;
using ADModUtils;
using System;

namespace Thor
{

    internal class TestMjolnir
    {
        private Prop mjolnir;
        private bool Initialized;
        private Vector3 DefaultSpawnPos;
        private ScriptSettings Settings;
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

            SetupTestMjolnir();
            DoTestStuff();
        }

        private void SetupTestMjolnir()
        {
            mjolnir.Velocity = new Vector3();
            mjolnir.IsCollisionEnabled = false;
            mjolnir.Position = DefaultSpawnPos;
            mjolnir.Quaternion = Quaternion.Zero;
        }

        private Quaternion tempRot = new Quaternion(Vector3.WorldUp, 0.1f);

        private void DoTestStuff()
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
    }
    internal static class TestObjects
    {
        public static void OnTick()
        {
            DrawTestMjolnir();
        }

        private static void DrawTestMjolnir()
        {
            TestMjolnir.Instance.OnTick();
        }
    }
}
