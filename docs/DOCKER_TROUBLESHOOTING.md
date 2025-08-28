# Docker Troubleshooting Guide for Brainarr

## Overview
This guide helps resolve common issues when running Brainarr with Docker-based Lidarr installations.

## Quick Diagnosis

```bash
# Check if Lidarr is running
docker ps | grep lidarr

# Check Lidarr version (must be nightly branch)
docker exec lidarr cat /app/lidarr/version.txt

# Check if plugins directory exists
docker exec lidarr ls -la /config/plugins/

# Check Brainarr plugin files
docker exec lidarr ls -la /config/plugins/Brainarr/
```

## Common Issues & Solutions

### Issue 1: Plugin Not Appearing After Installation

**Symptoms**: Installed Brainarr but it doesn't show in Import Lists

**Solutions**:

1. **Verify nightly branch**:
```bash
# Check branch
docker exec lidarr grep -i branch /config/config.xml

# Switch to nightly if needed (backup first!)
docker exec lidarr sed -i 's/<Branch>.*<\/Branch>/<Branch>nightly<\/Branch>/' /config/config.xml
docker restart lidarr
```

2. **Check plugin directory permissions**:
```bash
# Fix permissions
docker exec lidarr chown -R abc:abc /config/plugins/Brainarr
docker exec lidarr chmod -R 755 /config/plugins/Brainarr
```

3. **Verify plugin structure**:
```bash
# Should see these files
docker exec lidarr ls /config/plugins/Brainarr/ | grep -E "(dll|json)"
# Expected: Lidarr.Plugin.Brainarr.dll, plugin.json
```

### Issue 2: Connection to Local AI Provider Fails

**Symptoms**: Ollama/LM Studio connection refused from Docker

**Solutions**:

1. **For Ollama on host machine**:
```yaml
# docker-compose.yml
services:
  lidarr:
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

Configure provider URL as: `http://host.docker.internal:11434`

2. **For Ollama in Docker**:
```yaml
# docker-compose.yml
services:
  ollama:
    image: ollama/ollama
    ports:
      - "11434:11434"
    volumes:
      - ./ollama:/root/.ollama
    
  lidarr:
    depends_on:
      - ollama
```

Configure provider URL as: `http://ollama:11434`

3. **Network debugging**:
```bash
# Test connection from Lidarr container
docker exec lidarr wget -qO- http://host.docker.internal:11434/api/tags
```

### Issue 3: Plugin Fails to Load

**Symptoms**: Error in logs about missing assemblies

**Solutions**:

1. **Check .NET runtime**:
```bash
# Verify .NET version
docker exec lidarr dotnet --version
# Should be 6.0 or higher
```

2. **Reinstall plugin with correct version**:
```bash
# Remove old plugin
docker exec lidarr rm -rf /config/plugins/Brainarr

# Download correct version
docker exec lidarr wget https://github.com/RicherTunes/Brainarr/releases/latest/download/Brainarr.zip -O /tmp/brainarr.zip
docker exec lidarr unzip /tmp/brainarr.zip -d /config/plugins/Brainarr
docker exec lidarr chown -R abc:abc /config/plugins/Brainarr
docker restart lidarr
```

### Issue 4: High Memory Usage

**Symptoms**: Container using excessive RAM with AI providers

**Solutions**:

1. **Limit container memory**:
```yaml
# docker-compose.yml
services:
  lidarr:
    mem_limit: 2g
    memswap_limit: 2g
```

2. **Configure cache limits** in Brainarr settings:
- Set Cache Duration: 6 hours
- Enable rate limiting
- Use lighter AI models

### Issue 5: Plugin Updates Not Working

**Symptoms**: Can't update Brainarr through UI

**Solutions**:

1. **Manual update process**:
```bash
# Backup current plugin
docker exec lidarr cp -r /config/plugins/Brainarr /config/plugins/Brainarr.backup

# Download new version
docker exec lidarr wget https://github.com/RicherTunes/Brainarr/releases/latest/download/Brainarr.zip -O /tmp/brainarr.zip

# Extract and replace
docker exec lidarr unzip -o /tmp/brainarr.zip -d /config/plugins/Brainarr

# Fix permissions
docker exec lidarr chown -R abc:abc /config/plugins/Brainarr

# Restart
docker restart lidarr
```

2. **Automated update script**:
```bash
#!/bin/bash
# save as update-brainarr.sh

CONTAINER="lidarr"
PLUGIN_DIR="/config/plugins/Brainarr"

echo "Updating Brainarr plugin..."
docker exec $CONTAINER wget -q https://github.com/RicherTunes/Brainarr/releases/latest/download/Brainarr.zip -O /tmp/brainarr.zip
docker exec $CONTAINER unzip -qo /tmp/brainarr.zip -d $PLUGIN_DIR
docker exec $CONTAINER chown -R abc:abc $PLUGIN_DIR
docker restart $CONTAINER
echo "Update complete!"
```

### Issue 6: Logs Not Showing Plugin Activity

**Symptoms**: Can't see Brainarr logs for debugging

**Solutions**:

1. **Enable debug logging**:
```bash
# Edit Lidarr config
docker exec lidarr sed -i 's/<LogLevel>.*<\/LogLevel>/<LogLevel>debug<\/LogLevel>/' /config/config.xml
docker restart lidarr
```

2. **View plugin-specific logs**:
```bash
# Real-time logs
docker logs -f lidarr 2>&1 | grep -i brainarr

# Last 100 plugin entries
docker exec lidarr grep -i brainarr /config/logs/lidarr.txt | tail -100
```

## Docker Compose Examples

### Basic Setup
```yaml
version: '3.8'
services:
  lidarr:
    image: lscr.io/linuxserver/lidarr:nightly
    container_name: lidarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=UTC
    volumes:
      - ./config:/config
      - ./music:/music
      - ./downloads:/downloads
    ports:
      - 8686:8686
    restart: unless-stopped
```

### With Ollama Integration
```yaml
version: '3.8'
services:
  lidarr:
    image: lscr.io/linuxserver/lidarr:nightly
    container_name: lidarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=UTC
    volumes:
      - ./config:/config
      - ./music:/music
    ports:
      - 8686:8686
    extra_hosts:
      - "host.docker.internal:host-gateway"
    restart: unless-stopped

  ollama:
    image: ollama/ollama
    container_name: ollama
    ports:
      - 11434:11434
    volumes:
      - ./ollama:/root/.ollama
    restart: unless-stopped
```

## Verification Commands

```bash
# Full health check
docker exec lidarr sh -c '
echo "=== Lidarr Version ==="
cat /app/lidarr/version.txt
echo -e "\n=== .NET Version ==="
dotnet --version 2>/dev/null || echo ".NET not found"
echo -e "\n=== Plugin Directory ==="
ls -la /config/plugins/ 2>/dev/null || echo "No plugins directory"
echo -e "\n=== Brainarr Plugin ==="
ls -la /config/plugins/Brainarr/*.dll 2>/dev/null || echo "Brainarr not installed"
echo -e "\n=== Recent Logs ==="
grep -i brainarr /config/logs/lidarr.txt 2>/dev/null | tail -5 || echo "No Brainarr logs"
'
```

## Getting Help

If issues persist:

1. **Collect diagnostic info**:
```bash
# Save to file
docker exec lidarr sh -c 'echo "=== System Info ===" && uname -a && echo "=== Lidarr Version ===" && cat /app/lidarr/version.txt && echo "=== Plugin Files ===" && ls -la /config/plugins/Brainarr/ && echo "=== Recent Errors ===" && grep ERROR /config/logs/lidarr.txt | grep -i brainarr | tail -20' > brainarr-debug.txt
```

2. **Check GitHub Issues**: https://github.com/RicherTunes/Brainarr/issues

3. **Provide details**:
- Docker compose file (remove sensitive data)
- Lidarr version
- Error messages from logs
- Steps to reproduce

## Performance Tips

- Use volume mounts for `/config` (not bind mounts)
- Allocate sufficient RAM (2GB minimum recommended)
- Use SSD for config directory if possible
- Enable Docker BuildKit for faster rebuilds
- Consider using Docker networks instead of exposed ports

## Security Notes

- Never expose Lidarr directly to internet
- Use reverse proxy with authentication
- Keep API keys in `.env` files
- Regularly update both Lidarr and Brainarr
- Review container permissions (PUID/PGID)