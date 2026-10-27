# Third-party components

The Rust companion uses Axum, Tokio, Hyper, Tungstenite, Serde, UUID,
Windows bindings and their dependencies, pinned in
`tools/gta-use-server/Cargo.lock`. The package includes their upstream license
texts and the Rust standard-library notices in `scripts/GtaUse/rust-notices`.
`dependencies.json` records names, versions, license identifiers and upstream
repositories for the target's runtime and build dependencies.

The GTA use SDK and policy runner use uv-managed Python 3.14 and the Python packages
locked in `uv.lock`: websockets, HTTPX and
their dependencies. They are installed separately by uv; wheels and the Python
runtime are not included in the mod ZIP. Upstream notices are retained in the
installed distributions. See [uv](https://docs.astral.sh/uv/),
[websockets](https://github.com/python-websockets/websockets), and
[HTTPX](https://github.com/encode/httpx).

GTAVMods source and the original hammer/audio assets are from
[AndyRightNow/GTAVMods](https://github.com/AndyRightNow/GTAVMods). The repository
does not declare a top-level license; this migration does not grant new rights
to redistribute its code or assets. Existing copyright notices are preserved.
The former F8 developer console was based on Dakota628's console. That component
has been removed; its original attribution remains in Git history.

Enhanced model conversion uses [CodeWalker](https://github.com/dexyfex/CodeWalker)
at commit `485d56bec00262ed7fa472261cce7bbc6202b96e`; its source/license remain
in the separately fetched `.deps/codewalker` checkout. Converted assets retain
the original repository's rights. Original game archives and extracted game
keys are not distributed.

Separate runtime installations (not bundled in the mod ZIP):
[StraightToStoryMode v1.0](https://github.com/Chiheb-Bacha/StraightToStoryMode/releases/tag/v1.0),
by Chiheb-Bacha, GPL-3.0. The optional startup installer pins source commit
`f1acdee8288f825c31908cb25656fb45f4c3ba1b`, verifies the release archive and ASI,
and retains its license in the installation backup. Source and license remain
available at the linked upstream repository;
[DirectStorageFix 1.2](https://github.com/alloc8or/DirectStorageFix), by alloc8or;
[OpenRPF 0.3](https://www.gta5-mods.com/tools/openrpf-openiv-asi-for-gta-v-enhanced),
by Transmet, GiZz and Antasurris / Los Santos Multiplayer.

The locally installed intro save is
[Fresh Start, FINAL](https://www.gta5-mods.com/misc/fresh-start-introduction-completed-savegame),
by Captain_Wistful. It completes Prologue and Franklin and Lamar. It is installed
separately and is not part of the mod package.

ScriptHookVDotNet Enhanced is used as a build reference and separate game runtime.
It is not included in the mod ZIP. The verified download under `.deps/shvdne`
includes its upstream license files. See
[SHVDNE](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced).

NAudio 2.4.0 and its component assemblies are distributed under the MIT license:
Copyright (c) Mark Heath and contributors. See
[NAudio](https://github.com/naudio/NAudio).

Microsoft runtime dependency assemblies restored through NuGet are distributed
under the MIT license: Copyright (c) .NET Foundation and Contributors, or
Copyright (c) Microsoft Corporation, as specified in the corresponding package.
See [dotnet/runtime](https://github.com/dotnet/runtime). Exact package versions and
content hashes are recorded in each project's `packages.lock.json`.

## MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
