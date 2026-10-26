# Gameplay tuning

`GameplayTuning` collects the settings used by one mod session. The production
composition creates the default profile; integration scenarios can supply a
profile with different settings through the same composition entry point.
The values are calibrated defaults, not assertions about the best game feel.

| File | What to tune |
| --- | --- |
| `AnimationTuning.cs` | Animation dictionaries, clips, directional naming flags, release delays, playback blending and loading timeout. |
| `HammerTuning.cs` | Asset loading timeout, mass, recall distance bands and speeds, target contact range, impact damage, obstacle probes, terrain recovery, whirl geometry and audio. |
| `AbilityTuning.cs` | Health and stored power, fading and recovery, catch/return timing, throwing and flight, targeting, presentation and catch/approach audio. |
| `CombatTuning.cs` | Full-power melee force, damage and ragdoll duration. |
| `CameraTuning.cs` | Field of view, view duration, near/far framing, stationary lead and weighted view choices. |
| `NavigationTuning.cs` | Invisible navigation helper spawning, task parameters and movement correction. |
| `EffectsTuning.cs` | Trail subdivision, lateral sway and particle size. |

Names ending in `Ms` use milliseconds; `Seconds` uses seconds. Positions and
distances use game world units (metres), speeds use units per second, angles use
degrees, and volumes use a fraction from zero to one. Blend, sway and recovery
fractions describe proportions. Native force and mass values use GTA's engine
conventions and need live assessment. Navigation values of `-1` preserve the
existing native task sentinel; they are not ordinary distances or angles.

Edit defaults here, rebuild with `scripts/Build.ps1`, then install and verify in
Story Mode using the main README. Change the profile before constructing a
session: components capture some settings at construction, so mutating a running
profile does not provide reliable hot reload. There is no external JSON/INI
loader or automatic in-game tuning interface.

Keep animation arrays aligned with `AnimationActions`. Release delays are keyed
by dictionary and final clip name, including the directional suffix; missing
entries mean no release delay. Repeating a camera view in `Views` increases its
selection weight. Keep that list nonempty, intervals and ranges sensible,
near/medium distance bands ordered, and fractions within their intended range.
The profile is trusted source configuration, not unvalidated player input.

Test the mechanisms with supplied settings: hold a hammer until the host wait
completes, slow recall in the supplied distance bands, fade power over the supplied
duration, and keep a camera or trail correctly positioned when the hammer moves.
Assert results derived from those inputs and observable commands, resources and
diagnostics. Do not duplicate this folder's calibrated defaults in test tables.
Mathematical identities, animation sector naming and native ABI arguments remain
contracts in their owning code; they are not game-feel settings.

The tests establish the mod's behavior against its engine contracts. They cannot
choose the most convincing animation release point, camera distance or visual
density, or verify how GTA physically responds. Tune those with the real game.
