import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { errMsg, fetchMasters, fetchServices } from '../api'
import MasterCard from '../components/MasterCard'
import { IconArrow, IconClock, IconHeart, IconMail, IconMapPin, IconNail, IconPalette, IconPhone, IconScissors } from '../components/icons'
import type { MasterDto, ServiceDto } from '../types'

const PHONE = '+380 00 000 0000'
const PHONE_URL = 'tel:+380000000000'

/** Кольори під розділи Галереї (violet-reskin-plan.md §1). */
const GALLERY = [
  { icon: 'scissors', caption: 'Стрижка і укладка', c1: '#7851A9', c2: '#DA70D6' },
  { icon: 'nail', caption: 'Манікюр и педикюр', c1: '#DA70D6', c2: '#C71585' },
  { icon: 'palette', caption: 'Фарбування', c1: '#4B61D1', c2: '#7851A9' },
  { icon: 'heart', caption: 'SPA-догляд', c1: '#C71585', c2: '#DA70D6' }
]

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

  const hair = services.filter(s => s.category === 'hair')
  const manicure = services.filter(s => s.category === 'manicure')

  return (
    <div className="lp">
      {/* ---------- Hero ---------- */}
      <section className="lp-hero" id="top">
        <div className="lp-hero__bg" aria-hidden="true" />
        <div className="lp-hero__inner">
          <h1>Фіалочка</h1>
          <p className="lp-hero__sub">Салон краси: перукарські послуги, манікюр і догляд у самому центрі.</p>
          <p className="lp-hero__hint">Оберіть майстра і зручний час — підтвердіть запис кодом. Нагадаємо за 3 години до візиту.</p>
          <Link to="/booking" className="btn primary lp-btn-lg">Записатися онлайн <IconArrow /></Link>
        </div>
      </section>

      {/* ---------- Services ---------- */}
      <section className="lp-section" id="services">
        <h2 className="lp-h2">Послуги</h2>
        {err && <p className="error">{err}</p>}
        {loading && <p className="muted">Завантаження…</p>}
        {!loading && !err && (
          <>
            {hair.length > 0 && (
              <div className="lp-svc-group">
                <h3><span className="lp-icon"><IconScissors /></span> Перукарні послуги</h3>
                <div className="lp-svc-grid">
                  {hair.map(s => (
                    <div className="lp-svc" key={s.id}>
                      <h4>{s.name}</h4>
                      <p>{s.description}</p>
                    </div>
                  ))}
                </div>
              </div>
            )}
            {manicure.length > 0 && (
              <div className="lp-svc-group">
                <h3><span className="lp-icon"><IconNail /></span> Манікюр</h3>
                <div className="lp-svc-grid">
                  {manicure.map(s => (
                    <div className="lp-svc" key={s.id}>
                      <h4>{s.name}</h4>
                      <p>{s.description}</p>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </>
        )}
      </section>

      {/* ---------- Gallery ---------- */}
      <section className="lp-section lp-alt" id="gallery">
        <h2 className="lp-h2">Галерея</h2>
        <div className="lp-gallery">
          {GALLERY.map((g, i) => (
            <div className="lp-gallery__cell" key={g.caption} style={{ background: `linear-gradient(135deg, ${g.c1}, ${g.c2})` }}>
              <div className="lp-gallery__icon">{i === 0 ? <IconScissors /> : i === 1 ? <IconNail /> : i === 2 ? <IconPalette /> : <IconHeart />}</div>
              <span>{g.caption}</span>
            </div>
          ))}
        </div>
      </section>

      {/* ---------- Pricing ---------- */}
      <section className="lp-section" id="pricing">
        <h2 className="lp-h2">Ціни</h2>
        {err && <p className="error">{err}</p>}
        {loading && <p className="muted">Завантаження…</p>}
        {!loading && !err && (
          <div className="lp-price">
            <h3 className="lp-price__group">Перукарні послуги</h3>
            <ul>
              {hair.map(s => <li className="lp-price__row" key={s.id}><span>{s.name}</span><span className="lp-price__spacer" aria-hidden="true" /><b>{s.price} ₴</b><i>{s.durationMin} хв</i></li>)}
            </ul>
            <h3 className="lp-price__group">Манікюр</h3>
            <ul>
              {manicure.map(s => <li className="lp-price__row" key={s.id}><span>{s.name}</span><span className="lp-price__spacer" aria-hidden="true" /><b>{s.price} ₴</b><i>{s.durationMin} хв</i></li>)}
            </ul>
          </div>
        )}
      </section>

      {/* ---------- Майстри ---------- */}
      <section className="lp-section" id="masters">
        <h2 className="lp-h2">Наші майстри</h2>
        {err && <p className="error">{err}</p>}
        {loading && <p className="muted">Завантаження…</p>}
        {!loading && !err && (
          <div className="grid">
            {masters.map(m => <MasterCard key={m.id} m={m} />)}
          </div>
        )}
      </section>

      {/* ---------- Про салон ---------- */}
      <section className="lp-section lp-alt" id="about">
        <h2 className="lp-h2">Про салон</h2>
        <p className="lp-about__lead">
          «Фіалочка» — невеличкий затишний салон краси у самому центрі. Бережно ставимося і
          до волос, і до нігтів, і до вашого часу: онлайн-запис, зрозумлі ціни і фіксированный
          лікар від майстра без черг.
        </p>
        <ul className="lp-about__list">
          <li><span className="lp-icon"><IconClock /></span> Працюємо 09:00–18:00 щодня</li>
          <li><span className="lp-icon"><IconMapPin /></span> вул. Прикладна 1, центр міста</li>
          <li><span className="lp-icon"><IconHeart /></span> Догляд і комфорт на першому місці</li>
        </ul>
      </section>

      {/* ---------- Contact ---------- */}
      <section className="lp-section" id="contact">
        <h2 className="lp-h2">Контакти</h2>
        <div className="lp-contact">
          <p>Запишитеся онлайн — зручніше, або просто зателефонуйте в салон.</p>
          <div className="lp-contact__actions">
            <a className="btn primary" href={PHONE_URL}><IconPhone /> {PHONE}</a>
            <Link className="btn" to="/booking">Записатися онлайн</Link>
          </div>
          <p className="lp-contact__meta">
            <span><IconMail /> hello@fialochka.example</span>
            <span><IconPhone /> {PHONE}</span>
          </p>
        </div>
      </section>
    </div>
  )
}
