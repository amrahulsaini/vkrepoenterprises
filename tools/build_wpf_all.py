"""End-to-end per-agency Windows installer pipeline.

Run on the dev laptop:

    python tools/build_wpf_all.py

For every approved agency in crm_master.agencies:
  1. Pulls the agency-uploaded logo from /opt/vkapi/agency-uploads/<slug>.<ext>
     into a temp workspace.
  2. Generates two assets from that logo:
       - public/favicon.ico  (square-padded on white, embedded at 10 standard
         resolutions so Windows never has to stretch the taskbar icon)
       - public/crmrs-fulllogo.png  (1254x1254 with white backing)
     Both files OVERWRITE the source-tree copies in VKdesktopapp/public/ so
     `dotnet publish` picks them up as embedded resources.
  3. Writes VKdesktopapp/Resources/branding.json with slug/name/mobile/
     address/primaryColor. The Branding class reads this at runtime.
  4. Runs `dotnet publish -c Release -r win-x64 --self-contained true` into
     VKdesktopapp/publish/<slug>/, passing the per-agency AssemblyTitle and
     Product so the process name in Task Manager is also branded.
  5. Compiles installer.iss via ISCC with /D defines (AppName, AgencyGuid,
     PublishDir, OutputBaseFilename) — produces a side-by-side installer at
     installer-output/<safe_name>_Setup.exe.
  6. SCPs that installer to /opt/vkapi/agency-apps/<flavor>/setup.exe so the
     admin portal (Android Apps tab) can serve it for download.

Side-by-side install: AgencyGuid is hashed from the slug so each agency has
its own AppId, install dir, Add/Remove Programs entry, and shortcut. Two
agencies on the same Windows machine never collide.

The generic CRMS build (default Inno output with the fallback GUID) is
left untouched — run installer.iss without /D for that.

Restoration: at the end of a run the script restores the original
favicon.ico and crmrs-fulllogo.png from main-gallery/ so the source tree
isn't left with the last tenant's branding leaked in.
"""
from __future__ import annotations

import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path

# PowerShell on Windows captures stdout as cp1252 by default — force UTF-8
# so any non-ASCII char in our prints (or in agency names) doesn't crash.
if sys.platform == "win32" and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

try:
    from PIL import Image
except ImportError:
    sys.exit("[build_wpf_all] pip install pillow first")

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

SSH_HOST = "root@103.67.239.102"
SSH_PORT = "2719"
SERVER_LOGOS = "/opt/vkapi/agency-uploads"
SERVER_APPS  = "/opt/vkapi/agency-apps"

ISCC = r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
ICON_SIZES = [(16,16),(20,20),(24,24),(32,32),(40,40),
              (48,48),(64,64),(96,96),(128,128),(256,256)]
PALETTE = ["#FF6B35","#1565C0","#2E7D32","#6A1B9A",
           "#C2185B","#EF6C00","#00838F","#37474F"]


# ── helpers ─────────────────────────────────────────────────────────────────

def run(cmd: list[str], cwd: Path | None = None, check: bool = True) -> None:
    print("  $ " + " ".join(str(c) for c in cmd))
    subprocess.run(cmd, cwd=cwd, check=check)


def ssh(remote_cmd: str) -> str:
    r = subprocess.run(
        ["ssh", "-p", SSH_PORT, "-o", "BatchMode=yes", SSH_HOST, remote_cmd],
        capture_output=True, text=True, check=True)
    return r.stdout


def list_approved_agencies() -> list[dict]:
    """Pull approved agencies from crm_master via SSH+mariadb."""
    out = ssh(
        "mariadb -u root crm_master -sNe "
        "\"SELECT slug, name, COALESCE(mobile1,''), COALESCE(address,''), "
        "COALESCE(logo_path,'') FROM agencies WHERE status='approved' ORDER BY name;\"")
    rows = []
    for line in out.strip().splitlines():
        parts = line.split("\t")
        if len(parts) < 2:
            continue
        slug    = parts[0]
        name    = parts[1]
        mobile  = parts[2] if len(parts) > 2 else ""
        address = parts[3] if len(parts) > 3 else ""
        logo    = parts[4] if len(parts) > 4 else ""
        rows.append({
            "slug":         slug,
            "name":         name,
            "mobile":       mobile,
            "address":      address,
            "logoExt":      Path(logo).suffix.lstrip(".") if logo else "",
            "primaryColor": PALETTE[hash(slug) % len(PALETTE)],
        })
    return rows


def pull_logo(slug: str, hinted_ext: str = "") -> Path | None:
    """SCP the agency-uploaded logo into a temp path. Returns the path or None."""
    tmp = Path(tempfile.gettempdir()) / "crms-tenant-logos"
    tmp.mkdir(exist_ok=True)
    exts = [hinted_ext] + [e for e in ("jpg","jpeg","png","webp") if e != hinted_ext]
    for ext in [e for e in exts if e]:
        local = tmp / f"{slug}.{ext}"
        if local.exists():
            local.unlink()
        r = subprocess.run(
            ["scp", "-P", SSH_PORT, "-q", "-o", "BatchMode=yes",
             f"{SSH_HOST}:{SERVER_LOGOS}/{slug}.{ext}", str(local)],
            capture_output=True, timeout=30)
        if r.returncode == 0 and local.exists() and local.stat().st_size > 0:
            return local
        local.unlink(missing_ok=True)
    return None


def render_icon_and_logo(src_logo: Path) -> None:
    """Generate favicon.ico (multi-res) and crmrs-fulllogo.png from a source
    logo, overwriting the in-tree copies under VKdesktopapp/public/."""
    img = Image.open(src_logo).convert("RGBA")
    # Square-pad on white so the icon isn't stretched at small sizes.
    side = max(img.size)
    sq = Image.new("RGBA", (side, side), (255, 255, 255, 255))
    sq.paste(img, ((side - img.size[0]) // 2, (side - img.size[1]) // 2), img)
    sq.save(ICON_PATH, format="ICO", sizes=ICON_SIZES)
    # Login-card PNG — keep aspect ratio, 1254x1254 max canvas.
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
    """Deterministic GUID per slug — same slug always yields the same GUID."""
    h = hashlib.md5(("crms-agency:" + slug).encode("utf-8")).digest()
    return str(uuid.UUID(bytes=h)).upper()


def safe_name(name: str) -> str:
    """Filesystem-safe version of an agency name for the installer filename."""
    return "".join(c for c in name.title().replace(" ", "")
                   if c.isalnum() or c in "-_")


def publish_for(agency: dict) -> Path:
    """`dotnet publish` the WPF app for a single agency, returns publish dir."""
    out = PUBLISH_ROOT / agency["slug"]
    if out.exists():
        shutil.rmtree(out)
    run([
        "dotnet", "publish", "-c", "Release",
        "-r", "win-x64", "--self-contained", "true",
        "-p:PublishSingleFile=false",
        f'-p:AssemblyTitle={agency["name"]}',
        f'-p:Product={agency["name"]} — CRMS',
        "-o", str(out),
    ], cwd=WPF_DIR)
    return out


def compile_installer(agency: dict, publish_dir: Path) -> Path:
    """Run Inno Setup with /D defines — produces a per-tenant installer."""
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


def upload_setup(agency: dict, setup_exe: Path) -> None:
    flavor = agency["slug"].replace("_", "")
    ssh(f"mkdir -p {SERVER_APPS}/{flavor}")
    run([
        "scp", "-P", SSH_PORT, "-q", str(setup_exe),
        f"{SSH_HOST}:{SERVER_APPS}/{flavor}/setup.exe",
    ])


def restore_generic_assets() -> None:
    """Regenerate the in-tree icon + full-logo from the generic CRMRS brand
    file so the source tree isn't left in the last tenant's state."""
    if not GENERIC_LOGO_SRC.exists():
        return
    render_icon_and_logo(GENERIC_LOGO_SRC)
    BRANDING_PATH.unlink(missing_ok=True)


# ── main ────────────────────────────────────────────────────────────────────

def main() -> None:
    agencies = list_approved_agencies()
    if not agencies:
        sys.exit("[build_wpf_all] no approved agencies — nothing to build.")
    print(f"[build_wpf_all] {len(agencies)} approved agencies\n")

    for a in agencies:
        print(f"=== {a['name']}  (slug={a['slug']}) ===")
        logo = pull_logo(a["slug"], a.get("logoExt", ""))
        if logo is None:
            print(f"  [warn] no uploaded logo - falling back to generic CRMRS brand")
            logo = GENERIC_LOGO_SRC
        render_icon_and_logo(logo)
        write_branding(a)

        publish_dir = publish_for(a)
        setup_exe   = compile_installer(a, publish_dir)
        if not setup_exe.exists():
            print(f"  [fail] installer missing: {setup_exe}")
            continue
        print(f"  [ok]  installer: {setup_exe.name}  ({setup_exe.stat().st_size/1024/1024:.1f} MB)")
        upload_setup(a, setup_exe)

    restore_generic_assets()
    print(f"\n[build_wpf_all] done — {len(agencies)} agency installers built + uploaded.")


if __name__ == "__main__":
    main()
