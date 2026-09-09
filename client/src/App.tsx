import { Routes, Route, NavLink, useNavigate } from 'react-router-dom'
import { useAuth } from './auth'
import Home from './pages/Home'
import Booking from './pages/Booking'
import Login from './pages/Login'
import Cabinet from './pages/Cabinet'
import Admin from './pages/Admin'
import MasterSchedule from './pages/MasterSchedule'

export default function App() {
  const nav = useNavigate()
  const { user: currentUser, login } = useAuth()
  const logout = () => { login(null, null); nav('/') }

  return (
    <div className="app">
      <header className="header">
        <NavLink to="/" className="logo">✂ Beauty Salon</NavLink>
        <nav>
          <NavLink to="/">Головна</NavLink>
          <NavLink to="/booking">Записатись</NavLink>
          {currentUser ? (
            <>
              {currentUser.role !== 'Admin' && <NavLink to="/cabinet">Мій кабінет</NavLink>}
              {currentUser.role === 'Admin' && <NavLink to="/admin">Адмін</NavLink>}
              <button className="link" onClick={logout}>Вийти</button>
            </>
          ) : (
            <NavLink to="/login">Увійти</NavLink>
          )}
        </nav>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/booking" element={<Booking />} />
          <Route path="/masters/:id" element={<MasterSchedule />} />
          <Route path="/login" element={<Login />} />
          <Route path="/cabinet" element={<Cabinet />} />
          <Route path="/admin" element={<Admin />} />
        </Routes>
      </main>
      <footer className="footer">Beauty Salon — перукарня та манікюр · вул. Прикладна 1 · +380 00 000 0000</footer>
    </div>
  )
}
