using GTA;
using System;
using System.Windows.Forms;
using DeveloperConsole;
using ADModUtils;
using GTA.UI;

namespace Thor
{
    public class Thor : Script
    {
        private MjolnirWorthyAbility mjolnirAbility;
        private int previousPedHash;
        private bool abilityHasBeenTurnedOff;

        public Thor()
        {
            Tick += OnTick;
            Interval = 0;

            mjolnirAbility = MjolnirWorthyAbility.Instance;
            previousPedHash = -1;
            abilityHasBeenTurnedOff = false;

            this.RegisterConsoleScript(OnConsoleAttached);
        }

        private void OnConsoleAttached(DeveloperConsole.DeveloperConsole dc)
        {
            //Initialize console stuff here
            Logger.Init(dc);
        }

        void OnTick(object sender, EventArgs e)
        {
            HandleAbilityToggle();
            if (!abilityHasBeenTurnedOff)
            {
                HandleMjolnirAbilityTransfer();
                mjolnirAbility.OnTick(false);
            }
            else
            {
                if (mjolnirAbility.IsAttachedToPed)
                {
                    mjolnirAbility.RemoveAbility();
                }
            }

            TestObjects.OnTick();
        }

        private void HandleAbilityToggle()
        {
            if (Game.IsControlPressed(GTA.Control.VehicleSubDescend) &&
               Game.IsControlPressed(GTA.Control.ScriptPadLeft) &&
               Game.IsKeyPressed(Keys.O))
            {
                abilityHasBeenTurnedOff = false;
                Notification.Show("The Thor ability has been turned on.");
            }
            else if (Game.IsControlPressed(GTA.Control.VehicleSubDescend) &&
               Game.IsControlPressed(GTA.Control.ScriptPadLeft) &&
               Game.IsControlPressed(GTA.Control.VehicleExit))
            {
                abilityHasBeenTurnedOff = true;
                Notification.Show("The Thor ability has been turned off.");
            }
        }

        private void HandleMjolnirAbilityTransfer()
        {
            if (IsGameCharacterChanged() &&
                  mjolnirAbility.IsAttachedToPed)
            {
                mjolnirAbility.RemoveAbility();
            }

            UpdatePrevCharacter();

            if (!mjolnirAbility.IsAttachedToPed)
            {
                mjolnirAbility.ApplyOn(Game.Player.Character);
            }
        }

        private void UpdatePrevCharacter()
        {
            previousPedHash = Game.Player.Character.GetHashCode();
        }

        private bool IsGameCharacterChanged()
        {
            return previousPedHash != -1 &&
                              Game.Player.Character != null &&
                              Game.Player.Character.GetHashCode() != previousPedHash;
        }
    }
}
