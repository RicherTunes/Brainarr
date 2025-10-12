// Lightweight Playwright screenshotter for Lidarr + Brainarr UI
// Usage: node scripts/snapshots/snap.mjs
// Requires: `npx playwright install --with-deps chromium`

import { chromium } from 'playwright';

const BASE = process.env.LIDARR_BASE_URL || 'http://localhost:8686';
const OUTDIR = 'docs/assets/screenshots';

async function screenshotOrSkip(page, name, fn) {
  try {
    await fn();
    await page.screenshot({ path: `${OUTDIR}/${name}.png`, fullPage: true });
    console.log(`saved: ${OUTDIR}/${name}.png`);
  } catch (err) {
    console.warn(`skip ${name}: ${err?.message || err}`);
  }
}

async function run() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    userAgent: 'brainarr-ci-screenshot',
    colorScheme: 'dark'
  });
  const page = await context.newPage();

  // Basic navigation + wizard-friendly waits
  await page.goto(BASE, { waitUntil: 'domcontentloaded', timeout: 60_000 });
  await page.waitForLoadState('networkidle', { timeout: 60_000 }).catch(() => {});

  // Try to breeze through wizard if present by clicking common buttons
  const tryClick = async (text) => {
    const el = page.getByRole('button', { name: text });
    if (await el.count().catch(() => 0)) {
      await el.first().click({ timeout: 2000 }).catch(() => {});
    }
  };
  await tryClick('Next');
  await tryClick('Continue');
  await tryClick('Skip');
  await tryClick('Finish');

  // Landing
  await screenshotOrSkip(page, 'landing', async () => {
    await page.waitForTimeout(800);
  });

  // Navigate to Settings if visible
  const goSettings = async () => {
    const settings = page.getByRole('link', { name: /settings/i });
    if (await settings.count()) {
      await settings.first().click();
      await page.waitForLoadState('networkidle');
    } else {
      // try direct route
      await page.goto(`${BASE}/settings`, { waitUntil: 'domcontentloaded' });
      await page.waitForLoadState('networkidle').catch(() => {});
    }
  };

  await goSettings();

  // Plugins page (name varies; try matching text)
  await screenshotOrSkip(page, 'settings', async () => {
    await page.waitForTimeout(500);
  });

  // Try to open Import Lists modal and search Brainarr
  await screenshotOrSkip(page, 'import-lists', async () => {
    const importLists = page.getByText(/import lists/i).first();
    if (await importLists.count()) {
      await importLists.click({ timeout: 2000 }).catch(() => {});
    }
    const addBtn = page.getByRole('button', { name: /add/i }).first();
    if (await addBtn.count()) {
      await addBtn.click({ timeout: 2000 }).catch(() => {});
    }
    // type Brainarr into any search field
    const search = page.getByPlaceholder(/search/i).first();
    if (await search.count()) {
      await search.fill('Brainarr').catch(() => {});
      await page.waitForTimeout(500);
    }
  });

  // Result list (where available)
  await screenshotOrSkip(page, 'results', async () => {
    await page.waitForTimeout(500);
  });

  await browser.close();
}

run().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
