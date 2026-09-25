// End-to-end check of the files_middleclick app on a local Nextcloud 33.
// Usage: node test.js [with|without]   (whether the app is expected to be enabled)
const { chromium } = require('playwright')

const BASE = 'http://127.0.0.1:8081/index.php'
const expectApp = (process.argv[2] || 'with') === 'with'
const results = []
const ok = (name, cond, extra = '') => {
	results.push({ name, pass: !!cond, extra })
	console.log(`${cond ? 'PASS' : 'FAIL'}  ${name}${extra ? '  (' + extra + ')' : ''}`)
}

;(async () => {
	const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' })
	const context = await browser.newContext({ viewport: { width: 1400, height: 500 } })
	const page = await context.newPage()
	const jsErrors = []
	page.on('pageerror', (e) => jsErrors.push(String(e)))
	page.on('console', (m) => { if (m.type() === 'error' && !m.text().includes('Failed to load resource')) jsErrors.push(m.text()) })

	// --- login
	await page.goto(`${BASE}/login`)
	await page.fill('#user', 'admin')
	await page.fill('#password', 'Adm1nPassw0rd!')
	await page.click('button[type=submit]')
	await page.waitForURL(/apps\/(files|dashboard)/, { timeout: 60000 })
	await page.goto(`${BASE}/apps/files/files?dir=/Docs`)
	await page.waitForSelector('[data-cy-files-list-row-name="report_1.txt"]', { timeout: 60000 })

	// --- 1. script tag present only when app enabled
	const scriptLoaded = await page.evaluate(() => [...document.scripts].some((s) => s.src.includes('files_middleclick/js/main.js')))
	ok('script loaded == app enabled', scriptLoaded === expectApp, `loaded=${scriptLoaded}`)

	// --- 2. mousedown default is cancelled ONLY for middle button on file name
	const probe = async (selector, button) => {
		return page.evaluate(({ selector, button }) => new Promise((resolve) => {
			const el = document.querySelector(selector)
			const h = (e) => { if (e.button === button) { document.removeEventListener('mousedown', h); resolve(e.defaultPrevented) } }
			document.addEventListener('mousedown', h) // bubbling phase: runs after the app's capture listener
			const r = el.getBoundingClientRect()
			el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, button, clientX: r.x + 5, clientY: r.y + 5 }))
		}), { selector, button })
	}
	const nameSel = '[data-cy-files-list-row-name="report_1.txt"] .files-list__row-name button'
	const sizeSel = '[data-cy-files-list-row-name="report_1.txt"] .files-list__row-size'
	ok('middle mousedown on name: prevented == app enabled', (await probe(nameSel, 1)) === expectApp)
	ok('left mousedown on name: NOT prevented', (await probe(nameSel, 0)) === false)
	ok('right mousedown on name: NOT prevented', (await probe(nameSel, 2)) === false)
	ok('middle mousedown on size cell: NOT prevented', (await probe(sizeSel, 1)) === false)

	// --- 3. real middle click opens new tab with /f/<id>
	const nameBtn = page.locator(nameSel)
	const [popup] = await Promise.all([
		context.waitForEvent('page', { timeout: 10000 }).catch(() => null),
		nameBtn.click({ button: 'middle' }),
	])
	ok('middle click opens new tab', popup && /\/(f|files\/files)\/\d+/.test(popup.url()), popup ? popup.url() : 'no popup')
	if (popup) await popup.close()
	ok('middle click stays on Docs (no navigation)', /dir=(%2F|\/)Docs/.test(page.url()), page.url())

	// --- 4. ctrl+click opens new tab
	const [popup2] = await Promise.all([
		context.waitForEvent('page', { timeout: 10000 }).catch(() => null),
		nameBtn.click({ modifiers: ['Control'] }),
	])
	ok('ctrl+click opens new tab', popup2 && /\/(f|files\/files)\/\d+/.test(popup2.url()), popup2 ? popup2.url() : 'no popup')
	if (popup2) await popup2.close()

	// --- 5. right click opens the actions menu
	await nameBtn.click({ button: 'right' })
	const menu = page.locator('[data-cy-files-list-row-action="details"], .files-list__row-action-details, [data-cy-files-list-row-actions] [role=menu]')
	ok('right click opens actions menu', await menu.first().isVisible({ timeout: 5000 }).catch(() => false))
	await page.keyboard.press('Escape')

	// --- 6. left click on a file runs the default action (opens viewer / changes URL)
	const urlBefore = page.url()
	await nameBtn.click()
	await page.waitForTimeout(1500)
	const opened = page.url() !== urlBefore || await page.locator('#viewer, .viewer, [data-cy-viewer], .modal-mask').first().isVisible().catch(() => false)
	ok('left click runs default action', opened, page.url())
	await page.keyboard.press('Escape')
	await page.goto(`${BASE}/apps/files/files?dir=/`)
	await page.waitForSelector('[data-cy-files-list-row-name="root.txt"]', { timeout: 60000 })

	// --- 7. left click on a folder navigates into it
	await page.locator('[data-cy-files-list-row-name="Docs"] .files-list__row-name button').click()
	await page.waitForURL(/Docs/, { timeout: 10000 }).catch(() => {})
	ok('left click on folder navigates', /dir=(%2F|\/)Docs/.test(page.url()), page.url())
	await page.goto(`${BASE}/apps/files/files?dir=/`)
	await page.waitForSelector('[data-cy-files-list-row-name="root.txt"]', { timeout: 60000 })

	// --- 8. drag & drop root.txt onto Target folder still works
	const src = page.locator('[data-cy-files-list-row-name="root.txt"] .files-list__row-name')
	const dst = page.locator('[data-cy-files-list-row-name="Target"] .files-list__row-name')
	await src.dragTo(dst)
	await page.waitForTimeout(2500)
	const moved = await page.locator('[data-cy-files-list-row-name="root.txt"]').count() === 0
	ok('drag & drop moves file', moved)

	// --- 9. checkbox selection works
	await page.locator('[data-cy-files-list-row-name="Docs"] .files-list__row-checkbox input, [data-cy-files-list-row-name="Docs"] .files-list__row-checkbox').first().click({ force: true })
	const selected = await page.locator('.files-list__selected, [data-cy-files-list-selection-actions]').first().isVisible({ timeout: 5000 }).catch(() => false)
	ok('checkbox selection works', selected)

	ok('no JS errors on Files pages', jsErrors.length === 0, jsErrors.slice(0, 3).join(' | '))

	// --- 10. public share page
	const shareUrl = process.env.SHARE_URL
	if (shareUrl) {
		const anon = await browser.newContext({ viewport: { width: 1400, height: 500 } })
		const p = await anon.newPage()
		const errs = []
		p.on('pageerror', (e) => errs.push(String(e)))
		await p.goto(shareUrl)
		await p.waitForSelector('[data-cy-files-list-row-name="report_1.txt"]', { timeout: 60000 })
		const loadedPub = await p.evaluate(() => [...document.scripts].some((s) => s.src.includes('files_middleclick/js/main.js')))
		ok('public share: script loaded == app enabled', loadedPub === expectApp, `loaded=${loadedPub}`)
		const [pp] = await Promise.all([
			anon.waitForEvent('page', { timeout: 10000 }).catch(() => null),
			p.locator('[data-cy-files-list-row-name="report_1.txt"] .files-list__row-name button').click({ button: 'middle' }),
		])
		ok('public share: middle click opens new tab', !!pp, pp ? pp.url() : 'no popup')
		ok('public share: no JS errors', errs.length === 0, errs.slice(0, 3).join(' | '))
		await anon.close()
	}

	await browser.close()
	const failed = results.filter((r) => !r.pass).length
	console.log(`\n${results.length - failed}/${results.length} passed`)
	process.exit(failed ? 1 : 0)
})().catch((e) => { console.error('CRASH', e); process.exit(2) })
