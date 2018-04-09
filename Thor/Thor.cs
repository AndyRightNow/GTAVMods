using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Thor
{
    public class Thor : Script
    {
        private Mjonir hammer;
        private WorthyAbility ability;
        private bool isCollectingTargets;
        private HashSet<Entity> targets;

        public Thor()
        {
            Tick += OnTick;
            Interval = 0;

            isCollectingTargets = false;
            targets = new HashSet<Entity>();
            hammer = Mjonir.Instance;
            ability = WorthyAbility.Instance;
            ability.ApplyOn(Game.Player.Character);
        }

        void OnTick(Object sender, EventArgs e)
        {
            hammer.Init(false);
            InitializeCallingForMjonir();
            if (ability.IsHoldingHammer())
            {
                CollectTargets();
                InitializeThrowingMjonir();
            }
            DrawMarkersOnTargets();
        }

        private void DrawMarkersOnTargets()
        {
            foreach (var target in targets)
            {
                World.DrawMarker(
                    MarkerType.UpsideDownCone, 
                    target.Position + new Vector3(0.0f, 0.0f, 2.0f), 
                    GameplayCamera.Direction, 
                    Vector3.Zero, 
                    new Vector3(1.0f, 1.0f, 1.0f), Color.Red
                );
            }
        }

        private void CollectTargets()
        {
            if (Game.IsControlPressed(0, GTA.Control.Aim))
            {
                isCollectingTargets = true;
                var result = World.Raycast(Game.Player.Character.Position, GameplayCamera.Direction, 1000.0f, IntersectOptions.Everything);
                NativeHelper.DrawLine(Game.Player.Character.Position, Game.Player.Character.Position + GameplayCamera.Direction * 1000.0f, Color.Black);
                if (result.DitHitEntity && 
                    result.HitEntity != null &&
                    result.HitEntity != Game.Player.Character &&
                    (Function.Call<bool>(Hash.IS_ENTITY_A_PED, result.HitEntity) ||
                    Function.Call<bool>(Hash.IS_ENTITY_A_VEHICLE, result.HitEntity)))
                {
                    targets.Add(result.HitEntity);
                }
            }
            else if (Game.IsControlJustReleased(0, GTA.Control.Aim) && isCollectingTargets)
            {
                isCollectingTargets = false;
                targets.Clear();
            }
        }

        private void InitializeThrowingMjonir()
        {
            if (Game.IsControlPressed(0, GTA.Control.Aim))
            {
                UI.ShowHudComponentThisFrame(HudComponent.Reticle);

                if (Game.IsKeyPressed(Keys.T))
                {
                    ability.ThrowMjonir(ref targets);
                    isCollectingTargets = false;
                }
            }
        }

        private void InitializeCallingForMjonir()
        {
            if (Game.IsKeyPressed(Keys.H))
            {
                ability.CallForMjonir();
            }
            else if (Game.IsKeyPressed(Keys.B))
            {
                ability.CallForMjonir(true);
            }
        }

        void OnKeyDown(Object sender, KeyEventArgs e)
        {
            
        }
    }
}
