# Security Overview

This document is the canonical security note for Brainarr releases. It describes the data flows, the threat model we design for, and the concrete controls we use in code and CI. A deeper, operations‑focused guide with examples lives in `docs/SECURITY.md`.

## Compatibility

- Brainarr v1.3.1 requires Lidarr 2.14.2.4786+ on the plugins/nightly branch. See README for exact wording and CI source of truth.

## Threat Model (concise)

- Trust boundary: the plugin runs inside the Lidarr host process; Brainarr does not load or execute external code.
- Inputs: library metadata from Lidarr; user‑provided configuration (provider URLs, API keys, timeouts); optional network responses from configured AI providers.
- Outputs: network requests only to the configured provider(s) and standard logs to Lidarr’s logging sinks. No analytics or phone‑home calls are built in.
- Secrets: API keys live in Lidarr’s plugin configuration storage. Operators may also use environment variables or an external secret store (see docs/SECURITY.md). Brainarr never hard‑codes keys.

## Network & Transport

- Local‑first by default: Ollama and LM Studio operate without sending data off‑host.
- When using cloud providers, requests go over HTTPS using the platform TLS stack; the plugin does not disable certificate validation.
- Timeouts and retries are provider‑scoped; calls are budgeted so the UI stays responsive.

## Hardening & Correctness

- Deterministic planning and stable ordering across platforms (Windows/Linux) to keep results reproducible.
- Cancellation tokens propagate through provider calls to avoid “zombie” work after timeouts.
- Input and output payloads are strictly JSON; serializers are configured to avoid unsafe polymorphic deserialization.

## CI/CD Controls

- CodeQL (C#) runs on PRs and on a weekly schedule.
- The release workflow builds against real Lidarr assemblies extracted from `ghcr.io/hotio/lidarr:${{LIDARR_DOCKER_VERSION}}` (plugins branch) and attaches an SBOM to tagged releases.
- Docs and manifests are checked for version consistency in CI.

## Reporting & Scope

If you believe you've found a vulnerability, open a Security issue or email the maintainers via the GitHub security contact on the repository. Please do not file sensitive details in public issues.

Out-of-scope: vulnerabilities in third-party AI providers or models; issues in Lidarr itself; operator misconfiguration outside of Brainarr's documented settings.

## Release artifact verification (checksums + Cosign)

Starting with v1.3.2, every release publishes:

- `Brainarr-<version>.zip` — plugin package
- `Brainarr-<version>.zip.sha256` — SHA‑256 checksum
- `Brainarr-<version>.zip.sig` — Sigstore Cosign signature (keyless)

Verify integrity and provenance:

Linux/macOS

```bash
VERSION=v1.3.2
curl -LO https://github.com/RicherTunes/Brainarr/releases/download/$VERSION/Brainarr-$VERSION.zip
curl -LO https://github.com/RicherTunes/Brainarr/releases/download/$VERSION/Brainarr-$VERSION.zip.sha256
curl -LO https://github.com/RicherTunes/Brainarr/releases/download/$VERSION/Brainarr-$VERSION.zip.sig

# 1) Checksum
sha256sum -c Brainarr-$VERSION.zip.sha256

# 2) Cosign keyless signature (certificate embedded in signature)
cosign verify-blob \
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com" \
  --certificate-identity-regexp "^https://github.com/RicherTunes/Brainarr/.+" \
  --signature Brainarr-$VERSION.zip.sig \
  Brainarr-$VERSION.zip
```

Windows PowerShell

```powershell
$Version = "v1.3.2"
Invoke-WebRequest -OutFile "Brainarr-$Version.zip" "https://github.com/RicherTunes/Brainarr/releases/download/$Version/Brainarr-$Version.zip"
Invoke-WebRequest -OutFile "Brainarr-$Version.zip.sha256" "https://github.com/RicherTunes/Brainarr/releases/download/$Version/Brainarr-$Version.zip.sha256"
Invoke-WebRequest -OutFile "Brainarr-$Version.zip.sig" "https://github.com/RicherTunes/Brainarr/releases/download/$Version/Brainarr-$Version.zip.sig"

# 1) Checksum
Get-FileHash "Brainarr-$Version.zip" -Algorithm SHA256 | ForEach-Object {
  $expected = Get-Content "Brainarr-$Version.zip.sha256" | Select-Object -First 1
  if ($_.Hash.ToLower() -ne $expected.Split(' ')[0].ToLower()) { throw "SHA256 mismatch" } else { "Checksum OK" }
}

# 2) Cosign keyless signature
cosign verify-blob `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com" `
  --certificate-identity-regexp "^https://github.com/RicherTunes/Brainarr/.+" `
  --signature "Brainarr-$Version.zip.sig" `
  "Brainarr-$Version.zip"
```

Explanation

- We use Sigstore “keyless” signing from GitHub Actions OIDC. Cosign validates against GitHub’s issuer and requires the certificate identity to match this repository.
- No public keys to fetch or rotate; trust anchors are distributed by Sigstore/TUF and managed by Cosign.
