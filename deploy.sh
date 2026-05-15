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

# Sync download page + installer into LiteSpeed docRoot (world-readable, no 403)
DOCROOT_DOWNLOADS="/home/characterverse.tech/api.characterverse.tech/downloads"
mkdir -p "$DOCROOT_DOWNLOADS"
cp "$VKAPI_SRC/downloads/index.html" "$DOCROOT_DOWNLOADS/index.html"
if [ -f "$REPO_DIR/installer-output/VKEnterprises_Setup.exe" ]; then
    cp "$REPO_DIR/installer-output/VKEnterprises_Setup.exe" "$DOCROOT_DOWNLOADS/VKEnterprises_Setup.exe"
    info "Installer copied → $DOCROOT_DOWNLOADS/VKEnterprises_Setup.exe"
fi
chmod 755 "$DOCROOT_DOWNLOADS"
find "$DOCROOT_DOWNLOADS" -type f -exec chmod 644 {} \;
# LiteSpeed (nobody) needs traverse permission on every parent in the chain
chmod o+x /home/characterverse.tech /home/characterverse.tech/api.characterverse.tech 2>/dev/null || true
# CyberPanel-OLS enforces strict ownership — files must be owned by the
# domain user (auto-detected from the parent docRoot), not root.
DOMAIN_OWNER=$(stat -c '%U:%G' /home/characterverse.tech/api.characterverse.tech 2>/dev/null)
[ -n "$DOMAIN_OWNER" ] && chown -R "$DOMAIN_OWNER" "$DOCROOT_DOWNLOADS" || true
info "Download folder ready → https://api.characterverse.tech/downloads/"

# Sync /public/ assets (map HTML + Leaflet) — desktop app fetches these
# at runtime so map fixes ship to all clients without a re-install.
DOCROOT_PUBLIC="/home/characterverse.tech/api.characterverse.tech/public"
rm -rf "$DOCROOT_PUBLIC"
mkdir -p "$DOCROOT_PUBLIC"
cp -r "$VKAPI_SRC/public/." "$DOCROOT_PUBLIC/"
find "$DOCROOT_PUBLIC" -type d -exec chmod 755 {} \;
find "$DOCROOT_PUBLIC" -type f -exec chmod 644 {} \;
[ -n "$DOMAIN_OWNER" ] && chown -R "$DOMAIN_OWNER" "$DOCROOT_PUBLIC" || true
info "Public assets ready → https://api.characterverse.tech/public/"

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
# Ensure uploads directory exists and is writable by the service user
mkdir -p "$MOBILE_OUT/uploads/pfp" "$MOBILE_OUT/uploads/kyc"
chown -R www-data:www-data "$MOBILE_OUT/uploads"
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
