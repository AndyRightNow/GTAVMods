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
        private bool abilityHasBeenTurnedOff;

        public Thor()
        {
            Tick += OnTick;
            Interval = 0;

            ability = WorthyAbility.Instance;
            ability.ApplyOn(Game.Player.Character);
            previousPedHash = -1;
            abilityHasBeenTurnedOff = false;
        }

        void OnTick(Object sender, EventArgs e)
        {
            HandleAbilityToggle();
            if (!abilityHasBeenTurnedOff)
            {
                HandleAbilityTransfer();
                ability.OnTick();
            }
            else if (ability.IsAttachedToPed)
            {
                ability.RemoveAbility();
            }
        }

        private void HandleAbilityToggle()
        {
            if (Game.IsControlPressed(0, GTA.Control.VehicleSubDescend) &&
               Game.IsControlPressed(0, GTA.Control.ScriptPadLeft) &&
                Game.IsKeyPressed(Keys.O))
            {
                abilityHasBeenTurnedOff = false;
                UI.Notify("The Thor ability has been turned on.");
            }
            else if (Game.IsControlPressed(0, GTA.Control.VehicleSubDescend) &&
               Game.IsControlPressed(0, GTA.Control.ScriptPadLeft) &&
                Game.IsControlPressed(0, GTA.Control.VehicleExit))
            {
                abilityHasBeenTurnedOff = true;
                UI.Notify("The Thor ability has been turned off.");
            }
        }

        private void HandleAbilityTransfer()
        {
            if (previousPedHash != -1 &&
                            Game.Player.Character != null &&
                            Game.Player.Character.GetHashCode() != previousPedHash &&
                            ability.IsAttachedToPed)
            {
                ability.RemoveAbility();
            }

            previousPedHash = Game.Player.Character.GetHashCode();

            if (!ability.IsAttachedToPed)
            {
                ability.ApplyOn(Game.Player.Character);
            }
        }

        void OnKeyDown(Object sender, KeyEventArgs e)
        {

        }
    }
}
