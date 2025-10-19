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

async function dismissOverlays(page) {
  // Best-effort close of onboarding or modal overlays that block clicks
  try {
    await page.keyboard.press('Escape');
  } catch {}
  const candidates = [
    '[aria-label="Close"]',
    'button[aria-label="Close"]',
    'button:has-text("Close")',
    'button:has-text("Dismiss")',
    'button:has-text("Got it")',
    'button:has-text("Skip")',
    'button:has-text("Continue")',
    'button:has-text("Finish")',
  ];
  for (const sel of candidates) {
    const loc = page.locator(sel).first();
    if (await loc.count().catch(() => 0)) {
      await loc.click({ timeout: 1000 }).catch(() => {});
    }
  }
  // As a last resort, remove known modal backdrops to avoid intercepted clicks
  await page.evaluate(() => {
    const kill = (q) => document.querySelectorAll(q).forEach((n) => n.remove());
    kill('#portal-root [class*="Modal-modalBackdrop"]');
    kill('#portal-root [role="dialog"]');
  }).catch(() => {});
  await page.waitForTimeout(150);
}

async function gotoAndVerify(page, url, verify) {
  await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30_000 }).catch(() => {});
  await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {});
  await dismissOverlays(page);
  if (verify) {
    try {
      await verify();
    } catch {
      // one retry via hard reload
      await page.reload({ waitUntil: 'domcontentloaded' }).catch(() => {});
      await page.waitForLoadState('networkidle', { timeout: 5_000 }).catch(() => {});
      await dismissOverlays(page);
      if (verify) await verify().catch(() => {});
    }
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
    await dismissOverlays(page);
    await page.waitForTimeout(500);
  });

  // Navigate to Settings if visible
  const goSettings = async () => {
    // Prefer direct route; verify URL or presence of a common settings control
    await gotoAndVerify(page, `${BASE}/settings`, async () => {
      // Either the URL includes /settings or a Save/Apply button is visible
      if (!page.url().includes('/settings')) {
        const saveBtn = page.getByRole('button', { name: /save|apply/i }).first();
        await saveBtn.waitFor({ state: 'visible', timeout: 5000 });
      }
    });
  };

  // Never let navigation errors abort the whole run
  await goSettings().catch(() => {});

  // Plugins page (name varies; try matching text)
  await screenshotOrSkip(page, 'settings', async () => {
    await page.waitForTimeout(500);
  });

  // Try to open Import Lists page and search for Brainarr provider
  await screenshotOrSkip(page, 'import-lists', async () => {
    await gotoAndVerify(page, `${BASE}/settings/importlists`, async () => {
      const heading = page.getByText(/import lists/i).first();
      await heading.waitFor({ state: 'visible', timeout: 5000 });
    });
    // Open add modal if available
    const addBtn = page.getByRole('button', { name: /add/i }).first();
    if (await addBtn.count()) {
      await addBtn.click({ timeout: 2000 }).catch(() => {});
      await dismissOverlays(page);
    }
    // Type Brainarr into any search field
    const search = page.getByPlaceholder(/search/i).first();
    if (await search.count()) {
      await search.fill('Brainarr').catch(() => {});
      await page.waitForTimeout(400);
    }
  });

  // Result list (after searching Brainarr above)
  await screenshotOrSkip(page, 'results', async () => {
    // Prefer a result tile/card that mentions Brainarr
    const result = page.getByText(/brainarr/i).first();
    if (await result.count()) {
      await result.scrollIntoViewIfNeeded().catch(() => {});
    }
    await page.waitForTimeout(400);
  });

  await browser.close();
}

run().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
