import { useState } from 'react'
import { Routes, Route, NavLink, Navigate, useNavigate, useLocation } from 'react-router-dom'
import { useAuth } from './auth'
import Home from './pages/Home'
import Booking from './pages/Booking'
import Login from './pages/Login'
import Cabinet from './pages/Cabinet'
import Admin from './pages/Admin'
import MasterSchedule from './pages/MasterSchedule'
import { IconFacebook, IconFlower, IconInstagram, IconMenu, IconTelegram } from './components/icons'

/** Скрол-якорі секцій на головній (violet-reskin-plan.md §5). */
const SECTIONS: [id: string, label: string][] = [
  ['services', 'Послуги'],
  ['gallery', 'Галерея'],
  ['pricing', 'Ціни'],
  ['about', 'Про салон'],
  ['contact', 'Контакти']
]

export default function App() {
  const nav = useNavigate()
  const { pathname } = useLocation()
  const { user: currentUser, login } = useAuth()
  const [menuOpen, setMenuOpen] = useState(false)

  const logout = () => { login(null, null); nav('/') }

  /** Плавний перехід до секції: на головній прямо, інак — спочатку на `/`. */
  const goSection = (id: string) => {
    setMenuOpen(false)
    if (pathname === '/') {
      document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' })
    } else {
      nav('/')
      setTimeout(() => document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' }), 120)
    }
  }

  const routeLink = (to: string, label: string) => (
    <NavLink key={to} to={to} onClick={() => setMenuOpen(false)}>{label}</NavLink>
  )

  return (
    <div className="app">
      <header className="header">
        <NavLink to="/" className="logo" aria-label="Фіалочка — на головну">
          <span className="logo-flower" aria-hidden="true"><IconFlower /></span> Фіалочка
        </NavLink>
        <nav className="nav-desktop">
          {SECTIONS.map(([id, label]) => (
            <button key={id} className="nav-anchor link" onClick={() => goSection(id)}>{label}</button>
          ))}
          {currentUser?.role !== 'Admin' && (
            <button className="nav-anchor link"> {routeLink('/booking', 'Записатись')}</button>
          )}
          {currentUser ? (
              <>
                <button className="nav-anchor link">{currentUser.role !== 'Admin' && routeLink('/cabinet', 'Мій кабінет')} </button>
                <button className="nav-anchor link">{currentUser.role === 'Admin' && routeLink('/admin', 'Адмін')} </button>
                <button className="link" onClick={logout}>Вийти</button>
              </>
          ) : (
            routeLink('/login', 'Увійти')
          )}
        </nav>
        <button className="nav-burger" aria-label="Меню" aria-expanded={menuOpen} onClick={() => setMenuOpen(!menuOpen)}>
          <IconMenu />
        </button>
      </header>

      {menuOpen && (
        <nav className="nav-mobile" aria-label="Мобільне меню">
          {SECTIONS.map(([id, label]) => (
            <button key={id} className="link" onClick={() => goSection(id)}>{label}</button>
          ))}
          {currentUser?.role !== 'Admin' && routeLink('/booking', 'Записатись')}
          {currentUser ? (
            <>
              {currentUser.role !== 'Admin' && routeLink('/cabinet', 'Мій кабінет')}
              {currentUser.role === 'Admin' && routeLink('/admin', 'Адмін')}
              <button className="link" onClick={logout}>Вийти</button>
            </>
          ) : (
            routeLink('/login', 'Увійти')
          )}
        </nav>
      )}

      <main>
        <Routes>
          <Route path="/" element={<Home />} />
          {/* Адмін не бронює і не має кабінету — редирект на адмінку (вимога 2026-09-09). */}
          <Route path="/booking" element={currentUser?.role === 'Admin' ? <Navigate to="/admin" replace /> : <Booking />} />
          <Route path="/masters/:id" element={<MasterSchedule />} />
          <Route path="/login" element={<Login />} />
          <Route path="/cabinet" element={currentUser?.role === 'Admin' ? <Navigate to="/admin" replace /> : <Cabinet />} />
          <Route path="/admin" element={<Admin />} />
        </Routes>
      </main>
      <footer className="footer">
        <div>Фіалочка — салон краси · вул. Прикладна 1 · <a href="tel:+380000000000">+380 00 000 0000</a></div>
        <div className="footer__social" aria-label="Соціальні мережі">
          <IconInstagram /><IconFacebook /><IconTelegram />
        </div>
      </footer>
    </div>
  )
}
