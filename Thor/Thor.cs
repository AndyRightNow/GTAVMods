using GTA;
using GTA.Native;
using System;
using System.Windows.Forms;

namespace Thor
{
    public class Thor : Script
    {
        private WorthyAbility ability;
        private int previousPedHash;
        private bool hasJustTransferredAbility;

        public Thor()
        {
            Tick += OnTick;
            Interval = 0;
            
            ability = WorthyAbility.Instance;
            ability.ApplyOn(Game.Player.Character);
            previousPedHash = 0;
            hasJustTransferredAbility = false;
        }

        void OnTick(Object sender, EventArgs e)
        {
            if (Game.Player.Character != null &&
                Game.Player.Character.GetHashCode() != previousPedHash &&
                ability.IsAttachedToPed)
            {
                ability.RemoveAbility();
                hasJustTransferredAbility = true;
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
        }

        void OnKeyDown(Object sender, KeyEventArgs e)
        {
            
        }
    }
}
