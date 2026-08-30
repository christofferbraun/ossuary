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

---

Ossuary shows you the numbers you would otherwise have to remember: what is left
in your draw pile and how likely you are to see it, how much damage is coming
this turn, and how the community has actually performed with the card in front
of you.

It runs **inside** the game as a normal mod, so it reads real game state rather
than guessing at it, and it works with the window wherever you put it.

## Status

In development. Nothing is playable yet.

| Milestone | State |
| --- | --- |
| **M0** Toolchain, project skeleton, mod loads | ✅ done |
| **M1** HUD shell — click-through canvas, settings, hotkey | ✅ done |
| **M2** Bundled community data + normal-curve tiers | planned |
| **M3** Deck tracker | planned |
| **M4** Attack forecast | planned |
| **M5** Tier ratings on offers | planned |
| **M6** Workshop release | planned |

Deck-*conditioned* advice — grades that account for the deck you actually have —
is planned for v2.

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
src/Ossuary.Grading/   tier banding and confidence — references nothing from the game
tests/                 unit tests for the above, runnable without the game
tools/                 build, install, and log scripts
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
