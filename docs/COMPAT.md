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

## Game code called as a query

Ossuary reads and draws. Where it calls into the game to *ask* something, the
call must be free of side effects, and that has to be established rather than
assumed.

| Call | Why | How it was verified | Verified |
| --- | --- | --- | --- |
| `Hook.ModifyHandDraw(state, player, 5m, out _)` | The number of cards drawn next turn is computed each turn and discarded; nothing stores it. This is the same call `CombatManager` makes, so the answer is exactly what the game will do. | Decompiled every implementation: 17 `ModifyHandDraw` overrides (9 relics, 8 powers) and the 1 `ModifyHandDrawLate` override (`Fiddle`). All are pure — they read state and return a number, and none assigns to a field or property. `Hook.ModifyHandDraw` itself only iterates and accumulates. | `v0.107.1` |

| `AttackIntent.GetTotalDamage(targets, owner)` and `.Repeats` | The incoming-damage forecast. Going through the game's own calculation is what makes strength, vulnerable and weak correct without reimplementing them. | The game already calls `GetTotalDamage` itself from `AttackIntent.GetTexture` and `GetAnimation`, to pick the intent sprite and animation — it is a rendering path invoked on every intent update. Separately, all 38 damage modifiers were decompiled (12 `ModifyDamageAdditive`, 26 `ModifyDamageMultiplicative`, 3 `ModifyDamageCap`); none assigns to a field or property. | `v0.107.1` |

Two caveats, both deliberate:

- The dispatch iterates *all* hook listeners, including models contributed by
  other mods. Those cannot be verified. The call is throttled to twice a second
  rather than run per frame partly for this reason: at worst it doubles how
  often such an override runs instead of multiplying it by the frame rate.
- **Re-check this after a game update.** A future relic whose `ModifyHandDraw`
  has a side effect would make this call unsafe, and nothing would fail loudly.

## Assumptions about specific game types

| Assumption | Used for | If it breaks | Verified |
| --- | --- | --- | --- |
| A `DrawCardsNextTurnPower` with `AmountOnTurnStart == 0` and `Amount > 0` is draw the player has earned this turn that applies next turn | Playing "draw N extra cards next turn" must move the odds immediately. The game's own guard is `if (AmountOnTurnStart == 0) return count;` — deliberate, so a power gained mid-turn cannot apply to a draw that already happened — which means the hook under-reports until the turn flips. | The type match silently stops applying and the estimate degrades to the hook's answer. No exception, slightly stale number. | `v0.107.1` |

## Nodes Ossuary adds to the game's scene

The offer ratings attach a `Label` to the game's own card, relic and potion
nodes, so the badge moves with the thing it annotates and dies with it. This is
the most invasive thing Ossuary does, and it is bounded on every side: only a
`Label` is ever added, no property of a game node is changed, and the whole
feature can be turned off with `offerRatings` in `user://ossuary.json`.

| Host node | Attached | Removed when |
| --- | --- | --- |
| `NCard`, `NRelic`, `NPotion` | one child `Label` named `OssuaryRating` | the host is freed by the game, the HUD is hidden, or the setting is off |

## Assumptions about specific game types

| Assumption | Used for | If it breaks | Verified |
| --- | --- | --- | --- |
| `CardModel.Pile == null` means the card is being offered rather than held | Deciding which cards get a rating badge, without having to enumerate every screen that can offer one — a list that would silently go stale. `Pile` is `_owner?.Piles.FirstOrDefault(p => p.Cards.Contains(this))`, so it is null for a card with no owner or one not yet in a pile. | Badges appear on cards that are not offers. Guarded: more than 20 candidates on screen is treated as the rule having stopped being true, and no badges are drawn at all. | `v0.107.1` |
| A relic outside `NRelicInventory`, or a potion outside `NPotionContainer`, is not yet owned | The same decision for relics and potions | Badges on owned relics/potions | `v0.107.1` |
| `ModelId.ToString()` is `Category.Entry` (`CARD.BACKFLIP`), and `Entry` is the SNAKE_CASE of the model's class name | Resolving a game id to a Codex id. `RatingTable.Normalize` drops everything up to the last dot. | Every offer reads "no data" | Checked exhaustively offline: **296/296** bundled relic ids and **63/63** potion ids correspond to real game model classes. The only classes without a rating are `DEPRECATED_RELIC`, `VAKUU_CARD_SELECTOR`, `DEPRECATED_POTION` and a mock potion, none of which is ever offered. |

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
| Enemy intents are reachable as public state: `Creature.Monster.NextMove.Intents` → `IReadOnlyList<AbstractIntent>`, with `AttackIntent` exposing `GetSingleDamage`, `GetTotalDamage` and `Repeats`. No label parsing or sprite inspection is needed — the fallback the plan reserved for M4 is not required | reflection over `sts2.dll`; 16 intent subclasses, of which `SingleAttackIntent`, `MultiAttackIntent` and `DeathBlowIntent` carry damage |
| `NRun.GlobalUi` → `NGlobalUi : Godot.Control`, exposing `TopBar`, `Overlays`, `CardPreviewContainer`, `AboveTopBarVfxContainer` as parenting targets | reflection over `sts2.dll` |
| **Godot source generators fire for a mod assembly.** `Ossuary.Hud.HudController : CanvasLayer` compiles with the full interop bridge (`InvokeGodotClassMethod`, `HasGodotClassMethod`, `GetGodotMethodList`, `SaveGodotObjectData`) — the same shape the game's own `NRun` carries | reflection over the built `Ossuary.dll` |

## Known drift from community documentation

The widely-referenced community handbook documents `v0.103.3`. Differences
already observed on `v0.107.1`:

- `ModManifest` gained `min_game_version`.
- `ModManager.Initialize` gained a `SemanticVersion gameVersion` parameter.
- Manifests missing `id` are rejected with an explicit error naming the file.

Treat that handbook as a guide to concepts, not as an API reference for this build.
