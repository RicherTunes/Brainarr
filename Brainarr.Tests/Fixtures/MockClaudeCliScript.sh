#!/bin/bash
# MockClaudeCliScript.sh - Cross-platform mock for Linux CI
# Simulates Claude CLI NDJSON streaming output for E2E testing

if [[ "$1" == "--help" || "$1" == "-h" ]]; then
    echo "Mock Claude CLI for testing"
    exit 0
fi

if [[ "$1" == "--version" ]]; then
    echo "mock-claude 1.0.0 (test)"
    exit 0
fi

# Stream NDJSON with delays

# Start message
echo '{"type":"message_start","message":{"id":"msg_test_001","type":"message","role":"assistant"}}'

# Content block start
echo '{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}'

# Stream content deltas with small delays to prove streaming works
chunks=(
    '[{"artist"'
    ':"The Beatles"'
    ',"album":"Abbey Road"'
    ',"genre":"Rock"'
    ',"confidence":0.95'
    ',"reason":"Classic rock album"}]'
)

for chunk in "${chunks[@]}"; do
    sleep 0.05
    # Escape quotes and backslashes for JSON embedding
    escaped=$(echo "$chunk" | sed 's/\\/\\\\/g' | sed 's/"/\\"/g')
    echo "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"$escaped\"}}"
done

# Content block stop
echo '{"type":"content_block_stop","index":0}'

# Message delta with usage
echo '{"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":50}}'

# Message stop
echo '{"type":"message_stop"}'

exit 0
