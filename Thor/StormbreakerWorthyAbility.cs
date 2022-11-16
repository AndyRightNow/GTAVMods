using GTA;
using System.Windows.Forms;

namespace Thor
{
    public class StormbreakerWorthyAbility : WorthyAbility<StormbreakerWorthyAbility, Stormbreaker>
    {
        public StormbreakerWorthyAbility() : base()
        {
            Weapon = Stormbreaker.Instance;
            soundFileCatchWeapon = "./scripts/catch-hammer.wav";
            soundFileWeaponCloseToPed = "./scripts/hammer-close-to-player.wav";
        }

        protected override bool IsSummonWeaponKeyPressed()
        {
            return Game.IsKeyPressed(Keys.B);
        }

        protected override bool IsRenderWeaponCameraKeyPressed()
        {
            return Game.IsKeyPressed(Keys.N);
        }

        protected override void ThrowWeaponOut(bool hasInitialVelocity = true)
        {
            base.ThrowWeaponOut(hasInitialVelocity);
            Weapon.SetThrownOutInitialRotation(Weapon.WeaponObject.Rotation);
        }
    }
}
