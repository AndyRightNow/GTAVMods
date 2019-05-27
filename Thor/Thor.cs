using GTA;
using System;
using System.Windows.Forms;

namespace Thor
{
    public class Thor : Script
    {
        private MjolnirWorthyAbility mjolnirAbility;
        private StormbreakerWorthyAbility stormbreakerAbility;
        private int previousPedHash;
        private bool abilityHasBeenTurnedOff;
        private DeveloperConsole.DeveloperConsole dc;

        public Thor()
        {
            Tick += OnTick;
            Interval = 0;

            mjolnirAbility = MjolnirWorthyAbility.Instance;
            stormbreakerAbility = StormbreakerWorthyAbility.Instance;
            previousPedHash = -1;
            abilityHasBeenTurnedOff = false;
        }

        void OnTick(object sender, EventArgs e)
        {
            HandleAbilityToggle();
            if (!abilityHasBeenTurnedOff)
            {
                HandleMjolnirAbilityTransfer();
                mjolnirAbility.OnTick(stormbreakerAbility.IsHoldingWeapon);
                HandleStormbreakerAbilityTransfer();
                stormbreakerAbility.OnTick(mjolnirAbility.IsHoldingWeapon);
            }
            else
            {
                if (mjolnirAbility.IsAttachedToPed)
                {
                    mjolnirAbility.RemoveAbility();
                }
                if (stormbreakerAbility.IsAttachedToPed)
                {
                    stormbreakerAbility.RemoveAbility();
                }
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

        private void HandleStormbreakerAbilityTransfer()
        {
            if (IsGameCharacterChanged() &&
                  stormbreakerAbility.IsAttachedToPed)
            {
                stormbreakerAbility.RemoveAbility();
            }

            UpdatePrevCharacter();

            if (!stormbreakerAbility.IsAttachedToPed)
            {
                stormbreakerAbility.ApplyOn(Game.Player.Character);
            }
        }
    }
}
