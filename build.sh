#!/bin/bash
# Build script for Daxif (macOS/Linux)

set -e  # Exit on error

echo "=========================================="
echo "Building Daxif"
echo "=========================================="

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration
CONFIGURATION="${1:-Release}"  # Default to Release, or use first argument

echo -e "${YELLOW}Configuration: ${CONFIGURATION}${NC}"
echo ""

# Navigate to src directory
cd "$(dirname "$0")/src"

echo "Cleaning previous builds..."
dotnet clean -c $CONFIGURATION > /dev/null 2>&1 || true

echo "Restoring packages..."
dotnet restore

echo ""
echo "Building Delegate.Daxif..."
dotnet build Delegate.Daxif/Delegate.Daxif.fsproj -c $CONFIGURATION

echo ""
echo "Building Delegate.Daxif.Console..."
dotnet build Delegate.Daxif.Console/Delegate.Daxif.Console.fsproj -c $CONFIGURATION

echo ""
echo "Building Delegate.Daxif.Scripts..."
dotnet build Delegate.Daxif.Scripts/Delegate.Daxif.Scripts.fsproj -c $CONFIGURATION

echo ""
echo -e "${GREEN}✓ Build completed successfully!${NC}"
echo ""
echo "Outputs:"
echo "  - Library: src/Delegate.Daxif/bin/$CONFIGURATION/net10.0/Delegate.Daxif.dll"
echo "  - CLI Tool: src/Delegate.Daxif.Console/bin/$CONFIGURATION/net10.0/daxif.dll"
echo "  - Scripts: src/Delegate.Daxif.Scripts/ScriptTemplates/Daxif/"
echo ""
echo "Quick test:"
echo "  cd src/Delegate.Daxif.Console/bin/$CONFIGURATION/net10.0"
echo "  dotnet daxif.dll help"
echo ""
echo "=========================================="
