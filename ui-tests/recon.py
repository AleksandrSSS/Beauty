# -*- coding: utf-8 -*-
from playwright.sync_api import sync_playwright
with sync_playwright() as p:
    b = p.chromium.launch(headless=True)
    page = b.new_page()
    page.on('console', lambda m: print('CONSOLE:', m.type, m.text))
    page.on('requestfailed', lambda r: print('REQFAIL:', r.url))
    page.goto('http://localhost:5173')
    page.wait_for_load_state('networkidle')
    page.wait_for_timeout(2000)
    txt = page.locator('.grid').first.inner_text() if page.locator('.grid').count() else 'NO GRID'
    print('GRID:', txt[:200])
    b.close()
