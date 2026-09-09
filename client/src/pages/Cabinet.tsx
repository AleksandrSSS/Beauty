import { useEffect, useState } from 'react'
import { api, fetchAppointments } from '../api'
import { useAuth } from '../auth'
import type { AppointmentDto, UserInfo } from '../types'

const STATUS_LABELS: Record<string, string> = {
  PendingVerification: 'Очікує підтвердження',
  Confirmed: 'Підтверджено',
  Cancelled: 'Скасовано',
  Completed: 'Завершено'
}

export default function Cabinet() {
  const { user, login } = useAuth()
  const [appts, setAppts] = useState<AppointmentDto[]>([])
  const [profile, setProfile] = useState<UserInfo | null>(user)
  const [saved, setSaved] = useState(false)

  const load = () => fetchAppointments().then(setAppts)
  useEffect(() => { if (user) load() }, [user])
  useEffect(() => { setProfile(user) }, [user])

  if (!user) return <p className="muted">Увійдіть, щоб побачити кабінет.</p>

  const save = async () => {
    if (!profile || !user) return
    await api('/api/account/profile', {
      method: 'PUT',
      body: { name: profile.name, email: profile.email, preferredChannel: profile.preferredChannel, externalContact: profile.externalContact }
    })
    login(localStorage.getItem('token'), profile)
    setSaved(true)
  }

  const cancel = async (id: number) => {
    await api(`/api/account/appointments/${id}/cancel`, { method: 'POST' })
    load()
  }

  return (
    <div>
      <h2>Мій кабінет</h2>
      <div className="card profile">
        <h3>Профіль</h3>
        <label>Імʼя</label>
        <input value={profile?.name || ''} onChange={e => setProfile(p => p ? { ...p, name: e.target.value } : p)} />
        <label>Email</label>
        <input value={profile?.email || ''} onChange={e => setProfile(p => p ? { ...p, email: e.target.value } : p)} />
        <label>Контакт для повідомлень (chat id / email)</label>
        <input value={profile?.externalContact || ''} onChange={e => setProfile(p => p ? { ...p, externalContact: e.target.value } : p)} />
        <button className="btn" onClick={save}>Зберегти</button>
        {saved && <span className="muted"> збережено ✓</span>}
      </div>

      <h3>Історія записів</h3>
      {appts.length === 0 && <p className="muted">Записів ще немає.</p>}
      <table className="table">
        <thead>
          <tr><th>Дата</th><th>Послуга</th><th>Майстер</th><th>Ціна</th><th>Статус</th><th></th></tr>
        </thead>
        <tbody>
          {appts.map(a => (
            <tr key={a.id}>
              <td>{new Date(a.startTime).toLocaleString('uk-UA')}</td>
              <td>{a.service}</td>
              <td>{a.master}</td>
              <td>{a.price} ₴</td>
              <td><span className={`status ${a.status}`}>{STATUS_LABELS[a.status]}</span></td>
              <td>
                {(a.status === 'Confirmed' || a.status === 'PendingVerification') && (
                  <button className="link" onClick={() => cancel(a.id)}>Скасувати</button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
