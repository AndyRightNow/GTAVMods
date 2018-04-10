using GTA;
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
            ability.OnTick();
        }

        void OnKeyDown(Object sender, KeyEventArgs e)
        {
            
        }
    }
}
