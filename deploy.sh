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

# Preserve the downloads folder across deploys (installer + index.html live here)
mkdir -p "$VKAPI_OUT/downloads"
if [ -f "$VKAPI_OUT/downloads/index.html" ]; then
    # Keep existing installer if present; only update the HTML page
    cp "$VKAPI_SRC/downloads/index.html" "$VKAPI_OUT/downloads/index.html"
else
    cp -r "$VKAPI_SRC/downloads/." "$VKAPI_OUT/downloads/"
fi
info "Download page ready → $VKAPI_OUT/downloads"

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
