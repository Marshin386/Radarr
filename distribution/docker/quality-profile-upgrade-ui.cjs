const { chromium } = require('playwright');

const base = process.env.RADARR_URL || 'http://127.0.0.1:7878';
const user = process.env.RADARR_USER || 'radarr';
const pass = process.env.RADARR_PASSWORD || 'radarr-test';
const destination = process.env.UPGRADE_DEST || '/config/upgrade-dest';
const shots = process.env.SHOT_DIR || '/tmp';

(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1400, height: 1100 } });
  page.setDefaultTimeout(60000);

  try {
    await page.goto(`${base}/settings/profiles`, { waitUntil: 'domcontentloaded' });

    if (page.url().includes('/login')) {
      await page.fill('input[name="username"]', user);
      await page.fill('input[name="password"]', pass);
      await page.click('button[type="submit"]');
      await page.waitForURL((url) => !url.toString().includes('/login'));
      await page.goto(`${base}/settings/profiles`, { waitUntil: 'domcontentloaded' });
    }

    await page.getByText('Any', { exact: true }).click();
    const placement = page.getByTestId('upgrade-file-placement');
    const upgrades = page.locator('input[name="upgradeAllowed"]');
    await upgrades.waitFor();

    if (!(await upgrades.isChecked())) {
      await upgrades.click();
    }

    await placement.waitFor();
    await page.screenshot({ path: `${shots}/upgrade-placement-before.png`, fullPage: true });

    const noChanges = page.getByRole('button', { name: /No Changes|NoChanges/ });
    if ((await noChanges.count()) && (await noChanges.first().isVisible()) && !(await noChanges.first().isDisabled())) {
      throw new Error('profile save was enabled before any change');
    }

    await placement.getByRole('button').click();
    await page.getByText('Move to designated location', { exact: true }).click();
    const destinationInput = page.locator('input[name="upgradeDestination"]');
    await destinationInput.waitFor();
    await destinationInput.fill(destination);

    const save = page.getByRole('button', { name: /Save Changes|SaveChanges/ });
    await save.waitFor();
    if (await save.isDisabled()) {
      throw new Error('Save Changes did not activate for the upgrade location');
    }

    await page.screenshot({ path: `${shots}/upgrade-placement-dirty.png`, fullPage: true });
    await save.click();
    await placement.waitFor({ state: 'hidden' });

    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.getByText('Any', { exact: true }).click();
    await placement.waitFor();
    await placement.getByText('Move to designated location', { exact: true }).waitFor();
    await page.locator('input[name="upgradeDestination"]').waitFor();
    const savedValue = await page.locator('input[name="upgradeDestination"]').inputValue();
    if (savedValue !== destination) {
      throw new Error(`upgrade destination reloaded as '${savedValue}'`);
    }

    await placement.getByRole('button').click();
    await page.getByText('Replace in current location', { exact: true }).click();
    if (await page.locator('input[name="upgradeDestination"]').count()) {
      if (await page.locator('input[name="upgradeDestination"]').isVisible()) {
        throw new Error('destination picker stayed visible for replace in current location');
      }
    }

    await page.screenshot({ path: `${shots}/upgrade-placement-saved.png`, fullPage: true });
    await page.getByRole('button', { name: /Save Changes|SaveChanges/ }).click();
    await placement.waitFor({ state: 'hidden' });
    console.log('upgrade placement UI check passed');
  } catch (error) {
    await page.screenshot({ path: `${shots}/upgrade-placement-failed.png`, fullPage: true }).catch(() => {});
    console.error(error && error.stack ? error.stack : error);
    process.exitCode = 1;
  } finally {
    await browser.close();
  }

  if (process.exitCode) {
    process.exit(process.exitCode);
  }
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
