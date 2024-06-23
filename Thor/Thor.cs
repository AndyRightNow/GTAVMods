
using CitizenFX.Core;
using CitizenFX.Core.Native;
using System;
using System.Windows.Forms;
using ADModUtils;
using System.Threading.Tasks;

namespace Thor
{
    public class Thor : BaseScript
    {
        private MjolnirWorthyAbility mjolnirAbility;
        private int previousPedHash;
        private bool abilityHasBeenTurnedOff;

        public Thor()
        {
            Tick += OnTick;

            mjolnirAbility = MjolnirWorthyAbility.Instance;
            previousPedHash = -1;
            abilityHasBeenTurnedOff = false;
        }

        public async Task OnTick()
        {
            try
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
                        Game.Player.Character.Weapons.Give(WeaponHash.Parachute, 1, false, false);
                    }
                }

                //TestObjects.OnTick();
            }
            catch(Exception ex)
            {
                Logger.LogConsole("ERROR", ex.ToString());
            }
        }

        private void HandleAbilityToggle()
        {
            // ctrl + a + o
            if (Game.IsControlPressed(0, CitizenFX.Core.Control.VehicleSubDescend) &&
               Game.IsControlPressed(0, CitizenFX.Core.Control.ScriptPadLeft) &&
               Game.IsKeyPressed(Keys.O))
            {
                abilityHasBeenTurnedOff = false;
                CitizenFX.Core.UI.Screen.ShowNotification("The Thor ability has been turned on.");
            }
            // ctrl + a + f
            else if (Game.IsControlPressed(0, CitizenFX.Core.Control.VehicleSubDescend) &&
               Game.IsControlPressed(0, CitizenFX.Core.Control.ScriptPadLeft) &&
               Game.IsControlPressed(0, CitizenFX.Core.Control.VehicleExit))
            {
                abilityHasBeenTurnedOff = true;
                CitizenFX.Core.UI.Screen.ShowNotification("The Thor ability has been turned off.");
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
