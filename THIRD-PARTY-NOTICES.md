# Third-party components

GTAVMods source and the original hammer/audio assets are from
[AndyRightNow/GTAVMods](https://github.com/AndyRightNow/GTAVMods). The repository
does not declare a top-level license; this migration does not grant new rights
to redistribute its code or assets. Existing copyright notices are preserved.
The bundled developer console retains its Dakota628 attribution.

Enhanced model conversion uses [CodeWalker](https://github.com/dexyfex/CodeWalker)
at commit `485d56bec00262ed7fa472261cce7bbc6202b96e`; its source/license remain
in the separately fetched `.deps/codewalker` checkout. Converted assets retain
the original repository's rights. Original game archives and extracted game
keys are not distributed.

Separate runtime installations (not bundled in the mod ZIP):
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
