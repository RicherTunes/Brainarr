# Brainarr Deployment Guide

## Table of Contents
- [Deployment Methods](#deployment-methods)
- [Manual Installation](#manual-installation)
- [Docker Deployment](#docker-deployment)
- [Kubernetes Deployment](#kubernetes-deployment)
- [Docker Compose](#docker-compose)
- [Reverse Proxy Setup](#reverse-proxy-setup)
- [Monitoring Setup](#monitoring-setup)
- [Backup & Recovery](#backup--recovery)
- [High Availability](#high-availability)

## Deployment Methods

| Method | Complexity | Best For | Scalability |
|--------|------------|----------|-------------|
| Manual | Low | Single server | Limited |
| Docker | Medium | Containerized | Good |
| Docker Compose | Medium | Multi-container | Good |
| Kubernetes | High | Enterprise | Excellent |
| Unraid | Low | Home servers | Limited |
| TrueNAS | Low | NAS systems | Limited |

## Manual Installation

### Prerequisites

```bash
# Install .NET 6.0 Runtime
# Ubuntu/Debian
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install aspnetcore-runtime-6.0

# RHEL/CentOS/Fedora
sudo dnf install aspnetcore-runtime-6.0

# Arch Linux
sudo pacman -S aspnet-runtime-6.0
```

### Step-by-Step Installation

1. **Install Lidarr:**
```bash
# Using package manager (recommended)
# Ubuntu/Debian
curl -o lidarr.tar.gz -L "https://github.com/Lidarr/Lidarr/releases/download/v1.4.5.3639/Lidarr.master.1.4.5.3639.linux-core-x64.tar.gz"
tar -xvzf lidarr.tar.gz
sudo mv Lidarr /opt/

# Create service user
sudo useradd -r -s /bin/false lidarr
sudo chown -R lidarr:lidarr /opt/Lidarr
```

2. **Install Brainarr Plugin:**
```bash
# Create plugins directory
sudo mkdir -p /opt/Lidarr/plugins/Brainarr

# Extract plugin files
cd /tmp
wget https://github.com/your-repo/brainarr/releases/latest/download/Brainarr.zip
unzip Brainarr.zip -d /opt/Lidarr/plugins/Brainarr/

# Set permissions
sudo chown -R lidarr:lidarr /opt/Lidarr/plugins/
```

3. **Create Systemd Service:**
```ini
# /etc/systemd/system/lidarr.service
[Unit]
Description=Lidarr Daemon
After=network.target

[Service]
Type=simple
User=lidarr
Group=lidarr
ExecStart=/opt/Lidarr/Lidarr -nobrowser -data=/var/lib/lidarr
Restart=on-failure
RestartSec=10
TimeoutStopSec=20

# Security hardening
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/lidarr /opt/Lidarr/plugins
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
```

4. **Start Service:**
```bash
sudo systemctl daemon-reload
sudo systemctl enable lidarr
sudo systemctl start lidarr

# Verify
sudo systemctl status lidarr
sudo journalctl -u lidarr -f
```

5. **Configure Firewall:**
```bash
# UFW
sudo ufw allow 8686/tcp
sudo ufw reload

# firewalld
sudo firewall-cmd --permanent --add-port=8686/tcp
sudo firewall-cmd --reload
```

### Directory Structure

```
/opt/Lidarr/
├── Lidarr              # Main executable
├── plugins/
│   └── Brainarr/
│       ├── Lidarr.Plugin.Brainarr.dll
│       ├── plugin.json
│       └── [dependencies].dll
/var/lib/lidarr/
├── lidarr.db           # Database
├── config.xml          # Configuration
├── logs/               # Log files
└── .cache/brainarr/    # Recommendation cache
```

## Docker Deployment

### Official Docker Image

```bash
# Pull Lidarr image
docker pull linuxserver/lidarr:latest

# Create directories
mkdir -p ~/docker/lidarr/{config,music,downloads,plugins}

# Copy plugin files
cp -r /path/to/brainarr/plugin ~/docker/lidarr/plugins/Brainarr

# Run container
docker run -d \
  --name=lidarr \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=America/New_York \
  -p 8686:8686 \
  -v ~/docker/lidarr/config:/config \
  -v ~/docker/lidarr/plugins:/config/plugins \
  -v ~/music:/music \
  -v ~/downloads:/downloads \
  --restart unless-stopped \
  linuxserver/lidarr:latest
```

### Custom Dockerfile with Brainarr

```dockerfile
# Dockerfile
FROM linuxserver/lidarr:latest

# Install Brainarr plugin
COPY --chown=abc:abc ./Brainarr.Plugin /app/lidarr/plugins/Brainarr/

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD curl -f http://localhost:8686/api/v1/health || exit 1

# Labels for management
LABEL org.opencontainers.image.source="https://github.com/your-repo/brainarr"
LABEL org.opencontainers.image.description="Lidarr with Brainarr AI plugin"
LABEL org.opencontainers.image.version="1.0.0"
```

Build and run:
```bash
docker build -t lidarr-brainarr:latest .
docker run -d --name lidarr-brainarr \
  -p 8686:8686 \
  -v lidarr_config:/config \
  -v /path/to/music:/music \
  lidarr-brainarr:latest
```

## Docker Compose

### Basic Setup

```yaml
# docker-compose.yml
version: '3.8'

services:
  lidarr:
    image: linuxserver/lidarr:latest
    container_name: lidarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=UTC
    volumes:
      - ./config:/config
      - ./plugins:/config/plugins
      - /path/to/music:/music
      - /path/to/downloads:/downloads
    ports:
      - 8686:8686
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8686/api/v1/health"]
      interval: 30s
      timeout: 10s
      retries: 3
```

### With Local AI (Ollama)

```yaml
# docker-compose-with-ollama.yml
version: '3.8'

services:
  lidarr:
    image: linuxserver/lidarr:latest
    container_name: lidarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=UTC
    volumes:
      - lidarr_config:/config
      - ./plugins:/config/plugins
      - music:/music
      - downloads:/downloads
    ports:
      - 8686:8686
    depends_on:
      - ollama
    networks:
      - brainarr_net
    restart: unless-stopped

  ollama:
    image: ollama/ollama:latest
    container_name: ollama
    volumes:
      - ollama_data:/root/.ollama
    ports:
      - 11434:11434
    networks:
      - brainarr_net
    restart: unless-stopped
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu]  # Optional GPU support

volumes:
  lidarr_config:
  ollama_data:
  music:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /path/to/music
  downloads:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /path/to/downloads

networks:
  brainarr_net:
    driver: bridge
```

### Production Stack

```yaml
# docker-compose-production.yml
version: '3.8'

services:
  lidarr:
    image: linuxserver/lidarr:latest
    container_name: lidarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=${TZ:-UTC}
    volumes:
      - lidarr_config:/config
      - ./plugins:/config/plugins:ro
      - ${MUSIC_PATH}:/music
      - ${DOWNLOADS_PATH}:/downloads
    networks:
      - web
      - internal
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.lidarr.rule=Host(`lidarr.${DOMAIN}`)"
      - "traefik.http.routers.lidarr.entrypoints=websecure"
      - "traefik.http.routers.lidarr.tls.certresolver=letsencrypt"
      - "traefik.http.services.lidarr.loadbalancer.server.port=8686"
    restart: unless-stopped
    logging:
      driver: "json-file"
      options:
        max-size: "10m"
        max-file: "3"

  ollama:
    image: ollama/ollama:latest
    container_name: ollama
    volumes:
      - ollama_data:/root/.ollama
    networks:
      - internal
    restart: unless-stopped
    # No ports exposed - internal only

  traefik:
    image: traefik:v2.10
    container_name: traefik
    command:
      - "--api.dashboard=true"
      - "--providers.docker=true"
      - "--providers.docker.exposedbydefault=false"
      - "--entrypoints.web.address=:80"
      - "--entrypoints.websecure.address=:443"
      - "--certificatesresolvers.letsencrypt.acme.httpchallenge=true"
      - "--certificatesresolvers.letsencrypt.acme.httpchallenge.entrypoint=web"
      - "--certificatesresolvers.letsencrypt.acme.email=${EMAIL}"
      - "--certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json"
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - traefik_certs:/letsencrypt
    networks:
      - web
    restart: unless-stopped

  prometheus:
    image: prom/prometheus:latest
    container_name: prometheus
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prometheus_data:/prometheus
    networks:
      - internal
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
    restart: unless-stopped

  grafana:
    image: grafana/grafana:latest
    container_name: grafana
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD}
      - GF_INSTALL_PLUGINS=grafana-piechart-panel
    volumes:
      - grafana_data:/var/lib/grafana
      - ./grafana/dashboards:/etc/grafana/provisioning/dashboards:ro
      - ./grafana/datasources:/etc/grafana/provisioning/datasources:ro
    networks:
      - web
      - internal
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.grafana.rule=Host(`grafana.${DOMAIN}`)"
      - "traefik.http.routers.grafana.entrypoints=websecure"
      - "traefik.http.routers.grafana.tls.certresolver=letsencrypt"
    restart: unless-stopped

volumes:
  lidarr_config:
  ollama_data:
  traefik_certs:
  prometheus_data:
  grafana_data:

networks:
  web:
    external: true
  internal:
    internal: true
```

## Kubernetes Deployment

### Helm Chart

```yaml
# brainarr-helm/values.yaml
replicaCount: 1

image:
  repository: linuxserver/lidarr
  pullPolicy: IfNotPresent
  tag: "latest"

service:
  type: ClusterIP
  port: 8686

ingress:
  enabled: true
  className: nginx
  annotations:
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
  hosts:
    - host: lidarr.example.com
      paths:
        - path: /
          pathType: Prefix
  tls:
    - secretName: lidarr-tls
      hosts:
        - lidarr.example.com

persistence:
  config:
    enabled: true
    size: 10Gi
    storageClass: "fast-ssd"
  music:
    enabled: true
    size: 1Ti
    storageClass: "slow-hdd"
    existingClaim: "music-pvc"
  downloads:
    enabled: true
    size: 100Gi
    storageClass: "fast-ssd"

resources:
  limits:
    cpu: 2000m
    memory: 2Gi
  requests:
    cpu: 500m
    memory: 512Mi

env:
  - name: PUID
    value: "1000"
  - name: PGID
    value: "1000"
  - name: TZ
    value: "UTC"

# Brainarr plugin configuration
brainarr:
  enabled: true
  provider: "ollama"
  
ollama:
  enabled: true
  replicaCount: 1
  image: ollama/ollama:latest
  service:
    type: ClusterIP
    port: 11434
  persistence:
    enabled: true
    size: 50Gi
  resources:
    limits:
      nvidia.com/gpu: 1  # Optional GPU
```

### Kubernetes Manifests

```yaml
# lidarr-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: lidarr
  namespace: media
spec:
  replicas: 1
  selector:
    matchLabels:
      app: lidarr
  template:
    metadata:
      labels:
        app: lidarr
    spec:
      containers:
      - name: lidarr
        image: linuxserver/lidarr:latest
        ports:
        - containerPort: 8686
        env:
        - name: PUID
          value: "1000"
        - name: PGID
          value: "1000"
        - name: TZ
          value: "UTC"
        volumeMounts:
        - name: config
          mountPath: /config
        - name: music
          mountPath: /music
        - name: downloads
          mountPath: /downloads
        - name: plugins
          mountPath: /config/plugins
        livenessProbe:
          httpGet:
            path: /api/v1/health
            port: 8686
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /api/v1/health
            port: 8686
          initialDelaySeconds: 5
          periodSeconds: 5
        resources:
          requests:
            memory: "512Mi"
            cpu: "500m"
          limits:
            memory: "2Gi"
            cpu: "2000m"
      initContainers:
      - name: install-plugin
        image: busybox
        command: ['sh', '-c', 'cp -r /plugin/* /config/plugins/']
        volumeMounts:
        - name: plugin-source
          mountPath: /plugin
        - name: plugins
          mountPath: /config/plugins
      volumes:
      - name: config
        persistentVolumeClaim:
          claimName: lidarr-config
      - name: music
        persistentVolumeClaim:
          claimName: music-library
      - name: downloads
        persistentVolumeClaim:
          claimName: downloads
      - name: plugins
        emptyDir: {}
      - name: plugin-source
        configMap:
          name: brainarr-plugin

---
apiVersion: v1
kind: Service
metadata:
  name: lidarr
  namespace: media
spec:
  selector:
    app: lidarr
  ports:
  - port: 8686
    targetPort: 8686
  type: ClusterIP

---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: lidarr
  namespace: media
  annotations:
    kubernetes.io/ingress.class: nginx
    cert-manager.io/cluster-issuer: letsencrypt-prod
spec:
  tls:
  - hosts:
    - lidarr.example.com
    secretName: lidarr-tls
  rules:
  - host: lidarr.example.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: lidarr
            port:
              number: 8686
```

## Reverse Proxy Setup

### Nginx

```nginx
# /etc/nginx/sites-available/lidarr
server {
    listen 80;
    server_name lidarr.example.com;
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name lidarr.example.com;

    ssl_certificate /etc/letsencrypt/live/lidarr.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/lidarr.example.com/privkey.pem;

    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header Referrer-Policy "no-referrer-when-downgrade" always;

    # Proxy settings
    location / {
        proxy_pass http://localhost:8686;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # WebSocket support
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }
}
```

### Caddy

```caddyfile
# Caddyfile
lidarr.example.com {
    reverse_proxy localhost:8686 {
        header_up X-Real-IP {remote_host}
        header_up X-Forwarded-For {remote_host}
        header_up X-Forwarded-Proto {scheme}
    }
    
    # Security headers
    header {
        X-Frame-Options SAMEORIGIN
        X-Content-Type-Options nosniff
        X-XSS-Protection "1; mode=block"
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
    }
    
    # Rate limiting
    rate_limit {
        zone api {
            key {remote_host}
            events 100
            window 1m
        }
    }
}
```

### Traefik

```yaml
# traefik-dynamic.yml
http:
  routers:
    lidarr:
      rule: "Host(`lidarr.example.com`)"
      service: lidarr
      entryPoints:
        - websecure
      tls:
        certResolver: letsencrypt
      middlewares:
        - lidarr-auth
        - lidarr-headers

  services:
    lidarr:
      loadBalancer:
        servers:
          - url: "http://localhost:8686"

  middlewares:
    lidarr-auth:
      basicAuth:
        users:
          - "admin:$2y$10$..." # htpasswd encrypted

    lidarr-headers:
      headers:
        sslRedirect: true
        stsSeconds: 315360000
        stsIncludeSubdomains: true
        stsPreload: true
        customFrameOptionsValue: "SAMEORIGIN"
        contentTypeNosniff: true
        browserXssFilter: true
```

## Monitoring Setup

### Prometheus Configuration

```yaml
# prometheus.yml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'lidarr'
    static_configs:
      - targets: ['localhost:8686']
    metrics_path: '/api/v1/metrics'
    params:
      apikey: ['YOUR_LIDARR_API_KEY']

  - job_name: 'ollama'
    static_configs:
      - targets: ['localhost:11434']
    metrics_path: '/api/metrics'
```

### Grafana Dashboard

```json
{
  "dashboard": {
    "title": "Brainarr Monitoring",
    "panels": [
      {
        "title": "Recommendations Generated",
        "targets": [
          {
            "expr": "rate(brainarr_recommendations_total[5m])"
          }
        ]
      },
      {
        "title": "Provider Success Rate",
        "targets": [
          {
            "expr": "rate(brainarr_provider_success[5m]) / rate(brainarr_provider_requests[5m])"
          }
        ]
      },
      {
        "title": "Cache Hit Rate",
        "targets": [
          {
            "expr": "rate(brainarr_cache_hits[5m]) / rate(brainarr_cache_requests[5m])"
          }
        ]
      },
      {
        "title": "API Response Time",
        "targets": [
          {
            "expr": "histogram_quantile(0.95, brainarr_response_time_bucket)"
          }
        ]
      }
    ]
  }
}
```

## Backup & Recovery

### Automated Backup Script

```bash
#!/bin/bash
# backup-brainarr.sh

# Configuration
BACKUP_DIR="/backup/lidarr"
LIDARR_CONFIG="/var/lib/lidarr"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RETENTION_DAYS=30

# Create backup
echo "Starting Lidarr backup..."
mkdir -p "$BACKUP_DIR"

# Stop Lidarr to ensure consistency
systemctl stop lidarr

# Backup database
sqlite3 "$LIDARR_CONFIG/lidarr.db" ".backup '$BACKUP_DIR/lidarr_${TIMESTAMP}.db'"

# Backup configuration
tar -czf "$BACKUP_DIR/config_${TIMESTAMP}.tar.gz" \
  -C "$LIDARR_CONFIG" \
  config.xml \
  --exclude='*.log' \
  --exclude='*.txt'

# Backup plugin
tar -czf "$BACKUP_DIR/brainarr_${TIMESTAMP}.tar.gz" \
  -C "/opt/Lidarr" \
  plugins/Brainarr/

# Start Lidarr
systemctl start lidarr

# Upload to cloud (optional)
# aws s3 cp "$BACKUP_DIR/lidarr_${TIMESTAMP}.db" s3://backups/lidarr/
# rclone copy "$BACKUP_DIR" remote:backups/lidarr/

# Clean old backups
find "$BACKUP_DIR" -type f -mtime +$RETENTION_DAYS -delete

echo "Backup completed: $BACKUP_DIR/*_${TIMESTAMP}.*"
```

### Recovery Process

```bash
#!/bin/bash
# restore-brainarr.sh

# Configuration
BACKUP_FILE=$1
LIDARR_CONFIG="/var/lib/lidarr"

if [ -z "$BACKUP_FILE" ]; then
    echo "Usage: $0 <backup_file>"
    exit 1
fi

# Stop Lidarr
systemctl stop lidarr

# Restore database
cp "$BACKUP_FILE" "$LIDARR_CONFIG/lidarr.db"
chown lidarr:lidarr "$LIDARR_CONFIG/lidarr.db"

# Restore configuration if needed
# tar -xzf config_backup.tar.gz -C "$LIDARR_CONFIG"

# Start Lidarr
systemctl start lidarr

echo "Restore completed from $BACKUP_FILE"
```

## High Availability

### Active-Passive Setup

```yaml
# docker-compose-ha.yml
version: '3.8'

services:
  lidarr-primary:
    image: linuxserver/lidarr:latest
    environment:
      - PUID=1000
      - PGID=1000
    volumes:
      - nfs-config:/config
      - nfs-music:/music
    labels:
      - "keepalived.vip=192.168.1.100"
      - "keepalived.priority=100"
    restart: unless-stopped

  lidarr-secondary:
    image: linuxserver/lidarr:latest
    environment:
      - PUID=1000
      - PGID=1000
    volumes:
      - nfs-config:/config  # Shared storage
      - nfs-music:/music    # Shared storage
    labels:
      - "keepalived.vip=192.168.1.100"
      - "keepalived.priority=50"
    restart: unless-stopped

  keepalived:
    image: osixia/keepalived:stable
    cap_add:
      - NET_ADMIN
    environment:
      - KEEPALIVED_INTERFACE=eth0
      - KEEPALIVED_VIRTUAL_IPS=192.168.1.100
      - KEEPALIVED_PRIORITY=100
      - KEEPALIVED_UNICAST_PEERS=192.168.1.101,192.168.1.102
    restart: unless-stopped

volumes:
  nfs-config:
    driver: local
    driver_opts:
      type: nfs
      o: addr=nfs-server.local,rw,sync
      device: ":/exports/lidarr/config"
  
  nfs-music:
    driver: local
    driver_opts:
      type: nfs
      o: addr=nfs-server.local,rw
      device: ":/exports/music"
```

## Performance Tuning

### System Requirements

| Component | Minimum | Recommended | Optimal |
|-----------|---------|-------------|---------|
| CPU | 2 cores | 4 cores | 8+ cores |
| RAM | 2 GB | 4 GB | 8+ GB |
| Storage | 10 GB | 50 GB | 100+ GB SSD |
| Network | 10 Mbps | 100 Mbps | 1 Gbps |

### Optimization Tips

```bash
# Increase file descriptors
echo "lidarr soft nofile 65536" >> /etc/security/limits.conf
echo "lidarr hard nofile 65536" >> /etc/security/limits.conf

# Optimize SQLite
sqlite3 /var/lib/lidarr/lidarr.db "PRAGMA optimize;"
sqlite3 /var/lib/lidarr/lidarr.db "VACUUM;"

# Configure swap for low-memory systems
dd if=/dev/zero of=/swapfile bs=1G count=4
chmod 600 /swapfile
mkswap /swapfile
swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab

# Tune kernel parameters
cat >> /etc/sysctl.conf <<EOF
vm.swappiness = 10
net.core.somaxconn = 1024
net.ipv4.tcp_fin_timeout = 30
EOF
sysctl -p
```

## Health Checks

### Monitoring Endpoints

```bash
# Lidarr health
curl http://localhost:8686/api/v1/health

# Ollama health
curl http://localhost:11434/api/tags

# Combined health check script
#!/bin/bash
LIDARR_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8686/api/v1/health)
OLLAMA_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:11434/api/tags)

if [ "$LIDARR_STATUS" != "200" ] || [ "$OLLAMA_STATUS" != "200" ]; then
    echo "Health check failed: Lidarr=$LIDARR_STATUS, Ollama=$OLLAMA_STATUS"
    exit 1
fi
echo "All services healthy"
```

## Troubleshooting Deployment

### Common Issues

1. **Permission Denied:**
```bash
# Fix permissions
chown -R 1000:1000 /path/to/config
chmod -R 755 /path/to/config
```

2. **Port Already in Use:**
```bash
# Find process using port
lsof -i :8686
netstat -tulpn | grep 8686

# Change port in docker-compose
ports:
  - "8687:8686"  # Use different host port
```

3. **Container Restart Loop:**
```bash
# Check logs
docker logs lidarr --tail 50

# Interactive debug
docker run -it --rm \
  -v /path/to/config:/config \
  linuxserver/lidarr:latest \
  /bin/bash
```

4. **Database Locked:**
```bash
# Stop all services
docker-compose down

# Check database integrity
sqlite3 /path/to/lidarr.db "PRAGMA integrity_check;"

# Fix if needed
sqlite3 /path/to/lidarr.db ".recover" | sqlite3 recovered.db
mv recovered.db lidarr.db
```

## Support

For deployment assistance:
- Check logs first: `docker logs lidarr`
- Review this deployment guide
- Consult [TROUBLESHOOTING.md](TROUBLESHOOTING.md)
- Open GitHub issue with deployment details