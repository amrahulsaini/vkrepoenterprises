#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  VK Enterprises — One-command deploy script
#  Usage: bash /home/vkapp/vkrepoenterprises/deploy.sh
#  Run this on your server every time after pushing from your PC.
# ─────────────────────────────────────────────────────────────────────────────

set -e   # stop on first error

REPO_DIR="/home/vkapp"
VKAPI_SRC="$REPO_DIR/VKApiServer"
MOBILE_SRC="$REPO_DIR/VKmobileapi"
VKAPI_OUT="/opt/vkapi"
MOBILE_OUT="/opt/vkmobileapi"

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

info()    { echo -e "${GREEN}[✓]${NC} $1"; }
section() { echo -e "\n${YELLOW}══ $1 ══${NC}"; }

# ── 1. Pull latest code ───────────────────────────────────────────────────────
section "Pulling latest code from GitHub"
cd "$REPO_DIR"
git pull origin main
info "Code is up to date"

# ── 2. Build & deploy VKApiServer (desktop/web API — port 5002) ───────────────
section "Building VKApiServer"
# Stop the service and forcibly remove the .pdb so dotnet publish can overwrite it.
# systemctl stop is async on graceful shutdown; deleting the file ensures no lock.
systemctl stop vkapi 2>/dev/null || true
sleep 2
rm -f "$VKAPI_OUT/VKApiServer.pdb" 2>/dev/null || true
cd "$VKAPI_SRC"
dotnet publish -c Release -o "$VKAPI_OUT" --nologo -v quiet
# Copy env file
cp /home/vkapp/db/.env.local "$VKAPI_OUT/db/.env.local" 2>/dev/null || true
# `dotnet publish` does NOT copy the public/ folder (its files aren't marked
# as build content), so VKApiServer's /public/ static route (served from
# $VKAPI_OUT/public) goes stale — e.g. map_live.html wouldn't update. Force
# a fresh sync every deploy so server-hosted assets (live map, leaflet) match
# the repo.
mkdir -p "$VKAPI_OUT/public"
cp -r "$VKAPI_SRC/public/." "$VKAPI_OUT/public/"
info "VKApiServer built → $VKAPI_OUT (public/ synced)"

# Agency logo uploads — vkapi writes compressed agency logos here and serves
# them as static files at /agency-uploads/... . The directory MUST exist and be
# writable by the vkapi service user (www-data) before the service starts,
# otherwise startup throws and the service crash-loops.
mkdir -p /opt/vkapi/agency-uploads
chown -R www-data:www-data /opt/vkapi/agency-uploads
chmod -R o+rX /opt/vkapi/agency-uploads
chmod o+x /opt /opt/vkapi 2>/dev/null || true
info "Agency uploads dir ready → /opt/vkapi/agency-uploads"

# Sync /downloads/ (installer + index) and /public/ (map HTML + Leaflet) into
# every domain's LiteSpeed docRoot. To support a new domain, just add its
# docRoot path to the array below — everything else loops automatically.
DOMAIN_DOCROOTS=(
    "/home/characterverse.tech/api.characterverse.tech"
    "/home/crmrecoverysoftware.com/api.crmrecoverysoftware.com"
)

for DOCROOT in "${DOMAIN_DOCROOTS[@]}"; do
    if [ ! -d "$DOCROOT" ]; then
        info "Skipping $DOCROOT (not present)"
        continue
    fi
    PARENT_DIR="$(dirname "$DOCROOT")"
    # LiteSpeed (nobody) needs traverse permission on every parent in the chain
    chmod o+x "$PARENT_DIR" "$DOCROOT" 2>/dev/null || true
    # CyberPanel-OLS enforces strict ownership — files must be owned by the
    # domain user (auto-detected from the docRoot), not root.
    DOMAIN_OWNER=$(stat -c '%U:%G' "$DOCROOT" 2>/dev/null)

    # /downloads/  — installer + download page
    DL="$DOCROOT/downloads"
    mkdir -p "$DL"
    cp "$VKAPI_SRC/downloads/index.html" "$DL/index.html"
    if [ -f "$REPO_DIR/installer-output/CRMS_Setup.exe" ]; then
        cp "$REPO_DIR/installer-output/CRMS_Setup.exe" "$DL/CRMS_Setup.exe"
    fi
    chmod 755 "$DL"
    find "$DL" -type f -exec chmod 644 {} \;
    [ -n "$DOMAIN_OWNER" ] && chown -R "$DOMAIN_OWNER" "$DL" || true
    info "Downloads ready → $DL"

    # /public/  — map HTML + Leaflet (re-synced fresh each deploy)
    PB="$DOCROOT/public"
    rm -rf "$PB"
    mkdir -p "$PB"
    cp -r "$VKAPI_SRC/public/." "$PB/"
    find "$PB" -type d -exec chmod 755 {} \;
    find "$PB" -type f -exec chmod 644 {} \;
    [ -n "$DOMAIN_OWNER" ] && chown -R "$DOMAIN_OWNER" "$PB" || true
    info "Public assets ready → $PB"
done

# ── CRMS Agency Portal — static site at agency.crmrecoverysoftware.com ───────
# CyberPanel-OLS docRoot for the agency subdomain is `public_html/` under the
# subdomain folder. Earlier versions of this script were copying into the
# subdomain folder itself which OLS ignored entirely (it kept serving stale
# files from public_html/, leading to "I just deployed but apps.js is still
# old" symptoms). Always sync into the actual docRoot.
AGENCY_PARENT="/home/crmrecoverysoftware.com/agency.crmrecoverysoftware.com"
AGENCY_DOCROOT="$AGENCY_PARENT/public_html"
if [ ! -d "$AGENCY_DOCROOT" ]; then
    # Some older provisioning used the subdomain folder directly. Fall back so
    # this script still works on hosts without a public_html/.
    AGENCY_DOCROOT="$AGENCY_PARENT"
fi
if [ -d "$AGENCY_DOCROOT" ]; then
    chmod o+x /home/crmrecoverysoftware.com "$AGENCY_PARENT" "$AGENCY_DOCROOT" 2>/dev/null || true
    AGENCY_OWNER=$(stat -c '%U:%G' "$AGENCY_DOCROOT" 2>/dev/null)
    # Re-sync the portal files (html / css / js / assets), keep any logs folder.
    for item in .htaccess index.html app.html register.html manage.html status.html css js assets vk; do
        rm -rf "$AGENCY_DOCROOT/$item"
        if [ -e "$REPO_DIR/agency-portal/$item" ]; then
            cp -r "$REPO_DIR/agency-portal/$item" "$AGENCY_DOCROOT/$item"
        fi
    done
    find "$AGENCY_DOCROOT" -type d -exec chmod 755 {} \;
    find "$AGENCY_DOCROOT" -type f -exec chmod 644 {} \;
    [ -n "$AGENCY_OWNER" ] && chown -R "$AGENCY_OWNER" "$AGENCY_DOCROOT" 2>/dev/null || true
    info "Agency portal ready → $AGENCY_DOCROOT"
else
    info "Skipping agency portal ($AGENCY_DOCROOT not present — create the child domain first)"
fi

# ── CRMRS Manage (admin) portal — manage.crmrecoverysoftware.com ─────────────
# Created as a CyberPanel child domain. Its docRoot is public_html/<child>
# UNDER the master domain home (not the agency-style sibling layout), so the
# manage page is served at clean URLs (index.html at /). Syncs the
# self-contained manage-portal/ tree (its own copy of css/js/assets).
MANAGE_DOCROOT="/home/crmrecoverysoftware.com/public_html/manage.crmrecoverysoftware.com"
if [ -d "$MANAGE_DOCROOT" ]; then
    chmod o+x /home/crmrecoverysoftware.com "$MANAGE_DOCROOT" 2>/dev/null || true
    MANAGE_OWNER=$(stat -c '%U:%G' "$MANAGE_DOCROOT" 2>/dev/null)
    for item in index.html css js assets; do
        rm -rf "$MANAGE_DOCROOT/$item"
        if [ -e "$REPO_DIR/manage-portal/$item" ]; then
            cp -r "$REPO_DIR/manage-portal/$item" "$MANAGE_DOCROOT/$item"
        fi
    done
    find "$MANAGE_DOCROOT" -type d -exec chmod 755 {} \;
    find "$MANAGE_DOCROOT" -type f -exec chmod 644 {} \;
    [ -n "$MANAGE_OWNER" ] && chown -R "$MANAGE_OWNER" "$MANAGE_DOCROOT" 2>/dev/null || true
    info "Manage portal ready → $MANAGE_DOCROOT"
else
    info "Skipping manage portal ($MANAGE_DOCROOT not present — create the child domain first)"
fi

# ── Main site (landing page) + privacy policy on crmrecoverysoftware.com ────
# The main domain serves the marketing landing page (main-site/) and the
# privacy policy (required by Play Console). CyberPanel-OLS uses
# /home/<domain>/public_html for the main domain docRoot.
MAIN_DOCROOT="/home/crmrecoverysoftware.com/public_html"
if [ ! -d "$MAIN_DOCROOT" ]; then
    MAIN_DOCROOT="/home/crmrecoverysoftware.com/crmrecoverysoftware.com"
fi
if [ -d "$MAIN_DOCROOT" ]; then
    chmod o+x /home/crmrecoverysoftware.com "$MAIN_DOCROOT" 2>/dev/null || true
    MAIN_OWNER=$(stat -c '%U:%G' "$MAIN_DOCROOT" 2>/dev/null)

    # Landing page index.html + careers.html + assets/
    cp "$REPO_DIR/main-site/index.html"   "$MAIN_DOCROOT/index.html"
    cp "$REPO_DIR/main-site/careers.html" "$MAIN_DOCROOT/careers.html"
    rm -rf "$MAIN_DOCROOT/assets"
    cp -r "$REPO_DIR/main-site/assets" "$MAIN_DOCROOT/assets"
    # Privacy policy
    cp "$VKAPI_SRC/public/privacy-policy.html" "$MAIN_DOCROOT/privacy-policy.html"

    find "$MAIN_DOCROOT" -type d -exec chmod 755 {} \;
    find "$MAIN_DOCROOT" -type f -exec chmod 644 {} \;
    [ -n "$MAIN_OWNER" ] && chown -R "$MAIN_OWNER" \
        "$MAIN_DOCROOT/index.html" \
        "$MAIN_DOCROOT/careers.html" \
        "$MAIN_DOCROOT/assets" \
        "$MAIN_DOCROOT/privacy-policy.html" 2>/dev/null || true
    info "Main site + privacy policy deployed → $MAIN_DOCROOT"
else
    info "Main domain docRoot not found — main site not deployed. Check /home/crmrecoverysoftware.com/"
fi

section "Restarting vkapi service"
systemctl restart vkapi
sleep 2
if systemctl is-active --quiet vkapi; then
    info "vkapi is running"
else
    echo -e "${RED}[✗] vkapi failed to start — check: journalctl -u vkapi -n 30${NC}"
    exit 1
fi

# ── 4. Build & deploy VKmobileapi (mobile API — port 5001) ───────────────────
section "Building VKmobileapi"
systemctl stop vkmobileapi 2>/dev/null || true
sleep 2
rm -f "$MOBILE_OUT/VKmobileapi.pdb" 2>/dev/null || true
cd "$MOBILE_SRC"
dotnet publish -c Release -o "$MOBILE_OUT" --nologo -v quiet
# Copy env file so VKmobileapi can read MySQL credentials
mkdir -p "$MOBILE_OUT/db"
cp /home/vkapp/db/.env.local "$MOBILE_OUT/db/.env.local" 2>/dev/null || true
# Ensure uploads directory exists and is writable by the mobile service user.
# Also make files world-readable so LiteSpeed (running as nobody) can serve
# them as static content at https://api.characterverse.tech/uploads/...
mkdir -p "$MOBILE_OUT/uploads/pfp" "$MOBILE_OUT/uploads/kyc"
chown -R www-data:www-data "$MOBILE_OUT/uploads"
chmod -R o+rX "$MOBILE_OUT/uploads"
# Make /opt and /opt/vkmobileapi traversable for the nobody user
chmod o+x /opt /opt/vkmobileapi 2>/dev/null || true
info "VKmobileapi built → $MOBILE_OUT"

section "Restarting vkmobileapi service"
systemctl restart vkmobileapi
sleep 2
if systemctl is-active --quiet vkmobileapi; then
    info "vkmobileapi is running"
else
    echo -e "${RED}[✗] vkmobileapi failed to start — check: journalctl -u vkmobileapi -n 30${NC}"
    exit 1
fi

# ── 5. Done ───────────────────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${GREEN}  Deploy complete!${NC}"
echo -e "${GREEN}  vkapi       → http://localhost:5002${NC}"
echo -e "${GREEN}  vkmobileapi → http://localhost:5001${NC}"
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
