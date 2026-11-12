const { chromium } = require('playwright');

const base = process.env.RADARR_URL || 'http://127.0.0.1:7878';
const user = process.env.RADARR_USER || 'radarr';
const pass = process.env.RADARR_PASSWORD || 'radarr-test';
const shots = process.env.SHOT_DIR || '/tmp';

function fail(message) {
  console.error(message);
  process.exitCode = 1;
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1400, height: 1100 } });
  page.setDefaultTimeout(60000);

  try {
    await page.goto(`${base}/settings/mediamanagement`, { waitUntil: 'domcontentloaded' });

    if (page.url().includes('/login')) {
      await page.fill('input[name="username"]', user);
      await page.fill('input[name="password"]', pass);
      await page.click('button[type="submit"]');
      await page.waitForURL((url) => !url.toString().includes('/login'));
      await page.goto(`${base}/settings/mediamanagement`, { waitUntil: 'domcontentloaded' });
    }

    const structure = page.getByTestId('library-structure');
    await structure.waitFor();
    await page.screenshot({ path: `${shots}/library-structure-before.png`, fullPage: true });

    const noChanges = page.getByRole('button', { name: /No Changes|NoChanges/ });
    if (!(await noChanges.isDisabled())) {
      throw new Error('Save started enabled before Library Structure changed');
    }

    await structure.getByRole('button').click();
    await page.getByText('Flat folder', { exact: true }).click();

    const saveChanges = page.getByRole('button', { name: /Save Changes|SaveChanges/ });
    await saveChanges.waitFor();
    if (await saveChanges.isDisabled()) {
      throw new Error('Save Changes did not activate after changing Library Structure');
    }

    const movieFormat = page.locator('input[name="movieFolderFormat"]');
    const customFormat = page.locator('input[name="customMovieFolderFormat"]');
    if (await movieFormat.isEnabled()) {
      throw new Error('Movie Folder Format stayed enabled for Flat');
    }
    if (await customFormat.isEnabled()) {
      throw new Error('Custom Movie Folder Format stayed enabled for Flat');
    }

    await page.screenshot({ path: `${shots}/library-structure-dirty.png`, fullPage: true });
    await saveChanges.click();
    await page.getByRole('button', { name: /No Changes|NoChanges/ }).waitFor();

    await page.reload({ waitUntil: 'domcontentloaded' });
    await structure.waitFor();
    await structure.getByText('Flat folder', { exact: true }).waitFor();
    if (await movieFormat.isEnabled()) {
      throw new Error('Movie Folder Format was enabled after reload');
    }
    if (await customFormat.isEnabled()) {
      throw new Error('Custom Movie Folder Format was enabled after reload');
    }
    await page.screenshot({ path: `${shots}/library-structure-saved.png`, fullPage: true });

    await structure.getByRole('button').click();
    await page.getByText('Movie folder', { exact: true }).click();
    if (!(await movieFormat.isEnabled())) {
      throw new Error('Movie Folder Format did not enable for Movie folder');
    }
    if (await customFormat.isEnabled()) {
      throw new Error('Custom Movie Folder Format enabled for Movie folder');
    }

    await structure.getByRole('button').click();
    await page.getByText('Custom nested folders', { exact: true }).click();
    if (await movieFormat.isEnabled()) {
      throw new Error('Movie Folder Format stayed enabled for Custom');
    }
    if (!(await customFormat.isEnabled())) {
      throw new Error('Custom Movie Folder Format did not enable for Custom');
    }

    await page.screenshot({ path: `${shots}/library-structure-custom.png`, fullPage: true });
    console.log('library structure UI check passed');
  } catch (error) {
    await page.screenshot({ path: `${shots}/library-structure-failed.png`, fullPage: true }).catch(() => {});
    fail(error && error.stack ? error.stack : String(error));
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
