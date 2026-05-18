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
cd "$VKAPI_SRC"
dotnet publish -c Release -o "$VKAPI_OUT" --nologo -v quiet
# Copy env file
cp /home/vkapp/db/.env.local "$VKAPI_OUT/db/.env.local" 2>/dev/null || true
info "VKApiServer built → $VKAPI_OUT"

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
    if [ -f "$REPO_DIR/installer-output/VKEnterprises_Setup.exe" ]; then
        cp "$REPO_DIR/installer-output/VKEnterprises_Setup.exe" "$DL/VKEnterprises_Setup.exe"
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
