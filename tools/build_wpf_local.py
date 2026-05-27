"""Per-agency Windows installer build — local-only variant.

Used when the dev laptop can't SSH/SCP to the server with key auth (we
upload the produced installers via pscp/plink separately). Reads agency
metadata from a hardcoded list and pulls logos from a local staging dir
(populated up-front), so the build itself has no network dependency.

Run:

    python tools/build_wpf_local.py
"""
from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import uuid
import zipfile
from pathlib import Path

if sys.platform == "win32" and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

try:
    from PIL import Image
except ImportError:
    sys.exit("[build_wpf_local] pip install pillow first")

ROOT             = Path(__file__).resolve().parent.parent
WPF_DIR          = ROOT / "VKdesktopapp"
PUBLIC_DIR       = WPF_DIR / "public"
RESOURCES_DIR    = WPF_DIR / "Resources"
ICON_PATH        = PUBLIC_DIR / "favicon.ico"
FULL_LOGO_PATH   = PUBLIC_DIR / "crmrs-fulllogo.png"
BRANDING_PATH    = RESOURCES_DIR / "branding.json"
PUBLISH_ROOT     = WPF_DIR / "publish"
INSTALLER_OUTPUT = ROOT / "installer-output"
GENERIC_LOGO_SRC = ROOT / "main-gallery" / "crmrs-fulllogo.webp"
# pre-populated via pscp - uses the OS-correct temp dir (Windows: %TEMP%,
# Linux/Mac: /tmp) so a hardcoded /tmp doesn't silently fall back to the
# generic logo on Windows where /tmp resolves to C:\tmp.
LOGO_STAGING     = Path(tempfile.gettempdir()) / "crms-build-logos"

ISCC = r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
ICON_SIZES = [(16,16),(20,20),(24,24),(32,32),(40,40),
              (48,48),(64,64),(96,96),(128,128),(256,256)]

# Hardcoded agency list — matches crm_master.agencies (status=approved).
AGENCIES = [
    {
        "slug":         "v_k_enterprises",
        "name":         "V K ENTERPRISES",
        "mobile":       "9850637363",
        "address":      "SATARA",
        "primaryColor": "#1565C0",
        "logo_file":    "v_k_enterprises.jpg",
    },
    {
        "slug":         "rk_enterprises",
        "name":         "RK ENTERPRISES",
        "mobile":       "6377362603",
        "address":      "Jaipur",
        "primaryColor": "#C2185B",
        "logo_file":    "rk_enterprises.jpg",
    },
]


def run(cmd: list[str], cwd: Path | None = None) -> None:
    print("  $ " + " ".join(str(c) for c in cmd))
    subprocess.run(cmd, cwd=cwd, check=True)


def render_icon_and_logo(src_logo: Path) -> None:
    img = Image.open(src_logo).convert("RGBA")
    side = max(img.size)
    sq = Image.new("RGBA", (side, side), (255, 255, 255, 255))
    sq.paste(img, ((side - img.size[0]) // 2, (side - img.size[1]) // 2), img)
    sq.save(ICON_PATH, format="ICO", sizes=ICON_SIZES)
    big = img.copy()
    big.thumbnail((1254, 1254), Image.LANCZOS)
    bg = Image.new("RGBA", big.size, (255, 255, 255, 255))
    bg.paste(big, (0, 0), big)
    bg.convert("RGB").save(FULL_LOGO_PATH, "PNG", optimize=True)


def write_branding(agency: dict) -> None:
    RESOURCES_DIR.mkdir(exist_ok=True)
    BRANDING_PATH.write_text(json.dumps({
        "slug":         agency["slug"],
        "name":         agency["name"],
        "mobile":       agency["mobile"],
        "address":      agency["address"],
        "primaryColor": agency["primaryColor"],
    }, indent=2), encoding="utf-8")


def agency_guid(slug: str) -> str:
    h = hashlib.md5(("crms-agency:" + slug).encode("utf-8")).digest()
    return str(uuid.UUID(bytes=h)).upper()


def safe_name(name: str) -> str:
    return "".join(c for c in name.title().replace(" ", "")
                   if c.isalnum() or c in "-_")


def publish_for(agency: dict) -> Path:
    out = PUBLISH_ROOT / agency["slug"]
    if out.exists():
        shutil.rmtree(out)
    run([
        "dotnet", "publish", "-c", "Release",
        "-r", "win-x64", "--self-contained", "true",
        "-p:PublishSingleFile=false",
        f'-p:AssemblyTitle={agency["name"]}',
        f'-p:Product={agency["name"]} - CRMS',
        "-o", str(out),
    ], cwd=WPF_DIR)
    return out


def compile_installer(agency: dict, publish_dir: Path) -> Path:
    sname        = safe_name(agency["name"])
    output_base  = f"{sname}_Setup"
    guid         = agency_guid(agency["slug"])
    run([
        ISCC, str(ROOT / "installer.iss"),
        f"/DAppName={agency['name']}",
        f"/DAgencyGuid={guid}",
        f"/DPublishDir={publish_dir.relative_to(ROOT)}",
        f"/DOutputBaseFilename={output_base}",
    ], cwd=ROOT)
    return INSTALLER_OUTPUT / f"{output_base}.exe"


def package_portable_zip(agency: dict, publish_dir: Path) -> Path:
    """Zip the self-contained publish folder for portable distribution.

    Corporate Windows machines with AppLocker / WDAC block unsigned setup.exe
    extraction to %TEMP% (error 4551). The publish folder bundles the whole
    .NET runtime, so end users can just extract the zip anywhere and
    double-click CRMRS.exe — no installer, no admin rights, no
    SmartScreen warning."""
    sname    = safe_name(agency["name"])
    zip_name = f"{sname}_Portable.zip"
    zip_path = INSTALLER_OUTPUT / zip_name
    zip_path.unlink(missing_ok=True)
    INSTALLER_OUTPUT.mkdir(parents=True, exist_ok=True)
    print(f"  $ zipping {publish_dir.name} -> {zip_name}")
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
        for root, _dirs, files in publish_dir.walk() if hasattr(publish_dir, "walk") else _walk_legacy(publish_dir):
            for f in files:
                full = Path(root) / f
                rel  = full.relative_to(publish_dir.parent)
                zf.write(full, rel)
    return zip_path


def _walk_legacy(p: Path):
    """os.walk fallback for Python < 3.12 (Path.walk was added in 3.12)."""
    import os
    for r, d, f in os.walk(p):
        yield Path(r), d, f


def restore_generic_assets() -> None:
    if not GENERIC_LOGO_SRC.exists():
        return
    render_icon_and_logo(GENERIC_LOGO_SRC)
    BRANDING_PATH.unlink(missing_ok=True)


def main() -> None:
    print(f"[build_wpf_local] {len(AGENCIES)} agencies to build\n")

    built: list[tuple[dict, Path]] = []
    for a in AGENCIES:
        print(f"=== {a['name']}  (slug={a['slug']}) ===")
        logo = LOGO_STAGING / a["logo_file"]
        if not logo.exists():
            print(f"  [warn] {logo} missing - falling back to generic CRMRS brand")
            logo = GENERIC_LOGO_SRC
        render_icon_and_logo(logo)
        write_branding(a)

        publish_dir = publish_for(a)
        setup_exe   = compile_installer(a, publish_dir)
        if not setup_exe.exists():
            print(f"  [fail] installer missing: {setup_exe}")
            continue
        size_mb = setup_exe.stat().st_size / 1024 / 1024
        print(f"  [ok]  installer: {setup_exe.name}  ({size_mb:.1f} MB)")
        # Portable zip — same publish folder, no installer needed at the
        # end-user end. Distribute this when the user's PC blocks unsigned
        # installers (AppLocker / WDAC / SmartScreen).
        portable_zip = package_portable_zip(a, publish_dir)
        zip_mb = portable_zip.stat().st_size / 1024 / 1024
        print(f"  [ok]  portable: {portable_zip.name}  ({zip_mb:.1f} MB)\n")
        built.append((a, setup_exe))

    restore_generic_assets()

    print(f"[build_wpf_local] done - {len(built)} installer(s) produced:")
    for a, p in built:
        print(f"  - {a['name']:25s} -> {p}")


if __name__ == "__main__":
    main()
