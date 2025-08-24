# Installation Guide

Complete step-by-step guide to installing Brainarr on your system.

## Prerequisites

Before installing Brainarr, ensure you have:

### Required
- **Lidarr**: Version 4.0.0 or higher
- **.NET Runtime**: 6.0 or higher
- **Operating System**: Windows, Linux, or macOS

### Recommended
- **At least one AI provider**: Choose from local (Ollama, LM Studio) or cloud options
- **Sufficient RAM**: 4GB minimum, 8GB recommended for local providers
- **Storage**: 50MB for plugin, additional space for local models if using

## Installation Methods

### Method 1: Pre-built Release (Recommended)

#### Step 1: Download Latest Release
1. Go to [Brainarr Releases](https://github.com/RicherTunes/Brainarr/releases)
2. Download the latest `Brainarr-v1.0.0.zip` file
3. Extract the contents to a temporary folder

#### Step 2: Install Plugin
**Windows:**
```powershell
# Stop Lidarr service
Stop-Service Lidarr

# Create plugin directory
New-Item -Path "C:\ProgramData\Lidarr\plugins\Brainarr" -ItemType Directory -Force

# Copy plugin files
Copy-Item "path\to\extracted\files\*" "C:\ProgramData\Lidarr\plugins\Brainarr\" -Recurse

# Set permissions
icacls "C:\ProgramData\Lidarr\plugins\Brainarr" /grant "Users:(OI)(CI)F"

# Start Lidarr service
Start-Service Lidarr
```

**Linux:**
```bash
# Stop Lidarr
sudo systemctl stop lidarr

# Create plugin directory
sudo mkdir -p /var/lib/lidarr/plugins/Brainarr

# Copy plugin files
sudo cp -r /path/to/extracted/files/* /var/lib/lidarr/plugins/Brainarr/

# Set correct ownership
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr

# Set correct permissions
sudo chmod -R 755 /var/lib/lidarr/plugins/Brainarr

# Start Lidarr
sudo systemctl start lidarr
```

**Docker:**
```bash
# Stop container
docker stop lidarr

# Copy to mounted volume
docker cp /path/to/extracted/files/. lidarr:/config/plugins/Brainarr/

# Start container
docker start lidarr
```

#### Step 3: Verify Installation
1. Open Lidarr web interface
2. Go to **Settings → Import Lists**
3. Click **Add New (+)**
4. Look for **Brainarr** in the list

✅ If you see Brainarr, installation was successful!

### Method 2: Build from Source

#### Prerequisites for Building
- **.NET SDK**: 6.0 or higher
- **Git**: For cloning the repository
- **Lidarr assemblies**: For compilation

#### Step 1: Clone Repository
```bash
git clone https://github.com/RicherTunes/Brainarr.git
cd Brainarr
```

#### Step 2: Build Plugin
```bash
# Restore dependencies
dotnet restore

# Build release version
dotnet build -c Release

# Publish plugin
dotnet publish -c Release -o dist/
```

#### Step 3: Install Built Plugin
Follow the same installation steps as Method 1, using files from the `dist/` directory.

## Platform-Specific Instructions

### Windows Installation

#### Using PowerShell (Administrator)
```powershell
# Download and install script
iwr -useb https://raw.githubusercontent.com/RicherTunes/Brainarr/main/scripts/install-windows.ps1 | iex
```

#### Manual Installation
1. **Locate Lidarr Data Directory**:
   - Default: `C:\ProgramData\Lidarr\`
   - Custom: Check Lidarr settings

2. **Stop Lidarr Service**:
   - Services → Lidarr → Stop
   - Or: `Stop-Service Lidarr`

3. **Create Plugin Directory**:
   ```
   C:\ProgramData\Lidarr\plugins\Brainarr\
   ```

4. **Copy Plugin Files** and restart Lidarr

### Linux Installation

#### Ubuntu/Debian
```bash
# Install via script
curl -sSL https://raw.githubusercontent.com/RicherTunes/Brainarr/main/scripts/install-linux.sh | bash
```

#### Manual Installation
```bash
# Find Lidarr data directory
sudo find / -name "lidarr.db" 2>/dev/null

# Common locations:
# - /var/lib/lidarr/
# - /opt/lidarr/
# - ~/.config/Lidarr/

# Stop Lidarr
sudo systemctl stop lidarr

# Install plugin
sudo mkdir -p /var/lib/lidarr/plugins/Brainarr
sudo cp -r plugin-files/* /var/lib/lidarr/plugins/Brainarr/
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr
sudo systemctl start lidarr
```

### Docker Installation

#### Docker Compose
Add to your `docker-compose.yml`:

```yaml
services:
  lidarr:
    image: lscr.io/linuxserver/lidarr:latest
    volumes:
      - ./lidarr-config:/config
      - ./brainarr-plugin:/config/plugins/Brainarr  # Add this line
    # ... other configuration
```

#### Standalone Docker
```bash
# Create plugin directory on host
mkdir -p ./lidarr-plugins/Brainarr

# Copy plugin files
cp -r plugin-files/* ./lidarr-plugins/Brainarr/

# Mount as volume
docker run -d \
  --name lidarr \
  -v ./lidarr-config:/config \
  -v ./lidarr-plugins/Brainarr:/config/plugins/Brainarr \
  lscr.io/linuxserver/lidarr:latest
```

### macOS Installation

#### Using Homebrew (if Lidarr installed via brew)
```bash
# Find Lidarr location
brew --prefix lidarr

# Install plugin
mkdir -p "$(brew --prefix)/var/lib/lidarr/plugins/Brainarr"
cp -r plugin-files/* "$(brew --prefix)/var/lib/lidarr/plugins/Brainarr/"
```

#### Manual Installation
```bash
# Common Lidarr locations on macOS:
# - /Applications/Lidarr.app/Contents/
# - ~/.config/Lidarr/
# - /usr/local/var/lib/lidarr/

# Stop Lidarr
launchctl stop lidarr

# Install plugin (adjust path as needed)
mkdir -p ~/.config/Lidarr/plugins/Brainarr
cp -r plugin-files/* ~/.config/Lidarr/plugins/Brainarr/

# Start Lidarr
launchctl start lidarr
```

## Post-Installation Setup

### Step 1: Configure Brainarr
1. Open Lidarr → **Settings** → **Import Lists**
2. Click **Add (+)** → **Brainarr**
3. Configure basic settings:
   - **Name**: AI Music Recommendations
   - **Enable Automatic Add**: Yes
   - **Monitor**: All Albums
   - **Root Folder**: Your music directory
   - **Quality Profile**: Any
   - **Metadata Profile**: Standard

### Step 2: Choose AI Provider
See **[Provider Setup Overview](Provider-Setup-Overview)** for detailed provider configuration.

**Quick Recommendations**:
- **Privacy-focused**: Use [Ollama](Local-Providers#ollama) (free, local)
- **Getting started**: Use [Google Gemini](Cloud-Providers#google-gemini) (free tier)
- **Best quality**: Use [OpenAI GPT-4](Cloud-Providers#openai) or [Anthropic Claude](Cloud-Providers#anthropic)

### Step 3: Test Configuration
1. Click **Test** button in Brainarr settings
2. Should return: "Connection successful!"
3. Click **Save** to apply settings

### Step 4: Generate First Recommendations
1. Go to **Import Lists** → **Brainarr**
2. Click **Fetch Now**
3. Check **Activity** → **History** for results

## Troubleshooting Installation

### Plugin Not Appearing
**Problem**: Brainarr doesn't appear in Import Lists

**Solutions**:
1. **Check File Permissions**:
   ```bash
   # Linux
   sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr
   sudo chmod -R 755 /var/lib/lidarr/plugins/Brainarr
   ```

2. **Verify File Structure**:
   ```
   plugins/Brainarr/
   ├── Brainarr.Plugin.dll
   ├── plugin.json
   └── (other plugin files)
   ```

3. **Check Lidarr Logs**:
   - Location: Lidarr data directory → `logs/`
   - Look for plugin loading errors

### Permission Errors
**Problem**: "Access denied" or permission errors

**Solutions**:
1. **Run installation as administrator/sudo**
2. **Set correct ownership**: `chown -R lidarr:lidarr`
3. **Set correct permissions**: `chmod -R 755`

### .NET Runtime Issues
**Problem**: Plugin fails to load due to .NET version

**Solutions**:
1. **Install .NET 6 Runtime**:
   - Windows: Download from Microsoft
   - Linux: `sudo apt install dotnet-runtime-6.0`
   - macOS: `brew install --cask dotnet`

2. **Verify .NET Version**:
   ```bash
   dotnet --list-runtimes
   ```

### Container-Specific Issues
**Problem**: Docker/container installation issues

**Solutions**:
1. **Verify Volume Mounts**:
   ```bash
   docker inspect container_name | grep Mounts -A 10
   ```

2. **Check Container Permissions**:
   ```bash
   docker exec container_name ls -la /config/plugins/
   ```

3. **Restart Container**:
   ```bash
   docker restart lidarr
   ```

## Upgrading

### From Previous Versions
1. **Stop Lidarr**
2. **Backup current plugin**: Copy entire Brainarr folder
3. **Remove old plugin**: Delete existing Brainarr directory
4. **Install new version**: Follow installation steps above
5. **Start Lidarr**: Configuration should be preserved

### Automatic Updates (Future)
Future versions will support automatic updates through Lidarr's plugin manager.

## Verification

### Success Indicators
- ✅ Brainarr appears in Import Lists options
- ✅ Test connection returns "Connection successful"
- ✅ No errors in Lidarr logs related to Brainarr
- ✅ Can configure AI provider settings

### Next Steps
- **[Provider Setup Overview](Provider-Setup-Overview)** - Configure your AI provider
- **[Basic Configuration](Basic-Configuration)** - Essential settings
- **[Getting Your First Recommendations](Getting-Your-First-Recommendations)** - Generate recommendations

## Need Help?

- **[Common Issues](Common-Issues)** - Known problems and solutions
- **[Troubleshooting Guide](Troubleshooting-Guide)** - Systematic problem solving
- **GitHub Issues** - Report installation problems