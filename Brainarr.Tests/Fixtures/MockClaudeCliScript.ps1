#!/usr/bin/env pwsh
# MockClaudeCliScript.ps1
# Simulates Claude CLI NDJSON streaming output for E2E testing
# Outputs content_block_delta events with deliberate delays to prove streaming works

param(
    [string]$Prompt = "",
    [switch]$Help,
    [switch]$Version
)

if ($Help) {
    Write-Output "Mock Claude CLI for testing"
    Write-Output "Usage: MockClaudeCliScript.ps1 -p <prompt>"
    exit 0
}

if ($Version) {
    Write-Output "mock-claude 1.0.0 (test)"
    exit 0
}

# Simulate streaming NDJSON output with delays
# Each line is a separate JSON object (NDJSON format)

# Start message
Write-Output '{"type":"message_start","message":{"id":"msg_test_001","type":"message","role":"assistant"}}'

# Content block start
Write-Output '{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}'

# Stream content deltas with small delays to prove streaming works
$chunks = @(
    '[{"artist"',
    ':"The Beatles"',
    ',"album":"Abbey Road"',
    ',"genre":"Rock"',
    ',"confidence":0.95',
    ',"reason":"Classic rock album"}]'
)

foreach ($chunk in $chunks) {
    Start-Sleep -Milliseconds 50  # Small delay to prove incremental delivery
    # Escape quotes for JSON embedding
    $escaped = $chunk.Replace('\', '\\').Replace('"', '\"')
    Write-Output "{`"type`":`"content_block_delta`",`"index`":0,`"delta`":{`"type`":`"text_delta`",`"text`":`"$escaped`"}}"
}

# Content block stop
Write-Output '{"type":"content_block_stop","index":0}'

# Message delta with usage
Write-Output '{"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":50}}'

# Message stop
Write-Output '{"type":"message_stop"}'

exit 0
