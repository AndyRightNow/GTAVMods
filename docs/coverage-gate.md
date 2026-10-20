# Coverage gate

Run `scripts/Build.ps1` from the repository root. The build packages the mod only
after every inventoried behavior line and branch has a positive hit count and
every test passes. There is no threshold override.

Each run writes `artifacts/coverage/<timestamp>-<id>/`. `latest-run.txt` identifies
the newest run. The directory contains the compiler input records, portable-PDB
inventory, JSON and Cobertura coverage, TRX test results, and `gate-summary.json`.
CI uploads the coverage directory even when validation fails.

`Coverage.Inventory.targets` records the actual compiler inputs for every solution
project. `New-CoverageInventory.ps1` checks source checksums against portable PDBs,
checks that the test directory contains the same binaries and symbols, and records
executable source spans and branch identities. A new compiled production project
automatically participates. The separate EnhancedAssets installation tool is not
in the solution or shipped mod.

`Test-Coverage.ps1` rejects missing, empty, stale or incomplete reports, changed
sources or binaries, skipped or failed tests, missing production modules, and
unmeasured executable lines or branches. Counts use integers rather than rounded
percentages. Line identity is the document and line number: Coverlet attributes
shared lines, such as an accessor pair or lambda, to one method. Branch identity
also includes the exact method and IL path. For a generated lambda, the inventory
records Coverlet's enclosing-method attribution only when the generated type,
its unique delegate-creation reference, and the shared PDB line identify that
exact owner. Colliding branch identities fail
instead of allowing one hit to stand in for two branches. The inventory uses the pinned
collector's branch analysis; a Coverlet update requires validating that API and
rerunning the inventory checks.

The only exclusions are exact method signatures in
`scripts/coverage-boundaries.json`, paired with `GamePrimitiveAttribute` in source.
These have been reviewed as direct GTA, SHVDNE, audio-device or Windows operations
and host event wiring. Branches, loops, exception handling and arithmetic in a
binding fail the inventory check. Changes to a binding still require source
review; the IL guard does not replace it. Handle mapping, native argument order,
clock arithmetic, lifecycle decisions, resource cleanup and gameplay remain
covered.

The standalone checks in `scripts/tests/` exercise gate rejection paths and
compile real portable-PDB fixtures. They run before the production build and do
not need Pester. The gate measures execution of the real mod assemblies against
primitive substitutes; GTA rendering, physics and native/runtime compatibility
still need the live checks documented in the main README.
