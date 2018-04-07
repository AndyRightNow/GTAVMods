using GTA;
using GTA.Native;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.ComponentModel;
using GTA.Math;

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
            hammer.Init(false, Wait);
            ability = WorthyAbility.Instance;
            ability.ApplyOn(Game.Player.Character);
        }

        void OnTick(Object sender, EventArgs e)
        {
            //UI.ShowSubtitle(String.Format("Mjonir position {0}, Game play camera center {1}", hammer.Position, GameplayCamera.Direction));
            if (Game.IsKeyPressed(Keys.H))
            {
                ability.CallForMjonir();
            }
            else if (Game.IsKeyPressed(Keys.B))
            {
                ability.CallForMjonir(true, Wait);
            }

            if (Game.IsControlPressed(0, GTA.Control.Aim))
            {
                UI.ShowHudComponentThisFrame(HudComponent.Reticle);

                if (Game.IsKeyPressed(Keys.T))
                {
                    ability.ThrowMjonir(Wait);
                }
            }
        }

        void OnKeyDown(Object sender, KeyEventArgs e)
        {
            
        }
    }
}
