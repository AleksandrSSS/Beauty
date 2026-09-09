import { useMemo } from 'react'
import { todaySalon } from '../salon'

const MONDAY_FIRST = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Нд']

/** Стан дня в режимі редактора (mode='edit'). */
export type DayState = 'on' | 'off' | 'override-on' | 'override-off' | 'override-partial'

const EDIT_CLS: Record<DayState, string> = {
  on: '',
  off: 'cal-day--off',
  'override-on': 'cal-day--ov-on',
  'override-off': 'cal-day--ov-off',
  'override-partial': 'cal-day--ov-partial'
}

const EDIT_TITLE: Record<DayState, string> = {
  on: 'Робочий день',
  off: 'Вихідний',
  'override-on': 'Робочий день (відхилення)',
  'override-off': 'Вихідний (відхилення)',
  'override-partial': 'Скорочений день (відхилення)'
}

interface BookingCalendarProps {
  /** Дати (YYYY-MM-DD, у таймзоні салону), на які можна клікати. */
  availableDates: Set<string>
  /** Обрана дата або null. У readOnly-режимі необов'язкове. */
  selected?: string | null
  onSelect?: (date: string) => void
  /** Тільки перегляд (прев'ю ротації): усі комірки disabled, вибір вимкнено. */
  readOnly?: boolean
  /** 'schedule' — робочі дні майстра; 'booking' — дати з вільними слотами; 'edit' — редактор (клікабельні всі дні, стан із dayStates). */
  mode: 'schedule' | 'booking' | 'edit'
  /** Стан кожного дня для mode='edit'. Без нього всі дні виглядають звичайними. */
  dayStates?: Map<string, DayState>
  /** Робочі дні майстра: дає підказку «робочий, але без вільного часу» замість «вихідний». Опційно. */
  workingDates?: Set<string>
  /** Свої підписи легенди. За замовчуванням — залежать від mode. */
  labels?: { on: string; off: string }
  /** Перший день вікна, YYYY-MM-DD. Дефолт — сьогодні в таймзоні салону. */
  from?: string
  /** Глибина вікна в днях. Дефолт 28 (ScheduleService.WindowDays). */
  windowDays?: number
}

// Календарні операції над рядками YYYY-MM-DD через Date.UTC — без впливу DST.
function toUtcDate(d: string): Date {
  const [y, m, dd] = d.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, dd))
}
function toIso(d: Date): string { return d.toISOString().slice(0, 10) }
function addDays(d: string, n: number): string {
  const t = toUtcDate(d)
  t.setUTCDate(t.getUTCDate() + n)
  return toIso(t)
}
function formatLongDate(iso: string): string {
  const [y, m, dd] = iso.split('-').map(Number)
  return new Intl.DateTimeFormat('uk-UA', { timeZone: 'Europe/Kyiv', weekday: 'long', day: 'numeric', month: 'long' })
    .format(new Date(Date.UTC(y, m - 1, dd)))
}
function formatMonthName(iso: string): string {
  const [y, m] = iso.split('-').map(Number)
  return new Intl.DateTimeFormat('uk-UA', { timeZone: 'Europe/Kyiv', month: 'long' }).format(new Date(Date.UTC(y, m - 1, 1)))
}

/** Спільний календар вибору дати для /booking і /masters/:id. Запитів не робить. */
export default function BookingCalendar({
  availableDates,
  selected = null,
  onSelect,
  mode,
  workingDates,
  labels,
  from,
  windowDays,
  readOnly = false,
  dayStates
}: BookingCalendarProps) {
  const start = from ?? todaySalon()
  const days = windowDays ?? 28

  const { cells, endStr, monthCaption } = useMemo(() => {
    const endStr = addDays(start, days)
    const dates: string[] = []
    for (let i = 0; i <= days; i++) dates.push(addDays(start, i))

    // Сітка Пн–Нд. Добиваємо порожніми комірками обидва боки до цілого тижня.
    const startDow = toUtcDate(start).getUTCDay() // 0 = Нд
    const leading = (startDow + 6) % 7
    const trailing = (7 - ((leading + dates.length) % 7)) % 7
    const cells: (string | null)[] = [
      ...Array.from({ length: leading }, () => null),
      ...dates,
      ...Array.from({ length: trailing }, () => null)
    ]

    // Підпис місяця: «вересень — жовтень 2026».
    const monthKeys = [...new Set(dates.map(d => d.slice(0, 7)))]
    const names = monthKeys.map(k => formatMonthName(k + '-01'))
    const years = [...new Set(dates.map(d => d.slice(0, 4)))]
    const monthCaption = `${names.join(' — ')} ${years.join(' / ')}`

    return { cells, endStr, monthCaption }
  }, [start, days])

  const today = todaySalon()
  const isEdit = mode === 'edit'
  const legend = labels ?? (mode === 'booking'
    ? { on: 'є вільний час', off: 'немає вільного часу' }
    : { on: 'робочий день', off: 'вихідний' })

  return (
    <div className="calendar">
      <div className="calendar__month">{monthCaption}</div>
      <div className="calendar__grid">
        {MONDAY_FIRST.map(d => <div key={d} className="calendar__dow">{d}</div>)}
        {cells.map((iso, i) => {
          if (iso === null) {
            return <button key={`filler-${i}`} type="button" className="cal-day cal-day--outside" disabled aria-hidden="true" tabIndex={-1} />
          }
          const inWindow = iso >= start && iso <= endStr
          const available = inWindow && availableDates.has(iso)
          // Режим редактора: стан комірки з dayStates, клікабельні всі дні у вікні.
          const editState = isEdit && inWindow ? dayStates?.get(iso) : undefined
          const cls = [
            'cal-day',
            inWindow ? '' : 'cal-day--outside',
            isEdit ? (editState ? EDIT_CLS[editState] : '') : (available ? 'cal-day--on' : ''),
            !readOnly && selected === iso ? 'cal-day--sel' : '',
            iso === today ? 'cal-day--today' : ''
          ].filter(Boolean).join(' ')
          // Три стани комірки: вільний (клікабельний), робочий-без-вільного-часу, вихідний.
          const title = !inWindow
            ? undefined
            : isEdit
              ? (editState ? EDIT_TITLE[editState] : 'Робочий день')
              : available
                ? undefined
                : workingDates
                  ? (workingDates.has(iso) ? 'Немає вільного часу' : 'Вихідний')
                  : (mode === 'schedule' ? 'Вихідний' : 'Немає вільного часу')
          return (
            <button
              key={iso}
              type="button"
              className={cls}
              disabled={isEdit ? !inWindow : (!available || readOnly)}
              title={title}
              aria-label={formatLongDate(iso)}
              aria-pressed={!readOnly && selected === iso}
              aria-current={iso === today ? 'date' : undefined}
              onClick={() => onSelect?.(iso)}
            >
              {Number(iso.slice(8, 10))}
            </button>
          )
        })}
      </div>
      <div className="calendar__legend">
        <span>◻ {legend.on}</span>
        <span>✕ {legend.off}</span>
        <span>сьогодні</span>
      </div>
    </div>
  )
}