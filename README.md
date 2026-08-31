<h1 align="center">Ossuary</h1>

<p align="center">
  A native <b>Slay the Spire 2</b> mod: deck tracker with draw odds, an
  incoming-attack forecast, and community tier ratings on the cards, relics and
  potions you are offered.
</p>

<p align="center">
  <img alt="status" src="https://img.shields.io/badge/status-in%20development-9a6414">
  <img alt="game" src="https://img.shields.io/badge/Slay%20the%20Spire%202-v0.107.1-2c7a6c">
  <img alt="net" src="https://img.shields.io/badge/.NET-9.0-512BD4">
  <img alt="license" src="https://img.shields.io/badge/license-MIT-2c7a6c">
</p>

<p align="center">
  <a href="docs/ROADMAP.md"><b>Roadmap</b></a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="docs/COMPAT.md">Compatibility</a> ·
  <a href="#using-it">Using it</a> ·
  <a href="#tier-ratings">How the ratings work</a>
</p>

---

Ossuary shows you the numbers you would otherwise have to remember: what is left
in your draw pile and how likely you are to see it, how much damage is coming
this turn, and how the community has actually performed with the card in front
of you.

The odds use the draw you will actually get — the game recomputes that every
turn from your relics and powers, so Ossuary asks it rather than assuming five.
When the draw pile runs shorter than your draw, the cards coming back from the
reshuffle get their own section at real odds. The damage forecast runs the
game's own calculation, so strength, vulnerable and weak are already applied.

It runs **inside** the game as a normal mod, so it reads real game state rather
than guessing at it, and it works with the window wherever you put it.

## Status

In development, and playable. Everything in v1 is in `main` and working in game;
the Workshop release is the remaining piece.

### v1 — what it does today

| Milestone | State | |
| --- | --- | --- |
| **M0** Toolchain, project skeleton, mod loads | ✅ shipped | |
| **M1** HUD shell — click-through canvas, settings, hotkeys | ✅ shipped | `F9` / `F10` |
| **M2** Bundled community data, re-banded onto a normal curve | ✅ shipped | 503 cards, 296 relics, 63 potions |
| **M3** Deck tracker with per-card draw odds | ✅ shipped | reads the real draw, not an assumed five |
| **M4** Incoming-attack forecast | ✅ shipped | the game's own damage calculation |
| **M5** Tier ratings on offers | ✅ shipped | rewards, shop, chests, ancients' blessings |
| **M6** Steam Workshop release | 🔨 in progress | packaging and release gate done; not yet published |

### v2 — in flight

Numbered separately because these are additions to a working mod rather than
steps towards one. Each is independent and can ship in any order.

| | | State |
| --- | --- | --- |
| **v2M0** Deck-conditioned advice | grades that account for the deck you actually have, from Codex's pairwise draft model | model and tests done; the harvest has never been run |
| **v2M1** Party debuff coverage | whether anyone in a co-op party is holding Vulnerable or Weak **this turn** | built, in testing |
| **v2M2** Per-panel show/hide | an `ON`/`OFF` control on each panel in layout mode, saved between sessions; the panel carrying the hotkeys stays on | built, in testing |

**[docs/ROADMAP.md](docs/ROADMAP.md)** carries the reasoning behind each — what
it is for, why it works the way it does, and what is deliberately not planned.

### Not planned

Anything that changes a rule, pool, price or outcome, and any network request
during play. Both are load-bearing for the case that this is a reading tool
rather than an assist, not merely preferences.

## Using it

| Key | |
| --- | --- |
| `F9` | hide and show the whole HUD |
| `F10` | layout mode — drag panels, click a panel's `ON`/`OFF` to show or hide it, `-` and `+` resize the text |

Panel positions, on/off state and text size are saved to
`%APPDATA%\SlayTheSpire2\ossuary.json` and survive restarts.

Panels take themselves off screen when they have nothing to say — the deck
tracker and the forecast outside combat, the party panel outside co-op — and
all of them stay visible while you are arranging the HUD, so they can be
positioned and switched back on.

## What it does not do

Ossuary **reads and displays only**. It does not change any rule, pool, price,
or outcome, and its manifest declares `affects_gameplay: false`.

It is still a mod, so runs played with it loaded are flagged as modded by the
game's own reporting. If that matters to you, don't install it.

## Requirements

- Windows, Slay the Spire 2 **v0.107.1** or later
- To build: the [.NET 9 SDK](https://dotnet.microsoft.com/download) and a local
  copy of the game

## Building

```powershell
# builds and stages to build/mods/Ossuary
.\tools\build.ps1

# builds, stages, and copies into <STS2>\mods\Ossuary
.\tools\install.ps1

# shows what Ossuary reported in the last session
.\tools\logs.ps1
```

If the game crashes, this says whether Ossuary was anywhere near it. It reads
the minidumps the game's own crash handler leaves behind and reports the
faulting module and every module on the faulting thread's stack:

```powershell
python tools\crash-report.py
```

The game install is located automatically at the default Steam path. To point
elsewhere, use any of:

```powershell
.\tools\install.ps1 -GameDir "D:\Games\Slay the Spire 2"
```

```xml
<!-- GameDir.props in the repo root — gitignored -->
<Project>
  <PropertyGroup>
    <GameDir>D:\Games\Slay the Spire 2</GameDir>
  </PropertyGroup>
</Project>
```

...or set the `OSSUARY_GAME_DIR` environment variable.

## Layout

```
src/Ossuary/           the shipped mod assembly
src/Ossuary/Data/      the bundled rating table, embedded into the DLL
src/Ossuary.Grading/   tier banding, confidence, and the table reader —
                       references nothing from the game
tests/                 unit tests for the above, runnable without the game
tools/                 build, install, log and crash-inspection scripts
tools/FetchCodexData/  build-time only: refetches and regrades the table
CHANGELOG.md           every version, and what changed in it
docs/ROADMAP.md        what is shipped, what is in flight, and why
docs/COMPAT.md         every hook and patch, and the build it was verified against
```

`Ossuary.Grading` is kept free of game references on purpose: it is the part
worth unit-testing, and keeping it independent is what lets CI verify it on a
machine that does not have the game.

## Tier ratings

Ossuary uses [Spire Codex](https://spire-codex.com) community data, but re-bands
it. Codex's published tiers put 30% of cards in F and 26% in D — more than half
the game in the bottom two grades — which makes the list useless for choosing
between two cards. Ossuary places the same scores on a normal curve instead:

```
S  6.7%    A 16.0%    B 27.3%    C 27.3%    D 16.0%    F  6.7%
```

Ratings are bundled with each release and refreshed per build. **Ossuary makes
no network requests while you play.**

Grades are global — one rating per card, relic and potion across every character
and ascension. Codex publishes per-character slices too, but their scores
saturate: 61% of cards sit at exactly 100 for Necrobinder, which collapses A and
B to nothing and grades most of the game S. That is the same defect as a
bottom-loaded tier list, merely inverted, so v1 bundles only the cohort whose
numbers can actually carry a grade.

## Acknowledgements

- **[Spire Codex](https://spire-codex.com)** — the community run data behind
  every rating Ossuary shows.
- **[Reliquary](https://github.com/ProjectBarks/reliquary)** by Brandon Barker —
  the external overlay this mod is modelled on. Several of its algorithms,
  especially the deck-tracker grouping rules, are reimplemented here.
- **[Mega Crit](https://www.megacrit.com)** — for the game, and for shipping a
  real mod loader with it.

## Disclaimer

Unofficial and not affiliated with Mega Crit or Spire Codex.

## License

[MIT](LICENSE)
