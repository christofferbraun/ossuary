# Compatibility register

Slay the Spire 2 is in Early Access and patches often. A Harmony patch can still
*bind* successfully after its target has changed meaning — a silent failure that
only a written-down assumption catches. So every patch and every hook Ossuary
depends on is recorded here with the build it was last verified against.

**Verified against:** `v0.107.1` (commit `59260271`, 2026-06-18)
**Runtime:** .NET 9 (`net9.0`) · Godot 4.5.1 Mono · GodotSharp `4.5.1.0`

## Rules

1. Prefer a semantic hook (`MegaCrit.Sts2.Core.Hooks.Hook`) over a Harmony patch.
   A hook is a supported contract; a patch is an observation about private
   implementation.
2. Every patch below states *why* no hook was sufficient.
3. After a game update, re-check each entry before shipping — a successful build
   proves nothing about semantics.
4. Panels fail individually. A patch that stops working must disable its own
   panel and log once, never throw into the game.

## Harmony patches

_None yet._

| Target | Signature | Why not a hook | Verified |
| --- | --- | --- | --- |
| — | — | — | — |

## Semantic hooks

_None yet._

| Hook | Used for | Verified |
| --- | --- | --- |
| — | — | — |

## Verified environment facts

These were confirmed directly against the installed build rather than inferred
from community documentation.

| Fact | How it was checked |
| --- | --- |
| Mod loader types present (`ModManager`, `ModManifest`, `ModInitializerAttribute`, `ModHelper`, `ModelDb`) | symbol scan of `sts2.dll` |
| 146 semantic hooks on `Hook` | `sts2.xml` |
| `sts2.xml` ships beside `sts2.dll` (5.3 MB API documentation) | file listing |
| Manifest supports `min_game_version` | observed in a live Workshop mod; loader warns when absent |
| Loader reads `<manifest dir>/<id>.dll` and `<id>.pck` | loader log lines |
| Game log is `%APPDATA%\SlayTheSpire2\logs\godot.log` | observed; `<STS2>\sts2_stdout.log` is **not** reliably written |
| `ModManager.Initialize` takes `(IModManagerFileIo, ModSettings, SemanticVersion)` | loader stack trace — note the third parameter is **not** in the v0.103.3 community docs |
| Steam Workshop is live for app `2868840` | subscribed items present under `steamapps/workshop/content/2868840` |

## Known drift from community documentation

The widely-referenced community handbook documents `v0.103.3`. Differences
already observed on `v0.107.1`:

- `ModManifest` gained `min_game_version`.
- `ModManager.Initialize` gained a `SemanticVersion gameVersion` parameter.
- Manifests missing `id` are rejected with an explicit error naming the file.

Treat that handbook as a guide to concepts, not as an API reference for this build.
