# Local Providers - 100% Private AI

Complete setup guides for local AI providers that keep your data completely private.

## Why Choose Local Providers?

### ✅ Perfect Privacy
- **Zero data transmission** - Your music library never leaves your server
- **No external API calls** - Everything processed locally
- **Complete control** - You own the entire pipeline

### ✅ No Ongoing Costs
- **Free forever** - No API fees or subscriptions
- **No usage limits** - Generate unlimited recommendations
- **No rate limiting** - Process as many requests as your hardware allows

### ✅ Offline Operation
- **Internet independent** - Works without external connectivity
- **Reliable uptime** - No dependency on cloud service availability
- **Consistent performance** - No network latency issues

## Hardware Requirements

### Minimum Requirements
- **RAM**: 8GB system memory
- **Storage**: 4GB available space per model
- **CPU**: 4+ cores recommended
- **OS**: Windows, Linux, macOS

### Recommended Setup
- **RAM**: 16GB+ for best performance
- **Storage**: SSD for faster model loading
- **CPU**: 8+ cores for faster processing
- **GPU**: Optional but significantly improves speed

### Performance Expectations
| Hardware | Speed | Quality | Models Supported |
|----------|-------|---------|------------------|
| 8GB RAM, 4-core CPU | Good | Good | Small models (7B) |
| 16GB RAM, 8-core CPU | Better | Better | Medium models (13B) |
| 32GB RAM, GPU | Excellent | Excellent | Large models (70B+) |

---

## 🏠 Ollama Setup

Ollama is the most popular local AI solution with excellent model management.

### Installation

#### Linux/macOS
```bash
# Install Ollama
curl -fsSL https://ollama.com/install.sh | sh

# Verify installation
ollama --version
```

#### Windows
1. Download installer from [ollama.com](https://ollama.com/download)
2. Run the installer
3. Open Command Prompt and verify: `ollama --version`

### Model Installation

#### Recommended Models for Music Recommendations
```bash
# Best all-around model (8B parameters)
ollama pull llama3.2

# Fast and efficient (7B parameters)  
ollama pull mistral

# High quality, larger model (70B parameters - requires 32GB+ RAM)
ollama pull llama3.1:70b

# Chinese-trained model, good for diverse music (7B parameters)
ollama pull qwen2.5
```

#### Model Selection Guide
| Model | Size | RAM Required | Speed | Quality | Best For |
|-------|------|--------------|-------|---------|----------|
| **llama3.2** | 8B | 8GB | Good | Excellent | Recommended default |
| **mistral** | 7B | 7GB | Fast | Good | Speed priority |
| **llama3.1:70b** | 70B | 40GB | Slow | Best | Quality priority |
| **qwen2.5** | 7B | 7GB | Good | Good | International music |
| **gemma2:9b** | 9B | 10GB | Good | Good | Google's model |

### Starting Ollama Service

#### Automatic Startup (Recommended)
```bash
# Linux (systemd)
sudo systemctl enable ollama
sudo systemctl start ollama

# macOS (launchd)
brew services start ollama

# Windows (runs as service automatically after install)
```

#### Manual Startup
```bash
# Start Ollama server
ollama serve

# In another terminal, test it's running
curl http://localhost:11434/api/tags
```

### Brainarr Configuration

1. **In Lidarr**: Settings → Import Lists → Add → Brainarr
2. **Provider**: Select "🏠 Ollama (Local, Private)"
3. **Ollama URL**: `http://localhost:11434` (default)
4. **Click Test**: Should show "Connection successful" and list available models
5. **Ollama Model**: Select your preferred model from dropdown
6. **Save configuration**

#### Configuration Example
```yaml
Provider: Ollama
Ollama URL: http://localhost:11434
Ollama Model: llama3.2:latest
Discovery Mode: Adjacent
Recommendation Mode: Specific Albums
Max Recommendations: 10
Cache Duration: 60 minutes
```

### Ollama Management Commands

```bash
# List installed models
ollama list

# Pull a new model
ollama pull [model-name]

# Remove a model
ollama rm [model-name]

# Show model information
ollama show [model-name]

# Update all models
ollama pull --all

# Check running status
ollama ps
```

### Troubleshooting Ollama

#### Service Not Starting
```bash
# Check if port is in use
sudo netstat -tlnp | grep :11434

# Check Ollama logs
journalctl -u ollama -f  # Linux
brew services list | grep ollama  # macOS
```

#### Model Download Issues
```bash
# Check available disk space
df -h

# Manually specify model size
ollama pull llama3.2:7b  # Specify parameter size

# Clear model cache if needed
ollama rm --all  # Warning: removes all models
```

#### Memory Issues
```bash
# Check memory usage
free -h  # Linux
vm_stat  # macOS

# Use smaller model if running out of memory
ollama pull mistral:7b
```

---

## 🖥️ LM Studio Setup

LM Studio provides a user-friendly GUI for running local AI models.

### Installation

1. **Download LM Studio**
   - Visit [lmstudio.ai](https://lmstudio.ai)
   - Download for your operating system
   - Install following standard procedure

2. **Launch Application**
   - Open LM Studio
   - Allow firewall access when prompted

### Model Management

#### Downloading Models

1. **Go to Models Tab** (🔍 icon)
2. **Search for recommended models**:
   - `microsoft/Phi-3.5-mini` - Fast, efficient
   - `meta-llama/Llama-3.2-8B` - Balanced quality/speed  
   - `microsoft/DialoGPT-medium` - Good for conversations
   - `mistralai/Mistral-7B-v0.1` - Excellent performance

3. **Click Download** on your chosen model
4. **Wait for download** - Models are 4-40GB depending on size

#### Model Recommendations
| Model | Size | Quality | Speed | Best For |
|-------|------|---------|-------|----------|
| **Phi-3.5-mini** | 4GB | Good | Very Fast | Limited hardware |
| **Llama-3.2-8B** | 8GB | Excellent | Fast | Recommended default |
| **Mistral-7B** | 7GB | Very Good | Fast | Balanced option |
| **Llama-3.1-70B** | 40GB | Best | Slow | High-end hardware |

### Starting Local Server

1. **Go to Developer Tab** (⚙️ icon)
2. **Click "Start Server"**
3. **Configure Settings**:
   - **Port**: 1234 (default)
   - **Model**: Select your downloaded model
   - **Context Length**: 4096 (default)
   - **GPU Acceleration**: Enable if available

4. **Click "Start Server"**
5. **Verify**: Server status should show "Running"

### Brainarr Configuration

1. **In Lidarr**: Settings → Import Lists → Add → Brainarr
2. **Provider**: Select "🖥️ LM Studio (Local, GUI)"
3. **LM Studio URL**: `http://localhost:1234` (default)
4. **Click Test**: Should show "Connection successful"
5. **LM Studio Model**: Will auto-detect running model
6. **Save configuration**

#### Configuration Example
```yaml
Provider: LM Studio
LM Studio URL: http://localhost:1234
LM Studio Model: Llama-3.2-8B-Instruct
Discovery Mode: Similar
Recommendation Mode: Albums
Max Recommendations: 8
Cache Duration: 90 minutes
```

### LM Studio Tips

#### Performance Optimization
1. **Enable GPU Acceleration**: Settings → Hardware → Use GPU
2. **Adjust Context Length**: Lower for speed, higher for quality
3. **Model Caching**: Keep frequently used models loaded
4. **Memory Management**: Close other applications for better performance

#### Managing Multiple Models
1. **Download multiple models** for different use cases
2. **Switch models** by stopping server and selecting different model
3. **Compare results** by testing same prompts with different models

### Troubleshooting LM Studio

#### Server Won't Start
1. **Check port availability**: Ensure port 1234 is free
2. **Restart LM Studio**: Close completely and reopen
3. **Check model loading**: Ensure model is fully downloaded
4. **Firewall settings**: Allow LM Studio through firewall

#### Poor Performance
1. **Close other applications** to free up RAM
2. **Use smaller model** if system is struggling
3. **Enable GPU acceleration** if available
4. **Lower context length** in server settings

#### Model Issues
1. **Re-download model** if corrupted
2. **Check available storage** space
3. **Try different model** if one isn't working

---

## Local Provider Comparison

| Feature | Ollama | LM Studio |
|---------|---------|-----------|
| **Interface** | Command-line | GUI |
| **Ease of Use** | Moderate | Easy |
| **Model Management** | Excellent | Good |
| **Performance** | Excellent | Good |
| **Customization** | High | Moderate |
| **Resource Usage** | Lower | Higher |
| **Platform Support** | All platforms | All platforms |
| **Advanced Features** | API, scripting | Visual interface |

## Performance Optimization

### System-Level Optimizations

#### Linux
```bash
# Increase memory limits
echo 'vm.swappiness=10' | sudo tee -a /etc/sysctl.conf

# Optimize CPU governor
echo performance | sudo tee /sys/devices/system/cpu/cpu*/cpufreq/scaling_governor
```

#### Windows
```powershell
# Set high performance power plan
powercfg -setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
```

#### macOS
```bash
# Prevent system sleep during processing
caffeinate -i -t 3600
```

### Model-Specific Optimizations

#### For Speed
- Use smaller models (7B-8B parameters)
- Reduce context length
- Enable GPU acceleration
- Close unnecessary applications

#### For Quality
- Use larger models (13B+ parameters)
- Increase context length
- Allow more processing time
- Ensure adequate cooling

## Security Considerations

### Network Security
- **Bind to localhost only**: Don't expose to external networks
- **Use firewall rules**: Block external access to AI ports
- **Monitor connections**: Check who's accessing your AI service

### Data Security
- **No external data transmission**: Everything stays local
- **File system permissions**: Secure model files and cache
- **Process isolation**: Run AI services with limited privileges

## Backup and Maintenance

### Model Backup
```bash
# Ollama models location
# Linux/macOS: ~/.ollama/models/
# Windows: %USERPROFILE%\.ollama\models\

# Backup command
tar -czf ollama-models-backup.tar.gz ~/.ollama/models/
```

### Regular Maintenance
1. **Update models regularly** for improved performance
2. **Clean old model versions** to save disk space
3. **Monitor system resources** during operation
4. **Keep AI software updated**

## Next Steps

After setting up your local provider:

1. **[Basic Configuration](Basic-Configuration)** - Configure Brainarr settings
2. **[Performance Tuning](Performance-Tuning)** - Optimize for your hardware
3. **[Getting Your First Recommendations](Getting-Your-First-Recommendations)** - Test your setup

## Need Help?

- **[Provider Troubleshooting](Provider-Troubleshooting#local-providers)** - Local provider issues
- **[Common Issues](Common-Issues)** - General problems
- **[Performance Tuning](Performance-Tuning)** - Optimization guides

**Privacy achieved!** Your AI music recommendations are now completely private and cost-free.