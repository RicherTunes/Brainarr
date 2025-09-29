# 🏠 Local Providers - Privacy-First AI

Complete setup guide for privacy-focused local AI providers. These providers run entirely on your hardware with no data transmission to external services.

## 🔒 **Why Choose Local Providers?**

- **Complete Privacy**: Your music library data never leaves your system
- **No API Costs**: Free to use once set up
- **Offline Operation**: Works without internet connection
- **Data Control**: Full control over your recommendation data
- **No Rate Limits**: Only limited by your hardware

---

## 🦙 **Ollama Provider (Recommended)**

**Best for**: Users who want privacy and are comfortable with command-line tools.

### **Installation & Setup (Ollama)**

#### **1. Install Ollama**

```bash
# Linux/macOS
curl -fsSL https://ollama.ai/install.sh | sh

# Windows (PowerShell)
winget install Ollama.Ollama
```

#### **2. Download a Music-Optimized Model**

```bash
# Recommended: Qwen2.5 (excellent for music recommendations)
ollama pull qwen2.5:latest

# Alternatives:
ollama pull llama3.1:8b     # Faster, lower resource usage
ollama pull mistral:7b      # Lightweight option
ollama pull phi3:medium     # Microsoft's efficient model
```

#### **3. Verify Ollama is Running**

```bash
# Check service status
ollama list

# Test with a simple request
ollama run qwen2.5:latest "Recommend 3 jazz albums"
```

### **Brainarr Configuration**

**Basic Settings:**

- **Provider**: `Ollama`
- **Ollama URL**: `http://localhost:11434` (default)
- **Model**: `qwen2.5:latest`

Tip: To recommend entire artists rather than specific albums, set “Recommendation Type” to “Artists” in Brainarr settings. Lidarr will then import each recommended artist’s discography.

**Advanced Settings (Optional):**

- **Temperature**: `0.7` (creativity level, 0.0-1.0)
- **Top P**: `0.9` (nucleus sampling, 0.0-1.0)
- **Max Tokens**: `2000` (response length limit)
- **Stream Responses**: `false` (real-time streaming, usually disabled)

### **Model Recommendations**

| Model | Size | Speed | Quality | Best For |
|-------|------|-------|---------|----------|
| `qwen2.5:latest` | 7B | Fast | Excellent | General use (default) |
| `llama3.1:8b` | 8B | Medium | Very Good | Balanced performance |
| `mistral:7b` | 7B | Fast | Good | Low-resource systems |
| `phi3:medium` | 14B | Slower | Excellent | High-quality recommendations |

### **Hardware Requirements (Ollama)**

- **Minimum**: 8GB RAM, 4-core CPU
- **Recommended**: 16GB RAM, 8-core CPU
- **GPU**: Optional but significantly improves speed (NVIDIA recommended)
- **Storage**: 4-8GB per model

### **Performance Optimization (Ollama)**

```bash
# Enable GPU acceleration (if available)
ollama run qwen2.5:latest --gpu

# Adjust context window for memory usage
ollama run qwen2.5:latest --ctx-size 2048
```

### **Large Context Tips (Qwen/Llama)**

- If your local model supports 32k–40k tokens, set **Library Sampling** to **Comprehensive**.
- Brainarr scales prompt size for local providers; Comprehensive can reach ~40k tokens on LM Studio/Ollama.
- Combine Comprehensive with **Backfill Strategy: Standard or Aggressive** for best first-pass coverage (initial oversampling) and fewer iterations.

### **Troubleshooting**

**Ollama Not Responding:**

```bash
# Check if service is running
systemctl status ollama  # Linux
brew services list | grep ollama  # macOS

# Restart if needed
systemctl restart ollama  # Linux
brew services restart ollama  # macOS
```

**Model Download Issues:**

```bash
# Clear partial downloads
ollama rm qwen2.5:latest
ollama pull qwen2.5:latest

# Check available space
df -h  # Ensure sufficient disk space
```

**Performance Issues:**

- Reduce model size: Try `qwen2.5:7b` instead of `qwen2.5:latest`
- Lower token limit in Brainarr advanced settings
- Enable GPU if available

---

## 🎬 **LM Studio Provider**

**Best for**: Users who prefer GUI-based model management.

### **Installation & Setup (LM Studio)**

#### **1. Download LM Studio**

> Note: Local models can be slow, especially with large prompts. In Brainarr 1.2.1+ set "AI Request Timeout (s)" to 300-360s for LM Studio if you observe timeouts.
Visit <https://lmstudio.ai> and download for your platform:

- **Windows**: .exe installer
- **macOS**: .dmg package
- **Linux**: AppImage

#### **2. Download Models**

1. Open LM Studio
2. Go to **"Discover"** tab
3. Search for music-focused models:
   - `microsoft/Phi-3-medium-4k-instruct`
   - `mistralai/Mistral-7B-Instruct-v0.3`
   - `meta-llama/Llama-2-7b-chat-hf`
4. Click **"Download"** for your preferred model

#### **3. Start Local Server**

1. Go to **"Local Server"** tab
2. Select your downloaded model
3. Click **"Start Server"**
4. Note the server URL (usually `<http://localhost:1234>`)

### **Brainarr Configuration (LM Studio)**

**Basic Settings:**

- **Provider**: `LM Studio`
- **LM Studio URL**: `<http://localhost:1234>` (default)
- **Model**: `local-model` (auto-detected from LM Studio)
- **Status**: Verified in Brainarr 1.2.4

**Advanced Settings:**

- **Temperature**: `0.7` (creativity level)
- **Max Tokens**: `2000` (response length)

### **Model Selection Guide**

**For Music Recommendations:**

- **Phi-3-Medium**: Excellent reasoning, music-aware
- **Mistral-7B-Instruct**: Fast, good quality
- **Llama-2-7b-Chat**: Reliable, well-tested

**Performance Tiers:**

- **Fast**: 7B parameter models (Mistral, Llama-2-7b)
- **Balanced**: 13-14B parameter models (Phi-3-Medium)
- **Quality**: 70B+ parameter models (requires powerful hardware)

### **Hardware Requirements (LM Studio)**

- **Minimum**: 8GB RAM, 4GB VRAM (for 7B models)
- **Recommended**: 16GB RAM, 8GB VRAM (for 13B models)
- **High-End**: 32GB RAM, 24GB VRAM (for 70B+ models)

### **Troubleshooting (LM Studio)**

**LM Studio Server Not Starting:**

1. Check port 1234 isn't in use: `netstat -an | grep 1234`
2. Try different port in LM Studio settings
3. Update Brainarr URL to match LM Studio port

**Model Download Failures:**

1. Check internet connection
2. Verify sufficient disk space (models are 4-40GB)
3. Try downloading smaller model first

**Poor Performance:**

- Close other applications to free RAM
- Use smaller models (7B instead of 13B+)
- Enable GPU acceleration in LM Studio settings

---

## 🔧 **Local Provider Configuration Tips**

### **Choosing Between Ollama vs LM Studio**

**Choose Ollama if:**

- You're comfortable with command-line tools
- You want maximum flexibility and model options
- You prefer lightweight, efficient operation
- You need programmatic model management

**Choose LM Studio if:**

- You prefer GUI-based management
- You want easy model browsing and downloading
- You need visual model performance monitoring
- You're new to local AI models

### **Performance Optimization (System)**

**System Optimization:**

```bash
# Increase memory for better performance
echo 'vm.swappiness = 10' >> /etc/sysctl.conf  # Linux
```

**Model Optimization:**

- **Quality Priority**: Use larger models (13B+) with more RAM
- **Speed Priority**: Use smaller models (7B) with faster inference
- **Memory Constrained**: Use quantized models (Q4, Q5) for lower memory usage

### **Integration with Brainarr**

**Automatic Features:**

- **Model Detection**: Brainarr automatically discovers available models
- **Health Monitoring**: Checks provider availability every 5 minutes
- **Connection Testing**: Built-in test functionality
- **Error Recovery**: Automatic retry with exponential backoff

**Configuration Validation:**

- URL format validation with automatic http:// prefix
- Model name validation against available models
- Temperature range validation (0.0-1.0)
- Token limit validation (1-10000)

Tip: For artist-centric curation, set “Recommendation Type” to “Artists”. Brainarr will resolve artist MBIDs when possible and add the discography via Lidarr.

---

## 🎯 **Best Practices**

### **Resource Management**

- **RAM**: Allocate 4-8GB per active model
- **GPU**: Dedicated GPU significantly improves speed
- **Storage**: SSD recommended for model storage
- **Network**: Local providers work offline after initial setup

### **Security Considerations**

- **Local Network**: Ollama/LM Studio bind to localhost by default
- **No External Access**: Configure firewall to block external access
- **Model Updates**: Regular updates for security and performance
- **Data Isolation**: All processing happens locally

### **Monitoring & Maintenance**

- **Health Checks**: Brainarr automatically monitors provider health
- **Log Monitoring**: Check Lidarr logs for any issues
- **Model Updates**: Periodically update models for better performance
- **System Resources**: Monitor CPU/RAM usage during recommendation generation

---

**Next Steps:**

- **Reduce hallucinations:** See [Hallucination Reduction](Hallucination-Reduction) for model/prompt tips
- **Ready to configure?** Test your setup with [First Run Guide](First-Run-Guide)
- **Need cloud alternatives?** Check [Cloud Providers](Cloud-Providers)
- **Want to optimize?** See [Performance Tuning](https://github.com/RicherTunes/Brainarr/blob/main/docs/PERFORMANCE_TUNING.md)
