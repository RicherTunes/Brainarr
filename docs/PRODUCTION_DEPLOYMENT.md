# Brainarr Production Deployment Guide

## Overview

This guide provides comprehensive instructions for deploying Brainarr in production environments, covering security, scalability, monitoring, and best practices for enterprise deployments.

## Pre-Deployment Checklist

### System Requirements

#### Minimum Requirements (1-50 Users)
- **CPU**: 2 cores (x86_64 or ARM64)
- **RAM**: 2GB (4GB recommended)
- **Storage**: 10GB available space
- **Network**: 10 Mbps stable connection
- **OS**: Linux (Ubuntu 20.04+, Debian 11+, RHEL 8+), Windows Server 2019+, Docker 20.10+

#### Standard Requirements (50-500 Users)
- **CPU**: 4 cores
- **RAM**: 8GB
- **Storage**: 50GB SSD
- **Network**: 100 Mbps
- **Database**: SQLite with WAL mode or PostgreSQL 13+

#### Enterprise Requirements (500+ Users)
- **CPU**: 8+ cores
- **RAM**: 16GB+
- **Storage**: 100GB+ NVMe SSD
- **Network**: 1 Gbps
- **Database**: PostgreSQL 13+ with replication
- **Load Balancer**: Nginx/HAProxy
- **Cache**: Redis 6+

### Security Audit

```bash
#!/bin/bash
# Pre-deployment security check

echo "=== Brainarr Production Security Audit ==="

# Check file permissions
echo "Checking plugin permissions..."
find /var/lib/lidarr/plugins/Brainarr -type f -exec ls -l {} \; | grep -E "^-rw[x-]"

# Verify no hardcoded credentials
echo "Scanning for potential secrets..."
grep -r -E "(api[_-]?key|token|secret|password)" /var/lib/lidarr/plugins/Brainarr/*.json

# Check network exposure
echo "Checking exposed ports..."
netstat -tuln | grep -E ":(8686|11434|1234)"

# Verify TLS configuration
echo "Checking TLS settings..."
openssl s_client -connect localhost:8686 -tls1_2 2>/dev/null | grep "Cipher"
```

## Deployment Methods

### Method 1: Docker Deployment (Recommended)

#### Production Docker Compose

```yaml
version: '3.8'

services:
  lidarr:
    image: linuxserver/lidarr:nightly
    container_name: lidarr_prod
    restart: unless-stopped
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=UTC
      - UMASK=002
    volumes:
      - ./config:/config
      - ./music:/music
      - ./downloads:/downloads
      - ./plugins:/config/plugins:ro
    ports:
      - "127.0.0.1:8686:8686"  # Bind to localhost only
    networks:
      - brainarr_network
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8686/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
    deploy:
      resources:
        limits:
          memory: 2G
          cpus: '2'
        reservations:
          memory: 512M
          cpus: '0.5'

  # Local AI Provider (Optional)
  ollama:
    image: ollama/ollama:latest
    container_name: ollama_prod
    restart: unless-stopped
    volumes:
      - ./ollama:/root/.ollama
    ports:
      - "127.0.0.1:11434:11434"
    networks:
      - brainarr_network
    deploy:
      resources:
        limits:
          memory: 4G
          cpus: '4'

  # Reverse Proxy
  nginx:
    image: nginx:alpine
    container_name: nginx_prod
    restart: unless-stopped
    ports:
      - "443:443"
      - "80:80"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
      - ./ssl:/etc/nginx/ssl:ro
      - ./nginx_logs:/var/log/nginx
    networks:
      - brainarr_network
    depends_on:
      - lidarr

  # Monitoring
  prometheus:
    image: prom/prometheus:latest
    container_name: prometheus_prod
    restart: unless-stopped
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prometheus_data:/prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
      - '--storage.tsdb.retention.time=30d'
    ports:
      - "127.0.0.1:9090:9090"
    networks:
      - brainarr_network

networks:
  brainarr_network:
    driver: bridge
    ipam:
      config:
        - subnet: 172.20.0.0/24

volumes:
  prometheus_data:
```

#### Nginx Configuration

```nginx
# /etc/nginx/nginx.conf
user nginx;
worker_processes auto;
error_log /var/log/nginx/error.log warn;
pid /var/run/nginx.pid;

events {
    worker_connections 2048;
    use epoll;
}

http {
    include /etc/nginx/mime.types;
    default_type application/octet-stream;

    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header Referrer-Policy "no-referrer-when-downgrade" always;
    add_header Content-Security-Policy "default-src 'self' http: https: data: blob: 'unsafe-inline'" always;

    # Rate limiting
    limit_req_zone $binary_remote_addr zone=api_limit:10m rate=10r/s;
    limit_conn_zone $binary_remote_addr zone=addr_limit:10m;

    # SSL configuration
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers off;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 10m;

    # Logging
    log_format main '$remote_addr - $remote_user [$time_local] "$request" '
                    '$status $body_bytes_sent "$http_referer" '
                    '"$http_user_agent" "$http_x_forwarded_for" '
                    'rt=$request_time uct="$upstream_connect_time" '
                    'uht="$upstream_header_time" urt="$upstream_response_time"';

    access_log /var/log/nginx/access.log main buffer=32k;

    # Performance
    sendfile on;
    tcp_nopush on;
    tcp_nodelay on;
    keepalive_timeout 65;
    types_hash_max_size 2048;
    client_max_body_size 100M;

    # Gzip
    gzip on;
    gzip_vary on;
    gzip_proxied any;
    gzip_comp_level 6;
    gzip_types text/plain text/css text/xml text/javascript 
               application/json application/javascript application/xml+rss 
               application/rss+xml application/atom+xml image/svg+xml 
               text/x-js text/x-cross-domain-policy application/x-font-ttf 
               application/x-font-opentype application/vnd.ms-fontobject 
               image/x-icon;

    upstream lidarr_backend {
        least_conn;
        server lidarr:8686 max_fails=3 fail_timeout=30s;
        keepalive 32;
    }

    server {
        listen 80;
        server_name lidarr.example.com;
        return 301 https://$server_name$request_uri;
    }

    server {
        listen 443 ssl http2;
        server_name lidarr.example.com;

        ssl_certificate /etc/nginx/ssl/fullchain.pem;
        ssl_certificate_key /etc/nginx/ssl/privkey.pem;

        # Security
        limit_req zone=api_limit burst=20 nodelay;
        limit_conn addr_limit 100;

        location / {
            proxy_pass http://lidarr_backend;
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
            
            # Buffering
            proxy_buffering off;
            proxy_request_buffering off;
        }

        # API rate limiting
        location /api/ {
            limit_req zone=api_limit burst=10 nodelay;
            proxy_pass http://lidarr_backend;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
        }

        # Health check endpoint
        location /health {
            access_log off;
            proxy_pass http://lidarr_backend/health;
        }
    }
}
```

### Method 2: Kubernetes Deployment

#### Kubernetes Manifests

```yaml
# brainarr-deployment.yaml
apiVersion: v1
kind: Namespace
metadata:
  name: brainarr-prod

---
apiVersion: v1
kind: ConfigMap
metadata:
  name: brainarr-config
  namespace: brainarr-prod
data:
  TZ: "UTC"
  PUID: "1000"
  PGID: "1000"

---
apiVersion: v1
kind: Secret
metadata:
  name: brainarr-secrets
  namespace: brainarr-prod
type: Opaque
stringData:
  openai-key: "sk-..."
  anthropic-key: "sk-ant-..."

---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: lidarr
  namespace: brainarr-prod
spec:
  replicas: 3
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
        image: linuxserver/lidarr:nightly
        ports:
        - containerPort: 8686
        envFrom:
        - configMapRef:
            name: brainarr-config
        volumeMounts:
        - name: config
          mountPath: /config
        - name: plugins
          mountPath: /config/plugins
          readOnly: true
        resources:
          requests:
            memory: "512Mi"
            cpu: "500m"
          limits:
            memory: "2Gi"
            cpu: "2"
        livenessProbe:
          httpGet:
            path: /health
            port: 8686
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /api/v1/system/status
            port: 8686
          initialDelaySeconds: 5
          periodSeconds: 5
      volumes:
      - name: config
        persistentVolumeClaim:
          claimName: lidarr-config-pvc
      - name: plugins
        configMap:
          name: brainarr-plugin

---
apiVersion: v1
kind: Service
metadata:
  name: lidarr-service
  namespace: brainarr-prod
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
  name: lidarr-ingress
  namespace: brainarr-prod
  annotations:
    kubernetes.io/ingress.class: nginx
    cert-manager.io/cluster-issuer: letsencrypt-prod
    nginx.ingress.kubernetes.io/rate-limit: "10"
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
            name: lidarr-service
            port:
              number: 8686

---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: lidarr-hpa
  namespace: brainarr-prod
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: lidarr
  minReplicas: 2
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

### Method 3: Bare Metal Deployment

#### System Setup

```bash
#!/bin/bash
# Production deployment script for bare metal

# Create dedicated user
sudo useradd -r -s /bin/false brainarr
sudo usermod -aG lidarr brainarr

# Create directories
sudo mkdir -p /opt/brainarr/{config,logs,cache}
sudo mkdir -p /var/lib/lidarr/plugins/Brainarr

# Set permissions
sudo chown -R brainarr:lidarr /opt/brainarr
sudo chmod 750 /opt/brainarr
sudo chmod 750 /var/lib/lidarr/plugins/Brainarr

# Install plugin
cd /tmp
wget https://github.com/yourusername/Brainarr/releases/latest/download/Brainarr.zip
sudo unzip -o Brainarr.zip -d /var/lib/lidarr/plugins/Brainarr/
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr/

# Configure systemd service
cat <<EOF | sudo tee /etc/systemd/system/brainarr-monitor.service
[Unit]
Description=Brainarr Plugin Monitor
After=lidarr.service
Requires=lidarr.service

[Service]
Type=simple
User=brainarr
Group=lidarr
ExecStart=/usr/local/bin/brainarr-monitor
Restart=always
RestartSec=10
StandardOutput=append:/opt/brainarr/logs/monitor.log
StandardError=append:/opt/brainarr/logs/error.log

[Install]
WantedBy=multi-user.target
EOF

# Create monitoring script
cat <<'SCRIPT' | sudo tee /usr/local/bin/brainarr-monitor
#!/bin/bash
while true; do
    # Check plugin health
    if ! curl -sf http://localhost:8686/health > /dev/null; then
        echo "$(date): Lidarr health check failed" >> /opt/brainarr/logs/monitor.log
        systemctl restart lidarr
    fi
    
    # Check memory usage
    MEM_USAGE=$(ps aux | grep -i lidarr | grep -v grep | awk '{print $4}')
    if (( $(echo "$MEM_USAGE > 80" | bc -l) )); then
        echo "$(date): High memory usage: $MEM_USAGE%" >> /opt/brainarr/logs/monitor.log
    fi
    
    sleep 60
done
SCRIPT

sudo chmod +x /usr/local/bin/brainarr-monitor
sudo systemctl enable brainarr-monitor
sudo systemctl start brainarr-monitor
```

## Configuration Management

### Environment-Specific Settings

```yaml
# production.yaml
brainarr:
  providers:
    primary:
      type: openai
      model: gpt-4o-mini
      api_key: ${OPENAI_API_KEY}
      timeout: 30
      max_retries: 3
    fallback:
      type: anthropic
      model: claude-3-haiku
      api_key: ${ANTHROPIC_API_KEY}
      timeout: 45
      max_retries: 2
  cache:
    enabled: true
    duration_hours: 24
    max_size_mb: 500
    cleanup_interval_hours: 6
  performance:
    max_concurrent_requests: 5
    rate_limit_per_minute: 30
    sampling_strategy: balanced
  security:
    api_key_rotation_days: 90
    audit_logging: true
    encrypt_cache: true
```

### Secrets Management

#### Using HashiCorp Vault

```bash
# Store API keys in Vault
vault kv put secret/brainarr/prod \
    openai_key="sk-..." \
    anthropic_key="sk-ant-..." \
    groq_key="gsk_..."

# Retrieve secrets in application
export OPENAI_API_KEY=$(vault kv get -field=openai_key secret/brainarr/prod)
export ANTHROPIC_API_KEY=$(vault kv get -field=anthropic_key secret/brainarr/prod)
```

#### Using Kubernetes Secrets

```bash
# Create secrets
kubectl create secret generic brainarr-api-keys \
    --from-literal=openai-key="sk-..." \
    --from-literal=anthropic-key="sk-ant-..." \
    -n brainarr-prod

# Rotate secrets
kubectl create secret generic brainarr-api-keys-new \
    --from-literal=openai-key="sk-new..." \
    --from-literal=anthropic-key="sk-ant-new..." \
    -n brainarr-prod

kubectl patch deployment lidarr -n brainarr-prod \
    -p '{"spec":{"template":{"spec":{"containers":[{"name":"lidarr","env":[{"name":"API_KEY_VERSION","value":"v2"}]}]}}}}'
```

## Monitoring and Observability

### Prometheus Metrics

```yaml
# prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'brainarr'
    static_configs:
      - targets: ['lidarr:8686']
    metrics_path: '/metrics'
    
  - job_name: 'node'
    static_configs:
      - targets: ['node-exporter:9100']

rule_files:
  - 'alerts.yml'

alerting:
  alertmanagers:
    - static_configs:
        - targets: ['alertmanager:9093']
```

### Alert Rules

```yaml
# alerts.yml
groups:
  - name: brainarr_alerts
    interval: 30s
    rules:
      - alert: HighErrorRate
        expr: rate(brainarr_errors_total[5m]) > 0.05
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High error rate detected"
          description: "Error rate is {{ $value }} errors per second"
      
      - alert: SlowResponseTime
        expr: histogram_quantile(0.95, brainarr_request_duration_seconds_bucket) > 10
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Slow response times"
          description: "95th percentile response time is {{ $value }} seconds"
      
      - alert: LowCacheHitRate
        expr: rate(brainarr_cache_hits_total[5m]) / rate(brainarr_cache_requests_total[5m]) < 0.5
        for: 10m
        labels:
          severity: info
        annotations:
          summary: "Low cache hit rate"
          description: "Cache hit rate is {{ $value }}"
      
      - alert: HighMemoryUsage
        expr: process_resident_memory_bytes / 1024 / 1024 / 1024 > 2
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High memory usage"
          description: "Process using {{ $value }}GB of memory"
```

### Logging Strategy

```xml
<!-- NLog.config for production -->
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  
  <targets>
    <!-- Async wrapper for performance -->
    <target name="file" xsi:type="AsyncWrapper" queueLimit="5000">
      <target xsi:type="File"
              fileName="${basedir}/logs/brainarr-${shortdate}.log"
              archiveEvery="Day"
              maxArchiveFiles="30"
              layout="${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}" />
    </target>
    
    <!-- Structured logging for analysis -->
    <target name="json" xsi:type="File"
            fileName="${basedir}/logs/brainarr-${shortdate}.json"
            layout="${json-encode:${event-properties:item=EventProperties}}" />
    
    <!-- Syslog for centralized logging -->
    <target name="syslog" xsi:type="Syslog"
            syslogserver="syslog.example.com"
            port="514"
            facility="Local0"
            sender="Brainarr"
            layout="${message}" />
  </targets>
  
  <rules>
    <!-- Production: only warnings and above -->
    <logger name="*" minlevel="Warn" writeTo="file,json,syslog" />
    
    <!-- Detailed logging for Brainarr namespace -->
    <logger name="Brainarr.*" minlevel="Info" writeTo="file,json" />
    
    <!-- Performance logging -->
    <logger name="Brainarr.Performance" minlevel="Debug" writeTo="json" />
  </rules>
</nlog>
```

## Security Hardening

### API Key Rotation

```bash
#!/bin/bash
# Automated API key rotation script

# Rotate OpenAI key
NEW_KEY=$(openai api keys create --name "brainarr-prod-$(date +%Y%m%d)")
OLD_KEY=$(kubectl get secret brainarr-api-keys -o jsonpath='{.data.openai-key}' | base64 -d)

# Update secret
kubectl patch secret brainarr-api-keys \
    -p "{\"data\":{\"openai-key\":\"$(echo -n $NEW_KEY | base64)\"}}"

# Wait for rollout
kubectl rollout status deployment/lidarr -n brainarr-prod

# Revoke old key
openai api keys delete $OLD_KEY

# Log rotation
echo "$(date): Rotated OpenAI API key" >> /var/log/brainarr/key-rotation.log
```

### Network Security

```yaml
# NetworkPolicy for Kubernetes
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: brainarr-netpol
  namespace: brainarr-prod
spec:
  podSelector:
    matchLabels:
      app: lidarr
  policyTypes:
  - Ingress
  - Egress
  ingress:
  - from:
    - namespaceSelector:
        matchLabels:
          name: ingress-nginx
    ports:
    - protocol: TCP
      port: 8686
  egress:
  - to:
    - namespaceSelector: {}
    ports:
    - protocol: TCP
      port: 443  # HTTPS for API providers
    - protocol: TCP
      port: 11434  # Ollama
  - to:
    - namespaceSelector:
        matchLabels:
          name: kube-system
    ports:
    - protocol: TCP
      port: 53  # DNS
    - protocol: UDP
      port: 53
```

## Backup and Recovery

### Automated Backup Script

```bash
#!/bin/bash
# Brainarr backup script

BACKUP_DIR="/backup/brainarr"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_NAME="brainarr_backup_${TIMESTAMP}"

# Create backup directory
mkdir -p "${BACKUP_DIR}/${BACKUP_NAME}"

# Backup Lidarr database
sqlite3 /config/lidarr.db ".backup '${BACKUP_DIR}/${BACKUP_NAME}/lidarr.db'"

# Backup plugin configuration
cp -r /var/lib/lidarr/plugins/Brainarr "${BACKUP_DIR}/${BACKUP_NAME}/"

# Backup cache if needed
cp -r /opt/brainarr/cache "${BACKUP_DIR}/${BACKUP_NAME}/"

# Create tarball
cd "${BACKUP_DIR}"
tar -czf "${BACKUP_NAME}.tar.gz" "${BACKUP_NAME}/"
rm -rf "${BACKUP_NAME}/"

# Upload to S3 (optional)
aws s3 cp "${BACKUP_NAME}.tar.gz" s3://backup-bucket/brainarr/

# Keep only last 30 backups
find "${BACKUP_DIR}" -name "brainarr_backup_*.tar.gz" -mtime +30 -delete

echo "Backup completed: ${BACKUP_NAME}.tar.gz"
```

### Disaster Recovery Plan

```markdown
## Brainarr Disaster Recovery Runbook

### Recovery Time Objectives
- RTO (Recovery Time Objective): 2 hours
- RPO (Recovery Point Objective): 24 hours

### Recovery Procedures

1. **Service Failure**
   ```bash
   # Quick restart
   kubectl rollout restart deployment/lidarr -n brainarr-prod
   
   # Check logs
   kubectl logs -f deployment/lidarr -n brainarr-prod --tail=100
   ```

2. **Database Corruption**
   ```bash
   # Stop service
   kubectl scale deployment/lidarr --replicas=0 -n brainarr-prod
   
   # Restore from backup
   sqlite3 /config/lidarr.db ".restore '/backup/latest/lidarr.db'"
   
   # Start service
   kubectl scale deployment/lidarr --replicas=3 -n brainarr-prod
   ```

3. **Complete System Failure**
   ```bash
   # Deploy to alternate region
   kubectl apply -f brainarr-deployment-dr.yaml --context=dr-cluster
   
   # Restore data
   ./restore-from-backup.sh latest
   
   # Update DNS
   ./update-dns.sh dr-region
   ```
```

## Performance Tuning for Production

### Database Optimization

```sql
-- Optimize SQLite for production
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA cache_size = 10000;
PRAGMA temp_store = MEMORY;
PRAGMA mmap_size = 30000000000;

-- Create indexes for Brainarr queries
CREATE INDEX idx_artists_monitored ON Artists(Monitored);
CREATE INDEX idx_albums_artist ON Albums(ArtistId, ReleaseDate);
CREATE INDEX idx_artist_metadata ON ArtistMetadata(ForeignArtistId);

-- Vacuum and analyze
VACUUM;
ANALYZE;
```

### Linux Kernel Tuning

```bash
# /etc/sysctl.d/99-brainarr.conf

# Network optimizations
net.core.rmem_max = 134217728
net.core.wmem_max = 134217728
net.ipv4.tcp_rmem = 4096 87380 134217728
net.ipv4.tcp_wmem = 4096 65536 134217728
net.core.netdev_max_backlog = 5000
net.ipv4.tcp_congestion_control = bbr

# File system
fs.file-max = 2097152
fs.inotify.max_user_watches = 524288

# Memory
vm.swappiness = 10
vm.dirty_ratio = 15
vm.dirty_background_ratio = 5

# Apply settings
sudo sysctl -p /etc/sysctl.d/99-brainarr.conf
```

## Compliance and Auditing

### Audit Logging

```csharp
// Audit logger implementation
public class AuditLogger
{
    private readonly ILogger _logger;
    
    public void LogApiAccess(string userId, string action, string resource)
    {
        var audit = new
        {
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            Action = action,
            Resource = resource,
            IpAddress = GetClientIp(),
            CorrelationId = CorrelationContext.Current
        };
        
        _logger.Info($"AUDIT: {JsonConvert.SerializeObject(audit)}");
    }
}
```

### GDPR Compliance

```bash
#!/bin/bash
# GDPR data cleanup script

# Remove old recommendation data (> 90 days)
find /opt/brainarr/cache -name "*.json" -mtime +90 -delete

# Anonymize old logs
for log in /var/log/brainarr/*.log; do
    if [ -f "$log" ]; then
        # Replace IP addresses with hash
        sed -i 's/\([0-9]\{1,3\}\.\)\{3\}[0-9]\{1,3\}/[IP-REDACTED]/g' "$log"
        
        # Remove email addresses
        sed -i 's/[a-zA-Z0-9._%+-]\+@[a-zA-Z0-9.-]\+\.[a-zA-Z]\{2,\}/[EMAIL-REDACTED]/g' "$log"
    fi
done
```

## Health Checks and Readiness

### Custom Health Check Endpoint

```csharp
// HealthCheckService.cs
public class BrainarrHealthCheck : IHealthCheck
{
    private readonly IAIService _aiService;
    private readonly ICache _cache;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        
        try
        {
            // Check AI provider
            var providerHealthy = await _aiService.CheckProviderHealth();
            data.Add("provider", providerHealthy);
            
            // Check cache
            var cacheStats = _cache.GetStatistics();
            data.Add("cache_hit_rate", cacheStats.HitRate);
            
            // Check memory usage
            var memoryUsage = GC.GetTotalMemory(false) / 1024 / 1024;
            data.Add("memory_mb", memoryUsage);
            
            if (!providerHealthy)
                return HealthCheckResult.Degraded("Provider unhealthy", null, data);
            
            if (memoryUsage > 2000)
                return HealthCheckResult.Degraded("High memory usage", null, data);
            
            return HealthCheckResult.Healthy("All systems operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Health check failed", ex, data);
        }
    }
}
```

## Troubleshooting Production Issues

### Common Production Issues

1. **High API Costs**
   - Enable aggressive caching
   - Use cheaper models for simple tasks
   - Implement request batching

2. **Slow Response Times**
   - Check provider latency
   - Verify cache is working
   - Review sampling strategy

3. **Memory Leaks**
   - Monitor with dotnet-counters
   - Check for unclosed resources
   - Review object disposal

4. **Provider Failures**
   - Verify API keys are valid
   - Check rate limits
   - Review failover configuration

### Production Debugging

```bash
# Enable detailed logging temporarily
kubectl set env deployment/lidarr LOG_LEVEL=Debug -n brainarr-prod

# Collect diagnostics
kubectl exec -it deployment/lidarr -n brainarr-prod -- \
    dotnet-dump collect -p 1 -o /tmp/dump.dmp

# Analyze dump
dotnet-dump analyze /tmp/dump.dmp
```

## Maintenance Windows

### Rolling Updates

```bash
#!/bin/bash
# Zero-downtime update script

# Update one instance at a time
kubectl set image deployment/lidarr lidarr=linuxserver/lidarr:new-version \
    -n brainarr-prod --record

# Monitor rollout
kubectl rollout status deployment/lidarr -n brainarr-prod

# Verify health
kubectl get pods -n brainarr-prod -o wide
kubectl logs -f deployment/lidarr -n brainarr-prod --tail=50
```

## Related Documentation

- [Performance Tuning Guide](PERFORMANCE_TUNING.md) - Optimize for production load
- [Security Guide](SECURITY.md) - Security best practices
- [Troubleshooting Guide](TROUBLESHOOTING.md) - Debug production issues
- [API Reference](API_REFERENCE.md) - API documentation for integrations