// Робота з "настінною" датою салону на клієнті.
// Салонна дата живе у таймзоні Europe/Kyiv. Заборонено toISOString().slice(0,10) —
// він дає UTC-дату і на межі доби (23:30 за Києвом) помиляється на день.
const SALON_TZ = 'Europe/Kyiv'

/** Салонна дата (YYYY-MM-DD) з ISO-мітки UTC або з Date. */
export function salonDate(iso: string | Date): string {
  return new Intl.DateTimeFormat('sv-SE', { timeZone: SALON_TZ }).format(typeof iso === 'string' ? new Date(iso) : iso)
}

/** Сьогодні у таймзоні салону (YYYY-MM-DD). */
export function todaySalon(): string {
  return new Intl.DateTimeFormat('sv-SE', { timeZone: SALON_TZ }).format(new Date())
}

/**
 * Нормалізація українського телефону для полів вводу (§3.8).
 * Завжди повертає рядок у форматі `+380XXXXXXXXX` (до 9 цифр після префікса).
 * - Провідний `0` (формат `0XX…`) відкидається — лишається `+380XX…`.
 * - Префікс `380` у введенні не дублюється.
 * - Якщо поле повністю стерто — повертається `+380` як «підлога».
 */
export function normalizePhone(raw: string): string {
  let digits = raw.replace(/\D/g, '')
  if (digits.startsWith('380')) digits = digits.slice(3)
  else if (digits.startsWith('0')) digits = digits.slice(1)
  digits = digits.slice(0, 9)
  return '+380' + digits
}