# -*- coding: utf-8 -*-
# UI-тест Beauty Salon: головна → запис → код → кабінет → адмінка
import re, sys, json
from playwright.sync_api import sync_playwright

BASE = 'http://localhost:5173'
shots = r'C:\Users\dn240881sav\Desktop\Projects\TEST\Beauty\ui-tests'
import os
os.makedirs(shots, exist_ok=True)

def log(m): print(m, flush=True)

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    page = browser.new_page()
    errors = []
    page.on('console', lambda m: errors.append(m.text) if m.type == 'error' else None)

    # 1. Головна
    page.goto(BASE)
    page.wait_for_load_state('networkidle')
    assert page.locator('text=Стрижка чоловіча').count() > 0, 'services not rendered'
    assert page.locator('text=Олег').count() > 0, 'masters not rendered'
    page.screenshot(path=f'{shots}/01-home.png', full_page=True)
    log('HOME OK')

    # 2. Вхід за кодом
    page.goto(BASE + '/login')
    page.wait_for_load_state('networkidle')
    page.fill('input[placeholder="+380..."]', '+380991112233')
    page.click('text=Отримати код')
    page.wait_for_selector('text=DEV-режим', timeout=10000)
    dev_code = re.search(r'код — (\d{4})', page.locator('p.muted').first.inner_text()).group(1)
    log(f'CODE {dev_code}')
    page.fill('input[placeholder="0000"]', dev_code)
    page.click('text=Увійти')
    page.wait_for_url('**/cabinet')
    page.wait_for_load_state('networkidle')
    page.screenshot(path=f'{shots}/02-cabinet.png', full_page=True)
    assert 'Історія записів' in page.content(), 'cabinet missing'
    log('LOGIN+CABINET OK')

    # 3. Бронювання
    page.goto(BASE + '/booking')
    page.wait_for_load_state('networkidle')
    page.select_option('select >> nth=0', index=1)          # послуга
    page.wait_for_timeout(500)
    page.select_option('select >> nth=1', index=1)          # майстер
    page.wait_for_selector('.slot', timeout=10000)
    page.screenshot(path=f'{shots}/03-calendar.png', full_page=True)
    page.locator('.slot').first.click()
    page.click('text=Забронювати та отримати код')
    page.wait_for_selector('text=Введіть код із повідомлення', timeout=10000)
    dev_code2 = re.search(r'код — (\d{4})', page.locator('p.muted').first.inner_text()).group(1)
    page.fill('input[placeholder="0000"]', dev_code2)
    page.click('text=Підтвердити запис')
    page.wait_for_url('**/cabinet')
    page.wait_for_load_state('networkidle')
    page.screenshot(path=f'{shots}/04-booked.png', full_page=True)
    log('BOOKING OK')

    # 4. Адмінка
    page.goto(BASE + '/login')
    page.wait_for_load_state('networkidle')
    page.click('text=Вхід для адміністратора')
    page.fill('input[placeholder="+380..."]', '+380000000000')
    page.fill('input[type=password]', 'admin123')
    page.click('text=Увійти як адмін')
    page.wait_for_url('**/admin')
    page.wait_for_load_state('networkidle')
    page.screenshot(path=f'{shots}/05-admin-services.png', full_page=True)
    page.click('button.tab:has-text("Бронювання")')
    page.wait_for_selector('table', timeout=10000)
    page.screenshot(path=f'{shots}/06-admin-appts.png', full_page=True)
    log('ADMIN OK')

    real_errors = [e for e in errors if 'favicon' not in e]
    if real_errors:
        log('CONSOLE ERRORS: ' + json.dumps(real_errors[:5]))
    browser.close()
    log('ALL UI TESTS PASSED')
