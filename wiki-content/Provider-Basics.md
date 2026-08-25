
<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; CI auto-publishes to the Wiki.

# Provider basics

Provider status, defaults, and configuration live in repo docs so the generator keeps everything in sync.

- Review [`docs/PROVIDER_MATRIX.md`](https://github.com/RicherTunes/Brainarr/blob/main/docs/PROVIDER_MATRIX.md) for the generated compatibility table.
- Follow [docs/configuration.md](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md) for local-first defaults and how to enable cloud providers.
- See [docs/tokenization-and-estimates.md](https://github.com/RicherTunes/Brainarr/blob/main/docs/tokenization-and-estimates.md) when adding or tuning models.

Update those files first before adding extra notes here.

The sections below are the landing points for the **More info** links in the Brainarr settings form.

## Choosing a provider

Brainarr ships 14 providers in four families. Pick by what you already have:

| You have | Use | Why |
|---|---|---|
| A GPU box or spare CPU | **Ollama** or **LM Studio** | Local and private — nothing leaves your network. This is the default. |
| A ChatGPT or Claude subscription | **OpenAI Codex (Subscription)**, **Claude Code (Subscription)**, or **Claude Code CLI** | Reuses the CLI login you already have; no API key, no per-token bill. |
| An API key | OpenAI, Anthropic, Gemini, Perplexity, Groq, DeepSeek, OpenRouter, Z.AI GLM | Best quality per unit of effort, billed per token. |
| A Z.AI coding plan | **Z.AI Coding** | Subscription pricing against the GLM family. |

Whatever you pick, press **Test** before saving: it verifies connectivity, credentials, and that the
selected model is actually available to you. Full status table:
[`docs/PROVIDER_MATRIX.md`](https://github.com/RicherTunes/Brainarr/blob/main/docs/PROVIDER_MATRIX.md).

Two settings-form behaviours are worth knowing up front, because both look like bugs:

- The form loads model lists and the Configuration URL **when the page opens**. Changing only the *AI
  Provider* dropdown does not refetch them, so they can still show the previous provider's values. Test,
  Save, then reopen the import list.
- Cloud and subscription providers show a read-only Configuration URL — see
  [Configuration URL](#configuration-url).

## Configuration URL

For **local** providers this is the editable server address (Ollama `http://localhost:11434`, LM Studio
`http://localhost:1234`). Point it at the machine running the model; from inside a Docker container
`localhost` is the container itself, so a model on the host needs the host's address instead.

For **cloud and subscription** providers it is read-only and shown for reference only: the endpoint is
fixed in code and edits here are ignored. Because the field is only populated when the form loads, it can
lag a provider change until you save and reopen.

## API keys

Cloud providers reveal an **API Key** field. Keys are stored by Lidarr with its other settings and are
redacted in Brainarr's logs; get them from the provider's own console (linked per provider in
[docs/configuration.md](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#cloud-providers)).

Notes that trip people up:

- Editing the key is one of the few actions that makes Lidarr refetch the model list, so it is a quick way
  to refresh a stale dropdown.
- A key rejected several times in a row trips an auth circuit breaker: Brainarr stops retrying for a while
  instead of hammering the provider and risking a block. Fix the key, then wait for the cooldown or restart
  Lidarr.
- Subscription providers ([Claude Code](#claude-code-subscription),
  [OpenAI Codex](#openai-codex-subscription)) need no key at all — they read the CLI's own credentials file.

## Claude Code (Subscription)

Uses the OAuth token that `claude login` stores in `~/.claude/.credentials.json` — a Claude Pro/Max
subscription, not an `sk-ant-` API key.

1. `npm install -g @anthropic-ai/claude-code`
2. `claude login`
3. Select **Claude Code (Subscription)** and confirm the credentials path
4. Raise **AI Request Timeout** — see [Timeouts](Advanced-Settings.md#timeouts)

The path must point at a file the Lidarr process can actually read. In Docker that means a path *inside*
the container, and Brainarr only accepts credential files under the container user's home directory — if
yours lives elsewhere, set the container's `HOME` environment variable to its parent folder.
Setup detail: [docs/configuration.md](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#claude-code-subscription).

## OpenAI Codex (Subscription)

Uses the credentials `codex auth login` stores in `~/.codex/auth.json`. A plain ChatGPT subscription
(Plus/Pro) is enough — no API key. Brainarr talks to the same ChatGPT backend the Codex CLI uses and
refreshes the access token automatically, which matters because that token is only valid for about ten
days. If the file instead contains an `OPENAI_API_KEY`, that key is used against the standard OpenAI API.

1. `npm install -g @openai/codex`
2. `codex auth login`
3. Select **OpenAI Codex (Subscription)** and confirm the credentials path
4. **Raise AI Request Timeout to 60s or more** — with the 30s default a sync typically returns nothing
5. **Test**, then **Save**

Two constraints to respect:

- **Location.** The credentials path must sit under the container user's home directory (anything outside
  is rejected) and the folder must be **writable**, because the refreshed token is written back to this
  file. A read-only mount works until the token expires and then stops. Setting the container's `HOME`
  environment variable is the usual fix.
- **Models.** The available models depend on your ChatGPT plan, and this backend accepts only its own
  slugs — Platform model ids such as `gpt-4o` are rejected. Leave the default unless you have a reason to
  change it.

Setup detail: [docs/configuration.md](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#openai-codex-subscription).
