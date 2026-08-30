"""Generates the Steam Workshop preview image.

Committed so the image can be regenerated rather than hand-edited: the Workshop
thumbnail is the first thing anyone sees, and it should stay in step with what
the mod actually draws. Steam caps preview images at 1 MB.

    python tools/make-preview.py

Writes workshop/image.png.
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

SIZE = 512
ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "workshop" / "image.png"

# The HUD's own palette, so the thumbnail looks like the thing you install.
BG = (14, 18, 17)
PANEL = (13, 16, 15)
BORDER = (43, 122, 108)
TEAL = (107, 199, 179)
INK = (227, 232, 228)
DIM = (130, 140, 135)
AMBER = (209, 161, 89)

FONTS = Path("C:/Windows/Fonts")


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / name), size)


def main() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)

    img = Image.new("RGB", (SIZE, SIZE), BG)
    d = ImageDraw.Draw(img)

    title = font("seguisb.ttf", 58)
    sub = font("segoeui.ttf", 20)
    mono = font("consola.ttf", 19)
    mono_small = font("consola.ttf", 17)
    foot = font("segoeui.ttf", 18)

    # A faint vertical gradient stops the flat background looking unfinished.
    for y in range(SIZE):
        shade = int(6 * (1 - y / SIZE))
        d.line([(0, y), (SIZE, y)], fill=(BG[0] + shade, BG[1] + shade, BG[2] + shade))

    d.text((40, 44), "OSSUARY", font=title, fill=TEAL)
    d.text((44, 112), "Slay the Spire 2", font=sub, fill=DIM)

    # A mock of the deck panel — the mod's most recognisable surface.
    panel = (40, 160, SIZE - 40, 356)
    d.rounded_rectangle(panel, radius=6, fill=PANEL, outline=BORDER, width=1)

    d.text((60, 176), "DRAW PILE  8   ·   draws 6", font=mono_small, fill=TEAL)

    rows = [
        ("1", "Defend", "x3", "100%"),
        ("1", "Strike", "x2", "96%"),
        ("2", "Bash", "", "75%"),
        ("1", "Breakthrough", "", "75%"),
    ]
    y = 206
    for cost, name, count, odds in rows:
        d.text((60, y), cost, font=mono, fill=DIM)
        d.text((86, y), name, font=mono, fill=INK)
        d.text((262, y), count, font=mono, fill=DIM)
        d.text((SIZE - 60 - d.textlength(odds, font=mono), y), odds, font=mono, fill=INK)
        y += 26

    d.text((60, 322), "Attack 4   Skill 3   Status 1", font=mono_small, fill=DIM)

    # A rating badge, the M5 surface.
    badge = (40, 376, SIZE - 40, 424)
    d.rounded_rectangle(badge, radius=6, fill=PANEL, outline=BORDER, width=1)
    d.text((60, 390), "S  92", font=mono, fill=(115, 217, 158))
    d.text((132, 390), "·  62.3% win  ·  30.1% pick", font=mono, fill=DIM)

    d.text((44, 448), "draw odds  ·  incoming damage  ·  tier ratings", font=foot, fill=AMBER)

    img.save(OUT, "PNG", optimize=True)
    kb = OUT.stat().st_size / 1024
    print(f"wrote {OUT.relative_to(ROOT)}  {SIZE}x{SIZE}  {kb:.0f} KB")
    if kb > 1024:
        raise SystemExit("image exceeds Steam's 1 MB limit")


if __name__ == "__main__":
    main()
