# Mutation testing

Coverage proves that code executed. Mutation testing deliberately changes
operators, conditions and statements to check whether assertions detect changed
behavior. Both checks exercise the shipped production code.

After a successful `scripts/Build.ps1`, run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Mutations.ps1
```

Pass `-DotNet 'C:\path\to\dotnet.exe'` when using a portable SDK. The runner installs
Stryker.NET **5.0.0** into `.deps/mutation-tools/5.0.0`, restores locked dependencies,
and analyzes GameBridge, ModUtils, Thor and DeveloperConsole. It uses the Standard
mutation set and two test workers. It does not upload reports.

`-Assembly ModUtils -Mutate '**/Utilities.cs'` narrows an investigation.
Omit these options for the complete analysis. Source filtering is recorded in
`run.json`; a focused run is not evidence about omitted files.

The runner requires a passing, current **Release** coverage run. The inventory
records the actual compilation configuration for every production and test
assembly; missing or Debug configuration is rejected before creating a mutation
snapshot. The build records source,
test, project, configuration, lock and script inputs, including new Git-untracked
files that are not ignored. Changes require a new build. Mutation analysis uses
a separate source snapshot under `artifacts/mutations/<timestamp>-<id>/source`;
it does not rewrite the working checkout or its binaries. Snapshot hashes and
the coverage inventory accompany each report. The inventory also validates the
test assembly, its symbols and every actual test compiler input. Compiled test
sources are copied even when Git ignores them, so mutation analysis cannot
silently run a smaller test suite than the validated build.

`latest-run.txt` identifies the most recently attempted run. `run.json` marks
whether all requested assemblies completed; `latest-completed.txt` changes only
after success. Each assembly has a full JSON report and `summary.json` contains
status counts. A tool/build failure or baseline test failure stops the run.

## Reading results

No survivor baseline or mutation exclusion inflates the reported score.
Unexecuted raw game/OS primitive bindings remain visible as `NoCoverage`; compare
their exact methods with the saved coverage inventory's `binding` entries.
These are the same reviewed external operations excluded from line/branch
coverage, not untested mod decisions. Compile-error mutants and unsupported
mutations are also reported separately and are not assertion successes.

Review surviving mutations in three groups:

- A change that violates an observable contract is a test gap or defect.
  Add a small independent assertion or scenario, then rerun the affected analysis.
- An algebraically equivalent mutation does not change a valid result. Document
  the equivalence instead of adding tests that reproduce implementation details.
- A mutation within an intentionally flexible outcome, such as either valid
  perpendicular rotation axis, needs a precise contract decision. Do not force
  one internal representation solely to raise the score.

The 100% line and branch gate remains mandatory in every CI build. Mutation
analysis is a separate, reproducible review tool; its score is not presented as
a proof of correctness or a replacement for engine conformance checks.

The [contract and mutation review](mutation-review.md) records the reviewed
outcome ranges and validation evidence for the current enhancement.

Configuration follows the [official Stryker.NET documentation](https://stryker-mutator.io/docs/stryker-net/configuration/).
