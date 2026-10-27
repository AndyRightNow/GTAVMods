# Rust HTTP/WebSocket companion

`tools/gta-use-server` implements the GTA use companion with Axum 0.8.9 and
Tokio 1.54.1. It is an optional backend for the existing Python SDK and game
bridge. Python 3.14, uv, checkpoint scripts and the independent policy supervisor
remain available with either backend. FastAPI remains the installation default.

With GTA closed and the complete package extracted:

```powershell
.\Install-AgentService.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Grand Theft Auto V Enhanced' -Backend Rust -Uv 'C:\path\to\uv.exe'
# Switch back, also with GTA closed:
.\Install-AgentService.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Grand Theft Auto V Enhanced' -Backend FastApi -Uv 'C:\path\to\uv.exe'
```

The installer records the selected executable in `scripts/GtaUse/.rust-server-path`.
The managed bridge starts it directly, avoiding a resident Python launcher.
Both backends use the same per-game mutex, discovery directory, loopback bearer
authentication and session artifacts. Managed reload retains the companion and
URL; process failure is checked every ten seconds. Missing selected binaries
produce diagnostics while the in-game bridge remains available.

The Rust executable accepts three arguments: discovery directory, GTA PID and
the prepared Python executable. Normal operation uses automatic startup. Its
random loopback port and bearer token are published to `service-<PID>.json`;
never paste this credential into logs or shared evidence. `backend: "axum"`
identifies this implementation. Use `Client(process=...)` as before.

## Interface and resource limits

Both backends support session discovery, versioned requests, streamed states,
correlated WebSocket requests, artifact downloads, externally readable batch results and
Python job start/status/cancellation. Rust provides a compact OpenAPI discovery
document; FastAPI additionally provides its generated interactive documentation.
See [GTA use](gta-use.md) for the operation and state contracts.

The Rust runtime uses two executor threads and at most four blocking workers.
Accepted TCP connections disable Nagle coalescing for interactive control traffic.
One actor serializes complete Windows pipe exchanges. Disconnecting the caller
does not release an active exchange or replay its request. The pipe ACL requires
data-only access; generic write access incorrectly requests permission to create
a pipe instance. Requests are limited to 1 MiB and pipe responses to 4 MiB.
When the pipe is busy, a bounded Windows availability wait runs on a blocking
worker, avoiding fixed polling delays between game frames. That worker cannot
open the pipe or send requests after its caller times out. See Microsoft's
[WaitNamedPipe contract](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-waitnamedpipew).
The actor queue holds at most 64 requests; each WebSocket allows 16 outstanding
commands and one subscription, at most 10 Hz. Slow sends time out after two
seconds. Transport failures report uncertainty; inspect current game state before acting.

JSON artifacts are read with Windows delete sharing, allowing atomic game-side
replacement. Downloads are confined to the current session, including resolved
symlinks and Windows paths. Larger non-JSON artifacts stream from disk. Service
events rotate at 5 MiB with nine backups, and startup failures are written to
`service-<PID>-stdio.log`. Game exit closes listeners and WebSockets; the existing
Python watchdog and in-game lease still govern policy shutdown.

## Build and validation

Install Rust through rustup; `rust-toolchain.toml` selects 1.99.1. The usual
`scripts/Build.ps1` now also runs rustfmt, Clippy with warnings denied, Rust
transport scenarios, and the release build before the shared C# coverage gate.
Use `-Cargo C:\path\to\cargo.exe` with a portable installation. Dependencies are
locked in Cargo.lock. The package includes the executable and upstream notices.

On this PC the portable `x86_64-pc-windows-gnullvm` toolchain uses its bundled
`rust-lld.exe` through `CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_LINKER`; it needs no
machine-wide Visual Studio installation. CI uses the Windows MSVC toolchain.
For a workspace whose executables have Windows Low integrity labels, set
`GTA_USE_RUST_TEST_EXECUTABLE` to a prepared external staging path before the
build. The build copies the release there, and the integration test verifies
that its bytes match. This avoids changing the workspace or game's access rules.
The release statically links its runtime so neither build requires compiler DLLs
on the game machine. A panic terminates the companion after a file diagnostic;
the managed supervisor can restart it. Clients must inspect game state after an uncertain outcome.

Rust scenarios exercise actual loopback HTTP/WebSockets and Windows pipes,
replacing only the external game's responses. Shared player scenarios launch
both actual companions from the production bridge, use the same Python SDK and
checkpoint policy, and test restart and reload. Live timings and memory results
are recorded in [bridge performance](bridge-performance.md).
