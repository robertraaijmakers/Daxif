# DAXIF# - Delegate Automated Xrm Installation Framework

[![Join the chat at https://gitter.im/delegateas/Daxif](https://badges.gitter.im/delegateas/Daxif.svg)](https://gitter.im/delegateas/Daxif?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

A cross-platform framework for automating Microsoft Dataverse (Dynamics 365) development and deployment.

## ✨ What's New - .NET 10.0 Cross-Platform

Daxif has been completely modernized for cross-platform development:

- ✅ **Cross-Platform**: Works on Windows, macOS, and Linux
- ✅ **.NET 10.0**: Modern .NET with latest features
- ✅ **ServiceClient**: Uses Microsoft.PowerPlatform.Dataverse.Client
- ✅ **CLI Tool**: Command-line interface for automation
- ✅ **PowerShell Support**: Wrapper scripts for easy usage
- ✅ **CI/CD Ready**: Perfect for GitHub Actions, Azure DevOps

## 🚀 Quick Start

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PowerShell 7+ (for PowerShell scripts)

### Build

```bash
# Clone the repository
git clone https://github.com/delegateas/Daxif.git
cd Daxif

# Build the solution
cd src
dotnet build Delegate.Daxif.sln -c Release
```

Or use the build script:

```bash
# macOS/Linux
./build.sh

# Windows
./build.ps1
```

This will:
1. Build `Delegate.Daxif` library (.NET 10.0)
2. Build `Delegate.Daxif.Console` CLI tool
3. Copy all files to `ScriptTemplates/Daxif` folder

### Quick Test

```bash
cd src/Delegate.Daxif.Console/bin/Release/net10.0
dotnet daxif.dll help
```

## 📖 Usage

### Option 1: CLI Tool (Recommended)

```bash
# Set environment variables
export DATAVERSE_URL="https://yourorg.crm.dynamics.com"
export DATAVERSE_AUTH_TYPE="ClientSecret"
export DATAVERSE_CLIENT_ID="your-client-id"
export DATAVERSE_CLIENT_SECRET="your-secret"

# Test connection
dotnet daxif.dll test-connection

# Sync plugins
dotnet daxif.dll plugin sync --assembly ./MyPlugins.dll --solution MySolution

# Sync web resources
dotnet daxif.dll webresource sync --folder ./WebResources --solution MySolution
```

See [CLI Documentation](src/Delegate.Daxif.Console/README.md) for complete details.

### Option 2: PowerShell Scripts

```powershell
cd src/Delegate.Daxif.Scripts/ScriptTemplates

# Edit _Config.ps1 with your settings

# Run scripts
pwsh TestConnection.ps1
pwsh PluginSyncDev.ps1
pwsh WebResourceSyncDev.ps1
```

See [PowerShell Documentation](src/Delegate.Daxif.Scripts/ScriptTemplates/README.md) for complete details.

## 🏗️ Project Structure

```
Daxif/
├── src/
│   ├── Delegate.Daxif/              # Core library (.NET 10.0)
│   ├── Delegate.Daxif.Console/      # CLI tool (cross-platform)
│   └── Delegate.Daxif.Scripts/      
│       └── ScriptTemplates/         # PowerShell scripts
│           ├── Daxif/               # Binary folder (auto-generated)
│           ├── _Config.ps1          # Configuration
│           ├── PluginSyncDev.ps1    # Plugin sync
│           └── WebResourceSyncDev.ps1
└── build.sh / build.ps1             # Build scripts
```

## 🔧 Development

### Building

```bash
# Debug build
cd src
dotnet build

# Release build
dotnet build -c Release

# Clean
dotnet clean
```

### Testing

```bash
# Run tests (if available)
dotnet test

# Test CLI
cd Delegate.Daxif.Console/bin/Release/net10.0
dotnet daxif.dll help
```

### Adding Features

1. Update `Delegate.Daxif` library with new functionality
2. Add CLI commands in `Delegate.Daxif.Console/Program.fs`
3. Update PowerShell scripts if needed
4. Update documentation

## 📚 Documentation

- [CLI Tool Documentation](src/Delegate.Daxif.Console/README.md)
- [PowerShell Scripts Documentation](src/Delegate.Daxif.Scripts/ScriptTemplates/README.md)
- [Migration Guide](MIGRATION_GUIDE.md) (coming from .NET Framework)

## 🔐 Authentication

Daxif supports three authentication methods:

### OAuth (Interactive)
```bash
export DATAVERSE_AUTH_TYPE="OAuth"
export DATAVERSE_APP_ID="51f81489-12ee-4a9e-aaae-a2591f45987d"
```

### Client Secret (CI/CD)
```bash
export DATAVERSE_AUTH_TYPE="ClientSecret"
export DATAVERSE_CLIENT_ID="your-client-id"
export DATAVERSE_CLIENT_SECRET="your-secret"
```

### Certificate
```bash
export DATAVERSE_AUTH_TYPE="Certificate"
export DATAVERSE_CLIENT_ID="your-client-id"
export DATAVERSE_THUMBPRINT="cert-thumbprint"
```

## 🚢 CI/CD Examples

### GitHub Actions

```yaml
- name: Build Daxif
  run: |
    cd src
    dotnet build -c Release

- name: Sync Plugins
  env:
    DATAVERSE_URL: ${{ secrets.DATAVERSE_URL }}
    DATAVERSE_AUTH_TYPE: ClientSecret
    DATAVERSE_CLIENT_ID: ${{ secrets.CLIENT_ID }}
    DATAVERSE_CLIENT_SECRET: ${{ secrets.CLIENT_SECRET }}
  run: |
    cd src/Delegate.Daxif.Console/bin/Release/net10.0
    dotnet daxif.dll plugin sync --assembly ../../../../MyPlugin.dll --solution MySolution
```

### Azure DevOps

```yaml
- script: |
    cd src
    dotnet build -c Release
  displayName: 'Build Daxif'

- script: |
    cd src/Delegate.Daxif.Console/bin/Release/net10.0
    dotnet daxif.dll plugin sync --assembly ../../../../MyPlugin.dll --solution MySolution
  env:
    DATAVERSE_URL: $(DATAVERSE_URL)
    DATAVERSE_AUTH_TYPE: ClientSecret
    DATAVERSE_CLIENT_ID: $(CLIENT_ID)
    DATAVERSE_CLIENT_SECRET: $(CLIENT_SECRET)
  displayName: 'Sync Plugins'
```

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📄 License

See [LICENSE.md](LICENSE.md) for details.

## 🔗 Links

- [GitHub Repository](https://github.com/delegateas/Daxif)
- [Issues](https://github.com/delegateas/Daxif/issues)
- [Gitter Chat](https://gitter.im/delegateas/Daxif)

## 📝 Changelog

### Version 2.0 (.NET 10.0)
- ✨ Complete rewrite for .NET 10.0
- ✨ Cross-platform support (Windows, macOS, Linux)
- ✨ New CLI tool with command-line interface
- ✨ Modern authentication with ServiceClient
- ✨ PowerShell wrapper scripts
- ⚠️ Breaking: Removed deprecated Solution import/export features
- ⚠️ Breaking: Removed Windows-only credential management