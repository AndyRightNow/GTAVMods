namespace Thor
{
    // Per-session tuning data. Tune these defaults in Story Mode, then rebuild.
    // Algorithms consume the supplied profile; these values are not test oracles.
    public sealed class GameplayTuning
    {
        public AbilityTuning Ability = new AbilityTuning();
        public HammerTuning Hammer = new HammerTuning();
        public NavigationTuning Navigation = new NavigationTuning();
        public CameraTuning Camera = new CameraTuning();
        public AnimationTuning Animation = new AnimationTuning();
        public CombatTuning Combat = new CombatTuning();
        public EffectsTuning Effects = new EffectsTuning();
    }
}
