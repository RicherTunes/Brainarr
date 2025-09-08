# Brainarr Deployment Guide

## Overview

This guide covers deploying Brainarr to various environments including manual installation, Docker, and automated CI/CD pipelines.

> Compatibility
> Requires Lidarr 2.14.1.4716+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly). Ensure your Lidarr meets this before deploying the plugin.

## Table of Contents

- [Build Process](#build-process)
- [Manual Deployment](#manual-deployment)
- [Docker Deployment](#docker-deployment)
- [Automated Deployment](#automated-deployment)
- [CI/CD Pipelines](#cicd-pipelines)
- [Production Checklist](#production-checklist)

## Build Process

### Prerequisites

- .NET SDK 6.0 or higher
- Git (for version control)
- PowerShell Core (for build scripts)

### Building from Source

```bash
# Clone the repository
git clone https://github.com/brainarr/brainarr.git
cd brainarr

# Restore dependencies
dotnet restore

# Build in Release mode
dotnet build -c Release

# Run tests
dotnet test

# Create deployment package
dotnet publish -c Release -o dist/
```

### Using Build Scripts

**Windows:**

```powershell
.\scripts\build.ps1 -Configuration Release -Version 1.0.0
```

**Linux/Mac:**

```bash
./scripts/build.sh --configuration Release --version 1.0.0
```

### Build Output Structure

```text
dist/
├── Lidarr.Plugin.Brainarr.dll     # Main plugin assembly
├── Lidarr.Plugin.Brainarr.pdb     # Debug symbols
├── plugin.json                     # Plugin manifest
├── Newtonsoft.Json.dll            # Dependencies
└── *.dll                          # Other dependencies
```

## Manual Deployment

### Step 1: Prepare Files

```bash
# Create plugin directory
mkdir -p /var/lib/lidarr/plugins/RicherTunes/Brainarr

# Copy built files
cp -r dist/* /var/lib/lidarr/plugins/RicherTunes/Brainarr/

# Set permissions
chown -R lidarr:lidarr /var/lib/lidarr/plugins/RicherTunes/Brainarr
chmod 755 /var/lib/lidarr/plugins/RicherTunes/Brainarr
```

### Step 2: Platform-Specific Paths

**Linux:**

```bash
/var/lib/lidarr/plugins/RicherTunes/Brainarr/
```

**Windows:**

```powershell
C:\ProgramData\Lidarr\plugins\RicherTunes\Brainarr\
```

**Docker:**

```bash
/config/plugins/RicherTunes/Brainarr/
```

**MacOS:**

```bash
~/Library/Application Support/Lidarr/plugins/RicherTunes/Brainarr/
```

### Step 3: Restart Lidarr

```bash
# Linux systemd
sudo systemctl restart lidarr

# Docker
docker restart lidarr

# Windows Service
Restart-Service Lidarr

# MacOS
brew services restart lidarr
```

### Step 4: Verify Installation

```bash
# Check logs for successful load
tail -f /var/log/lidarr/lidarr.txt | grep "Loaded plugin: Brainarr"

# Verify in UI
# Navigate to Settings > Import Lists > Add > Brainarr
```

## Docker Deployment

### Docker Compose Configuration

```yaml
version: '3.8'

services:
  lidarr:
    image: lscr.io/linuxserver/lidarr:latest
    container_name: lidarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=UTC
    volumes:
      - ./config:/config
      - ./music:/music
      - ./downloads:/downloads
      - ./plugins:/config/plugins  # Plugin directory
    ports:
      - 8686:8686
    restart: unless-stopped

  # Optional: Ollama for local AI
  ollama:
    image: ollama/ollama:latest
    container_name: ollama
    volumes:
      - ./ollama:/root/.ollama
    ports:
      - 11434:11434
    restart: unless-stopped
```

### Dockerfile for Custom Image

```dockerfile
FROM lscr.io/linuxserver/lidarr:latest

# Copy plugin files
COPY --chown=abc:abc dist/ /config/plugins/RicherTunes/Brainarr/

# Install additional dependencies if needed
RUN apk add --no-cache curl jq

# Health check
HEALTHCHECK --interval=30s --timeout=3s \
  CMD curl -f http://localhost:8686/api/v1/system/status || exit 1
```

### Build and Deploy

```bash
# Build custom image
docker build -t lidarr-brainarr:latest .

# Run container
docker run -d \
  --name lidarr \
  -p 8686:8686 \
  -v $(pwd)/config:/config \
  -v $(pwd)/music:/music \
  lidarr-brainarr:latest

# Deploy plugin to running container
docker cp dist/. lidarr:/config/plugins/RicherTunes/Brainarr/
docker restart lidarr
```

## Automated Deployment

### Deployment Script

```bash
#!/bin/bash
# deploy.sh - Automated deployment script

set -e

# Configuration
LIDARR_HOST=${LIDARR_HOST:-"localhost"}
LIDARR_PORT=${LIDARR_PORT:-"8686"}
LIDARR_API_KEY=${LIDARR_API_KEY}
PLUGIN_VERSION=$(cat plugin.json | jq -r .version)

echo "Deploying Brainarr v${PLUGIN_VERSION} to ${LIDARR_HOST}:${LIDARR_PORT}"

# Build
echo "Building plugin..."
dotnet build -c Release
dotnet publish -c Release -o dist/

# Create package
echo "Creating deployment package..."
cd dist
zip -r ../Brainarr-v${PLUGIN_VERSION}.zip .
cd ..

# Deploy via API (if supported)
if [ ! -z "$LIDARR_API_KEY" ]; then
    echo "Deploying via API..."
    curl -X POST \
      -H "X-Api-Key: ${LIDARR_API_KEY}" \
      -F "plugin=@Brainarr-v${PLUGIN_VERSION}.zip" \
      "http://${LIDARR_HOST}:${LIDARR_PORT}/api/v1/plugin/install"
else
    echo "Manual deployment required - API key not provided"
    echo "Package created: Brainarr-v${PLUGIN_VERSION}.zip"
fi

echo "Deployment complete!"
```

### Ansible Playbook

```yaml
---
- name: Deploy Brainarr Plugin
  hosts: lidarr_servers
  become: yes

  vars:
    plugin_version: "1.0.0"
    lidarr_path: "/var/lib/lidarr"
    plugin_source: "./dist"

  tasks:
    - name: Stop Lidarr service
      systemd:
        name: lidarr
        state: stopped

    - name: Create plugin directory
      file:
        path: "{{ lidarr_path }}/plugins/RicherTunes/Brainarr"
        state: directory
        owner: lidarr
        group: lidarr
        mode: '0755'

    - name: Copy plugin files
      copy:
        src: "{{ plugin_source }}/"
        dest: "{{ lidarr_path }}/plugins/RicherTunes/Brainarr/"
        owner: lidarr
        group: lidarr
        mode: '0644'

    - name: Start Lidarr service
      systemd:
        name: lidarr
        state: started
        enabled: yes

    - name: Wait for Lidarr to be ready
      uri:
        url: "http://localhost:8686/api/v1/system/status"
        status_code: 200
      register: result
      until: result.status == 200
      retries: 30
      delay: 2
```

## CI/CD Pipelines

### GitHub Actions

```yaml
name: Build and Deploy

on:
  push:
    tags:
      - 'v*'
  workflow_dispatch:

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3
      with:
        submodules: true

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '6.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build -c Release --no-restore

    - name: Test
      run: dotnet test --no-build --verbosity normal

    - name: Publish
      run: dotnet publish -c Release -o dist/

    - name: Create Release Package
      run: |
        cd dist
        zip -r ../Brainarr-${{ github.ref_name }}.zip .
        cd ..

    - name: Create GitHub Release
      uses: softprops/action-gh-release@v1
      with:
        files: Brainarr-${{ github.ref_name }}.zip
        generate_release_notes: true
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

  deploy:
    needs: build
    runs-on: ubuntu-latest
    if: github.event_name == 'push' && startsWith(github.ref, 'refs/tags/')

    steps:
    - name: Deploy to Production
      uses: appleboy/ssh-action@v0.1.5
      with:
        host: ${{ secrets.PROD_HOST }}
        username: ${{ secrets.PROD_USER }}
        key: ${{ secrets.PROD_SSH_KEY }}
        script: |
          cd /opt/lidarr-plugins
          wget https://github.com/${{ github.repository }}/releases/download/${{ github.ref_name }}/Brainarr-${{ github.ref_name }}.zip
          unzip -o Brainarr-${{ github.ref_name }}.zip -d /var/lib/lidarr/plugins/RicherTunes/Brainarr/
          systemctl restart lidarr
```

### GitLab CI/CD

```yaml
# .gitlab-ci.yml

stages:
  - build
  - test
  - package
  - deploy

variables:
  DOTNET_VERSION: "6.0"

before_script:
  - apt-get update -y
  - apt-get install -y dotnet-sdk-${DOTNET_VERSION}

build:
  stage: build
  script:
    - dotnet restore
    - dotnet build -c Release
  artifacts:
    paths:
      - Brainarr.Plugin/bin/Release/

test:
  stage: test
  script:
    - dotnet test --no-build
  coverage: '/Total[^|]*\|[^|]*\s+([\d\.]+)/'

package:
  stage: package
  script:
    - dotnet publish -c Release -o dist/
    - cd dist && zip -r ../Brainarr-${CI_COMMIT_TAG}.zip . && cd ..
  artifacts:
    paths:
      - Brainarr-*.zip
  only:
    - tags

deploy:
  stage: deploy
  script:
    - 'curl -X POST -H "Authorization: Bearer $DEPLOY_TOKEN" -F "plugin=@Brainarr-${CI_COMMIT_TAG}.zip" $LIDARR_URL/api/v1/plugin/install'
  only:
    - tags
  when: manual
```

### Jenkins Pipeline

```groovy
pipeline {
    agent any

    environment {
        DOTNET_VERSION = '6.0'
        PLUGIN_VERSION = sh(returnStdout: true, script: "cat plugin.json | jq -r .version").trim()
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                sh 'git submodule update --init'
            }
        }

        stage('Build') {
            steps {
                sh 'dotnet restore'
                sh 'dotnet build -c Release'
            }
        }

        stage('Test') {
            steps {
                sh 'dotnet test --no-build --logger:trx'
                publishTestResults(testResultsFiles: '**/*.trx')
            }
        }

        stage('Package') {
            steps {
                sh 'dotnet publish -c Release -o dist/'
                sh 'cd dist && zip -r ../Brainarr-v${PLUGIN_VERSION}.zip .'
                archiveArtifacts artifacts: 'Brainarr-*.zip'
            }
        }

        stage('Deploy') {
            when {
                branch 'main'
            }
            steps {
                sshagent(['lidarr-deploy-key']) {
                    sh '''
                        scp Brainarr-v${PLUGIN_VERSION}.zip lidarr@prod:/tmp/
                        ssh lidarr@prod "unzip -o /tmp/Brainarr-v${PLUGIN_VERSION}.zip -d /var/lib/lidarr/plugins/RicherTunes/Brainarr/"
                        ssh lidarr@prod "systemctl restart lidarr"
                    '''
                }
            }
        }
    }

    post {
        success {
            slackSend(color: 'good', message: "Brainarr v${PLUGIN_VERSION} deployed successfully!")
        }
        failure {
            slackSend(color: 'danger', message: "Brainarr deployment failed!")
        }
    }
}
```

## Production Checklist

### Pre-Deployment

- [ ] All tests passing
- [ ] Version number updated in plugin.json
- [ ] CHANGELOG.md updated
- [ ] Documentation current
- [ ] API keys secured (not in source control)
- [ ] Performance benchmarks acceptable
- [ ] Security scan completed

### Deployment Steps

1. **Backup Current Installation**

```bash
cp -r /var/lib/lidarr/plugins/RicherTunes/Brainarr /backup/Brainarr-$(date +%Y%m%d)
```

2. **Stop Lidarr Service**

```bash
systemctl stop lidarr
```

3. **Deploy New Version**

```bash
rm -rf /var/lib/lidarr/plugins/RicherTunes/Brainarr/*
unzip Brainarr-v1.0.0.zip -d /var/lib/lidarr/plugins/RicherTunes/Brainarr/
chown -R lidarr:lidarr /var/lib/lidarr/plugins/RicherTunes/Brainarr
```

4. **Start Lidarr Service**

```bash
systemctl start lidarr
```

5. **Verify Deployment**

```bash
# Check plugin loaded
grep "Loaded plugin: Brainarr" /var/log/lidarr/lidarr.txt

# Test functionality
curl -X GET "http://localhost:8686/api/v1/importlist" \
  -H "X-Api-Key: YOUR_API_KEY" | jq '.[] | select(.implementation=="Brainarr")'
```

### Post-Deployment

- [ ] Plugin appears in UI
- [ ] Test connection successful
- [ ] Recommendations generating
- [ ] No errors in logs
- [ ] Performance metrics normal
- [ ] Monitor for 24 hours

### Rollback Procedure

```bash
#!/bin/bash
# rollback.sh

BACKUP_DIR="/backup"
PLUGIN_DIR="/var/lib/lidarr/plugins/RicherTunes/Brainarr"

# Stop Lidarr
systemctl stop lidarr

# Find latest backup
LATEST_BACKUP=$(ls -t $BACKUP_DIR/Brainarr-* | head -1)

if [ -z "$LATEST_BACKUP" ]; then
    echo "No backup found!"
    exit 1
fi

# Restore backup
rm -rf $PLUGIN_DIR/*
cp -r $LATEST_BACKUP/* $PLUGIN_DIR/
chown -R lidarr:lidarr $PLUGIN_DIR

# Start Lidarr
systemctl start lidarr

echo "Rolled back to: $LATEST_BACKUP"
```

## Monitoring

### Health Check Script

```bash
#!/bin/bash
# healthcheck.sh

LIDARR_URL="http://localhost:8686"
API_KEY="YOUR_API_KEY"

# Check Lidarr is running
if ! curl -s "$LIDARR_URL/api/v1/system/status" -H "X-Api-Key: $API_KEY" > /dev/null; then
    echo "ERROR: Lidarr not responding"
    exit 1
fi

# Check Brainarr plugin
PLUGIN_STATUS=$(curl -s "$LIDARR_URL/api/v1/importlist" \
  -H "X-Api-Key: $API_KEY" | \
  jq '.[] | select(.implementation=="Brainarr") | .name')

if [ -z "$PLUGIN_STATUS" ]; then
    echo "ERROR: Brainarr plugin not found"
    exit 1
fi

echo "OK: Brainarr plugin active - $PLUGIN_STATUS"
```

### Prometheus Metrics

```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'lidarr'
    static_configs:
      - targets: ['localhost:8686']
    metrics_path: '/metrics'
```

## Security Considerations

### API Key Management

```bash
# Store API keys in environment variables
export OPENAI_API_KEY="sk-..."
export ANTHROPIC_API_KEY="sk-ant-..."

# Or use secrets management
vault kv put secret/brainarr \
  openai_key="sk-..." \
  anthropic_key="sk-ant-..."
```

### File Permissions

```bash
# Secure plugin directory
chmod 755 /var/lib/lidarr/plugins/RicherTunes/Brainarr
chmod 644 /var/lib/lidarr/plugins/RicherTunes/Brainarr/*
chown -R lidarr:lidarr /var/lib/lidarr/plugins/RicherTunes/Brainarr
```

### Network Security

```bash
# Firewall rules for local providers only
ufw allow from 127.0.0.1 to any port 11434  # Ollama
ufw allow from 127.0.0.1 to any port 1234   # LM Studio
```

## Troubleshooting Deployment

### Plugin Not Loading

```bash
# Check file permissions
ls -la /var/lib/lidarr/plugins/RicherTunes/Brainarr/

# Verify plugin.json
cat /var/lib/lidarr/plugins/RicherTunes/Brainarr/plugin.json | jq .

# Check for missing dependencies
ldd /var/lib/lidarr/plugins/RicherTunes/Brainarr/*.dll
```

### Version Conflicts

```bash
# Check .NET version
dotnet --version

# Check Lidarr version
curl http://localhost:8686/api/v1/system/status | jq .version
```

### Clean Deployment

```bash
# Complete cleanup and redeploy
systemctl stop lidarr
rm -rf /var/lib/lidarr/plugins/RicherTunes/Brainarr
rm -f /var/lib/lidarr/config.xml.backup
# Deploy fresh
unzip Brainarr-v1.0.0.zip -d /var/lib/lidarr/plugins/RicherTunes/Brainarr/
systemctl start lidarr
```

## Additional Resources

- [Lidarr Plugin Development](https://wiki.servarr.com/lidarr/plugins)
- [Docker Best Practices](https://docs.docker.com/develop/dev-best-practices/)
- [CI/CD Best Practices](https://www.atlassian.com/continuous-delivery/principles/continuous-integration-vs-delivery-vs-deployment)
- [.NET Deployment Guide](https://docs.microsoft.com/en-us/dotnet/core/deploying/)
