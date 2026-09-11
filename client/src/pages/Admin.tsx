import { useEffect, useMemo, useState } from 'react'
import { api, deleteAppointment, deleteMasterDayOverride, deleteMasterPhoto, errMsg, fetchAdminAppointments, fetchMasterDayOverrides, fetchMasterSchedule, fetchSalonHours, previewRotation, putMasterDayOverride, resolvePhotoUrl, updateSalonHours, uploadMasterPhoto } from '../api'
import { useAuth } from '../auth'
import BookingCalendar, { type DayState } from '../components/BookingCalendar'
import { todaySalon } from '../salon'
import type { AppointmentDto, DayOverride, MasterDto, MasterForm, MasterScheduleDayDto, ServiceDto, ServiceForm } from '../types'

const STATUSES = ['PendingVerification', 'Confirmed', 'Cancelled', 'Completed']

interface DeactCandidate { id: number; name: string }
interface DeactAppointment {
  id: number
  startTime: string
  service: string
  client: string
  phone: string
  status: string
  candidates: DeactCandidate[]
}
interface DeactImpact {
  masterId: number
  masterName: string
  futureAppointments: DeactAppointment[]
}

function emptyMaster(): MasterForm {
  // Дефолт якоря — сьогодні (перший робочий день, будь-який день тижня), салонна дата, не UTC.
  return { id: 0, name: '', specialty: '', bio: '', isActive: true,
    rotationType: 'Weekly',
    rotationAnchor: todaySalon(),
    services: [] as number[],
    photoUrl: '' }
}

function emptyService(): ServiceForm {
  return { id: 0, name: '', description: '', category: 'hair', durationMin: 60, price: 300, isActive: true }
}

export default function Admin() {
  const { user } = useAuth()
  const [tab, setTab] = useState('services')
  const [services, setServices] = useState<ServiceDto[]>([])
  const [masters, setMasters] = useState<MasterDto[]>([])
  const [appts, setAppts] = useState<AppointmentDto[]>([])
  // Пагінація записів (S2-3).
  const [apptPage, setApptPage] = useState(1)
  const [apptTotalPages, setApptTotalPages] = useState(1)
  const [svcForm, setSvcForm] = useState<ServiceForm>(emptyService())
  const [mstForm, setMstForm] = useState<MasterForm>(emptyMaster())
  const [err, setErr] = useState('')
  const [rotPreview, setRotPreview] = useState<{ date: string; isWorking: boolean }[]>([])
  const [deact, setDeact] = useState<DeactImpact | null>(null)
  const [deactAssign, setDeactAssign] = useState<Record<number, number>>({})
  // Графік закладу (єдиний на всі дні), вкладка «Графік закладу».
  const [salonOpen, setSalonOpen] = useState('09:00')
  const [salonClose, setSalonClose] = useState('18:00')
  const [hoursMsg, setHoursMsg] = useState('')
  // Редактор відхилень графіка збереженого майстра (master-day-override-plan).
  const [schedDays, setSchedDays] = useState<MasterScheduleDayDto[]>([])
  const [dayOverrides, setDayOverrides] = useState<DayOverride[]>([])
  const [ovDay, setOvDay] = useState<string | null>(null)
  const [ovKind, setOvKind] = useState<'off' | 'full' | 'partial'>('full')
  const [ovStart, setOvStart] = useState('09:00')
  const [ovEnd, setOvEnd] = useState('18:00')
  const [ovMsg, setOvMsg] = useState('')

  const load = () => {
    api<ServiceDto[]>('/api/admin/services').then(setServices)
    api<MasterDto[]>('/api/admin/masters').then(setMasters)
    fetchAdminAppointments(apptPage).then(r => {
      setAppts(r.items)
      setApptTotalPages(r.totalPages || 1)
    })
  }
  useEffect(() => { if (user?.role === 'Admin') load() }, [user])
  // Перезавантаження записів при зміні сторінки.
  useEffect(() => {
    if (user?.role !== 'Admin') return
    fetchAdminAppointments(apptPage).then(r => {
      setAppts(r.items)
      setApptTotalPages(r.totalPages || 1)
    }).catch(() => {})
  }, [apptPage])
  useEffect(() => {
    if (user?.role !== 'Admin' || !mstForm.rotationAnchor) return
    let alive = true
    previewRotation({ rotationType: mstForm.rotationType, rotationAnchor: mstForm.rotationAnchor })
      .then(d => { if (alive) setRotPreview(d) })
      .catch(() => { if (alive) setRotPreview([]) })
    return () => { alive = false }
  }, [user, mstForm.rotationType, mstForm.rotationAnchor])
  useEffect(() => {
    if (user?.role !== 'Admin' || tab !== 'hours') return
    let alive = true
    fetchSalonHours()
      .then(h => { if (alive) { setSalonOpen(h.openTime.slice(0, 5)); setSalonClose(h.closeTime.slice(0, 5)) } })
      .catch(e => { if (alive) setHoursMsg(errMsg(e)) })
    return () => { alive = false }
  }, [user, tab])

  // Дані редактора відхилень: графік майстра на вікно + його override. Тільки для збереженого майстра.
  useEffect(() => {
    if (user?.role !== 'Admin' || mstForm.id <= 0) {
      setSchedDays([]); setDayOverrides([]); setOvDay(null)
      return
    }
    let alive = true
    Promise.all([fetchMasterSchedule(mstForm.id), fetchMasterDayOverrides(mstForm.id)])
      .then(([sched, ovs]) => { if (alive) { setSchedDays(sched); setDayOverrides(ovs) } })
      .catch(() => { if (alive) { setSchedDays([]); setDayOverrides([]) } })
    return () => { alive = false }
  }, [user, mstForm.id])

  // Стан кожної дати вікна: override поверх (ротація + заклад).
  const editStates = useMemo(() => {
    const ovByDate = new Map(dayOverrides.map(o => [o.date, o]))
    const map = new Map<string, DayState>()
    schedDays.forEach(d => {
      const ov = ovByDate.get(d.date)
      if (ov) map.set(d.date, !ov.isWorking ? 'override-off' : (ov.start && ov.end ? 'override-partial' : 'override-on'))
      else map.set(d.date, d.isWorking ? 'on' : 'off')
    })
    return map
  }, [schedDays, dayOverrides])
  const editAvailable = useMemo(() => new Set(schedDays.filter(d => d.isWorking).map(d => d.date)), [schedDays])
  const ovOfDay = ovDay ? dayOverrides.find(o => o.date === ovDay) : undefined

  // Клік по дню в редакторі: обрати дату й підставити поточний стан у міні-панель.
  const pickOvDay = (d: string) => {
    setOvDay(d); setOvMsg('')
    const ov = dayOverrides.find(o => o.date === d)
    if (!ov) { setOvKind('full'); return }
    if (!ov.isWorking) setOvKind('off')
    else if (ov.start && ov.end) { setOvKind('partial'); setOvStart(ov.start.slice(0, 5)); setOvEnd(ov.end.slice(0, 5)) }
    else setOvKind('full')
  }

  const reloadEditor = async () => {
    if (mstForm.id <= 0) return
    const [sched, ovs] = await Promise.all([fetchMasterSchedule(mstForm.id), fetchMasterDayOverrides(mstForm.id)])
    setSchedDays(sched); setDayOverrides(ovs)
  }

  const saveOverride = async () => {
    if (!ovDay || mstForm.id <= 0) return
    setErr(''); setOvMsg('')
    try {
      if (ovKind === 'off') await putMasterDayOverride(mstForm.id, { date: ovDay, isWorking: false })
      else if (ovKind === 'full') await putMasterDayOverride(mstForm.id, { date: ovDay, isWorking: true })
      else await putMasterDayOverride(mstForm.id, {
        date: ovDay, isWorking: true,
        start: ovStart.length === 5 ? ovStart + ':00' : ovStart,
        end: ovEnd.length === 5 ? ovEnd + ':00' : ovEnd
      })
      await reloadEditor()
      setOvMsg('Збережено')
    } catch (e) { setErr(errMsg(e)) }
  }

  const resetOverride = async () => {
    if (!ovDay || mstForm.id <= 0) return
    setErr(''); setOvMsg('')
    try {
      await deleteMasterDayOverride(mstForm.id, ovDay)
      await reloadEditor()
      setOvMsg('Повернено до стандарту')
    } catch (e) { setErr(errMsg(e)) }
  }

  const saveSalonHours = async () => {
    setErr(''); setHoursMsg('')
    try {
      const h = await updateSalonHours({
        openTime: salonOpen.length === 5 ? salonOpen + ':00' : salonOpen,
        closeTime: salonClose.length === 5 ? salonClose + ':00' : salonClose
      })
      setSalonOpen(h.openTime.slice(0, 5)); setSalonClose(h.closeTime.slice(0, 5))
      setHoursMsg('Збережено')
    } catch (e) { setErr(errMsg(e)) }
  }

  if (user?.role !== 'Admin') return <p className="muted">Доступ лише для адміністратора.</p>

  const saveService = async () => {
    setErr('')
    try {
      if (svcForm.id) await api(`/api/admin/services/${svcForm.id}`, { method: 'PUT', body: svcForm })
      else await api('/api/admin/services', { method: 'POST', body: svcForm })
      setSvcForm(emptyService())
      load()
    } catch (e) { setErr(errMsg(e)) }
  }

  const saveMaster = async () => {
    setErr('')
    // Години робочого дня більше не per-master — лише ротація + послуги.
    const body = { ...mstForm, serviceIds: mstForm.services }
    try {
      if (mstForm.id) await api(`/api/admin/masters/${mstForm.id}`, { method: 'PUT', body })
      else await api('/api/admin/masters', { method: 'POST', body })
      setMstForm(emptyMaster()); load()
    } catch (e) { setErr(errMsg(e)) }
  }

  const onPickPhoto = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    setErr('')
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) { setErr('Дозволені лише файли JPG, PNG або WebP'); e.target.value = ''; return }
    if (file.size > 3 * 1024 * 1024) { setErr('Розмір файлу перевищує 3 МБ'); e.target.value = ''; return }
    try {
      const r = await uploadMasterPhoto(mstForm.id, file)
      setMstForm({ ...mstForm, photoUrl: r.photoUrl })
      load()
    } catch (ex) { setErr(errMsg(ex)) }
    finally { e.target.value = '' }
  }

  const onRemovePhoto = async () => {
    if (!confirm('Прибрати фото майстра?')) return
    try {
      await deleteMasterPhoto(mstForm.id)
      setMstForm({ ...mstForm, photoUrl: '' })
      load()
    } catch (ex) { setErr(errMsg(ex)) }
  }

  const closeDeact = () => { setDeact(null); setDeactAssign({}) }

  const deactivate = async (id: number, name: string) => {
    setErr('')
    try {
      const impact = await api<DeactImpact>(`/api/admin/masters/${id}/deactivation-impact`)
      if (!impact.futureAppointments.length) {
        if (!confirm(`Видалити майстра ${name}? У нього немає майбутніх записів.`)) return
        await api(`/api/admin/masters/${id}`, { method: 'DELETE' })
      } else {
        const init: Record<number, number> = {}
        impact.futureAppointments.forEach(a => { init[a.id] = a.candidates[0]?.id ?? 0 })
        setDeact(impact)
        setDeactAssign(init)
      }
      load()
    } catch (e) { setErr(errMsg(e)) }
  }

  const confirmDeactivate = async (policy: 'CancelAll' | 'Reassign') => {
    if (!deact) return
    setErr('')
    const body = policy === 'Reassign'
      ? { policy, reassignments: deact.futureAppointments.map(a => ({ appointmentId: a.id, masterId: deactAssign[a.id] ?? 0 })) }
      : { policy }
    try {
      await api(`/api/admin/masters/${deact.masterId}/deactivate`, { method: 'POST', body })
      closeDeact()
      load()
    } catch (e) { setErr(errMsg(e)) }
  }

  const reschedule = async (id: number, startTime: string) => {
    await api(`/api/admin/appointments/${id}/reschedule`, { method: 'PUT', body: { startTime } })
    load()
  }

  const setStatus = async (id: number, status: string) => {
    await api(`/api/admin/appointments/${id}/status`, { method: 'PUT', body: { status } })
    load()
  }

  // Hard-delete запису: зникає в адмінці, у кабінеті клієнта і звільняє слот (один рядок БД).
  const removeAppointment = async (id: number) => {
    if (!confirm('Видалити запис? Дію не можна скасувати. Запис зникне і в кабінеті клієнта.')) return
    setErr('')
    try {
      await deleteAppointment(id)
      load()
    } catch (e) { setErr(errMsg(e)) }
  }

  const masterRow = (m: MasterDto) => (
    <tr key={m.id}>
      <td>{m.name}</td><td>{m.specialty}</td>
      <td>{m.rotationType === 'TwoTwo' ? '2/2' : 'тижні чергування'}</td>
      <td>{m.photoUrl ? '✓' : '—'}</td>
      <td>
        <button className="link" onClick={() => setMstForm({
          ...emptyMaster(), id: m.id, name: m.name, specialty: m.specialty ?? '', bio: m.bio ?? '', isActive: m.isActive ?? true, photoUrl: m.photoUrl ?? '',
          rotationType: m.rotationType ?? 'Weekly', rotationAnchor: m.rotationAnchor ?? todaySalon(), services: m.services
        })}>Ред.</button>
        {' '}
        <button className="link" onClick={() => deactivate(m.id, m.name)}>Звільнити</button>
      </td>
    </tr>
  )

  const showDeactAssign = (id: number, val: number) => setDeactAssign({ ...deactAssign, [id]: val })

  return (
    <div>
      <h2>Адмін-панель</h2>
      <div className="tabs">
        {(['services', 'masters', 'appointments', 'hours'] as const).map(t => (
          <button key={t} className={tab === t ? 'tab active' : 'tab'} onClick={() => setTab(t)}>
            {{ services: 'Послуги', masters: 'Майстри', appointments: 'Бронювання', hours: 'Графік закладу' }[t]}
          </button>
        ))}
      </div>
      {err && <p className="error">{err}</p>}

      {tab === 'services' && (
        <div>
          <div className="card">
            <h3>{svcForm.id ? `Редагувати послугу #${svcForm.id}` : 'Нова послуга'}</h3>
            <input placeholder="Назва" value={svcForm.name} onChange={e => setSvcForm({ ...svcForm, name: e.target.value })} />
            <input placeholder="Опис" value={svcForm.description} onChange={e => setSvcForm({ ...svcForm, description: e.target.value })} />
            <select value={svcForm.category} onChange={e => setSvcForm({ ...svcForm, category: e.target.value as ServiceForm['category'] })}>
              <option value="hair">Перукарня</option>
              <option value="manicure">Манікюр</option>
            </select>
            <input type="number" placeholder="Тривалість, хв" value={svcForm.durationMin} onChange={e => setSvcForm({ ...svcForm, durationMin: +e.target.value })} />
            <input type="number" placeholder="Ціна" value={svcForm.price} onChange={e => setSvcForm({ ...svcForm, price: +e.target.value })} />
            <button className="btn primary" onClick={saveService}>{svcForm.id ? 'Зберегти' : 'Додати'}</button>
            {svcForm.id > 0 && <button className="link" onClick={() => setSvcForm({ id: 0, name: '', description: '', category: 'hair', durationMin: 60, price: 300, isActive: true })}>Скасувати</button>}
          </div>
          <table className="table">
            <thead><tr><th>Назва</th><th>Категорія</th><th>Хв</th><th>Ціна</th><th>Активна</th><th></th></tr></thead>
            <tbody>
              {services.map(s => (
                <tr key={s.id}>
                  <td>{s.name}</td><td>{s.category}</td><td>{s.durationMin}</td><td>{s.price} ₴</td><td>{s.isActive ? '✓' : '—'}</td>
                  <td><button className="link" onClick={() => setSvcForm({ ...s, description: s.description || '' })}>Ред.</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {tab === 'masters' && (
        <div>
          <div className="card">
            <h3>{mstForm.id ? `Редагувати майстра #${mstForm.id}` : 'Новий майстер'}</h3>
            <input placeholder="Імʼя" value={mstForm.name} onChange={e => setMstForm({ ...mstForm, name: e.target.value })} />
            <input placeholder="Спеціальність" value={mstForm.specialty} onChange={e => setMstForm({ ...mstForm, specialty: e.target.value })} />
            <input placeholder="Про майстра" value={mstForm.bio} onChange={e => setMstForm({ ...mstForm, bio: e.target.value })} />
            <label>
              <input type="checkbox" checked={mstForm.isActive} onChange={e => setMstForm({ ...mstForm, isActive: e.target.checked })} />
              {' '}Активний (працює в салоні)
            </label>
            {mstForm.id > 0 ? (
              <div className="photo-edit">
                <label>Фото майстра</label>
                {mstForm.photoUrl
                  ? <img className="photo-preview" src={resolvePhotoUrl(mstForm.photoUrl)!} alt="Поточне фото" />
                  : <p className="muted">Фото не завантажено</p>}
                <input type="file" accept="image/jpeg,image/png,image/webp" onChange={onPickPhoto} />
                {mstForm.photoUrl && <button className="link" onClick={onRemovePhoto}>Прибрати фото</button>}
              </div>
            ) : (
              <p className="muted">Фото можна завантажити після збереження майстра.</p>
            )}
            <label>Графік роботи:</label>
            <select value={mstForm.rotationType} onChange={e => setMstForm({ ...mstForm, rotationType: e.target.value as MasterForm['rotationType'] })}>
              <option value="Weekly">Тижні через тиждень</option>
              <option value="TwoTwo">2/2 (2 дні зміна, 2 вихідні)</option>
            </select>
            <label>Дата початку роботи: {mstForm.rotationType === 'TwoTwo' ? '(перший робочий день зміни 2/2)' : '(перший робочий день)'}</label>
            <input type="date" value={mstForm.rotationAnchor} onChange={e => setMstForm({ ...mstForm, rotationAnchor: e.target.value })} />
            {/* Збережений майстер — клікабельний редактор відхилень; новий — прев'ю ротації. */}
            {mstForm.id > 0 ? (
              schedDays.length > 0 && (
                <>
                  <label>Графік майстра (редактор відхилень, 4 тижні):</label>
                  <BookingCalendar
                    mode="edit"
                    availableDates={editAvailable}
                    dayStates={editStates}
                    selected={ovDay}
                    onSelect={pickOvDay}
                    labels={{ on: 'робочий день', off: 'вихідний' }}
                  />
                  {ovDay && (
                    <div className="ov-panel">
                      <p className="muted">Дата: {ovDay}{ovOfDay ? ' (є відхилення)' : ''}</p>
                      <div className="row">
                        <button type="button" className={ovKind === 'off' ? 'slot sel' : 'slot'} onClick={() => setOvKind('off')}>Вихідний</button>
                        <button type="button" className={ovKind === 'full' ? 'slot sel' : 'slot'} onClick={() => setOvKind('full')}>Робочий повний день</button>
                        <button type="button" className={ovKind === 'partial' ? 'slot sel' : 'slot'} onClick={() => setOvKind('partial')}>Робочий, свої години</button>
                      </div>
                      {ovKind === 'partial' && (
                        <div className="row">
                          <input type="time" value={ovStart} onChange={e => setOvStart(e.target.value)} />
                          <input type="time" value={ovEnd} onChange={e => setOvEnd(e.target.value)} />
                        </div>
                      )}
                      <div className="row">
                        <button className="btn primary" onClick={saveOverride}>Зберегти</button>
                        {ovOfDay && <button className="link" onClick={resetOverride}>Скинути до стандарту</button>}
                      </div>
                      {ovMsg && <p className="muted">{ovMsg}</p>}
                    </div>
                  )}
                </>
              )
            ) : (
              rotPreview.length > 0 && mstForm.rotationAnchor && (
                <>
                  <label>Графік роботи (превʼю на 5 тижнів):</label>
                  <BookingCalendar
                    mode="schedule"
                    from={rotPreview[0].date}
                    windowDays={rotPreview.length - 1}
                    availableDates={new Set(rotPreview.filter(d => d.isWorking).map(d => d.date))}
                    readOnly
                  />
                </>
              )
            )}
            <label>Послуги майстра:</label>
            <div className="slots">
              {services.map(s => (
                <button key={s.id}
                  className={mstForm.services.includes(s.id) ? 'slot sel' : 'slot'}
                  onClick={() => setMstForm({
                    ...mstForm,
                    services: mstForm.services.includes(s.id) ? mstForm.services.filter((x: number) => x !== s.id) : [...mstForm.services, s.id]
                  })}>{s.name}</button>
              ))}
            </div>
            <button className="btn primary" disabled={mstForm.services.length === 0} onClick={saveMaster}>{mstForm.id ? 'Зберегти' : 'Додати майстра'}</button>
            {' '}
            {mstForm.id > 0 && <button className="link" onClick={() => setMstForm(emptyMaster())}>Новий майстер</button>}
            {mstForm.services.length === 0
              ? <p className="muted">Для збереження оберіть хоча б одну послугу.</p> : null}
          </div>
          {deact && (
            <div className="card deact-panel">
              <h3>Звільнення майстра: {deact.masterName}</h3>
              <p className="muted">Майбутні записи ({deact.futureAppointments.length}):</p>
              <table className="table">
                <thead><tr><th>Клієнт</th><th>Послуга</th><th>Час</th><th>Переназначити</th></tr></thead>
                <tbody>
                  {deact.futureAppointments.map(a => (
                    <tr key={a.id}>
                      <td>{a.client} · {a.phone}</td>
                      <td>{a.service}</td>
                      <td>{a.startTime}</td>
                      <td>
                        {a.candidates.length
                          ? <select value={deactAssign[a.id] ?? 0} onChange={e => showDeactAssign(a.id, +e.target.value)}>
                              {a.candidates.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                            </select>
                          : <span className="muted">Немає вільних</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <button className="btn primary" onClick={() => confirmDeactivate('Reassign')}>Переназначити всі і звільнить</button>
              {' '}
              <button className="btn" onClick={() => confirmDeactivate('CancelAll')}>Скасувати всі і звільнить</button>
              {' '}
              <button className="link" onClick={closeDeact}>Скасувати</button>
            </div>
          )}

          <h4>Активні</h4>
          <table className="table">
            <thead><tr><th>Імʼя</th><th>Спеціальність</th><th>Графік</th><th>Фото</th><th></th></tr></thead>
            <tbody>
              {masters.filter(m => m.isActive).map(masterRow)}
            </tbody>
          </table>

          <h4>Неактивні (звільнені)</h4>
          <table className="table">
            <thead><tr><th>Імʼя</th><th>Спеціальність</th><th>Графік</th><th>Фото</th><th></th></tr></thead>
            <tbody>
              {masters.filter(m => !m.isActive).map(masterRow)}
            </tbody>
          </table>
        </div>
      )}

      {tab === 'appointments' && (
        <table className="table">
          <thead><tr><th>Клієнт</th><th>Телефон</th><th>Послуга</th><th>Майстер</th><th>Час</th><th>Статус</th><th>Дії</th></tr></thead>
          <tbody>
            {appts.map(a => (
              <tr key={a.id}>
                <td>{a.client}</td><td>{a.phone}</td><td>{a.service}</td><td>{a.master}</td>
                <td>
                  <input type="datetime-local" defaultValue={a.startTime.slice(0, 16)}
                    onBlur={e => { if (e.target.value && e.target.value !== a.startTime.slice(0, 16)) reschedule(a.id, e.target.value) }} />
                </td>
                <td>
                  <select value={a.status} onChange={e => setStatus(a.id, e.target.value)}>
                    {STATUSES.map(s => <option key={s} value={s}>{s}</option>)}
                  </select>
                </td>
                <td>
                  <button className="btn" onClick={() => removeAppointment(a.id)}>Видалити</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {tab === 'appointments' && apptTotalPages > 1 && (
        <div className="pager" style={{ display: 'flex', gap: '.5rem', alignItems: 'center', marginTop: '.75rem' }}>
          <button className="btn" disabled={apptPage <= 1} onClick={() => setApptPage(p => Math.max(1, p - 1))}>← Назад</button>
          <span className="muted">Сторінка {apptPage} з {apptTotalPages}</span>
          <button className="btn" disabled={apptPage >= apptTotalPages} onClick={() => setApptPage(p => Math.min(apptTotalPages, p + 1))}>Далі →</button>
        </div>
      )}

      {tab === 'hours' && (
        <div className="card">
          <h3>Графік закладу</h3>
          <p className="muted">Єдині години на всі 7 днів тижня. Хто працює конкретного дня — визначає лише ротація майстра.</p>
          <label>Відкриття</label>
          <input type="time" value={salonOpen} onChange={e => setSalonOpen(e.target.value)} />
          <label>Закриття</label>
          <input type="time" value={salonClose} onChange={e => setSalonClose(e.target.value)} />
          <button className="btn primary" onClick={saveSalonHours}>Зберегти</button>
          {hoursMsg && <p className="muted">{hoursMsg}</p>}
        </div>
      )}
    </div>
  )
}


