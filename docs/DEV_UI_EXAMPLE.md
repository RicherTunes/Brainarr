# Dev UI Example: Test Connection Details

This is a small, dev‑only HTML page that helps you exercise Brainarr’s provider actions without wiring a full UI.

- File: `examples/ui/testconnection_details.html`
- Purpose:
  - Call `testconnection/details` and render `hint` + `docs` (Learn more link)
  - Call `sanitycheck/commands` to get copy‑paste curl commands for quick out‑of‑band checks

Usage
- Start Lidarr with the Brainarr plugin loaded.
- Open the HTML file locally in your browser:
  - `file:///.../examples/ui/testconnection_details.html`
- Fill in:
  - `Lidarr URL` (e.g., `http://localhost:8686`)
  - `Lidarr API Key` (Settings > General > Security)
  - Select provider and supply provider key/model if needed
- Click:
  - `Test Connection (details)` to see `{ success, provider, hint, message, docs }`
  - `Get Sanity Commands` to see provider‑specific curl examples

Notes
- This page is not intended for production deployment; it’s a developer helper only.
- Keys entered here are used locally by your browser to call your Lidarr instance; do not share screenshots or screen recordings that reveal keys.
- The Learn more link points to the relevant section in our GitHub docs (e.g., Gemini `SERVICE_DISABLED`).

Related docs
- API: `docs/API_REFERENCE.md` (Provider UI Actions)
- Troubleshooting: `docs/troubleshooting.md`
