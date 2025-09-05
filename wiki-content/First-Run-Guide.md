# 🚀 First Run Guide

Get your first AI-powered music recommendations working perfectly. This guide walks through your initial setup, testing, and optimization.

## ✅ **Pre-Flight Checklist**

Before starting, ensure:
- ✅ **Lidarr Running**: Accessible at http://localhost:8686
- ✅ **Plugins Enabled**: Settings → General → "Enable Plugins" is checked  
- ✅ **Brainarr Installed**: Visible in Settings → Import Lists → Add (+)
- ✅ **AI Provider Ready**: At least one provider configured (see [[Provider Setup]])

---

## 🎯 **Step 1: Initial Configuration**

### **Access Brainarr Settings**
1. **Settings** → **Import Lists** → **Add** (+)
2. Select **"Brainarr"** from the list
3. You'll see the configuration interface

### **Essential Settings**

#### **Provider Configuration**
- **Provider**: Choose your preferred AI service
- **API Key/URL**: Enter credentials for your chosen provider  
- **Model**: Select appropriate model (defaults are recommended)

#### **Recommendation Settings**
- **Max Recommendations**: Start with `10` for testing
- **Discovery Mode**: Use `Adjacent` for balanced discovery
- **Recommendation Mode**: Try `Specific Albums` first
- **Sampling Strategy**: Use `Balanced` for optimal results (use `Comprehensive` for powerful models)
- **Backfill Strategy**: `Aggressive` (default; strongly hits target)

#### **Basic Settings**
- **Name**: `Brainarr AI Recommendations` (or custom name)
- **Enable Automatic Add**: ✅ (so Lidarr imports recommendations)
- **Search for missing albums**: ✅ (trigger downloads)
- **Minimum Availability**: Match your Lidarr quality settings

---

## 🧪 **Step 2: Test Your Configuration**

### **Connection Test**
1. After entering provider details, click **"Test"**
2. **Success**: "Test was successful" ✅
3. **Failure**: Check error message and fix configuration

**Common Test Issues:**
- **"Connection timeout"**: Check URL and firewall
- **"Invalid API key"**: Verify key format and validity  
- **"Model not found"**: Ensure model exists for your provider
- **"Rate limited"**: Wait a moment and retry

### **Manual Import Test**  
1. Click **"Manual Import"** button
2. Watch for **System** → **Tasks** activity
3. Check **System** → **Logs** for detailed progress
4. Should complete in 30-60 seconds

**Expected Log Messages:**
```
Info: [Brainarr] Starting recommendation generation...
Info: [Brainarr] Analyzing library profile (X albums, Y artists)...  
Info: [Brainarr] Requesting recommendations from [Provider]...
Info: [Brainarr] Generated X unique recommendations
Info: [Brainarr] Import completed successfully
```

---

## 🎵 **Step 3: Review Your First Recommendations**

### **Check Generated Recommendations**
1. **Activity** → **Queue** - See albums being processed
2. **Wanted** → **Search All** - See albums added for search
3. **Activity** → **History** - Track download progress

### **Quality Assessment**

**Good Recommendations Look Like:**
- ✅ **Relevant Artists**: Similar to your existing library
- ✅ **Discoverable**: New artists that match your taste
- ✅ **Available**: Albums that exist and can be found
- ✅ **Balanced**: Mix of familiar and exploratory content

**Poor Recommendations Might Be:**
- ❌ **Duplicates**: Albums you already have (shouldn't happen with proper deduplication)
- ❌ **Hallucinations**: Non-existent albums or artists  
- ❌ **Off-Target**: Completely different genres than your library
- ❌ **Unavailable**: Albums that can't be found by your indexers

### **Recommendation Analysis**

**Review in Lidarr:**
1. **Wanted** → **Manual Import** to see what was added
2. **Music** → **Add New** to see discovered albums
3. **Activity** → **Queue** to track download progress

**Check Quality:**
- Look for artists similar to your existing collection
- Verify albums actually exist (no AI hallucinations)
- Ensure reasonable genre/style matching
- Confirm release dates are realistic

---

## ⚙️ **Step 4: Optimization**

Based on your first results, optimize settings:

### **If Recommendations Are Too Similar**
```
Discovery Mode: Adjacent → Exploratory
Sampling Strategy: Balanced → Comprehensive  
Max Recommendations: Increase to 20-30
```

### **If Recommendations Are Too Random**
```
Discovery Mode: Exploratory → Similar
Sampling Strategy: Comprehensive → Minimal
Consider different AI provider (Claude for reasoning)
```

### **If Getting Duplicates**
```
Check library sync in Lidarr
Verify deduplication is working (check logs)
Consider Artist-Only mode instead of Albums
```

### **If Low Success Rate**
```
Switch to premium provider (Claude, GPT-4o)
Increase Max Recommendations (more attempts)
Check indexer connectivity and availability
```

### **Large Context Tip (Local Models)**
- If your local model supports 32k–40k context (e.g., Qwen3), set **Sampling Strategy** to **Comprehensive**.
- Combine with **Backfill Strategy: Standard/Aggressive** for better first-pass coverage thanks to initial oversampling.

---

## 📊 **Understanding Performance Metrics**

### **Key Metrics to Monitor**

#### **Provider Performance**
- **Response Time**: How fast your AI provider responds
- **Success Rate**: Percentage of successful API calls
- **Cache Hit Rate**: How often cached results are reused

#### **Recommendation Quality**
- **Uniqueness Rate**: Percentage of non-duplicate recommendations
- **Discovery Success**: How many recommendations lead to actual downloads
- **User Acceptance**: Manual tracking of recommendations you actually want

### **Performance Optimization**

#### **Cache Settings**
```
Fast Libraries (weekly changes):     Cache = 30 minutes
Stable Libraries (monthly changes):  Cache = 120 minutes
Testing/Tuning:                     Cache = 5 minutes
```

#### **Request Optimization**
```
Small Library (< 500 albums):     Max Recs = 10
Medium Library (500-2000 albums): Max Recs = 20  
Large Library (2000+ albums):     Max Recs = 30-50
```

---

## 🔧 **Advanced First Run Settings**

### **Custom Filters** 
Add custom hallucination filters if needed:
```
Custom Filter Patterns:
- "AI Version"
- "Director's Cut"  
- "Extended Universe"
- "Reimagined"
```

### **Debug Logging**
For troubleshooting, enable enhanced logging:
1. **Settings** → **General** → **Logging**
2. **Log Level**: `Debug` (temporarily)
3. Generate recommendations
4. **System** → **Logs** for detailed output
5. **Important**: Return to `Info` level after testing

---

## 🎉 **Step 5: Schedule Automatic Updates**

### **Configure Refresh Interval**
1. **Import Lists** → **Brainarr** → **[[Advanced Settings]]**
2. **Refresh Interval**: Recommended values:
   - **Active Discovery**: 6-12 hours
   - **Passive Discovery**: 24-48 hours  
   - **Large Libraries**: 48-72 hours

### **Monitor Performance**
- **Activity** → **Queue**: Track download success
- **System** → **Tasks**: Monitor health checks
- **System** → **Logs**: Watch for any issues

---

## 🚨 **Troubleshooting First Run Issues**

### **No Recommendations Generated**

**Check:**
1. **Provider Test**: Does connection test pass?
2. **Library Size**: Do you have enough music for analysis?
3. **API Limits**: Have you exceeded free tier limits?
4. **Logs**: Any error messages in System → Logs?

**Solutions:**
```bash
# Check Lidarr logs for errors
tail -f /var/lib/lidarr/.config/Lidarr/logs/lidarr.txt | grep Brainarr

# Common fixes:
- Verify API key format
- Check provider service status  
- Ensure sufficient library content (10+ albums minimum)
- Try different provider temporarily
```

### **Poor Quality Recommendations**

**Symptoms:**
- All recommendations are duplicates
- Recommendations are completely off-genre
- Getting non-existent albums (hallucinations)

**Solutions:**
1. **Try Different Provider**: Each AI has different strengths
2. **Adjust Discovery Mode**: Similar vs. Adjacent vs. Exploratory
3. **Change Sampling Strategy**: Minimal vs. Balanced vs. Comprehensive
4. **Enable Strict Validation**: Filters more aggressively

### **Performance Issues**

**Slow Response Times:**
- **Local Providers**: Check CPU/GPU resources
- **Cloud Providers**: Try different region/model
- **Network**: Check internet connection speed

**High Resource Usage:**
- **Reduce Max Recommendations**: Lower batch sizes
- **Increase Cache Duration**: Reduce API call frequency
- **Use Faster Models**: Trade quality for speed if needed

---

## 🎯 **Success Criteria**

Your first run is successful when:

✅ **Connection**: Provider test passes consistently  
✅ **Generation**: Recommendations are created without errors
✅ **Quality**: Recommendations match your music taste
✅ **Uniqueness**: No duplicates with existing library
✅ **Variety**: Good mix of familiar and new content
✅ **Performance**: Completes within reasonable time (< 2 minutes)

---

## 🔄 **Iterative Improvement**

### **Week 1: Baseline**
- Use default settings
- Monitor success rate
- Note preference patterns

### **Week 2: Optimization**  
- Adjust discovery mode based on results
- Fine-tune recommendation count
- Experiment with different providers

### **Week 3: Advanced Tuning**
- Configure multi-provider setup
- Optimize cache settings
- Add custom filters if needed

---

## 🎓 **Graduation: You're Ready!**

Once you have reliable, quality recommendations, you can:

- **[[Advanced Settings]]** - Explore deeper customization
- **[[Performance Tuning]]** - Optimize for your specific setup  
- **[[Health Monitoring]]** - Set up monitoring and alerting
- **[[Multi-Provider Setup]]** - Configure failover strategies

**Enjoy discovering new music with Brainarr! 🎵**
