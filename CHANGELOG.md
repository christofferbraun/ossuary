# Changelog

Every version of Ossuary, newest first.

## How versions work

`vMAJOR.MINOR.PATCH`, and pre-1.0 until the mod is on the Steam Workshop.

| | |
| --- | --- |
| **minor** | a feature, or a change someone would notice while playing |
| **patch** | a bug fix, or work on the tooling and docs around the mod |

A version is assigned when a change reaches `main`, not while it is still on a
branch — so feature branches carry the version they will land as, and the number
means "this is what a player would have". The version is declared in exactly one
place, `Directory.Build.props`, and everything else reads it: the assembly, the
Workshop manifest, the status panel in game, and the tag the release gate checks.

**1.0.0 is reserved for the first Workshop publish.** Until then this is
software that works but that nobody outside this repository has ever installed,
and the version should say so.

Entries before 0.7.0 were reconstructed from the commit history after the fact,
so they are honest about *what* shipped but do not carry the day-to-day detail
that later entries will.

---

## 0.6.1 — 2026-08-31

Tooling and documentation. No change to anything the mod does in game.

- **Crash-report reader** (`tools/crash-report.py`). The game ships Sentry's
  native crash handler, which leaves Windows minidumps behind. This reads them
  and reports the exception, the module the faulting instruction is in, and
  every module with an address on the faulting thread's stack — so "was the mod
  involved in this crash" is a question that can be answered rather than argued
  about. It is careful to say that absence of the mod is evidence, not proof.
- **`docs/ROADMAP.md`**, and a README that says what is shipped, what is in
  flight, and what is deliberately not planned.

## 0.6.0 — 2026-08-30 — M5, tier ratings on offers

Community grade, score, win rate and pick rate on every card, relic and potion
the game offers: rewards, choices, the shop, treasure chests, and an ancient's
opening blessings.

Badges are drawn on Ossuary's own overlay and positioned each frame from the
measured screen rectangle of the slot hitbox the game itself uses for clicking.
They track hover and tween animations, and the scene is never modified.

Screens that show you your own collection are excluded, as is anything sitting
under a screen something else has been opened on top of.

**Fixed along the way**

- A crash that had been silently disabling offer ratings for the whole session.
  `NRelic.Model` and `NPotion.Model` are not nullable properties — their getters
  *throw* when nothing has been assigned, and the empty slots in the potion belt
  are exactly that. This was the real cause of every symptom reported against
  the badges: once the feature disabled itself, labels already parented to game
  nodes stayed there with frozen text, colour and position.
- Shop cards measured wrong, because the shop holds purchasables in
  `NMerchantSlot` rather than `NCardHolder`.
- Hover jitter, from recomputing an integer point size every frame instead of
  scaling the label.
- The draw pile double-marking upgrades — `CardModel.Title` already carries the
  `+`, so appending another produced `Bash++`.

## 0.5.0 — 2026-08-30 — M4, incoming-damage forecast

What every enemy intends this turn, hit counts included, what it costs after
block, and a clear warning when the turn would kill you. The numbers come from
the game's own damage calculation, so strength, vulnerable and weak are already
applied rather than re-implemented.

Combat panels take themselves off screen outside combat.

## 0.4.0 — 2026-08-29 — M3, draw pile tracker

What is left in the draw pile, grouped, with the chance of drawing each card
next turn and a roll-up by card type.

The odds use the draw you will actually get: the game recomputes that every turn
from relics and powers, so Ossuary asks it rather than assuming five. When the
pile is shorter than the draw, the cards coming back from the reshuffle get
their own section at real odds.

Panels size themselves to their contents, and the text scales.

## 0.3.0 — 2026-08-29 — M2, community ratings

Spire Codex data, bundled with the build and re-banded onto a normal curve.
Codex's published tiers put more than half the game in the bottom two grades,
which makes them useless for choosing between two cards.

Only the cohorts whose scores can actually carry a grade are bundled — the
per-character slices saturate at 100 and collapse A and B to nothing.

The table is reconciled against Codex's compendium, so anything retired from the
game is dropped rather than left to influence where the band thresholds fall.

A weekly workflow refetches, regrades, and opens a pull request when Codex
publishes a newer snapshot; it merges itself when its own checks pass and stays
a draft when they do not.

**Ossuary makes no network requests while you play.**

## 0.2.0 — 2026-08-29 — M1, the HUD

A `CanvasLayer` parented into the run, so its lifetime is the run's. Panels
draw, hide with `F9`, and rearrange with `F10`; positions and text size persist.

The whole HUD is transparent to input and stays that way — dragging is handled
by reading the mouse directly, so no panel ever becomes clickable and there is
no state in which the HUD can swallow something meant for the game.

Every panel isolates its own failures: one that throws disables itself, logs
once, and leaves the rest of the HUD running.

## 0.1.0 — 2026-08-29 — M0, it loads

Project skeleton, toolchain, and a mod that the game's own loader picks up and
initialises. Build, install and log scripts. `Ossuary.Grading` kept free of game
references so CI can test it on a machine without the game.
