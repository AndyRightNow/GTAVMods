using ADModUtils;
using GTA;
using GTAVMods.Game;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace Thor
{
    public class Mjolnir : GodlyWeapon
    {
        private static float HAMMER_ROPE_LENGTH = 0.19f;
        private int hammerRope;
        private int hammerRopeAttachedPed;
        private Bone hammerRopeAttachedPedBoneId;
        private int hammerRopeAttachedIntermediateEnt;
        private readonly AudioClip hammerWhirlingSoundPlayer;
        private Vector3 AttachedIntermediateEntOffset = Vector3.UnitX * 0.05f;
        private bool isWhirling;

        public Mjolnir(GameServices game, NativeHelper effects, Random random,
            PathFinder.PathFinder pathfinder, WeaponCamera camera, Thunder thunder,
            IAudioPlaybackFactory audio, IEventLog log)
            : base(WeaponHash.Hammer, game, effects, random, pathfinder, camera, thunder, log)
        {
            hammerWhirlingSoundPlayer = new AudioClip(AudioClip.ResourcePath("whirling-hammer.wav"), 1, audio, log);
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
                hammerWhirlingSoundPlayer.Play(restart: false);
            }
            else
            {
                hammerWhirlingSoundPlayer.Stop();
            }
            if (hammerRopeAttachedPed != 0)
            {
                //hammerRope.Length = 0;
                //hammerRope.Length = HAMMER_ROPE_LENGTH;
                return;
            }
            HandleWeaponAvailability();
            HandleWeaponInAirRotation();
            isWhirling = false;

        }

        private void PointAt(Vector3 direction)
        {
            var prevQua = game.Entities.Quaternion(weaponObject);
            // Aim from the model's identity basis; no temporary engine pose or
            // immediate readback is needed to determine the desired orientation.
            Quaternion targetQua = Quaternion.Normalize(
                Utilities.Math.DirectionToQuaternion(Vector3.UnitY, direction) *
                Utilities.Math.DirectionToQuaternion(Vector3.UnitZ, LocalForwardVector)
            );

            game.Entities.SetQuaternion(weaponObject, Quaternion.Lerp(prevQua, targetQua, HAMMER_ROTATION_UPWARD_LERP_RATIO));
        }

        private void HandleWeaponInAirRotation()
        {
            if (IsMoving && game.Entities.IsInAir(weaponObject))
            {
                if (isBeingSummoned && isCloseToSummoningPed)
                {
                    PointAt(Vector3.WorldUp);
                }
                else
                {
                    PointAt(game.Entities.Velocity(weaponObject).Normalized);
                }
            }
        }

        protected Vector3 GetHammerRopeAttachPosition(int weaponObject)
        {
            return game.Entities.Position(weaponObject) + new Vector3(0.0f, 0.0f, -0.15f);
        }

        public void Whirl(ADModUtils.Plane hammerWhirlingPlane, bool physically = true, int weaponObj = 0)
        {
            isWhirling = true;
            if (weaponObj == 0)
            {
                weaponObj = weaponObject;
            }

            var planeCenter = hammerWhirlingPlane.Center;
            var hammerPosProjOnPlane =
                planeCenter +
                Vector3.ProjectOnPlane(game.Entities.Position(weaponObj) - planeCenter, hammerWhirlingPlane.Normal).Normalized *
                planeCenter.DistanceTo(game.Entities.Position(weaponObj));
            var hammerPosProjOnPlanePlanePoint = hammerWhirlingPlane.GetPlaneCoord(hammerPosProjOnPlane);
            var perpHammerPosProjOnPlanePlanePoint = new Vector2(-hammerPosProjOnPlanePlanePoint.Y, hammerPosProjOnPlanePlanePoint.X);
            var perpHammerPosProjOnPlaneWorldPoint = hammerWhirlingPlane.GetWorldCoord(perpHammerPosProjOnPlanePlanePoint);

            if (physically)
            {
                var velocity = (hammerPosProjOnPlane - game.Entities.Position(weaponObj)) * 1.0f;

                velocity += ((perpHammerPosProjOnPlaneWorldPoint - planeCenter) * 1500.0f);
                velocity += ((game.Entities.Position(weaponObj) - planeCenter) * 500.0f);
                game.Entities.SetVelocity(weaponObj, velocity);

                RotateToDirection(weaponObj, (game.Entities.Position(weaponObj) - planeCenter).Normalized);

            }
            else
            {
                var nextHammerPos = planeCenter + (hammerPosProjOnPlane - planeCenter).Normalized;
                var perpHammerPosProjOnPlaneWorldVec = (perpHammerPosProjOnPlaneWorldPoint - hammerPosProjOnPlane).Normalized;
                nextHammerPos = planeCenter + (nextHammerPos + perpHammerPosProjOnPlaneWorldVec - planeCenter).Normalized * 0.3f;
                game.Entities.SetPosition(weaponObj, nextHammerPos);
                RotateToDirection(weaponObj, (nextHammerPos - planeCenter).Normalized);
            }
        }

        public void AttachHammerRopeTo(int ped, Bone boneId)
        {
            log.Event("INFO", "rope.attach", "ped", ped, "bone", boneId.ToString());
            DetachRope();
            var planeCenter = game.Peds.BonePosition(ped, boneId);

            // DetachRope has released both previous owned resources.
            hammerRope = game.Ropes.Create(RopeType.ThickRope, planeCenter, Vector3.Zero, HAMMER_ROPE_LENGTH, 0.0f, false);
            if (hammerRope == 0)
                throw new InvalidOperationException("Could not create the hammer rope.");
            hammerRopeAttachedIntermediateEnt = effects.Operations.CreateWeaponObject(WeaponHash.Grenade, 1, planeCenter);
            if (hammerRopeAttachedIntermediateEnt == 0)
                throw new InvalidOperationException("Could not create the hammer rope anchor.");
            game.Entities.SetCollisionEnabled(hammerRopeAttachedIntermediateEnt, true);
            game.Entities.SetNoCollision(hammerRopeAttachedIntermediateEnt, ped, true);
            game.Entities.SetVisible(hammerRopeAttachedIntermediateEnt, false);


            var hammerAttachPos = GetHammerRopeAttachPosition(weaponObject);

            game.Entities.AttachToBone(hammerRopeAttachedIntermediateEnt, ped, boneId);

            // Reset weapon rotation so that the attach position is normalized
            game.Entities.SetRotation(weaponObject, Vector3.Zero);

            game.Ropes.Connect(hammerRope, hammerRopeAttachedIntermediateEnt, game.Entities.Position(hammerRopeAttachedIntermediateEnt), weaponObject, hammerAttachPos, HAMMER_ROPE_LENGTH);
            game.Ropes.ActivatePhysics(hammerRope);

            game.Entities.SetNoCollision(weaponObject, ped, true);
            hammerRopeAttachedPed = ped;
            hammerRopeAttachedPedBoneId = boneId;

            //effects.Operations.SetObjectPhysicsParams(weaponObject, 1000000.0f);
        }

        public void DetachRope()
        {
            if (hammerRope != 0) log.Event("INFO", "rope.detach");
            hammerRopeAttachedPed = 0;
            Cleanup.Run(ReleaseRopeAnchor, ReleaseRope, RestoreHammerPhysics);
        }

        private void ReleaseRopeAnchor()
        {
            if (hammerRopeAttachedIntermediateEnt != 0)
            {
                if (game.Entities.Exists(hammerRopeAttachedIntermediateEnt))
                {
                    Cleanup.Run(
                        () => game.Entities.Detach(hammerRopeAttachedIntermediateEnt),
                        () =>
                        {
                            game.Entities.Delete(hammerRopeAttachedIntermediateEnt);
                            hammerRopeAttachedIntermediateEnt = 0;
                        });
                }
                hammerRopeAttachedIntermediateEnt = 0;
            }
        }

        private void ReleaseRope()
        {
            if (hammerRope != 0)
            {
                game.Ropes.Delete(hammerRope);
                hammerRope = 0;
            }
        }

        private void RestoreHammerPhysics()
        {
            if (weaponObject != 0 && game.Entities.Exists(weaponObject))
                effects.Operations.SetObjectPhysicsParams(weaponObject, WEAPON_MASS);
        }

        public void StopEffects()
        {
            isWhirling = false;
            hammerWhirlingSoundPlayer.Stop();
            DetachRope();
        }

        public void Dispose()
        {
            Cleanup.Run(StopEffects, hammerWhirlingSoundPlayer.Dispose,
                () => WeaponObject = 0, DestroyWeaponTrackCam);
        }

        protected override bool ShouldApplyForcesToNearbyEntities()
        {
            return !isWhirling;
        }
    }
}
