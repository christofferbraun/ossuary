# Compliance

Whether Ossuary may exist, and whether it may be published to the Steam
Workshop. Re-checked 2026-08-30 against Slay the Spire 2 `v0.107.1` and Spire
Codex snapshot v26.

Everything below is evidence, not reassurance. Where a claim rests on something
observable, the observation is named so it can be checked again after a game or
policy update.

## 1. Is modding Slay the Spire 2 permitted?

Yes, and it is first-party. The evidence has become stronger since this was
first checked at M0.

| Evidence | Where it came from |
| --- | --- |
| **Mega Crit publish an official Workshop uploader** at `github.com/megacrit/sts2-mod-uploader`, on their own GitHub organisation. Latest release v0.2.0, 2026-06-26, with binaries for Windows, macOS and Linux. It targets app id `2868840` directly. | the repository and its releases |
| The game ships a mod loader — `ModManager`, `ModHelper`, `ModInitializerAttribute`, and a 146-method public `Hook` API | symbols in `sts2.dll` |
| The game ships HarmonyLib (`0Harmony.dll`), the patching library, as a game file | the install directory |
| The game ships `sts2.xml`, 5.3 MB of public API documentation for its own assemblies | the install directory |
| `ModSource.SteamWorkshop` is a first-class enum value, with full SteamUGC query code | `sts2.dll` |
| The game's own API docs describe Workshop behaviour in detail — version-range comparisons, precedence when a mod is both subscribed and local | `sts2.xml` |
| Steam Workshop is live for app `2868840` and serving mods | subscribed items under `steamapps/workshop/content/2868840` |

**No EULA or terms file ships with the game.** There is nothing in the install
that restricts modding, and a search of the install for `eula`, `licen`, `terms`
and `legal` returns nothing.

### The one caveat, unchanged

Steam Workshop is **not** listed in the store page's feature sidebar. As of
2026-08-30 the listed features are Single-player, Online Co-op, Steam Cloud,
Stats, Steam Leaderboards and Family Sharing. Workshop demonstrably works, is
documented in the game's own API, and has a first-party uploader — so this reads
as a stale store tag rather than a signal. It is recorded here because it is the
only piece of evidence pointing the other way.

## 2. Is *this* mod acceptable?

Ossuary reads and displays. It changes no rule, pool, price, drop or outcome,
and its manifest declares `affects_gameplay: false`.

Two places it touches the game at all, both deliberate and both bounded:

- **One Harmony patch**, a postfix on `NRun._Ready`, which reads `__instance` to
  parent the HUD and returns void. It changes no argument, no return value and
  no game state.
- **Adding `Label` children** to card, relic and potion nodes for the offer
  ratings. Only a `Label` is ever added; no property of a game node is changed.
  It can be turned off entirely with `offerRatings` in `user://ossuary.json`.

Where Ossuary calls into the game to *ask* something — the turn's draw count,
an intent's damage — the call is a query, and that was established by
decompiling every implementation rather than assumed. See `COMPAT.md`.

For comparison, mods already published to this game's Workshop add cards,
relics and enemy mechanics and declare `affects_gameplay: true`.

### Disclosed, not hidden

Runs played with **any** mod loaded are flagged as modded by the game's own
reporting (`ModManager.IsRunningModded()`,
`GetGameplayRelevantModNameList()`). That is the game's design rather than a
penalty Ossuary incurs, and it is stated plainly in the README so nobody
installs it expecting otherwise.

## 3. May Ossuary bundle Spire Codex data?

Yes. Their published terms are explicit, and quoted here verbatim:

> "The public API and embeddable widgets are free to use, including in
> commercial projects. Reasonable rate limits apply (currently 60–120
> requests/minute per IP on common endpoints)."

> "You agree that you will not: Scrape, mirror, or systematically copy the
> Service's pages outside of the documented public API."

> "Attribution back to spire-codex.com is appreciated but not required."

How Ossuary sits against each:

| Their term | What Ossuary does |
| --- | --- |
| Free to use, including commercially | Ossuary is free and MIT-licensed either way |
| Do not scrape outside the documented public API | Every request goes to a documented endpoint — `/api/runs/snapshot-status`, `/api/runs/metrics/{kind}`, `/api/{kind}`. Nothing is scraped from rendered pages. |
| Do not degrade the service for others | A **typical week is one request** — the check that asks whether the snapshot moved, which usually says no and stops. The weeks it has moved cost **eight**: the check, a snapshot read, three compendium reads and three metrics reads. An installed copy makes **zero**. |
| Attribution appreciated | Given prominently in the README and in the mod itself |

### On rate limits specifically

Their terms say 60–120/minute, but the live `/api/rate-limits` endpoint reports
less for an unregistered caller: **15/minute per endpoint** on the general tier,
60 when registered. The fetch tool reads that endpoint at startup and paces to
whatever it says, falling back to the most conservative tier if it cannot be
read — so the figure is never a stale guess in the source.

The scope matters as much as the number. Limits are **per endpoint**, and the
weekly refresh spreads its requests over six different ones, hitting none more
than twice. It is nowhere near constrained.

The one heavy thing Ossuary could ever ask of them is the v2 lift harvest —
about 500 requests to a single endpoint. That is why it is a one-off per data
version rather than anything scheduled, why it paces from the published figure,
and why it checkpoints so an interrupted run resumes instead of starting the 500
again. See `V2.md`.

### A correction worth recording

Codex's API refused us during development, and that was initially assumed to be
us exceeding the rate limit. It was not. Measured on 2026-08-30: every
`/api/runs/*` endpoint returned 502 or timed out while `/api/cards`,
`/api/relics` and `/api/rate-limits` answered in under 200 ms. That is their
runs subsystem being unwell, independent of anything we did.

The pacing is still right, and the weekly workflow now treats an unreachable
Codex as "nothing to do this week" rather than a failure — a community service
being down is not our error to raise an alarm about.

## 4. Publishing to the Steam Workshop

Nothing above prohibits it, and the first-party uploader exists to enable it.
Practical requirements are in `docs/RELEASING.md`.

Two things to get right at publication time, neither legal so much as honest:

- The Workshop description should say what the README says — that this reads and
  displays only, and that modded runs are flagged by the game.
- Attribution to Spire Codex belongs in the Workshop description as well as the
  README, since most people will only ever read the former.

## When to re-check this

- After a Slay the Spire 2 update that changes the mod loader or the Workshop
  integration
- If Mega Crit publish an EULA, modding policy, or Workshop guidelines
- If Spire Codex change their terms or rate limits
- Before the first Workshop publication, and before any change to what Ossuary
  writes to or reads from the game
