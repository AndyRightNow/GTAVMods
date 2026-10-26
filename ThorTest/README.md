# Test organization

Test source and the HTML review catalogue follow the same feature hierarchy.
Each file contains one behavior group; its namespace follows its folder.
The historical `ModUtilsTest.csproj` name is retained as the build entry point.

```text
ThorTest/
├── PlayerIntegration/
│   ├── HammerActions/
│   │   ├── SummonRecallCatchTests.cs
│   │   ├── AimThrowImpactTests.cs
│   │   ├── DropWhirlTests.cs
│   │   ├── ObstacleNavigationTests.cs
│   │   ├── FollowCameraTests.cs
│   │   └── SoundVisualFeedbackTests.cs
│   ├── PowerEnchantments/
│   │   ├── StrengthHealthDecayTests.cs
│   │   ├── FlightHoverTests.cs
│   │   ├── MeleeAerialAttackTests.cs
│   │   └── GrabReleaseTests.cs
│   ├── ConsoleOperations/
│   │   ├── OpenCloseControlsTests.cs
│   │   ├── TypingClipboardHistoryTests.cs
│   │   ├── CommandsExpressionsShortcutsTests.cs
│   │   ├── PlayerWorldToolsTests.cs
│   │   └── DrawingOutputTests.cs
│   ├── ModLifecycle/
│   │   ├── StartupCompatibilityTests.cs
│   │   ├── EnableDisableTests.cs
│   │   ├── CharacterChangeDeathTests.cs
│   │   ├── ReloadCleanupTests.cs
│   │   └── PauseRetryTests.cs
│   └── Diagnostics/
│       ├── ManualSnapshotTests.cs
│       ├── TimingPeriodicReportTests.cs
│       └── LogStorageTests.cs
├── PureFunctions/
│   ├── MathAndGeometry/
│   │   ├── VectorDistanceTests.cs
│   │   ├── AngleRotationTests.cs
│   │   └── PlaneShapeTests.cs
│   ├── ParsingAndFormatting/
│   │   ├── CommandTextTests.cs
│   │   ├── AnimationNameTimingTests.cs
│   │   └── DiagnosticVersionTextTests.cs
│   └── ValidationAndGameValues/
│       ├── RequiredInputDurationTests.cs
│       ├── GameObjectIdentifierTests.cs
│       └── RuntimeCompatibilityTests.cs
├── Support/                         Shared fixtures, observations and assertions
└── Properties/AssemblyInfo.cs       Assembly-wide test execution policy
```

`PlayerIntegration` scenarios drive real mod components through player inputs,
frame updates and lifecycle events. Only external game, device, storage and host
boundaries are replaced. `PureFunctions` covers deterministic helpers with
explicit inputs and results; stateful module tests do not belong there.

Choose a case's home by its primary player expectation. For example, throwing
despite a missing animation belongs under hammer throwing, while pausing after
an allocation failure and recovering with Ctrl+A+O belongs under lifecycle
recovery. Cleanup triggered by script reload belongs under `ReloadCleanupTests`,
even when the failed resource supports hammer navigation. Diagnostic records
across a gameplay session belong under diagnostics. Keep each scenario in one
place; contract assertions stay inside the behavior that relies on them.

Keep helpers local to a behavior class when only that class uses them. Shared
fixtures, boundary fakes and scenario setup belong under `Support`; do not put
test methods there or move mod decisions into fakes. The assembly remains
nonparallel because console registration and some culture/clock scenarios use
process-wide state.

Gameplay scenarios supply their own [tuning profile](../Thor/Tuning/README.md)
when checking adjustable behavior. For example, a throw must request the selected
clip's configured delay and keep the hammer until the host resumes; it does not
need a separate test for every calibrated animation delay. Camera tests protect
framing relative to the hammer, and power tests protect proportional strength
and expiry under a supplied duration. Changing a default for game feel should
not require rewriting an expected-value table. Keep boundary and failure cases
that represent different behavior.

Run the complete workflow from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
```

During development, a namespace filter can select a feature, for example:

```powershell
dotnet test ThorTest/ModUtilsTest.csproj --configuration Release --no-restore --filter "FullyQualifiedName~GTAVMods.Tests.PlayerIntegration.HammerActions" -p:CollectCoverage=false
```

That filtered run is a development check. The complete workflow still requires
100% measured executable line and branch coverage across all four mod assemblies.
PowerShell build/deployment guardrail tests stay under `scripts/tests` and are
excluded from the mod test review catalogue.
