# Friend Request development run - 2026-10-30

Friend Request reached an independently inspected **Mission Passed - Friend
Request** banner through normal Story Mode play. Michael met Lester, bought the
required clothes with existing cash, drove to Lifeinvader, followed the programmer,
completed the fictional computer minigame, reached the prototype, returned home
and called the displayed mission contact during the keynote. Thor stayed disabled.
No teleport, grants, direct damage, mission edits or Skip was used. This was an
assisted development run with policy revisions and pauses, not an uninterrupted
benchmark or an unaided model evaluation.

## Time and attempts

The first observed active mission at **21:34:26 UTC** to the named result capture
at **22:45:29 UTC** took **1h 11m 3s wall time**, with **20m 18s of supervised runner
time across 41 runs** during that interval. Runner time is clipped to the two
observation timestamps; its untrimmed sum is 20m 36s. There was **one mission start,
zero actual mission failures and zero checkpoint retries**. Five policies failed;
other inspection stops also required parent decisions. These are not game retries.

Before the active-mission boundary, travel and attempts to enter Lester's house
consumed another **39m 8s wall time and 10m 14s runner time across 18 runs**. Keeping
that interval separate must not hide its cost. Several routes approached the
wrong side of the house; a native task at the door and a short camera shot did not
start the mission. Ordinary forward walking finally entered the hall, then native
navigation reached Lester. The exact reason for the native doorway stop remains
unverified.

The parent authored/revised policies, interpreted screenshots, consulted mission
walkthroughs for the Lester entrance and used the normal save menu. No user
provided gameplay input and no other agent controlled the game concurrently.
Wall time includes paused investigation, tool/model latency and builds. This run
does not satisfy the earlier uninterrupted-mission benchmark.

## Evidence and save

Paths below are relative to ignored `artifacts/gta-story-continuation`.
`friend-request-evaluation.json` records exact boundaries, all included runs,
source metadata, save hashes and assistance. Failed traces are retained alongside
successful stages. The archive under
`D:/GTAVMods-evidence/2026-10-31/friend-request-evaluation` includes a file/hash
manifest (1,564 files, approximately 1.05 GB); saves are backed up separately.
Post-mission UI validation has its own `friend-request-ui-validation` archive.

| Observation | Evidence |
| --- | --- |
| Normal doorway entry and actual mission introduction | `lester-manual-entry-001/`, `friend-request-lester-001/` |
| Unsuitable mixer selected, then normal road car travel | `friend-request-suburban-001/`, `friend-request-road-001/` |
| Required clothes bought for $120 and $68 | `suburban-buy-001/`, `suburban-shorts-buy-001/` |
| Drive to office; follow programmer upstairs | `friend-request-lifeinvader-003/`, `lifeinvader-programmer-001/` |
| Continuous visual loop closes 14 pop-ups in 13.4 seconds | `lifeinvader-popup-policy-003/` |
| Scan, Exterminate and resulting prototype objective | `lifeinvader-scan-001/`, `lifeinvader-exterminate-001/`, `computer-cleared-001/` |
| Prototype interaction, office exit and drive home | `prototype-access-001/`, `lifeinvader-exit-001/`, `friend-request-home-002/` |
| TV, ordinary phone list navigation and final call | `keynote-*/`, `friend-request-ending-001/` |
| Named Mission Passed | `friend-request-result-001/004-friend-request-banner.png` |
| Separate normal save and recovery | `friend-request-save-001/`, `friend-request-quicksave-001/`, `after-friend-request-save-001/` |

Result image SHA-256:
`f7c7fdc52d7d968374198bdf3484bd5aefa9d65a2c0a1d99fa8dfcfe245dfe48`.
Quick Save created **SGTA50006**, preserving earlier slots. Its 566,664 bytes and
matching `.bak` were copied and hash-verified outside the repo:
`cc0fc1cb48b191eb8fe010c3db46020673da599706e647114807563d1f87fc13`.

## Reusable improvements and limits

Vehicle acquisition now prefers ordinary road-car/bike classes ahead of industrial
vehicles while retaining eligibility checks and explicit mission-vehicle priority.
Callers can supply different classes or disable ranking. The updated helper
successfully acquired Michael's road car live; the specific mixer-versus-car
ranking and locked-car fallback are covered by deterministic external observations,
not a second paired live comparison.

`gta_use.ui.click_at` extracts bounded normalized cursor placement, readback and
one click. Rejected/mismatched coordinates or a pause-menu transition withhold the
click; lost receipts are not replayed. The raw operation successfully controlled
the fictional computer minigame. The extracted helper positioned correctly on the
phone but CursorAccept did not open Contacts; ordinary Enter did. Input acceptance
is explicitly not UI completion. Phone navigation and cursor-driven interfaces
must use their respective controls. After saving, the extracted helper opened
an observed article link in GTA's fictional browser, then closed the browser via
its visible X. The before/after images and source hashes are retained in
`game-browser-click-001/` and `game-browser-close-001/`; right-click remains
untested live. This post-mission validation is excluded from mission timings.

The one-off visual policy used an observed X template and OpenCV in an isolated uv
overlay, not a new project dependency. It stopped on missing/unchanged visual
matches and retained frames/source hashes. Two early runs made no clicks because
an idle camera hid the screen. A short ordinary W tap restored that particular
computer view. Scan Computer then required a separate Exterminate click; waiting
for control after Scan alone stalled. None of these stops failed the mission.

Requiring routed GPS caused two unnecessary stops: Lifeinvader's visible objective
marker was unrouted on foot. The skill now explains checking the full bounded
blip list against the visible objective. Another policy used an unsupported drive
radius (22 m); the 1..20 m contract is now in offline helper documentation.
There is still no textual tree for game menu contents or mission prompt text;
screenshots remain necessary for those decisions. No native runtime was replaced
while GTA was running.

## Validation

`build-vehicle-selection-001.log`: 115 Python, 15 Rust and 346 C# tests;
`build-ui-helper-001.log`: 122 Python, 15 Rust and 346 C# tests. Both complete
build/test/package runs passed Rust formatting/Clippy and the strict 100% C#
executable line/branch coverage gate. Cursor scenarios use the real Python SDK,
supervisor and Rust service, replacing only external engine observations/input
receipts. They cover normal left/right input, rejected/mismatched positioning,
menu takeover, lost receipt and out-of-image coordinates.
