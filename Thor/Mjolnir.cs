using ADModUtils;
using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace Thor
{
    public class Mjolnir : GodlyWeapon<Mjolnir>
    {
        private static float HAMMER_ROPE_LENGTH = 0.19f;
        private static string SOUND_FILE_WHIRLING_HAMMER = "./scripts/whirling-hammer.wav";
        private Rope hammerRope;
        private Ped hammerRopeAttachedPed;
        private Bone hammerRopeAttachedPedBoneId;
        private Entity hammerRopeAttachedIntermediateEnt;
        private NAudio.Wave.WaveOut hammerWhirlingSoundPlayer;
        private Vector3 AttachedIntermediateEntOffset = Vector3.UnitX * 0.05f;
        private bool isWhirling;

        public Mjolnir() : base(WeaponHash.Hammer)
        {
            hammerWhirlingSoundPlayer = new NAudio.Wave.WaveOut();
            isWhirling = false;
            throwActions = new List<AnimationActions>
                {
                    AnimationActions.ThrowHammer1,
                    AnimationActions.ThrowHammer2,
                    AnimationActions.ThrowHammer3,
                    AnimationActions.ThrowHammer4,
                    AnimationActions.ThrowHammer5
                }.ToArray();
        }

        public static Vector3 LocalForwardVector = Vector3.UnitY;

        public override void OnTick()
        {
            base.OnTick();
            if (isWhirling)
            {
                if (hammerWhirlingSoundPlayer.PlaybackState != NAudio.Wave.PlaybackState.Playing)
                {
                    hammerWhirlingSoundPlayer.Init(new NAudio.Wave.AudioFileReader(SOUND_FILE_WHIRLING_HAMMER));
                    hammerWhirlingSoundPlayer.Play();
                }
            }
            else
            {
                hammerWhirlingSoundPlayer.Stop();
            }
            if (hammerRopeAttachedPed != null)
            {
                //hammerRope.Length = 0;
                //hammerRope.Length = HAMMER_ROPE_LENGTH;
                return;
            }
            HandleWeaponAvailability();
            HandleWeaponInAirRotation();
            isWhirling = false;

        }

        public void PointAt(Vector3 direction, bool shouldLerp = true)
        {
            var prevQua = weaponObject.Quaternion;

            weaponObject.Quaternion = Quaternion.Zero;

            Quaternion targetQua = Quaternion.Normalize(
                Utilities.Math.DirectionToQuaternion(weaponObject.ForwardVector, direction) *
                Utilities.Math.DirectionToQuaternion(Vector3.UnitZ, LocalForwardVector)
            );

            if (shouldLerp)
            {
                weaponObject.Quaternion = Quaternion.Lerp(prevQua, targetQua, HAMMER_ROTATION_UPWARD_LERP_RATIO);
            }
            else
            {
                weaponObject.Quaternion = targetQua;
            }
        }

        private void HandleWeaponInAirRotation()
        {
            if (IsMoving && weaponObject.IsInAir)
            {
                if (isBeingSummoned && isCloseToSummoningPed)
                {
                    PointAt(Vector3.WorldUp);
                }
                else
                {
                    PointAt(weaponObject.Velocity.Normalized);
                }
            }
        }

        public override Vector3 Velocity
        {
            set
            {
                if (hammerRopeAttachedPed != null)
                {
                    hammerRopeAttachedIntermediateEnt.Velocity = value;
                }
                else if (weaponObject != null)
                {
                    base.Velocity = value;
                }
            }
            get
            {
                if (hammerRopeAttachedPed != null)
                {
                    return hammerRopeAttachedIntermediateEnt.Velocity;
                }
                else
                {
                    return base.Velocity;
                }
            }
        }

        protected Vector3 GetHammerRopeAttachPosition(Entity weaponObject)
        {
            return weaponObject.Position + new Vector3(0.0f, 0.0f, -0.15f);
        }

        public void Whirl(ADModUtils.Plane hammerWhirlingPlane, bool physically = true, Entity weaponObj = null, bool lerp = false)
        {
            isWhirling = true;
            if (weaponObj == null)
            {
                weaponObj = weaponObject;
            }

            var planeCenter = hammerWhirlingPlane.Center;
            var hammerPosProjOnPlane =
                planeCenter +
                Vector3.ProjectOnPlane(weaponObj.Position - planeCenter, hammerWhirlingPlane.Normal).Normalized *
                planeCenter.DistanceTo(weaponObj.Position);
            var hammerPosProjOnPlanePlanePoint = hammerWhirlingPlane.GetPlaneCoord(hammerPosProjOnPlane);
            var perpHammerPosProjOnPlanePlanePoint = new Vector2(-hammerPosProjOnPlanePlanePoint.Y, hammerPosProjOnPlanePlanePoint.X);
            var perpHammerPosProjOnPlaneWorldPoint = hammerWhirlingPlane.GetWorldCoord(perpHammerPosProjOnPlanePlanePoint);

            if (physically)
            {
                var velocity = (hammerPosProjOnPlane - weaponObj.Position) * 1.0f;

                velocity += ((perpHammerPosProjOnPlaneWorldPoint - planeCenter) * 1500.0f);
                velocity += ((weaponObj.Position - planeCenter) * 500.0f);
                if (lerp)
                {
                    var currentVelocity = weaponObj.Velocity;
                    weaponObj.Velocity = Vector3.Lerp(currentVelocity, velocity, 0.25f);
                }
                else
                {
                    weaponObj.Velocity = velocity;
                }

                RotateToDirection(weaponObj, (weaponObj.Position - planeCenter).Normalized);

            }
            else
            {
                var nextHammerPos = planeCenter + (hammerPosProjOnPlane - planeCenter).Normalized;
                var perpHammerPosProjOnPlaneWorldVec = (perpHammerPosProjOnPlaneWorldPoint - hammerPosProjOnPlane).Normalized;
                nextHammerPos = planeCenter + (nextHammerPos + perpHammerPosProjOnPlaneWorldVec - planeCenter).Normalized * 0.3f;
                weaponObj.Position = nextHammerPos;
                RotateToDirection(weaponObj, (nextHammerPos - planeCenter).Normalized);
            }
        }

        public Vector3 ConvertToHammerHeadDirection(Vector3 dir)
        {
            var right = Vector3.Cross(dir, Vector3.WorldUp);

            return Vector3.Cross(dir, right).Normalized;
        }

        public void AttachHammerRopeTo(Ped ped, Bone boneId)
        {
            if (weaponObject == null)
            {
                return;
            }

            DetachRope();
            var planeCenter = ped.Bones[boneId].Position;

            if (hammerRope == null)
            {
                hammerRope = World.AddRope(RopeType.ThickRope, planeCenter, Vector3.Zero, HAMMER_ROPE_LENGTH, 0.0f, false);
            }
            if (hammerRopeAttachedIntermediateEnt == null)
            {
                hammerRopeAttachedIntermediateEnt = ADModUtils.NativeHelper.CreateWeaponObject(WeaponHash.Grenade, 1, planeCenter);
                hammerRopeAttachedIntermediateEnt.IsCollisionEnabled = true;
                hammerRopeAttachedIntermediateEnt.SetNoCollision(ped, true);
                hammerRopeAttachedIntermediateEnt.IsVisible = false;
            }


            var hammerAttachPos = GetHammerRopeAttachPosition(weaponObject);

            hammerRopeAttachedIntermediateEnt.AttachTo(ped.Bones[boneId]);

            // Reset weapon rotation so that the attach position is normalized
            weaponObject.Rotation = Vector3.Zero;

            hammerRope.Connect(hammerRopeAttachedIntermediateEnt, hammerRopeAttachedIntermediateEnt.Position, weaponObject, hammerAttachPos, HAMMER_ROPE_LENGTH);
            hammerRope.ActivatePhysics();

            weaponObject.SetNoCollision(ped, true);
            hammerRopeAttachedPed = ped;
            hammerRopeAttachedPedBoneId = boneId;

            //ADModUtils.NativeHelper.SetObjectPhysicsParams(weaponObject, 1000000.0f);
        }

        public void DetachRope()
        {
            if (hammerRope != null && hammerRopeAttachedPed != null && hammerRopeAttachedIntermediateEnt != null)
            {
                hammerRope.Detach(hammerRopeAttachedIntermediateEnt);
                hammerRopeAttachedPed = null;
                hammerRopeAttachedIntermediateEnt.Detach();
                hammerRopeAttachedIntermediateEnt.Delete();
                hammerRopeAttachedIntermediateEnt = null;
                hammerRope.Delete();
                hammerRope = null;
            }
            ADModUtils.NativeHelper.SetObjectPhysicsParams(weaponObject, WEAPON_MASS);
        }

        protected override bool ShouldApplyForcesToNearbyEntities()
        {
            return !isWhirling;
        }
    }
}
