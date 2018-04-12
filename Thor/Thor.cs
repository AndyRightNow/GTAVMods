using GTA;
using System;
using System.Windows.Forms;

namespace Thor
{
    public class Thor : Script
    {
        private Mjolnir hammer;
        private WorthyAbility ability;

        public Thor()
        {
            Tick += OnTick;
            Interval = 0;

            hammer = Mjolnir.Instance;
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
