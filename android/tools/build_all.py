"""End-to-end per-agency build pipeline.

Run this any time agencies are approved (or their logos change):

    python tools/build_all.py

What it does, for every approved agency in crm_master.agencies:
  1. Pulls the uploaded logo from the production server
     (/opt/vkapi/agency-uploads/<slug>.<ext>) into tenant-logos/<slug>.<ext>.
     If the agency has not uploaded a logo, the default CRMS logo is used.
  2. Rewrites android/tenants.json with the current agency list
     (slug, name, primaryColor — color falls back to a deterministic
     hash-based palette pick if not stored).
  3. Re-runs gen_flavors.py to resize every logo into the per-flavor
     mipmap buckets.
  4. Runs ./gradlew assembleDebug — produces one APK per agency.
  5. SCPs each built APK to the server at
     /opt/vkapi/agency-apps/<flavor>/app.apk so the admin portal can
     serve it.

Re-running is idempotent: agencies that already have a built APK get
their APK overwritten with a fresh one; nothing else is touched.
"""
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT       = Path(__file__).resolve().parent.parent
TJSON      = ROOT / "tenants.json"
LOGOS_DIR  = ROOT / "tenant-logos"
APK_DIR    = ROOT / "app" / "build" / "outputs" / "apk"

SSH_HOST       = "root@103.67.239.102"
SSH_PORT       = "2719"
SERVER_LOGOS   = "/opt/vkapi/agency-uploads"
SERVER_APPS    = "/opt/vkapi/agency-apps"

# 8 distinct colors to deterministically pick from for agencies whose record
# does not store a primary color. Picked by hashing the slug so the same
# agency always gets the same color across rebuilds.
PALETTE = [
    "#FF6B35", "#1565C0", "#2E7D32", "#6A1B9A",
    "#C2185B", "#EF6C00", "#00838F", "#37474F",
]


def run(cmd: list[str], check: bool = True, **kw) -> subprocess.CompletedProcess:
    print("  $ " + " ".join(cmd))
    return subprocess.run(cmd, check=check, text=True, **kw)


def ssh(remote_cmd: str) -> str:
    r = subprocess.run(
        ["ssh", "-p", SSH_PORT, "-o", "BatchMode=yes", SSH_HOST, remote_cmd],
        capture_output=True, text=True, check=True)
    return r.stdout


def list_approved_agencies() -> list[dict]:
    """Pull approved agencies from crm_master via SSH+mariadb."""
    out = ssh(
        "mariadb -u root crm_master -sNe "
        "\"SELECT slug, name, COALESCE(logo_path,'') FROM agencies "
        "WHERE status='approved' ORDER BY name;\"")
    agencies = []
    for line in out.strip().split("\n"):
        if not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) < 2:
            continue
        slug, name = parts[0], parts[1]
        logo_path = parts[2] if len(parts) > 2 else ""
        color = PALETTE[hash(slug) % len(PALETTE)]
        agencies.append({"slug": slug, "name": name, "primaryColor": color,
                          "logoExt": Path(logo_path).suffix.lstrip(".") if logo_path else ""})
    return agencies


def pull_logo(slug: str, hinted_ext: str = "") -> bool:
    """SCP the agency-uploaded logo into tenant-logos/. Returns True on success."""
    LOGOS_DIR.mkdir(exist_ok=True)
    # Wipe stale local versions of this slug first.
    for ext in ("png", "jpg", "jpeg", "webp"):
        (LOGOS_DIR / f"{slug}.{ext}").unlink(missing_ok=True)
    # Try the hinted extension first, then everything else.
    exts = [hinted_ext] + [e for e in ("jpg", "jpeg", "png", "webp") if e != hinted_ext]
    for ext in [e for e in exts if e]:
        remote = f"{SSH_HOST}:{SERVER_LOGOS}/{slug}.{ext}"
        local  = LOGOS_DIR / f"{slug}.{ext}"
        r = subprocess.run(
            ["scp", "-P", SSH_PORT, "-q", "-o", "BatchMode=yes", remote, str(local)],
            capture_output=True, timeout=30)
        if r.returncode == 0 and local.exists() and local.stat().st_size > 0:
            return True
        if local.exists():
            local.unlink(missing_ok=True)
    return False


def gradle_cmd() -> str:
    return str(ROOT / ("gradlew.bat" if os.name == "nt" else "gradlew"))


def main() -> None:
    print("[build_all] fetching approved agencies from crm_master…")
    agencies = list_approved_agencies()
    if not agencies:
        sys.exit("[build_all] no approved agencies — nothing to build.")
    print(f"[build_all] {len(agencies)} approved agencies")

    print("\n[build_all] pulling agency-uploaded logos from server")
    for a in agencies:
        ok = pull_logo(a["slug"], a.get("logoExt", ""))
        a["logoSource"] = "agency upload" if ok else "default CRMS logo"
        print(f"  {a['slug']:24s}  {a['logoSource']}")

    print("\n[build_all] writing tenants.json")
    tenants = [{"slug": a["slug"], "name": a["name"], "primaryColor": a["primaryColor"]}
               for a in agencies]
    TJSON.write_text(json.dumps(tenants, indent=4), encoding="utf-8")

    print("\n[build_all] regenerating per-flavor mipmap icons")
    run([sys.executable, str(ROOT / "tools" / "gen_flavors.py")], cwd=ROOT)

    print("\n[build_all] gradle assembleDebug (all flavors)")
    run([gradle_cmd(), "assembleDebug"], cwd=ROOT)

    print("\n[build_all] uploading APKs to portal storage")
    for a in agencies:
        flavor = a["slug"].replace("_", "")
        apk    = APK_DIR / flavor / "debug" / f"app-{flavor}-debug.apk"
        if not apk.exists():
            print(f"  ⚠️  missing  {apk}")
            continue
        ssh(f"mkdir -p {SERVER_APPS}/{flavor}")
        r = subprocess.run(
            ["scp", "-P", SSH_PORT, "-q", str(apk),
             f"{SSH_HOST}:{SERVER_APPS}/{flavor}/app.apk"],
            capture_output=True, text=True)
        if r.returncode == 0:
            print(f"  ✓  {flavor:24s}  {apk.stat().st_size / 1024 / 1024:.1f} MB")
        else:
            print(f"  ✗  {flavor:24s}  {r.stderr.strip()}")

    print(f"\n[build_all] done — {len(agencies)} apps published to the admin portal.")


if __name__ == "__main__":
    main()
