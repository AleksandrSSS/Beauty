import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { errMsg, fetchMasters, fetchServices } from '../api'
import MasterCard from '../components/MasterCard'
import type { MasterDto, ServiceDto } from '../types'

export default function Home() {
  const [services, setServices] = useState<ServiceDto[]>([])
  const [masters, setMasters] = useState<MasterDto[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState('')

  useEffect(() => {
    let alive = true
    Promise.all([fetchServices(), fetchMasters()])
      .then(([s, m]) => { if (alive) { setServices(s); setMasters(m) } })
      .catch(e => { if (alive) setErr(errMsg(e)) })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [])

  return (
    <div>
      <section className="hero">
        <h1>Перукарня та манікюр у центрі міста</h1>
        <p>Оберіть майстра, зручний час — і підтвердіть запис кодом. Нагадаємо за 3 години до візиту.</p>
        <Link to="/booking" className="btn primary">Записатись онлайн</Link>
      </section>

      <section className="section">
        <h2>Послуги</h2>
        <div className="grid">
          {services.map(s => (
            <div className="card" key={s.id}>
              <h3>{s.name}</h3>
              <p>{s.description}</p>
              <div className="row"><span className="price">{s.price} ₴</span><span>{s.durationMin} хв</span></div>
            </div>
          ))}
        </div>
      </section>

      <section className="section">
        <h2>Майстри</h2>
        {err && <p className="error">{err}</p>}
        {loading && <p className="muted">Завантаження…</p>}
        <div className="grid">
          {masters.map(m => <MasterCard key={m.id} m={m} />)}
        </div>
      </section>
    </div>
  )
}
