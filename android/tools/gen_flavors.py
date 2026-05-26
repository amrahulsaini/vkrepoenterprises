"""Generate per-flavor launcher icons for every tenant in tenants.json.

For each tenant:
  - Look for a custom logo at tenant-logos/<slug>.png|jpg
  - Fall back to app/src/main/res/drawable/crmrs_logo.png
  - Resize to 48/72/96/144/192 px and write
       app/src/<slug-flavor>/res/mipmap-{m,h,xh,xxh,xxxh}dpi/ic_launcher.png

Run from the android/ directory:
    python tools/gen_flavors.py

Re-run any time tenants.json changes or a logo is added/replaced.
"""
import json
import os
import subprocess
import sys
from pathlib import Path
from PIL import Image

# Optional: pull agency-uploaded logos from the production server before
# rendering. Set CRMS_PULL_LOGOS=1 to enable (uses ssh on port 2719).
SSH_HOST = "root@103.67.239.102"
SSH_PORT = "2719"
SERVER_LOGOS = "/opt/vkapi/agency-uploads"

ROOT          = Path(__file__).resolve().parent.parent
TENANTS_JSON  = ROOT / "tenants.json"
LOGOS_DIR     = ROOT / "tenant-logos"
DEFAULT_LOGO_DIR = ROOT / "app" / "src" / "main" / "res" / "drawable"
APP_SRC       = ROOT / "app" / "src"


def default_logo() -> Path:
    for ext in ("png", "webp", "jpg", "jpeg"):
        p = DEFAULT_LOGO_DIR / f"crmrs_logo.{ext}"
        if p.exists():
            return p
    sys.exit(f"[gen_flavors] no default logo found in {DEFAULT_LOGO_DIR}")

DENSITIES = [
    ("mdpi",     48),
    ("hdpi",     72),
    ("xhdpi",    96),
    ("xxhdpi",  144),
    ("xxxhdpi", 192),
]


def flavor_dir(slug: str) -> str:
    """Gradle product-flavor names cannot have underscores in source-set dirs
    without quoting headaches — strip underscores."""
    return slug.replace("_", "")


def fetch_from_server(slug: str) -> Path | None:
    """Try to scp the agency-uploaded logo from the production server. Returns
    the local path on success, None if the file does not exist remotely."""
    if os.environ.get("CRMS_PULL_LOGOS") != "1":
        return None
    for ext in ("jpg", "jpeg", "png", "webp"):
        remote = f"{SSH_HOST}:{SERVER_LOGOS}/{slug}.{ext}"
        local  = LOGOS_DIR / f"{slug}.{ext}"
        try:
            r = subprocess.run(
                ["scp", "-P", SSH_PORT, "-q", "-o", "BatchMode=yes", remote, str(local)],
                capture_output=True, timeout=15)
            if r.returncode == 0 and local.exists() and local.stat().st_size > 0:
                return local
            if local.exists():
                local.unlink(missing_ok=True)
        except (FileNotFoundError, subprocess.TimeoutExpired):
            return None
    return None


def find_logo(slug: str) -> tuple[Path, str]:
    # 1. Local override (placeholder or hand-supplied)
    for ext in ("png", "jpg", "jpeg", "webp"):
        p = LOGOS_DIR / f"{slug}.{ext}"
        if p.exists():
            return p, "local file"
    # 2. Try to pull from production
    server = fetch_from_server(slug)
    if server is not None:
        return server, "pulled from server"
    # 3. Default CRMS logo
    return default_logo(), "default CRMS logo"


def render_icons(logo_path: Path, flavor: str) -> None:
    # Open + force RGBA so transparent PNGs keep alpha and JPGs work.
    img = Image.open(logo_path).convert("RGBA")
    for density, px in DENSITIES:
        dst_dir = APP_SRC / flavor / "res" / f"mipmap-{density}"
        dst_dir.mkdir(parents=True, exist_ok=True)
        # Square-fit on a white backing so wide logos do not get squashed and
        # transparent PNGs do not show as black on the launcher.
        canvas = Image.new("RGBA", (px, px), (255, 255, 255, 255))
        scaled = img.copy()
        scaled.thumbnail((px, px), Image.LANCZOS)
        ox = (px - scaled.width) // 2
        oy = (px - scaled.height) // 2
        canvas.paste(scaled, (ox, oy), scaled if scaled.mode == "RGBA" else None)
        canvas.convert("RGB").save(dst_dir / "ic_launcher.png", "PNG", optimize=True)
    # Also emit a higher-res, non-square version for in-app UI use
    # (top bar, landing panel). Preserves the original aspect ratio so wide
    # wordmark-style logos do not get cropped.
    drawable_dir = APP_SRC / flavor / "res" / "drawable"
    drawable_dir.mkdir(parents=True, exist_ok=True)
    big = img.copy()
    big.thumbnail((512, 512), Image.LANCZOS)
    # White backing — same reason as the launcher icon, but kept rectangular.
    rect = Image.new("RGBA", big.size, (255, 255, 255, 255))
    rect.paste(big, (0, 0), big if big.mode == "RGBA" else None)
    rect.convert("RGB").save(drawable_dir / "agency_logo.png", "PNG", optimize=True)


def main() -> None:
    if not TENANTS_JSON.exists():
        sys.exit(f"[gen_flavors] {TENANTS_JSON} not found")
    tenants = json.loads(TENANTS_JSON.read_text(encoding="utf-8"))
    LOGOS_DIR.mkdir(exist_ok=True)
    for t in tenants:
        slug      = t["slug"]
        flavor    = flavor_dir(slug)
        logo, src = find_logo(slug)
        render_icons(logo, flavor)
        print(f"  {slug:20s}  flavor={flavor:18s}  icon=({src})")
    print(f"\n[gen_flavors] generated icons for {len(tenants)} tenants")


if __name__ == "__main__":
    main()
