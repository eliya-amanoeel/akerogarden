#!/usr/bin/env python3
"""Regenerate the Åkerögården logo family in wwwroot/logo/.

    pip install fonttools brotli          # brotli is required to read .woff2
    python docs/ux/logo-generator.py

Not part of the build — the SVGs it writes are the committed artefacts. It exists so the
lockups can be rebuilt (new tagline, new colour, retuned spacing) without redrawing the
mark by hand, and so the reasoning behind each number survives outside a chat log.

Two things drive the whole design and are easy to undo by accident:

1. WORDMARKS ARE BAKED TO OUTLINES, not <text>. The lockups are loaded through <img>,
   which renders SVG in a sandboxed mode: no page CSS, no webfonts, no external anything.
   A font-family in there resolves against whatever the viewer's OS happens to have, so
   the pre-2026-08 lockup asked for Cormorant Garamond and silently got Georgia on macOS
   and something generic on Linux. Outlines render identically everywhere.

2. THE HORIZONTAL BOX IS 52 UNITS TALL, AND THAT IS LOAD-BEARING. It renders at a CSS
   `height: 55px`, so every unit of vertical padding in the viewBox shrinks the ink. At
   the old 80 units the tagline came out at 4.5px — invisible. At 52 it lands at 6.9px.
   Horizontal padding is trimmed just as hard (4 units of edge) because at 1024px, where
   the public header swaps its hamburger for the inline nav, the row only has ~66px to
   spare. Grow either and you take it out of the navigation.

The mark is a redraw traced from the client's PNG (2026-08), not a vector conversion.
"""
import os
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.misc.transform import Transform

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
FONTS = os.path.join(ROOT, "src/AkeroGarden.Web.Core/wwwroot/fonts")
OUT = os.path.join(ROOT, "src/AkeroGarden.Web.Core/wwwroot/logo")
PF_REG = f"{FONTS}/playfair-display-regular.woff2"
RB_BLD = f"{FONTS}/roboto-bold.woff2"

# ── Palette ──────────────────────────────────────────────────────────────────────────
# Gold is aligned to --color-secondary; the client's ochre was an off-palette #b5822a.
GOLD = "#865628"
# Teal is the client's own and stays as the mark's signature — a logo is exempt from the
# WCAG 1.4.3 / 1.4.11 contrast minimums (both carve out logotypes explicitly).
TEAL = "#3a9698"
# The tagline inherits that exemption but does NOT use #3a9698: at 6.9px it measures
# 3.30:1 on the off-white and 2.95:1 on the warm tint, and "older eyes, phones, rural
# connections" is a stated design principle. #1e7070 is the same hue at 5.49 / 4.90.
TEAL_INK = "#1e7070"
# On the #26221d sidebar the gold falls to ~3.5:1 and reads as mud, so the dark cut
# re-tones rather than recolours: warm off-white building, lifted teal (6.86:1 on nav).
GOLD_D, TEAL_D = "#f0ebe1", "#5cb9bb"

MARK_W, MARK_H = 100.0, 90.0
ALT = "Åkerögården — Kaldéiska Kursgård"
SUB = "KALDÉISKA KURSGÅRD"

# Cupped hand. What makes this read as a hand rather than a wave is the DEPTH OF THE WEB
# NOTCH between palm and thumb, not the thumb itself — an earlier pass put the notch only
# 2.8 units below the palm edge and the thumb dissolved back into the crescent. Here it
# drops ~5 units at x≈63 and the thumb tip sits 17 above it, so the forms separate at 55px.
HAND = """M12.7,67.0
      C21.0,74.5 28.0,78.5 36.0,79.5
      C44.0,80.3 51.0,78.0 57.0,74.3
      C60.0,72.4 62.0,75.8 63.6,79.4
      C66.2,84.0 70.5,80.0 73.8,74.5
      C77.0,69.2 79.5,64.3 82.0,61.8
      C84.6,59.2 88.3,60.8 88.4,65.4
      C88.5,72.0 86.5,80.2 81.0,84.6
      C72.0,91.0 56.0,89.6 44.0,88.0
      C30.0,86.1 19.0,78.6 12.7,67.0 Z"""

# Roof band, held at ~5 units through mid-span and tapered only at the eaves. Let the two
# edge curves converge earlier and the roof renders as a hairline at header size.
ROOF = """M50,22.0 C36,28.0 12,41.0 0,45.2
      C11,42.8 30,37.0 50,30.0
      C70,37.0 89,42.8 100,45.2
      C88,41.0 64,28.0 50,22.0 Z"""


def text_to_path(font_path, text, size, letter_spacing=0.0, x=0.0, y=0.0):
    """Return (svg path 'd', advance width). y is the BASELINE in SVG coordinates."""
    font = TTFont(font_path)
    upem = font["head"].unitsPerEm
    cmap = font.getBestCmap()
    glyphset = font.getGlyphSet()
    kern = {}
    if "kern" in font:
        for st in font["kern"].kernTables:
            kern.update(st.kernTable)

    scale = size / upem
    pen = SVGPathPen(glyphset, ntos=lambda v: f"{v:.2f}")
    cursor, prev = 0.0, None
    for ch in text:
        if ord(ch) not in cmap:
            raise SystemExit(f"glyph missing for {ch!r} in {os.path.basename(font_path)}")
        gname = cmap[ord(ch)]
        if prev is not None:
            cursor += kern.get((prev, gname), 0)
        # font units are y-up, SVG is y-down, hence the negative y scale
        glyphset[gname].draw(TransformPen(
            pen, Transform(scale, 0, 0, -scale, x + cursor * scale, y)))
        cursor += glyphset[gname].width + (letter_spacing / scale)
        prev = gname
    return pen.getCommands(), cursor * scale


def word(text, size, ls, x, baseline, fill, font=None):
    d, w = text_to_path(font or PF_REG, text, size, ls, x, baseline)
    return f'<path fill="{fill}" d="{d}"/>', w


def mark(gold, teal, x=0.0, y=0.0, h=MARK_H, simple=False):
    """The church-in-a-hand mark, scaled to height h and placed at (x, y).

    simple=True is the favicon cut. The 2x2 window becomes four sub-pixel smudges below
    ~32px, so it goes, and what survives is fattened to hold the silhouette. Detail that
    cannot be resolved does not read as detail; it reads as dirt on the roofline.
    """
    s = h / MARK_H
    window = "" if simple else (
        '<path d="M45.4,34h4.2v4.4h-4.2z"/><path d="M50.4,34h4.2v4.4h-4.2z"/>'
        '<path d="M45.4,39.2h4.2v4.4h-4.2z"/><path d="M50.4,39.2h4.2v4.4h-4.2z"/>')
    cw, cb = (3.6, 8.6) if simple else (2.6, 7.1)     # cross + crossbar half-widths
    ww = 7.0 if simple else 5.5                       # wall thickness
    dx, dr = (38.6, 11.4) if simple else (40.8, 9.2)  # door left edge, arch radius
    return f"""<g transform="translate({x:.2f},{y:.2f}) scale({s:.5f})">
    <g fill="{gold}">
      <path d="M{50 - cw},0.6h{cw * 2}v23.5h-{cw * 2}z"/>
      <path d="M{50 - cb},5.0h{cb * 2}v5.4h-{cb * 2}z"/>
      <path d="{ROOF}"/>
      <path d="M19.4,38h{ww}v34h-{ww}z"/>
      <path d="M{80.6 - ww},38h{ww}v34h-{ww}z"/>
      {window}
      <path d="M{dx},69V57.6 A{dr},{dr} 0 0 1 {100 - dx},57.6 V69 Z"/>
    </g>
    <path fill="{teal}" d="{HAND}"/>
  </g>"""


def horizontal(gold, teal, ink, rule, sub):
    """Header / admin-sidebar lockup: mark left, two-line wordmark right, hairline divider.

    Tagline is half the wordmark's size — the conventional lockup-subline ratio. Larger and
    the two lines start competing for the same voice. See the module docstring before
    changing any padding here; the 52-unit box and 4-unit edges are both load-bearing.
    """
    mh = 40.0
    mw = mh / MARK_H * MARK_W
    mx, my = 4.0, 6.0
    div = mx + mw + 10
    tx = div + 11
    n1, w1 = word("Åkerögården", 13, 3.9, tx, 26.5, ink)
    n2, w2 = word(SUB, 6.5, 1.3, tx, 34.8, sub, font=RB_BLD)
    w = round(tx + max(w1, w2) + 4)
    return f"""<svg width="{w}" height="52" viewBox="0 0 {w} 52" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="{ALT}">
{mark(gold, teal, mx, my, mh)}
  <line x1="{div:.0f}" y1="12" x2="{div:.0f}" y2="40" stroke="{rule}" stroke-width="1" opacity="0.4"/>
  {n1}
  {n2}
</svg>"""


def stacked(gold, teal, ink, sub):
    """Login-card lockup: the client's stacked composition, both names centred.

    Its box is 250x156, a completely different aspect from the horizontal lockup — which is
    why .login-card__logo .logo carries its own height (104px) instead of the shared 55px.
    """
    mh, w = 92.0, 250.0
    mw = mh / MARK_H * MARK_W
    n1, w1 = word("Åkerögården", 17, 4.6, 0, 129, ink)
    n2, w2 = word(SUB, 8.0, 1.8, 0, 145, sub, font=RB_BLD)
    return f"""<svg width="{w:.0f}" height="156" viewBox="0 0 {w:.0f} 156" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="{ALT}">
{mark(gold, teal, (w - mw) / 2, 10, mh)}
  <g transform="translate({(w - w1) / 2:.2f},0)">{n1}</g>
  <g transform="translate({(w - w2) / 2:.2f},0)">{n2}</g>
</svg>"""


def mark_only(gold, teal, simple=False):
    return (f'<svg width="100" height="90" viewBox="0 0 100 90" '
            f'xmlns="http://www.w3.org/2000/svg" role="img" aria-label="Åkerögården">\n'
            f'{mark(gold, teal, simple=simple)}\n</svg>')


FILES = {
    "lockup-transparent.svg": horizontal(GOLD, TEAL, "#1e1c1a", "#1e1c1a", TEAL_INK),
    "lockup-dark.svg":        horizontal(GOLD_D, TEAL_D, "#ffffff", "#ffffff", TEAL_D),
    "lockup-stacked.svg":     stacked(GOLD, TEAL, "#1e1c1a", TEAL_INK),
    "mark-light.svg":         mark_only(GOLD, TEAL),
    "mark-dark.svg":          mark_only(GOLD_D, TEAL_D),
    "mark-favicon.svg":       mark_only(GOLD, TEAL, simple=True),
}

if __name__ == "__main__":
    for name, svg in FILES.items():
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as fh:
            fh.write(svg)
        print(f"  {name:24} {len(svg):6d} B")
    # favicon.png is rendered from mark-favicon.svg centred on a #faf8f4 square (a thin
    # transparent gold mark vanishes against dark browser chrome). Not automated here —
    # any SVG-to-PNG rasteriser at 512x512 will do.
    print("\n  favicon.png is NOT regenerated by this script — see the comment above.")
