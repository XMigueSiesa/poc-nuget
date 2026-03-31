#!/bin/bash
set -e

# =============================================================================
# POS Backend - Demo Script
# Ejecuta el ciclo completo: pack NuGet → arrancar hosts desde NuGet
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

echo -e "${CYAN}═══════════════════════════════════════════════${NC}"
echo -e "${CYAN}  POS Backend - Demo NuGet Module Distribution  ${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════${NC}"
echo ""

# ─── Step 1: Pack ────────────────────────────────────────────────────────────
echo -e "${YELLOW}[1/3] Empaquetando modulos como NuGet...${NC}"
rm -rf artifacts/nupkg
dotnet pack -c Release --verbosity quiet
echo -e "${GREEN}  ✓ $(ls artifacts/nupkg/*.nupkg | wc -l | tr -d ' ') paquetes generados en artifacts/nupkg/${NC}"
ls artifacts/nupkg/*.nupkg | while read f; do echo "    $(basename $f)"; done
echo ""

# ─── Step 2: Verify NuGet build ─────────────────────────────────────────────
echo -e "${YELLOW}[2/3] Verificando que ambos hosts compilan desde NuGet...${NC}"
dotnet build src/Hosts/Pos.Host.CloudHub -c Release -p:UseProjectReference=false --verbosity quiet
echo -e "${GREEN}  ✓ CloudHub compila desde NuGet${NC}"
dotnet build src/Hosts/Pos.Host.LocalPOS -c Release -p:UseProjectReference=false --verbosity quiet
echo -e "${GREEN}  ✓ LocalPOS compila desde NuGet${NC}"
echo ""

# ─── Step 3: Instructions ───────────────────────────────────────────────────
echo -e "${CYAN}═══════════════════════════════════════════════${NC}"
echo -e "${GREEN}  ✓ NuGet build exitoso!${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════${NC}"
echo ""
echo -e "${YELLOW}Para arrancar los hosts (cada uno en su terminal):${NC}"
echo ""
echo -e "  ${CYAN}Terminal 1 (Cloud Hub - nube central):${NC}"
echo "  dotnet run --project src/Hosts/Pos.Host.CloudHub -c Release -p:UseProjectReference=false"
echo ""
echo -e "  ${CYAN}Terminal 2 (Local POS - tienda):${NC}"
echo "  dotnet run --project src/Hosts/Pos.Host.LocalPOS -c Release -p:UseProjectReference=false"
echo ""
echo -e "${YELLOW}Scalar UI:${NC}"
echo "  CloudHub:  http://localhost:5200/scalar/v1"
echo "  LocalPOS:  http://localhost:5100/scalar/v1"
echo ""
echo -e "${YELLOW}Prerequisito: PostgreSQL${NC}"
echo "  docker-compose up -d"
echo ""
