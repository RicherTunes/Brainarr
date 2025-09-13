#!/usr/bin/env bash
set -euo pipefail

# Simple dev script to call Brainarr provider action testconnection/details against a running Lidarr.
# Prints success, message, and hint (if any) to verify provider-specific guidance (e.g., Gemini SERVICE_DISABLED activation link).

usage() {
  cat <<EOF
Usage: $0 --lidarr-url URL --api-key KEY --provider NAME [--model MODEL] [--gemini-key KEY] [--openai-key KEY] [--anthropic-key KEY] [--openrouter-key KEY]

Examples:
  $0 --lidarr-url http://localhost:8686 --api-key LIDARR_API --provider Gemini --gemini-key AIza... --model gemini-1.5-flash
  $0 --lidarr-url http://localhost:8686 --api-key LIDARR_API --provider OpenAI --openai-key sk-... --model gpt-4o-mini

Provider names: Ollama, LMStudio, OpenAI, Anthropic, OpenRouter, Perplexity, Gemini, DeepSeek, Groq
EOF
}

LIDARR_URL=""
LIDARR_API_KEY=""
PROVIDER=""
MODEL=""
GEMINI_KEY=""
OPENAI_KEY=""
ANTHROPIC_KEY=""
OPENROUTER_KEY=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --lidarr-url) LIDARR_URL="$2"; shift 2;;
    --api-key) LIDARR_API_KEY="$2"; shift 2;;
    --provider) PROVIDER="$2"; shift 2;;
    --model) MODEL="$2"; shift 2;;
    --gemini-key) GEMINI_KEY="$2"; shift 2;;
    --openai-key) OPENAI_KEY="$2"; shift 2;;
    --anthropic-key) ANTHROPIC_KEY="$2"; shift 2;;
    --openrouter-key) OPENROUTER_KEY="$2"; shift 2;;
    -h|--help) usage; exit 0;;
    *) echo "Unknown arg: $1"; usage; exit 1;;
  esac
done

[[ -n "$LIDARR_URL" && -n "$LIDARR_API_KEY" && -n "$PROVIDER" ]] || { usage; exit 1; }

# Build settings payload
case "$PROVIDER" in
  Gemini)
    [[ -n "$GEMINI_KEY" ]] || { echo "--gemini-key is required"; exit 1; }
    MODEL_VAL=${MODEL:-gemini-1.5-flash}
    read -r -d '' SETTINGS <<JSON || true
{ "Provider": "Gemini", "GeminiApiKey": "$GEMINI_KEY", "GeminiModel": "$MODEL_VAL" }
JSON
    ;;
  OpenAI)
    [[ -n "$OPENAI_KEY" ]] || { echo "--openai-key is required"; exit 1; }
    MODEL_VAL=${MODEL:-gpt-4o-mini}
    read -r -d '' SETTINGS <<JSON || true
{ "Provider": "OpenAI", "OpenAIApiKey": "$OPENAI_KEY", "OpenAIModel": "$MODEL_VAL" }
JSON
    ;;
  Anthropic)
    [[ -n "$ANTHROPIC_KEY" ]] || { echo "--anthropic-key is required"; exit 1; }
    MODEL_VAL=${MODEL:-claude-3.5-haiku}
    read -r -d '' SETTINGS <<JSON || true
{ "Provider": "Anthropic", "AnthropicApiKey": "$ANTHROPIC_KEY", "AnthropicModel": "$MODEL_VAL" }
JSON
    ;;
  OpenRouter)
    [[ -n "$OPENROUTER_KEY" ]] || { echo "--openrouter-key is required"; exit 1; }
    MODEL_VAL=${MODEL:-anthropic/claude-3.5-haiku}
    read -r -d '' SETTINGS <<JSON || true
{ "Provider": "OpenRouter", "OpenRouterApiKey": "$OPENROUTER_KEY", "OpenRouterModel": "$MODEL_VAL" }
JSON
    ;;
  *)
    echo "Provider $PROVIDER not implemented in this script"; exit 1;;
esac

BODY=$(jq -c --arg action "testconnection/details" '. + {action: $action}' <<< "$SETTINGS")

echo "POST $LIDARR_URL/api/v1/brainarr/provider/action (action=testconnection/details)"
RESP=$(curl -sS -X POST "$LIDARR_URL/api/v1/brainarr/provider/action" \
  -H "X-Api-Key: $LIDARR_API_KEY" \
  -H "Content-Type: application/json" \
  -d "$BODY")

echo "$RESP" | jq '.' || { echo "$RESP"; exit 1; }

