# Contract and mutation review, 2026-10-20

This review accompanies the contract assurance enhancement in PR #7. Three
independent xhigh reviewers inspected production boundaries, fake semantics and
one another's tests. The workflow and reproduction commands are described in
[mutation-testing.md](mutation-testing.md).

## What changed after the first mutation run

The first complete Standard mutation run found assertion gaps despite 100%
line and branch coverage. Existing scenarios gained outcome assertions for
resource ownership, native argument mapping, elapsed-time boundaries, translated
geometry, diagnostics, console editing and composed gameplay transitions.
Additional cases cover distinct failure modes rather than duplicating production
algorithms in tests.

Regression tests also exposed and corrected:

- Aiming that temporarily submitted an invalid zero quaternion and relied on
  immediate engine readback. Aiming now computes the target from the model basis.
- Control restoration that retained an already restored full snapshot and could
  overwrite another script's later changes while retrying a different control.
- Power decay that could retain inventory-only half power indefinitely or use an
  obsolete timestamp after long rope possession. Decay now begins at loss of the
  final source, from the actual power at loss, including clock wrap, and cannot
  increase half power to full power.
- Rope attachment that continued after zero-handle creation, and marker setup
  that configured an unavailable blip. Required attachment resources now fail
  before dependent commands; an unavailable optional marker leaves the hammer
  functional. Partial resource ownership remains available for cleanup/retry.

Reviews rejected tests that would pin arbitrary rotation axes, cosmetic phase,
exception prose or duplicate idempotent writes solely to kill mutants. They also
corrected a fake obstacle observation outside the queried capsule segment.

## Result interpretation

The reports preserve every survivor, compile error, runner-ignored mutant and
unexecuted primitive binding. There is no survivor baseline or score exclusion.
Timeouts represent liveness regressions detected by the runner, not successful
assertions. Full JSON reports and saved source snapshots with recorded hashes are
generated under the ignored `artifacts/mutations` directory.

Coverage still independently requires every executable behavior line and branch
in Thor, ModUtils, DeveloperConsole and GameBridge. Exact raw engine/OS bindings
are inventoried separately. Test binaries, symbols, actual compiler inputs and
source snapshots must match the validated build; stale or partial evidence fails.

The final local Release build passed 374 tests with none skipped, zero warnings
and zero errors, then produced the inspected installation package.

| Assembly | Covered / executable lines | Covered / branches |
| --- | ---: | ---: |
| Thor | 1469 / 1469 | 656 / 656 |
| ModUtils | 476 / 476 | 212 / 212 |
| DeveloperConsole | 765 / 765 | 320 / 320 |
| GameBridge | 214 / 214 | 6 / 6 |

The gate's 23 acceptance/rejection cases and compiled portable-PDB fixtures also
passed. They include test-source freshness, ignored compiler inputs, forced
compilation and rejection of missing or duplicate compiler-time hashes.

Full-module mutation confirmations before the final rope and marker guards:

| Assembly | Killed | Timeout | Survived | No coverage | Compile error | Runner ignored |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| GameBridge | 46 | 0 | 0 | 6 | 1 | 5 |
| ModUtils | 420 | 2 | 14 | 0 | 19 | 111 |
| Thor | 1473 | 0 | 60 | 5 | 33 | 281 |
| DeveloperConsole | 711 | 8 | 30 | 32 | 59 | 181 |

These are separate saved runs, not one combined final-checkout mutation score.
The utility confirmation used snapshot `20261020-091019`, the console used
`20261020-093905`, and gameplay used `20261020-100412`. Reports retain their full
unique directory suffixes locally. All 45 targeted utility, 51 console, 69 Thor
support, 62 controller/diagnostic, 15 final hammer and 39 final ability gaps were
confirmed killed in their corresponding reruns. Two obsolete held-only power
bookkeeping mutations disappeared with the regression-backed power fix.

After the final creation guards, snapshot `20261020-102111` reran only
`GodlyWeapon.cs` and `Mjolnir.cs` against all 374 tests. Their per-file results
were 202 killed / 9 survived / 6 compile errors / 28 runner-ignored and
104 killed / 8 survived / 0 compile errors / 34 runner-ignored respectively.
All three new failure guards are detected. The only additional survivors remove
the two new exception sentences; exception types, allocation suppression,
retained ownership and retry behavior remain asserted. Files outside this
focused selection are explicitly filtered in its report, not counted as tested.
GameBridge was also rerun in full as `20261020-102014`, with the same 46 killed
and zero survivors, through the finalized Windows PowerShell 5.1 runner.

Source comparison confirmed ModUtils and DeveloperConsole still match their
reviewed full-module snapshots, and only the two focused hammer files changed
since the full Thor run. The local source directories are saved evidence, not
tamper-proof storage; their recorded hashes identify the analyzed inputs.

## Accepted survivor contracts

These are explicit limits on what tests promise, not exemptions from coverage.

| Area | Accepted differences and reason |
| --- | --- |
| Utility math | Multiplication/division by a sign in `{−1, +1}`; identical saturation endpoints; either stable perpendicular axis for an underdetermined half-turn; a numerical cutoff within the tested direction tolerance. |
| Logging and diagnostics | Equivalent JSON escaping, session-unique filename separators/part direction, and explanatory prose. Structured event names, severities, data fields, original errors, ordering and retention remain required. |
| Console | Equivalent clamp endpoints and unreachable conditions, redundant pending-control flush before a capture that already flushes, and cosmetic explanatory strings. Input, rendering geometry, state restoration, timeouts and resource ownership remain required. |
| Animation metadata | Unused wait names and angle/sign flags outside supported directional action selection. Selected asset/clip names, supported angles, delays and native requests remain tested. |
| Hammer motion | Either orbital direction or phase, lightning jaggedness density, and idle target reselection within one to two seconds. Finite bounded speed, orbital plane/radius, translation invariance, head orientation, trail bounds and no unintended damage remain required. |
| Hammer lifecycle | Empty cleanup after assigning the same handle, redundant existence guards already enforced by movement checks, `First` versus `FirstOrDefault` after a nonempty check, and a close-to-player flag overwritten before relevance. |
| Ability bookkeeping | Visibility overwritten to the same state in a successfully completed frame, immutable zero plane center, zero-momentum transfer, thunder state overwritten before reading, zero health increment at zero power, a private zero-power fade whose outputs stay zero and whose timestamp is cleared by any real source, equivalent decay endpoints, and ragdoll permission already enabled by the called helper. |
| Controller | Recovery state overwritten whenever the controller becomes disabled before its next relevant read. |

The floating-point idle interval case is deliberately classified as a permitted
cadence difference, not an algebraic equivalence: a double below one can round to
`1f`. A near-one sample is tested to ensure interpolation remains bounded and
successive ticks never divide by a zero interval.

Offline checks establish behavior against the
[engine contracts](engine-contracts.md). They cannot establish the engine's
actual collision response, rendering, audio output or runtime ABI. Focused live
conformance checks remain the boundary for those assumptions.
