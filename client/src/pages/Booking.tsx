import { useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { CHANNELS, confirmBooking, createBooking, errMsg, fetchMasters, fetchServices, fetchSlots } from '../api'
import { useAuth } from '../auth'
import BookingCalendar from '../components/BookingCalendar'
import { normalizePhone, salonDate } from '../salon'
import type { BookingCreateResponse, MasterDto, ServiceDto, SlotDto } from '../types'

interface Prefill { masterId?: number; serviceId?: number; startTime?: string }

function fmtTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('uk-UA', { hour: '2-digit', minute: '2-digit' })
}

export default function Booking() {
  const nav = useNavigate()
  const location = useLocation()
  const prefill = (location.state as Prefill | null) ?? null
  const pendingStart = useRef<string | null>(null)
  // Після появи блоку «Підтвердження» (внизу сторінки — зокрема прихід зі сторінки
  // майстра) прокручуємо до нього, щоб не лишатись у верхній точці.
  const confirmRef = useRef<HTMLDivElement | null>(null)
  const { user, login } = useAuth()

  const [services, setServices] = useState<ServiceDto[]>([])
  const [masters, setMasters] = useState<MasterDto[]>([])
  const [serviceId, setServiceId] = useState<number | null>(null)
  const [mode, setMode] = useState<'any' | 'master' | null>(null)
  const [masterId, setMasterId] = useState<number | null>(null)
  const [slots, setSlots] = useState<SlotDto[]>([])
  const [date, setDate] = useState<string | null>(null)
  const [slot, setSlot] = useState<string | null>(null)
  const [slotMasterId, setSlotMasterId] = useState<number | null>(null)
  // Гостьове бронювання (один код): телефон + канал + контакт + опційне ім'я.
  const [guestPhone, setGuestPhone] = useState('+380')
  const [guestChannel, setGuestChannel] = useState('Sms')
  const [guestContact, setGuestContact] = useState('')
  const [guestName, setGuestName] = useState('')
  const [pending, setPending] = useState<BookingCreateResponse | null>(null)
  const [code, setCode] = useState('')
  const [err, setErr] = useState('')
  const [msg, setMsg] = useState('')
  const [prefillCleared, setPrefillCleared] = useState(false)
  // §3.7: прийшли зі сторінки майстра — спосіб запису вже визначено, крок «Як записатись» ховаємо.
  const [fromMaster, setFromMaster] = useState(false)

  useEffect(() => {
    let alive = true
    Promise.all([fetchServices(), fetchMasters()])
      .then(([s, ms]) => { if (alive) { setServices(s); setMasters(ms) } })
      .catch(e => { if (alive) setErr(errMsg(e)) })
    return () => { alive = false }
  }, [])

  // Префіл зі сторінки майстра: встановити послугу й майстра, запам'ятати бажаний час.
  useEffect(() => {
    if (prefillCleared || !prefill?.masterId || !prefill.serviceId) return
    setServiceId(prefill.serviceId)
    setMode('master')
    setMasterId(prefill.masterId)
    pendingStart.current = prefill.startTime ?? null
    setPrefillCleared(true)
    setFromMaster(true)
  }, [prefill, prefillCleared])

  useEffect(() => {
    if (!serviceId || (mode !== 'any' && masterId == null)) {
      setSlots([]); setDate(null); setSlot(null); setSlotMasterId(null)
      return
    }
    let alive = true
    fetchSlots(mode === 'any' ? null : masterId, serviceId).then(res => {
      if (!alive) return
      setSlots(res); setDate(null); setSlot(null); setSlotMasterId(null)
      const wanted = pendingStart.current
      if (wanted) {
        if (res.some(s => s.start === wanted)) { setDate(salonDate(wanted)); setSlot(wanted) }
        else setErr('Обраний час уже зайнятий, оберіть інший')
      }
      pendingStart.current = null
    }).catch(e => { if (alive) setErr(errMsg(e)) })
    return () => { alive = false }
  }, [serviceId, mode, masterId])

  useEffect(() => {
    if (slot) confirmRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [slot])

  const byDate = useMemo(() => {
    const map = new Map<string, SlotDto[]>()
    slots.forEach(s => {
      const d = salonDate(s.start)
      if (!map.has(d)) map.set(d, [])
      map.get(d)!.push(s)
    })
    return map
  }, [slots])

  const selectedSlot = slots.find(s => s.start === slot) || null
  const daySlots = date ? byDate.get(date) ?? [] : []
  const bookingMasterId = mode === 'master' ? masterId : slotMasterId
  const selectedService = services.find(s => s.id === serviceId)
  const eligibleMasters = serviceId ? masters.filter(m => m.services.includes(serviceId)) : masters
  const slotMasters = mode === 'any' && selectedSlot?.masters?.length ? selectedSlot.masters : []
  const masterName = masterId ? masters.find(m => m.id === masterId)?.name : null

  // §3.7: наскрізна нумерація без пропусків. Коли крок «Як записатись» сховано
  // (прихід зі сторінки майстра), наступні кроки зсуваються, щоб не було стрибка 1 → 3.
  const showHowToBook = !fromMaster
  const nHowTo = 2
  const nMaster = nHowTo + (showHowToBook ? 1 : 0)
  const nDate = nMaster + (mode === 'master' ? 1 : 0)
  const nTime = nDate + (slots.length > 0 ? 1 : 0)
  const nWhoFree = nTime + (date ? 1 : 0)
  const nConfirm = nWhoFree + (mode === 'any' && slot && slotMasters.length > 0 ? 1 : 0)

  const resetChoice = () => {
    setMasterId(null); setMode(null); setFromMaster(false)
    setDate(null); setSlot(null); setSlotMasterId(null)
  }

  const refreshSlots = async () => {
    if (!serviceId || (mode !== 'any' && masterId == null)) return
    const res = await fetchSlots(mode === 'any' ? null : masterId, serviceId)
    setSlots(res); setSlot(null); setSlotMasterId(null)
  }

  // Створення запису. Залогінений шле лише masterId/serviceId/startTime
  // (телефон і канал сервер бере з профілю, запис одразу Confirmed);
  // гість додає phone/name/channel/contact і проходить крок коду.
  const doCreate = async () => {
    setErr(''); setMsg('')
    try {
      const r = user
        ? await createBooking({ masterId: bookingMasterId, serviceId, startTime: slot })
        : await createBooking({
            masterId: bookingMasterId, serviceId, startTime: slot,
            channel: guestChannel, contact: guestContact || null,
            phone: guestPhone, name: guestName || null
          })
      if (r.confirmed) {
        nav('/cabinet')
        return
      }
      setPending(r)
      setMsg(r.devCode ? `DEV-режим: код — ${r.devCode}` : r.info || 'Код надіслано')
    } catch (e) {
      setErr(errMsg(e))
      // Конфлікт зайнятості (409): перезапитати слоти, дату зберегти, слот скинути.
      refreshSlots().catch(() => {})
    }
  }

  // Кнопка «Забронювати» для залогіненого користувача (без коду).
  const create = async () => {
    if (!user) return
    await doCreate()
  }

  // Гостьовий шлях: підтвердження запису кодом + автологін у кабінет.
  const confirm = async () => {
    setErr('')
    try {
      if (!pending) return
      const r = await confirmBooking({ appointmentId: pending.appointmentId, code, phone: guestPhone })
      login(r.token, r.user)
      nav('/cabinet')
    } catch (e) { setErr(errMsg(e)) }
  }

  return (
    <div className="booking">
      <h2>Запис на послугу</h2>

      {mode === 'master' && masterName && (
        <div className="step">
          <p>Запис до майстра <b>{masterName}</b>{' '}
            <button type="button" className="link" onClick={resetChoice}>Змінити вибір</button>
          </p>
        </div>
      )}

      <div className="step">
        <h3>1. Послуга</h3>
        <select value={serviceId ?? ''}
          onChange={e => { setServiceId(e.target.value === '' ? null : Number(e.target.value)); setMode(null); setMasterId(null); setDate(null); setSlot(null); setSlotMasterId(null) }}>
          <option value="">— оберіть —</option>
          {services.map(s => <option key={s.id} value={s.id}>{s.name} ({s.price} ₴, {s.durationMin} хв)</option>)}
        </select>
      </div>

      {serviceId && (
        <>
          {showHowToBook && (
            <div className="step">
              <h3>{nHowTo}. Як записатись</h3>
              <div className="slots">
                <button type="button" className={mode === 'any' ? 'slot sel' : 'slot'}
                  onClick={() => { setMode('any'); setMasterId(null); setDate(null); setSlot(null); setSlotMasterId(null) }}>
                  Будь-який майстер
                </button>
                <button type="button" className={mode === 'master' ? 'slot sel' : 'slot'}
                  onClick={() => { setMode('master'); setDate(null); setSlot(null); setSlotMasterId(null) }}>
                  Обрати майстра
                </button>
              </div>
            </div>
          )}

          {mode === 'master' && (
            <div className="step">
              <h3>{nMaster}. Майстер</h3>
              <select value={masterId ?? ''}
                onChange={e => { setMasterId(e.target.value === '' ? null : Number(e.target.value)); setDate(null); setSlot(null); setSlotMasterId(null) }}>
                <option value="">— оберіть —</option>
                {eligibleMasters.map(m => (
                  <option key={m.id} value={m.id}>
                    {m.name} — {m.specialty}{' '}
                    {m.rotationType === 'TwoTwo'
                      ? (m.worksToday ? '(працює сьогодні)' : m.worksTomorrow ? '(працює завтра)' : '(на зміні інший майстер)')
                      : (m.worksThisWeek ? '(працює цього тижня)' : '(відпочиває цього тижня)')}
                  </option>
                ))}
              </select>
            </div>
          )}

          {slots.length > 0 && (
            <div className="step">
              <h3>{nDate}. Дата</h3>
              <p className="muted">Оберіть дату — покажеться вільний час.</p>
              <BookingCalendar mode="booking" availableDates={new Set(byDate.keys())} selected={date} onSelect={setDate} />
            </div>
          )}

          {date && (
            <div className="step">
              <h3>{nTime}. Час</h3>
              <p className="muted">Вільний час на {date}</p>
              <div className="slots">
                {daySlots.map(s => (
                  <button key={s.start} className={slot === s.start ? 'slot sel' : 'slot'}
                    onClick={() => { setSlot(s.start); setSlotMasterId(s.masters?.[0]?.id ?? null) }}
                    title={s.masters && s.masters.length ? `Вільні: ${s.masters.map(m => m.name).join(', ')}` : undefined}>
                    {fmtTime(s.start)}
                    {s.masters && s.masters.length > 1 && <span className="slot-count"> ×{s.masters.length}</span>}
                  </button>
                ))}
              </div>
            </div>
          )}

          {mode === 'any' && slot && slotMasters.length > 0 && (
            <div className="step">
              <h3>{nWhoFree}. Хто вільний у цей час</h3>
              <p className="muted">{new Date(slot).toLocaleString('uk-UA')} — доступні майстри:</p>
              <div className="slots">
                {slotMasters.map(m => (
                  <button key={m.id} className={slotMasterId === m.id ? 'slot sel' : 'slot'}
                    onClick={() => setSlotMasterId(m.id)}>{m.name}</button>
                ))}
              </div>
            </div>
          )}

          {slot && bookingMasterId != null && (
            <div className="step" ref={confirmRef}>
              <h3>{nConfirm}. Підтвердження</h3>

              {/* Залогінений: без коду, без вибору каналу — все з профілю. */}
              {!pending && user && (
                <>
                  <p>{selectedService?.name} · {new Date(slot).toLocaleString('uk-UA')}
                    {' · майстер: '}{masters.find(m => m.id === bookingMasterId)?.name}</p>
                  <button className="btn primary" onClick={create}>Забронювати</button>
                </>
              )}

              {/* Гість: телефон + канал + один код підтвердження запису. */}
              {!pending && !user && (
                <div className="login-inline">
                  <p className="muted">Вкажіть телефон — надішлемо код підтвердження запису. Після підтвердження ви автоматично увійдете в кабінет.</p>
                  <label>Телефон</label>
                  <input value={guestPhone} onChange={e => setGuestPhone(normalizePhone(e.target.value))} placeholder="+380..." />
                  <label>Куди надіслати код</label>
                  <select value={guestChannel} onChange={e => setGuestChannel(e.target.value)}>
                    {CHANNELS.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
                  </select>
                  <input value={guestContact} onChange={e => setGuestContact(e.target.value)}
                    placeholder={guestChannel === 'Email' ? 'email@example.com' : 'контакт для повідомлень'} />
                  <label>Імʼя (необовʼязково)</label>
                  <input value={guestName} onChange={e => setGuestName(e.target.value)} placeholder="Як до вас звертатись" />
                  <button className="btn primary" onClick={doCreate}>Отримати код</button>
                </div>
              )}

              {/* Крок коду — лише для гостя (залогінений отримав confirmed=true і вже в кабінеті). */}
              {pending && !user && (
                <>
                  <p className="muted">{msg}</p>
                  <label>Код підтвердження запису</label>
                  <input value={code} onChange={e => setCode(e.target.value)} placeholder="0000" />
                  <button className="btn primary" onClick={confirm}>Підтвердити запис</button>
                </>
              )}
            </div>
          )}
        </>
      )}
      {err && <p className="error">{err}</p>}
    </div>
  )
}
