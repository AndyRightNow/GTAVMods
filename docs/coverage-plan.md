# Mod behavior coverage plan

## Goal and scope

Run the real production mod code in Windows CI against deterministic substitutes
for game and operating-system operations. Require 100% executable line and branch
coverage for mod behavior, with meaningful assertions and an independently
reviewed engine boundary.

The scope is the four shipped assemblies: Thor, ModUtils, DeveloperConsole and
the new GameBridge boundary library.
Gameplay, input interpretation, target selection, movement calculations, effects
policy, animation selection, timing, resource ownership, console behavior,
diagnostics and failure handling remain in scope. Existing uncompiled examples
and Legacy experiments stay uncompiled. The separate CodeWalker asset conversion
tool is installation tooling, not shipped mod behavior; it is outside this gate.

Engine implementation (SHVDNE/GTA), NAudio and framework implementations are
external dependencies. Any excluded binding must be a named, reviewed primitive
interop operation; decisions, loops, retries, cleanup and argument calculations
must remain covered. Do not relabel existing gameplay classes as adapters or
recompile alternative production sources for tests.

## Baseline

Measured on 2026-10-20 with Coverlet 10.1.0 and Release production assemblies:

| Assembly | Covered / executable lines | Covered / branches |
| --- | ---: | ---: |
| Thor | 0 / 1508 | 0 / 684 |
| ModUtils | 118 / 381 | 70 / 160 |
| DeveloperConsole | 0 / 791 | 0 / 290 |

The existing 20 tests pass. Aggregate coverage is 4.40% lines and 6.17% branches.
The test project now references Thor, so completely unexecuted gameplay is visible.
The working branch starts from `codex/gta-v-enhanced`; the PR should target that
branch to keep this refactor separate from the Enhanced migration.

## Implementation sequence

1. **Define the boundary and inventory.**
   Enumerate every compiled source and game/OS operation. Use focused capability
   interfaces for entity operations, world queries, input/time, effects, drawing
   and resource creation. Keep math values and enums where they do not invoke
   GTA. Review the boundary before migrating behavior.
2. **Make utilities and external resources deterministic.**
   Inject clocks, randomness, audio and storage primitives. Retain real temporary
   file tests for JSON logging. Cover numerical invariants, timeout boundaries,
   throttling, fallback, retry, retention and resource disposal. Fix defects
   demonstrated by behavioral tests.
3. **Separate and test the console.**
   Cover tokenization/evaluation, argument validation, registration, command
   execution, input editing, history, scrolling and rendering calculations.
   Replace live input, clipboard, compiler and drawing operations at their
   primitive boundaries.
4. **Migrate and test gameplay in vertical slices.**
   Preserve existing behavior while injecting game operations into lifecycle,
   power/health, recall/catch/throw, target attacks, flight/air dash, whirling,
   ropes, cameras, lightning and pathfinding. Exercise public workflows using
   scripted world observations and recorded commands, including cleanup,
   entity disappearance, character changes and errors.
5. **Enforce coverage in the shared build.**
   Collect fresh reports, check the compiled-source inventory, require exactly
   100% line and branch counts, and fail on missing/empty/stale/partial coverage
   or zero tests. Run the gate before packaging and preserve diagnostics on CI
   failure. Test rejection of one uncovered line and one uncovered branch.
6. **Independent xhigh reviews and validation.**
   Review test assertions, deterministic fakes, exclusions and production parity.
   Correct weak/redundant tests. Spot-check representative mutations to prove
   assertions detect wrong behavior. Run the locked restore/build/test/package
   workflow, inspect package contents and `git diff --check`.
7. **Create the PR.**
   Commit and push the reviewed change, open and attach a PR explaining the
   boundary, exact coverage, validation and remaining live-game limitations.
   Check CI and resolve failures before marking the goal complete.

## Test design rules

- Fakes model primitive game operations, never recall, catch, targeting or other
  mod decisions. Unknown scripted queries should fail clearly where appropriate.
- Use controllable time and randomness, explicit world observations and recorded
  ordered commands. Avoid sleeps, random sampling and reflection into state.
- Assert observable outcomes, boundary values, ordering and cleanup. Coverage
  alone is not an assertion; invoking every getter is not a behavioral test plan.
- Exercise the same compiled logic used in the installed mod.
- Remove proven redundant/unreachable branches with justification rather than
  manufacturing invalid private states.
- Adapter mapping tests verify handles, units, flags and argument order.
- Unit coverage does not certify GTA physics, rendering, keyboard delivery,
  audible output or native runtime compatibility. Keep live smoke checks explicit.

## Completion checklist

- [x] Reviewed boundary and full source inventory
- [x] Utilities and resources covered
- [x] Console behavior covered
- [x] All gameplay slices covered
- [x] Strict coverage gate and negative gate tests
- [x] Xhigh test-quality and boundary reviews resolved
- [ ] Release build/tests/package and CI pass at 100% lines and branches
- [ ] PR created and attached

The goal remains active until every required item is complete. Do not lower the
threshold or narrow the behavior scope to make CI green.

## Review evidence

Three xhigh subagents reviewed boundary decisions and one another's code/tests.
Their reviews caught first-frame flight initialization, stale entity handles,
cleanup that skipped later resources, retry ownership, console parser/geometry
defects, and a test that could pass because the fake threw the same exception as
the intended validation. These cases now have behavior assertions.

Four focused mutation checks deliberately broke the timer boundary, signed plane
projection, independent cleanup, and native argument order. The first projection
mutation survived and exposed a missing assertion; the strengthened asymmetric
positive/negative coordinate test now detects it. All four mutations were then
detected. These are spot checks, not a claim of exhaustive mutation testing.
