"""Renders a placeholder logo for every tenant in tenants.json — a colored
circle with the agency's initials in the center. Output goes into
android/tenant-logos/<slug>.png so gen_flavors.py picks it up.

Used as a fallback for agencies that have not yet uploaded a real logo.
Delete tenant-logos/<slug>.png any time to revert that tenant to the
default CRMS logo.
"""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT  = Path(__file__).resolve().parent.parent
TJSON = ROOT / "tenants.json"
OUT   = ROOT / "tenant-logos"
OUT.mkdir(exist_ok=True)


def hex_to_rgb(h: str) -> tuple[int, int, int]:
    h = h.lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


def initials(name: str) -> str:
    parts = [p for p in name.replace("_", " ").split() if p]
    return "".join(p[0].upper() for p in parts)[:3] or "?"


def render(slug: str, name: str, color_hex: str) -> Path:
    px = 512
    img = Image.new("RGBA", (px, px), (255, 255, 255, 0))
    draw = ImageDraw.Draw(img)
    # Filled colored circle
    draw.ellipse((0, 0, px, px), fill=hex_to_rgb(color_hex))
    # Initials in white, centered
    text = initials(name)
    try:
        font = ImageFont.truetype("arialbd.ttf", int(px * 0.42))
    except OSError:
        font = ImageFont.load_default()
    box = draw.textbbox((0, 0), text, font=font)
    tw, th = box[2] - box[0], box[3] - box[1]
    draw.text(((px - tw) // 2 - box[0], (px - th) // 2 - box[1]), text,
              fill=(255, 255, 255, 255), font=font)
    p = OUT / f"{slug}.png"
    img.save(p, "PNG", optimize=True)
    return p


def main() -> None:
    tenants = json.loads(TJSON.read_text(encoding="utf-8"))
    for t in tenants:
        p = render(t["slug"], t["name"], t.get("primaryColor", "#FF6B35"))
        print(f"  {t['slug']:20s}  {t['primaryColor']:8s}  {initials(t['name']):4s}  -> {p}")


if __name__ == "__main__":
    main()
