import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { errMsg, fetchMaster, fetchMasterSchedule, fetchSlots } from '../api'
import BookingCalendar from '../components/BookingCalendar'
import MasterPhoto from '../components/MasterPhoto'
import { salonDate } from '../salon'
import type { MasterDetailDto, MasterScheduleDayDto, SlotDto } from '../types'

function fmtTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('uk-UA', { hour: '2-digit', minute: '2-digit' })
}

/** Публічна сторінка майстра: графік роботи + бронювання послуги. */
export default function MasterSchedule() {
  const { id } = useParams()
  const mid = Number(id)
  const nav = useNavigate()

  const [master, setMaster] = useState<MasterDetailDto | null>(null)
  const [schedule, setSchedule] = useState<MasterScheduleDayDto[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState('')

  const [serviceOverride, setServiceOverride] = useState<number | null>(null)
  const [slots, setSlots] = useState<SlotDto[]>([])
  const [slotsErr, setSlotsErr] = useState('')
  const [slotsLoading, setSlotsLoading] = useState(false)
  const [date, setDate] = useState<string | null>(null)
  const [slot, setSlot] = useState<string | null>(null)
  // Після появи блоку «Підтвердження» (внизу сторінки) прокручуємо до нього,
  // щоб користувач не лишався у верхній точці після кліку по часу.
  const confirmRef = useRef<HTMLDivElement | null>(null)
  useEffect(() => {
    if (slot) confirmRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [slot])

  // Послуга — фільтр із дефолтом (§3.2): перша послуга майстра, поки користувач не обрав іншу.
  // Похідне значення замість окремого стану — не потребує setState у ефекті.
  const serviceId = (serviceOverride != null && master?.services.some(s => s.id === serviceOverride)
    ? serviceOverride
    : master?.services[0]?.id) ?? null

  useEffect(() => {
    if (Number.isNaN(mid)) { setLoading(false); return }
    let alive = true
    Promise.all([fetchMaster(mid), fetchMasterSchedule(mid)])
      .then(([m, s]) => { if (alive) { setMaster(m); setSchedule(s) } })
      .catch(e => { if (alive) setErr(errMsg(e)) })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [mid])

  useEffect(() => {
    if (!serviceId) { setSlots([]); setDate(null); setSlot(null); setSlotsErr(''); return }
    let alive = true
    setSlotsLoading(true)
    fetchSlots(mid, serviceId)
      .then(s => {
        if (!alive) return
        setSlots(s)
        // §3.6: зміна послуги скидає лише те, що стало недійсним для нового списку слотів.
        // Дата зберігається, якщо на неї є хоч один вільний слот; час — якщо рівно цей start ще вільний.
        // slotOk ⇒ dateOk (слот із тим самим start лежить на тій самій салонній даті), тому узгоджено.
        setDate(prev => (prev !== null && s.some(x => salonDate(x.start) === prev) ? prev : null))
        setSlot(prev => (prev !== null && s.some(x => x.start === prev) ? prev : null))
      })
      .catch(e => { if (alive) setSlotsErr(errMsg(e)) })
      .finally(() => { if (alive) setSlotsLoading(false) })
    return () => { alive = false }
  }, [mid, serviceId])

  const workingDates = useMemo(() => new Set(schedule.filter(d => d.isWorking).map(d => d.date)), [schedule])

  const byDate = useMemo(() => {
    const map = new Map<string, SlotDto[]>()
    slots.forEach(s => {
      const d = salonDate(s.start)
      if (!map.has(d)) map.set(d, [])
      map.get(d)!.push(s)
    })
    return map
  }, [slots])

  const bookingAvailable = useMemo(() => {
    const set = new Set<string>()
    schedule.forEach(d => { if (d.isWorking && byDate.has(d.date)) set.add(d.date) })
    return set
  }, [schedule, byDate])

  const daySlots = date ? byDate.get(date) ?? [] : []
  const selDay = schedule.find(d => d.date === date) ?? null

  if (Number.isNaN(mid)) return <p className="error">Майстра не знайдено</p>
  if (loading) return <p className="muted">Завантаження…</p>
  if (err) return <p className="error">{err}</p>
  if (!master) return <p className="error">Майстра не знайдено</p>

  return (
    <div>
      <header className="master-head">
        <MasterPhoto name={master.name} photoUrl={master.photoUrl} />
        <div>
          <h2>{master.name}</h2>
          {master.specialty && <p>{master.specialty}</p>}
          {master.bio && <p className="muted">{master.bio}</p>}
          <p className="muted">
            {master.rotationType === 'TwoTwo' ? 'Графік: 2 дні через 2' : 'Графік: тиждень через тиждень'}
          </p>
        </div>
      </header>
{/* Єдиний календар: графік роботи; клікабельні — робочі дні з вільним часом під обрану послугу */}
      <div className="step">
        <h3>Графік роботи (найближчі 4 тижні)</h3>
        <label className="svc-filter">
          Послуга:
          <select value={serviceId ?? ''} onChange={e => setServiceOverride(Number(e.target.value))}>
            {master.services.map(s => (
              <option key={s.id} value={s.id}>{s.name} · {s.price} ₴ · {s.durationMin} хв</option>
            ))}
          </select>
        </label>
        {slotsErr && <p className="error">{slotsErr}</p>}
        {slotsLoading ? <p className="muted">Завантаження…</p> : (
          <BookingCalendar
            mode="schedule"
            availableDates={bookingAvailable}
            workingDates={workingDates}
            labels={{ on: 'є вільний час', off: 'вихідний / немає вільного часу' }}
            selected={date}
            onSelect={d => { setDate(d); setSlot(null) }}
          />
        )}
        {selDay && selDay.isWorking && (
          <p className="day-hours">
            {selDay.date} · {selDay.start?.slice(0, 5)} – {selDay.end?.slice(0, 5)}
          </p>
        )}
      </div>

      {date && (
        <div className="step">
          <h3>Вільний час на {date}</h3>
          <div className="slots">
            {daySlots.map(s => (
              <button key={s.start} type="button" className={slot === s.start ? 'slot sel' : 'slot'}
                onClick={() => setSlot(s.start)}>
                {fmtTime(s.start)}
              </button>
            ))}
          </div>
        </div>
      )}

      {slot && serviceId && (
        <div className="step" ref={confirmRef}>
          <h3>Підтвердження</h3>
          <p>Запис до майстра {master.name}. Наступний крок — підтвердження кодом.</p>
          <button className="btn primary"
            onClick={() => nav('/booking', { state: { masterId: mid, serviceId, startTime: slot } })}>
            Обрати цей час і продовжити
          </button>
        </div>
      )}
    </div>
  )
}