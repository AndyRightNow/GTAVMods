using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Windows.Forms;

namespace Thor
{
    public class Thor : Script
    {
        private Mjonir hammer;
        private WorthyAbility ability;

        public Thor()
        {
            Tick += OnTick;
            Interval = 0;

            hammer = Mjonir.Instance;
            ability = WorthyAbility.Instance;
            ability.ApplyOn(Game.Player.Character);
        }

        void OnTick(Object sender, EventArgs e)
        {
            hammer.Init(false);
            InitializeCallingForMjonir();
            InitializeThrowingMjonir();
        }

        private void InitializeThrowingMjonir()
        {
            if (Game.IsControlPressed(0, GTA.Control.Aim))
            {
                UI.ShowHudComponentThisFrame(HudComponent.Reticle);

                if (Game.IsKeyPressed(Keys.T))
                {
                    ability.ThrowMjonir();
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
