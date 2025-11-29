// Brainarr Screenshot Wrapper
// Thin wrapper around centralized screenshot utility in lidarr.plugin.common
// Usage: node scripts/snapshots/snap.mjs

import { spawn } from 'child_process';
import { existsSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));

// Find the shared utility - check multiple possible locations
const possiblePaths = [
  resolve(__dirname, '../../../ext/lidarr.plugin.common/scripts/snapshots/snap.mjs'),
  resolve(__dirname, '../../../Lidarr.Plugin.Common/scripts/snapshots/snap.mjs'),
  resolve(__dirname, '../../ext/Lidarr.Plugin.Common/scripts/snapshots/snap.mjs'),
];

let sharedScript = null;
for (const p of possiblePaths) {
  if (existsSync(p)) {
    sharedScript = p;
    break;
  }
}

// Configuration for Brainarr
const PLUGIN_NAME = 'Brainarr';
const PLUGIN_TYPES = 'import-list';  // Brainarr is an Import List, not Indexer
const OUTPUT_DIR = process.env.OUTPUT_DIR || 'docs/assets/screenshots';
const LIDARR_URL = process.env.LIDARR_BASE_URL || 'http://localhost:8686';

if (sharedScript) {
  // Use centralized utility
  console.log(`Using shared screenshot utility: ${sharedScript}`);
  const child = spawn('node', [
    sharedScript,
    `--plugin=${PLUGIN_NAME}`,
    `--type=${PLUGIN_TYPES}`,
    `--output=${OUTPUT_DIR}`,
    `--url=${LIDARR_URL}`
  ], { stdio: 'inherit' });

  child.on('exit', (code) => {
    process.exitCode = code;
  });
} else {
  // Fallback to inline implementation if shared utility not found
  console.log('Shared utility not found, using inline implementation...');

  import('playwright').then(async ({ chromium }) => {
    const BASE = LIDARR_URL;
    const OUTDIR = OUTPUT_DIR;

    async function screenshotOrSkip(page, name, fn) {
      try {
        await fn();
        await page.screenshot({ path: `${OUTDIR}/${name}.png`, fullPage: true });
        console.log(`saved: ${OUTDIR}/${name}.png`);
      } catch (err) {
        console.warn(`skip ${name}: ${err?.message || err}`);
      }
    }

    const browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({
      viewport: { width: 1440, height: 900 },
      userAgent: 'brainarr-ci-screenshot',
      colorScheme: 'dark'
    });
    const page = await context.newPage();

    await page.goto(BASE, { waitUntil: 'domcontentloaded', timeout: 60_000 });
    await page.waitForLoadState('networkidle', { timeout: 60_000 }).catch(() => {});

    // Skip wizard
    for (const text of ['Next', 'Continue', 'Skip', 'Finish']) {
      const el = page.getByRole('button', { name: text });
      if (await el.count().catch(() => 0)) {
        await el.first().click({ timeout: 2000 }).catch(() => {});
      }
    }

    // Landing
    await screenshotOrSkip(page, 'landing', () => page.waitForTimeout(800));

    // Settings
    await page.goto(`${BASE}/settings`, { waitUntil: 'domcontentloaded' });
    await screenshotOrSkip(page, 'settings', () => page.waitForTimeout(500));

    // Import Lists
    await screenshotOrSkip(page, 'import-lists', async () => {
      const importLists = page.getByText(/import lists/i).first();
      if (await importLists.count()) {
        await importLists.click({ timeout: 2000 }).catch(() => {});
      }
      const addBtn = page.getByRole('button', { name: /add/i }).first();
      if (await addBtn.count()) {
        await addBtn.click({ timeout: 2000 }).catch(() => {});
      }
      const search = page.getByPlaceholder(/search/i).first();
      if (await search.count()) {
        await search.fill('Brainarr').catch(() => {});
        await page.waitForTimeout(500);
      }
    });

    // Results
    await screenshotOrSkip(page, 'results', () => page.waitForTimeout(500));

    await browser.close();
    console.log('Screenshot capture complete!');
  }).catch((e) => {
    console.error(e);
    process.exitCode = 1;
  });
}
