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

| Target | Signature | Why not a hook | Verified |
| --- | --- | --- | --- |
| `MegaCrit.Sts2.Core.Nodes.NRun._Ready` | `public override void _Ready()` | Every hook on `Hook` reports a *gameplay* event to a model. None of them yields the scene node the HUD must be parented to, and the HUD has to exist before any gameplay event fires. Postfix only; reads `__instance` and returns void. | `v0.107.1` |

Attaching to `NRun` rather than to the scene root is deliberate: the HUD's
lifetime becomes the run's lifetime, so abandoning a run frees it and the next
run builds a fresh one with no teardown code of our own.

## Semantic hooks

_None yet — M3 onward._ The mechanism is settled and recorded under
"Verified environment facts" below: hooks are consumed by subclassing
`AbstractModel` and registering the instance through `ModHelper`, not by
implementing an interface.

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
| Hooks are **`public virtual Task` methods on `AbstractModel`**, not interfaces — there are zero `IAfterX` types in the assembly | reflection over `sts2.dll`; `Hook.AfterRoomEntered` documents itself as "See `AbstractModel.AfterRoomEntered`" |
| A mod receives hooks by returning its own `AbstractModel` instances from `ModHelper.SubscribeForRunStateHooks(string, RunHookSubscriptionDelegate)` / `SubscribeForCombatStateHooks` | `sts2.xml`: "custom model types to a RunState when IterateHookListeners is called" |
| `RunHookSubscriptionDelegate` is `IEnumerable<AbstractModel> (RunState)`; the combat twin takes `CombatState` | reflection over `sts2.dll` |
| `AbstractModel` is abstract with a **protected parameterless constructor** — subclassable by a mod | reflection over `sts2.dll` |
| `NRun.GlobalUi` → `NGlobalUi : Godot.Control`, exposing `TopBar`, `Overlays`, `CardPreviewContainer`, `AboveTopBarVfxContainer` as parenting targets | reflection over `sts2.dll` |
| **Godot source generators fire for a mod assembly.** `Ossuary.Hud.HudController : CanvasLayer` compiles with the full interop bridge (`InvokeGodotClassMethod`, `HasGodotClassMethod`, `GetGodotMethodList`, `SaveGodotObjectData`) — the same shape the game's own `NRun` carries | reflection over the built `Ossuary.dll` |

## Known drift from community documentation

The widely-referenced community handbook documents `v0.103.3`. Differences
already observed on `v0.107.1`:

- `ModManifest` gained `min_game_version`.
- `ModManager.Initialize` gained a `SemanticVersion gameVersion` parameter.
- Manifests missing `id` are rejected with an explicit error naming the file.

Treat that handbook as a guide to concepts, not as an API reference for this build.
