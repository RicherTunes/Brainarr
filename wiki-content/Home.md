# 🧠 Brainarr Wiki

**AI-Powered Music Discovery Plugin for Lidarr**

Welcome to the comprehensive documentation for Brainarr - a production-ready plugin that brings intelligent music recommendations to your Lidarr setup using cutting-edge AI technology.

## 🚀 Quick Start

**New User?** Get started in 3 steps:
1. **[[Installation]]** - Set up Brainarr with Docker or manual installation
2. **[[Provider Setup]]** - Configure your preferred AI provider (local or cloud)
3. **[[First Run Guide]]** - Test and optimize your first recommendations

## 📖 Documentation

### 🏗️ **Setup & Installation**
- **[[Installation]]** - Complete setup guide for all platforms
- **[[Requirements]]** - System requirements and compatibility
- **[[Docker Setup]]** - Recommended Docker configuration
- **[[Manual Installation]]** - Step-by-step manual setup

### ⚙️ **Configuration**
- **[[Provider Setup]]** - Complete guide for all 9 AI providers
- **[[Local Providers]]** - Ollama and LM Studio setup (privacy-first)
- **[[Cloud Providers]]** - OpenAI, Anthropic, Gemini, and 6 others
- **[[Advanced Settings]]** - Performance tuning and optimization
- **[[Recommendation Modes]]** - Artist vs. album recommendation strategies
  
  **Best Practices**
  - **[[Settings Best Practices]]** - Opinionated defaults for great results
  
  Quick picks:
  - Library Sampling: Balanced (default); Comprehensive for powerful models (bigger token budgets)
  - Backfill Strategy: Standard (default); Aggressive to strongly hit targets

### 💡 **Features & Usage**
- **[[AI Provider System]]** - Multi-provider architecture with failover
- **[[Library Analysis]]** - How Brainarr understands your music taste
- **[[Discovery Modes]]** - Similar, Adjacent, and Exploratory recommendations
- **[[Caching System]]** - Intelligent caching for performance
- **[[Rate Limiting]]** - Provider management and API limits

### 🔧 **Advanced Topics**
- **[[Performance Tuning]]** - Optimize for large libraries
- **[[Health Monitoring]]** - Provider status and automatic failover
- **[[Security Features]]** - API key protection and validation
- **[[Logging & Debugging]]** - Troubleshooting and diagnostics

### ❓ **Support & Troubleshooting**
- **[[FAQ]]** - Frequently asked questions
- **[[Common Issues]]** - Solutions to typical problems
- **[[Error Messages]]** - Understanding error codes and fixes
- **[[Performance Issues]]** - Speed and resource optimization

### 🛠️ **Developer Resources**
- **[[Architecture Overview]]** - Technical architecture and patterns
- **[[API Reference]]** - Internal APIs and interfaces
- **[[Contributing Guide]]** - How to contribute to Brainarr
- **[[Testing Guide]]** - Running tests and development workflow

## 🎯 **What is Brainarr?**

Brainarr is a sophisticated AI-powered import list plugin for Lidarr that analyzes your existing music library and generates personalized recommendations using state-of-the-art AI models. It supports both privacy-focused local AI models and powerful cloud-based services.

### ✨ **Core Features**

**🤖 Multi-Provider AI System**
- **9 AI Providers**: From privacy-focused local models to enterprise cloud services
- **Automatic Failover**: Seamless switching when providers are unavailable
- **Health Monitoring**: Real-time provider status tracking
- **Model Auto-Detection**: Dynamic discovery of available local models

**🎵 Intelligent Music Discovery** 
- **Library-Aware**: Analyzes your collection to understand your taste
- **Deduplication**: Prevents importing music you already have
- **Quality Filtering**: Detects and filters AI hallucinations
- **Multiple Modes**: Artist-only or specific album recommendations

**⚡ Enterprise-Grade Performance**
- **Smart Caching**: 60-minute intelligent caching system
- **Rate Limiting**: Per-provider API limit management
- **Retry Logic**: Exponential backoff with circuit breaker
- **Concurrent Safety**: Thread-safe operations throughout

**🔒 Security & Privacy**
- **Local-First Options**: Complete privacy with Ollama/LM Studio
- **API Key Protection**: Secure storage and validation
- **Input Sanitization**: Protection against injection attacks
- **Audit Logging**: Comprehensive operation tracking

### 🌟 **Supported AI Providers**

| Provider | Type | Cost | Speed | Quality | Privacy |
|----------|------|------|--------|---------|---------|
| **Ollama** | Local | Free | Medium | Good | 🔒 Complete |
| **LM Studio** | Local | Free | Medium | Good | 🔒 Complete |
| **DeepSeek** | Cloud | Very Low | Fast | Excellent | ⚠️ API Only |
| **Gemini** | Cloud | Low | Very Fast | Very Good | ⚠️ API Only |
| **Groq** | Cloud | Low | Ultra Fast | Good | ⚠️ API Only |
| **OpenRouter** | Cloud | Variable | Fast | Excellent | ⚠️ API Only |
| **Perplexity** | Cloud | Medium | Fast | Excellent | ⚠️ API Only |
| **OpenAI** | Cloud | High | Fast | Excellent | ⚠️ API Only |
| **Anthropic** | Cloud | High | Fast | Excellent | ⚠️ API Only |

## 📊 **Current Status**

- **Version**: 1.0.3 (Production Ready)
- **Test Coverage**: 485 tests with 100% pass rate
- **Platform Support**: Windows, macOS, Linux
- **Lidarr Compatibility**: 2.13.1.4681+ (plugins branch)
- **Active Development**: Regular updates and improvements

## 🔗 **Quick Links**

- **📦 Download**: [Latest Release v1.0.3](https://github.com/RicherTunes/Brainarr/releases/tag/v1.0.3)
- **🐳 Docker**: `ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692`
- **🐛 Issues**: [GitHub Issues](https://github.com/RicherTunes/Brainarr/issues)
- **💬 Discussions**: [Community Forum](https://github.com/RicherTunes/Brainarr/discussions)
- **📧 Support**: Create an issue for support requests

## 🎉 **Ready to Start?**

Choose your path:
- **🏠 Privacy-Focused**: Start with [[Local Providers]] (Ollama recommended)
- **☁️ Cloud Performance**: Jump to [[Cloud Providers]] (DeepSeek or Gemini recommended)
- **🐳 Docker Users**: Follow the [[Docker Setup]] guide
- **🔧 Advanced Users**: Check [[Advanced Settings]] for customization

---

**Happy music discovering! 🎵**
