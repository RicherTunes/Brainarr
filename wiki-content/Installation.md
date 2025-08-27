# 📦 Installation Guide

Complete installation guide for Brainarr v1.0.3. Choose the method that best fits your setup.

## 🐳 **Docker Installation (Recommended)**

**Why Docker**: Simplest setup with plugins branch support built-in.

### **1. Use Plugins Branch Docker Image**

#### **Docker Compose (Recommended)**
```yaml
version: "3.8"
services:
  lidarr:
    image: ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692
    container_name: lidarr-brainarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=America/New_York
      - UMASK=002
    volumes:
      - ./lidarr-config:/config
      - ./downloads:/downloads
      - ./music:/music
    ports:
      - "8686:8686"
    restart: unless-stopped
```

#### **Docker Run Command**
```bash
docker run -d \
  --name=lidarr-brainarr \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=America/New_York \
  -p 8686:8686 \
  -v ./lidarr-config:/config \
  -v ./downloads:/downloads \
  -v ./music:/music \
  --restart unless-stopped \
  ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692
```

### **2. Enable Plugins in Lidarr**
1. Open Lidarr: http://localhost:8686
2. **Settings** → **General** → **Updates**
3. Enable **"Enable Plugins"** ✅
4. **Save** and restart container

### **3. Install Brainarr Plugin**
1. **Settings** → **Import Lists** → **Add** (+)
2. Select **"Brainarr"** from the list
3. Configure your AI provider (see [[Provider Setup]])
4. **Test** connection and **Save**

---

## 🔧 **Manual Installation**

**For existing Lidarr installations without Docker.**

### **Prerequisites Check**
- ✅ Lidarr version 2.13.1.4681 or higher
- ✅ Plugins enabled in Lidarr settings  
- ✅ .NET 6.0+ runtime installed
- ✅ At least one AI provider available

### **1. Download Latest Release**

#### **From GitHub Releases**
```bash
# Download plugin package
wget https://github.com/RicherTunes/Brainarr/releases/download/v1.0.3/Brainarr-v1.0.3.net6.0.zip

# Or the universal package
wget https://github.com/RicherTunes/Brainarr/releases/download/v1.0.3/Brainarr-v1.0.3.zip
```

#### **Verify Download (Optional but Recommended)**
```bash
# Download checksum file
wget https://github.com/RicherTunes/Brainarr/releases/download/v1.0.3/Brainarr-v1.0.3.zip.sha256

# Verify integrity
sha256sum -c Brainarr-v1.0.3.zip.sha256
```

### **2. Extract to Plugins Directory**

**Find your Lidarr config directory:**

| Platform | Default Location |
|----------|------------------|
| **Linux** | `/home/username/.config/Lidarr/` |
| **Windows** | `C:\ProgramData\Lidarr\` |
| **macOS** | `~/Library/Application Support/Lidarr/` |
| **Docker** | Your mapped `/config` volume |

**Extract plugin:**
```bash
# Create plugins directory
mkdir -p /path/to/lidarr/config/plugins/

# Extract Brainarr
unzip Brainarr-v1.0.3.zip -d /path/to/lidarr/config/plugins/

# Verify structure
ls -la /path/to/lidarr/config/plugins/Brainarr/
# Should show: Lidarr.Plugin.Brainarr.dll + dependencies
```

### **3. Set Correct Permissions**

#### **Linux/macOS**
```bash
# Set ownership (replace 'lidarr' with your Lidarr user)
sudo chown -R lidarr:lidarr /path/to/lidarr/config/plugins/

# Set permissions
sudo chmod -R 755 /path/to/lidarr/config/plugins/
sudo chmod 644 /path/to/lidarr/config/plugins/Brainarr/*.dll
```

#### **Windows**
```powershell
# Run as Administrator - Set permissions for Lidarr service account
icacls "C:\ProgramData\Lidarr\plugins" /grant "NT AUTHORITY\NETWORK SERVICE":(OI)(CI)F
```

### **4. Restart Lidarr**

```bash
# Linux (systemd)
sudo systemctl restart lidarr

# Linux (manual)
sudo pkill lidarr && sudo -u lidarr /opt/Lidarr/Lidarr

# Windows (service)
Restart-Service -Name "Lidarr"

# macOS (launchd)
launchctl unload ~/Library/LaunchAgents/com.lidarr.Lidarr.plist
launchctl load ~/Library/LaunchAgents/com.lidarr.Lidarr.plist
```

---

## 🪟 **Platform-Specific Instructions**

### **Windows Installation**

#### **Windows Service Installation**
```powershell
# Download plugin
Invoke-WebRequest -Uri "https://github.com/RicherTunes/Brainarr/releases/download/v1.0.3/Brainarr-v1.0.3.zip" -OutFile "Brainarr.zip"

# Extract to plugins directory
$pluginPath = "C:\ProgramData\Lidarr\plugins\Brainarr"
Expand-Archive -Path "Brainarr.zip" -DestinationPath $pluginPath -Force

# Set permissions
icacls $pluginPath /grant "NT AUTHORITY\NETWORK SERVICE":(OI)(CI)F

# Restart Lidarr
Restart-Service -Name "Lidarr" -Force
```

#### **Windows Standalone Installation**
```powershell
# For portable/standalone Lidarr installations
$lidarrPath = "C:\Lidarr"  # Adjust to your Lidarr location
$configPath = "$lidarrPath\config\plugins\Brainarr"

# Create directory and extract
New-Item -ItemType Directory -Path $configPath -Force
Expand-Archive -Path "Brainarr.zip" -DestinationPath $configPath

# Restart Lidarr manually or via task manager
```

---

### **Linux Installation**

#### **Ubuntu/Debian**
```bash
# Install dependencies
sudo apt update
sudo apt install unzip wget

# Download and install
cd /tmp
wget https://github.com/RicherTunes/Brainarr/releases/download/v1.0.3/Brainarr-v1.0.3.zip
sudo mkdir -p /var/lib/lidarr/.config/Lidarr/plugins
sudo unzip Brainarr-v1.0.3.zip -d /var/lib/lidarr/.config/Lidarr/plugins/
sudo chown -R lidarr:lidarr /var/lib/lidarr/.config/Lidarr/plugins/
sudo systemctl restart lidarr
```

#### **CentOS/RHEL/Fedora**
```bash
# Install dependencies  
sudo dnf install unzip wget

# Download and install
wget https://github.com/RicherTunes/Brainarr/releases/download/v1.0.3/Brainarr-v1.0.3.zip
sudo unzip Brainarr-v1.0.3.zip -d /opt/lidarr/config/plugins/
sudo chown -R lidarr:lidarr /opt/lidarr/config/plugins/
sudo systemctl restart lidarr
```

#### **Docker on Linux**
```bash
# Using Docker directly
docker pull ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692

# Run with Brainarr support
docker run -d --name lidarr-brainarr \
  -p 8686:8686 \
  -v ./config:/config \
  -v ./music:/music \
  -e PUID=1000 -e PGID=1000 \
  ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692
```

---

### **macOS Installation**

#### **Homebrew Lidarr**
```bash
# Download plugin
cd ~/Downloads
curl -L https://github.com/RicherTunes/Brainarr/releases/download/v1.0.3/Brainarr-v1.0.3.zip -o Brainarr.zip

# Extract to Lidarr config
mkdir -p ~/Library/Application\ Support/Lidarr/plugins
unzip Brainarr.zip -d ~/Library/Application\ Support/Lidarr/plugins/

# Restart Lidarr
brew services restart lidarr
```

#### **Manual macOS Installation**
```bash
# For manually installed Lidarr
cd ~/Downloads
curl -L https://github.com/RicherTunes/Brainarr/releases/download/v1.0.3/Brainarr-v1.0.3.zip -o Brainarr.zip
mkdir -p /Applications/Lidarr.app/Contents/config/plugins
unzip Brainarr.zip -d /Applications/Lidarr.app/Contents/config/plugins/

# Restart Lidarr application
```

---

## ✅ **Installation Verification**

After installation, verify everything is working:

### **1. Check Plugin Load**
1. Open Lidarr web interface
2. **Settings** → **General** → **Updates**
3. Scroll to **"Plugins"** section
4. Should show: `Brainarr v1.0.3` ✅

### **2. Test Plugin Functionality**
1. **Settings** → **Import Lists** → **Add** (+)
2. **"Brainarr"** should appear in the list ✅
3. Click **"Brainarr"** to configure

### **3. Provider Test**
1. Configure any provider (see [[Provider Setup]])
2. Click **"Test"** button
3. Should show: **"Test was successful"** ✅

### **4. Check Logs**
1. **System** → **Logs**
2. Look for: `Brainarr plugin loaded successfully`
3. No error messages about missing dependencies ✅

---

## ❗ **Common Installation Issues**

### **Plugin Not Appearing**

**Cause**: Plugins not enabled or wrong Lidarr version

**Solutions:**
```bash
# Check Lidarr version
curl -s http://localhost:8686/api/v1/system/status | grep version

# Must be 2.13.1.4681+ with plugins support
# If not, update to plugins branch Docker image
```

### **Permission Denied Errors**

**Linux/macOS:**
```bash
# Fix ownership and permissions
sudo chown -R lidarr:lidarr /path/to/lidarr/config/
sudo chmod -R 755 /path/to/lidarr/config/plugins/
sudo systemctl restart lidarr
```

**Windows:**
```powershell
# Run PowerShell as Administrator
icacls "C:\ProgramData\Lidarr\plugins" /grant "Everyone":(OI)(CI)F
Restart-Service -Name "Lidarr" -Force
```

### **Assembly Load Errors**

**Symptoms**: Plugin loads but crashes when used

**Solutions:**
1. **Verify .NET Version**: Ensure .NET 6.0+ is installed
2. **Check Dependencies**: Re-extract plugin zip to ensure all DLLs present
3. **Clean Install**: Remove old plugin files before installing new version

```bash
# Clean installation
rm -rf /path/to/lidarr/config/plugins/Brainarr/
# Re-extract fresh copy
unzip Brainarr-v1.0.3.zip -d /path/to/lidarr/config/plugins/
```

### **Networking Issues**

**Docker Networking:**
```bash
# Ensure proper network access for AI providers
docker run --network host ...  # For local providers
# OR
docker run -p 8686:8686 ...     # For cloud providers
```

**Firewall Configuration:**
```bash
# Allow Lidarr through firewall (Linux)
sudo ufw allow 8686
sudo ufw allow out 443  # HTTPS for cloud providers
sudo ufw allow out 11434  # Ollama if using local
```

---

## 🔄 **Updating Brainarr**

### **Automatic Update (Future Feature)**
Brainarr will support automatic updates in a future release.

### **Manual Update Process**
1. **Download** latest release
2. **Stop** Lidarr service
3. **Backup** current plugin directory (optional)
4. **Replace** plugin files
5. **Start** Lidarr service
6. **Verify** functionality

```bash
# Quick update script
wget https://github.com/RicherTunes/Brainarr/releases/latest/download/Brainarr-latest.zip
sudo systemctl stop lidarr
sudo rm -rf /var/lib/lidarr/.config/Lidarr/plugins/Brainarr/
sudo unzip Brainarr-latest.zip -d /var/lib/lidarr/.config/Lidarr/plugins/
sudo chown -R lidarr:lidarr /var/lib/lidarr/.config/Lidarr/plugins/
sudo systemctl start lidarr
```

---

## 📋 **System Requirements**

### **Minimum Requirements**
- **Lidarr**: 2.13.1.4681+ (plugins branch)
- **.NET**: 6.0+ runtime
- **RAM**: 512MB additional for plugin
- **Storage**: 50MB for plugin files
- **Network**: Internet access for cloud providers

### **Recommended System**
- **CPU**: 2+ cores (for local AI providers)
- **RAM**: 2GB+ additional (for optimal caching)
- **Storage**: SSD for better performance
- **Network**: Stable broadband for cloud providers

### **For Local AI Providers**
- **RAM**: 8GB+ system memory
- **GPU**: NVIDIA recommended (optional but faster)
- **Storage**: 5-50GB for AI models
- **CPU**: 4+ cores for inference

---

## 🎯 **Next Steps**

After successful installation:

1. **[[Provider Setup]]** - Configure your preferred AI provider
2. **[[First Run Guide]]** - Generate your first recommendations  
3. **[[Advanced Settings]]** - Optimize for your library size
4. **[[Troubleshooting]]** - If you encounter any issues

**Installation successful?** You're ready for [[Provider Setup]]! 🚀