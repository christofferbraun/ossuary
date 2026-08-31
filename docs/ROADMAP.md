# Roadmap

What is shipped, what is in flight, and what the milestone names mean.

## V1 — shipped

| | |
| --- | --- |
| M0 | Project skeleton, toolchain, a mod that loads |
| M1 | HUD: a CanvasLayer parented into the run, F9 toggle, F10 layout mode |
| M2 | Bundled Codex ratings, re-banded onto a normal curve |
| M3 | Draw pile tracker with per-card draw odds |
| M4 | Incoming damage forecast from the game's own damage pipeline |
| M5 | Community ratings on the cards, relics and potions you are offered |
| M6 | Steam Workshop packaging and release gate — *in flight, not yet published* |

## V2 — in flight

Numbered separately because these are additions to a working mod rather than
steps towards one. They are independent of each other and can ship in any
order.

### v2M0 — deck-conditioned advice

Branch: `v2-deck-advice`

Rating a card against the whole population ignores the deck you are actually
building. Codex publishes a pairwise model behind its draft advice; harvesting
it lets a card be scored against what you already hold rather than against
everybody's average run.

Status: the model and its tests exist. The harvest has never been run against
a live API — see `docs/V2.md` on that branch for the request-count argument and
what is still unverified.

### v2M1 — party debuff coverage

Branch: `v2m1-team-afflictions`

Whether anyone in a co-op party is holding a way to apply Vulnerable or Weak
**this turn**, per player and for the party as a whole.

The problem it solves is one you cannot see the answer to: whether a teammate
has *drawn* it. Everybody assumes somebody else has it, nobody can read three
other hands, and the turn gets planned around a debuff that never lands.

**The hand, not the deck.** "Somebody owns a card that applies Vulnerable" is
true all run and useful on almost none of the turns in it. Sources are the
cards in each player's hand right now, plus the potions in their belt. Relics
are out: one that applied Weak at the start of combat has already done it,
which is a state the enemy is in rather than something a player can choose.
That makes the panel combat-only — outside a fight there is no hand and so no
question.

**Why only these two.** They are the debuffs the whole party benefits from and
the two a run can plausibly end with nobody carrying — both are concentrated in
a handful of cards and relics rather than spread across the pool. Strength or
Block are not comparable; everyone has some.

**How a source is recognised.** From the game's declarations, never from card
text — text would break in every language but English and would confuse "Weak"
with "Weakened". Two independent signals, either sufficient:

- `IHoverTip.CanonicalModel`. Every model that applies a power declares a hover
  tip for it, because that tip is the tooltip the player reads, and the tip
  names the model it is for.
- `PowerVar<T>` registers itself under `typeof(T).Name`, so a model's dynamic
  vars name the powers it applies. This catches anything that applies a power
  without declaring a tip.

Neither is a list of card ids maintained here, so a card added in a patch is
recognised without Ossuary changing.

**Potions are not a yes.** A potion is an escape hatch that is gone once used,
so it answers a different question from a card sitting in hand. Reported as
`potion` in its own colour rather than folded into the answer.

**Single player.** Hidden by default: alone there is no hand you cannot see,
and your own is on screen. `TeamPanelInSinglePlayer` turns it on.

### v2M2 — per-panel show/hide

Branch: `v2m2-panel-toggles`

An `ON`/`OFF` control on every panel while layout mode is open. Clicking it
hides that panel; the state is saved beside the panel's position and restored
next session.

**A hidden panel is still shown while arranging.** Otherwise the only control
for turning it back on would be inside the thing that is off.

**And it does no work at all when hidden.** Switching a panel off usually means
not wanting it to cost anything either, so a hidden panel returns before its
update runs rather than updating and drawing nothing.

**Why the control is not a child of the panel.** Every panel root is a
`PanelContainer`, and a container lays out all of its children — a second child
is stretched to fill the panel, which would have made the whole panel one large
button and eaten every drag. So it lives on the HUD layer beside the panels and
is positioned from the panel's own rect, the same approach the offer badges
take, and is repositioned while dragging, on rescale, and every frame in layout
mode, since a panel resizes as its contents change.

It is a `Label`, not a `Button`: the HUD stays `MouseFilter.Ignore` throughout
and clicks are hit-tested in `HudController._Input`, so no part of the HUD can
swallow input meant for the game.

An older settings file has no `hidden` key, which deserialises to false, so
upgrading cannot silently switch a panel off.

## Not planned

- Anything that changes a rule, a pool, a price or an outcome. Ossuary declares
  `affects_gameplay: false` and that is the whole basis of its compliance case —
  see `COMPLIANCE.md`.
- Network requests during play. Ratings are bundled per build.
