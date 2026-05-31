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
import os
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
# Agency logos are pre-staged here via pscp before the build. We search a few
# candidate dirs because the temp dir Python sees depends on who launches the
# build: an interactive shell resolves %TEMP% to ...\Local\Temp, but some task
# runners inject a sandboxed TEMP (e.g. ...\Local\Temp\claude). If we only
# trusted tempfile.gettempdir() we'd silently miss the logos and fall back to
# the generic CRMRS brand for EVERY agency. First dir that actually contains
# logos wins.
def _find_logo_staging() -> Path:
    candidates = [
        Path(tempfile.gettempdir()) / "crms-build-logos",
        Path(os.environ.get("LOCALAPPDATA", "")) / "Temp" / "crms-build-logos",
        Path.home() / "AppData" / "Local" / "Temp" / "crms-build-logos",
        Path(os.environ.get("TEMP", "")) / "crms-build-logos",
        Path(os.environ.get("TMP", "")) / "crms-build-logos",
    ]
    for c in candidates:
        try:
            if c.exists() and any(c.glob("*.jpg")):
                return c
        except OSError:
            continue
    return candidates[0]


LOGO_STAGING     = _find_logo_staging()

ISCC = r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
# NOTE: max frame is 128, NOT 256. WPF's WIC .ico decoder throws
# "The image decoder cannot decode the image" on a 256x256 PNG-compressed
# frame, so a Window with Icon="favicon.ico" crashes at startup. Capping at
# 128 keeps every frame decodable. (128 is plenty for taskbar / Start.)
ICON_SIZES = [(16,16),(20,20),(24,24),(32,32),(40,40),
              (48,48),(64,64),(96,96),(128,128)]

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
    # Force a clean compile so THIS agency's Win32 EXE icon — public/favicon.ico,
    # regenerated from the agency logo by render_icon_and_logo() just above — is
    # actually re-embedded into CRMRS.exe. MSBuild's incremental up-to-date check
    # does NOT notice that only the .ico changed (the .cs files are identical
    # across agencies), so a warm obj/ would otherwise leave the *previous*
    # agency's icon — or the generic CRMRS icon restored at the end of the last
    # run — baked into the EXE. The Desktop / Start-Menu shortcuts inherit the
    # EXE's embedded icon, so a stale icon there means VK ships with RK's (or the
    # generic) logo. Nuking obj/ + bin/ guarantees csc re-runs with the right
    # /win32icon. Costs ~30-60s per agency; correctness beats speed here.
    for d in ("obj", "bin"):
        p = WPF_DIR / d
        if p.exists():
            shutil.rmtree(p, ignore_errors=True)
    # Per-agency file properties:
    #   - AssemblyTitle  → Windows "File description" (e.g. "V K ENTERPRISES")
    #   - Description    → Windows "File description" tooltip in some shells
    #   - Product        → "Product name" property — kept short, generic
    #   - Company stays "CRMRS" (set in csproj)
    run([
        "dotnet", "publish", "-c", "Release",
        "-r", "win-x64", "--self-contained", "true",
        "-p:PublishSingleFile=false",
        f'-p:AssemblyTitle={agency["name"]}',
        f'-p:Description={agency["name"]} — Recovery agent app (CRMRS)',
        f'-p:Product={agency["name"]} Recovery',
        "-o", str(out),
    ], cwd=WPF_DIR)
    # Ship the agency icon as a loose file too, so the installer's Desktop /
    # Start-Menu shortcuts can point at it explicitly (IconFilename in
    # installer.iss) — a belt-and-suspenders guarantee on top of the embedded
    # EXE icon. <Resource> only embeds the .ico into the assembly; it is never
    # copied loose, so we copy it ourselves.
    shutil.copyfile(ICON_PATH, out / "app-icon.ico")
    return out


def write_setup_shortcut_bat(publish_dir: Path, agency_name: str) -> None:
    """Drop a Setup-Shortcut.bat inside the publish folder. End users double-
    click it once after extracting the portable ZIP — it makes a Desktop
    shortcut AND a Start Menu shortcut pointing at CRMRS.exe, with the
    agency name as the display label. No admin rights required.

    Two things the script must do for the shortcut to work on locked-down
    corporate machines:
      1. Strip Mark-of-the-Web (Unblock-File) from every file in the
         extracted folder. When a user downloads a ZIP, Windows tags every
         file inside with a "Zone.Identifier" alternate data stream marking
         it as "from the web". AppLocker / WDAC then blocks ANY shortcut
         that points at a flagged file — even one the user pinned to the
         taskbar themselves. Once we Unblock-File the CRMRS.exe and DLLs,
         AppLocker treats them as local files.
      2. Save the shortcut atomically, then Unblock-File the .lnk itself
         too — when the user later "pin to taskbar"s the shortcut, Windows
         copies it under User Pinned\\TaskBar\\ and the copy inherits the
         web tag. Pre-stripping the source shortcut prevents that."""
    bat = publish_dir / "Setup-Shortcut.bat"
    bat.write_text(
        "@echo off\r\n"
        "REM Creates Desktop + Start Menu shortcuts for " + agency_name + " (CRMRS).\r\n"
        "REM Safe to run multiple times — it overwrites existing shortcuts.\r\n"
        "REM Also strips Mark-of-the-Web from extracted files so AppLocker\r\n"
        "REM / WDAC corporate policies don't block the EXE or the shortcut.\r\n"
        "setlocal\r\n"
        'set "TARGET=%~dp0CRMRS.exe"\r\n'
        'set "WORKDIR=%~dp0"\r\n'
        'set "ICON=%~dp0app-icon.ico"\r\n'
        'set "NAME=' + agency_name + '"\r\n'
        "\r\n"
        "echo Preparing %NAME% (this only takes a few seconds)...\r\n"
        "\r\n"
        "REM ── Phase 1: strip Mark-of-the-Web from every extracted file ──────\r\n"
        "powershell -NoProfile -ExecutionPolicy Bypass -Command ^\r\n"
        '  "Get-ChildItem -LiteralPath \'%WORKDIR%\' -Recurse -Force -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue"\r\n'
        "\r\n"
        "REM ── Phase 2: create the shortcuts ───────────────────────────────\r\n"
        "powershell -NoProfile -ExecutionPolicy Bypass -Command ^\r\n"
        '  "$ws = New-Object -ComObject WScript.Shell;'
        ' $desk = $ws.SpecialFolders(\'Desktop\');'
        ' $sm   = $ws.SpecialFolders(\'StartMenu\');'
        ' $made = @();'
        ' foreach ($folder in @($desk, (Join-Path $sm \'Programs\'))) {'
        '   $lnkPath = Join-Path $folder \'%NAME%.lnk\';'
        '   $lnk = $ws.CreateShortcut($lnkPath);'
        '   $lnk.TargetPath = \'%TARGET%\';'
        '   $lnk.WorkingDirectory = \'%WORKDIR%\';'
        '   if (Test-Path \'%ICON%\') { $lnk.IconLocation = \'%ICON%,0\' }'
        '   $lnk.Description = \'%NAME% — CRMRS Recovery\';'
        '   $lnk.Save();'
        '   $made += $lnkPath;'
        ' };'
        ' Start-Sleep -Milliseconds 200;'
        ' $made | ForEach-Object { Unblock-File -LiteralPath $_ -ErrorAction SilentlyContinue }"\r\n'
        "\r\n"
        "if %ERRORLEVEL% NEQ 0 (\r\n"
        '  echo Failed to create shortcuts. Try right-clicking CRMRS.exe and choosing "Send to ^> Desktop".\r\n'
        "  pause\r\n"
        "  exit /b 1\r\n"
        ")\r\n"
        "\r\n"
        'echo.\r\n'
        'echo Setup complete for "%NAME%".\r\n'
        "echo  - Desktop shortcut created\r\n"
        "echo  - Start Menu shortcut created\r\n"
        'echo  - Web-tag stripped so corporate AppLocker won\'t block the app\r\n'
        'echo.\r\n'
        "echo You can now close this window and launch the app from your Desktop.\r\n"
        "timeout /t 4 >nul\r\n"
        "endlocal\r\n",
        encoding="utf-8")


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
        print(f"  [ok]  installer: {setup_exe.name}  ({size_mb:.1f} MB)\n")
        # Portable-zip generation intentionally disabled — we distribute the
        # Setup.exe installer ONLY now. The installer puts the app in Program
        # Files (which corporate AppLocker trusts), creates a Desktop + Start
        # Menu shortcut, and best-effort pins to the taskbar. The portable zip
        # caused Mark-of-the-Web / AppLocker blocks because it ran from
        # Downloads. To re-enable, restore the package_portable_zip() call.
        built.append((a, setup_exe))

    restore_generic_assets()

    print(f"[build_wpf_local] done - {len(built)} installer(s) produced:")
    for a, p in built:
        print(f"  - {a['name']:25s} -> {p}")


if __name__ == "__main__":
    main()
