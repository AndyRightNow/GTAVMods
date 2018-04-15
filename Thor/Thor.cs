using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Windows.Forms;

namespace Thor
{
    public class Thor : Script
    {
        private WorthyAbility ability;
        private int previousPedHash;

        public Thor()
        {
            Tick += OnTick;
            Interval = 0;
            
            ability = WorthyAbility.Instance;
            ability.ApplyOn(Game.Player.Character);
            previousPedHash = -1;
        }

        void OnTick(Object sender, EventArgs e)
        {
            if (previousPedHash != -1 &&
                Game.Player.Character != null &&
                Game.Player.Character.GetHashCode() != previousPedHash &&
                ability.IsAttachedToPed)
            {
                ability.RemoveAbility();
            }

            previousPedHash = Game.Player.Character.GetHashCode();

            if (ability.IsAttachedToPed)
            {
                ability.OnTick();
            }
            else
            {
                ability.ApplyOn(Game.Player.Character);
            }

            if (Game.IsKeyPressed(Keys.X))
            {
                Test();
            }
        }

        private void Test()
        {
        }

        void OnKeyDown(Object sender, KeyEventArgs e)
        {
            
        }
    }
}
