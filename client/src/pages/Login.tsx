import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { adminLogin, errMsg, requestCode, verifyCode, CHANNELS } from '../api'
import { useAuth } from '../auth'
import { normalizePhone } from '../salon'
import type { ChannelId } from '../types'

export default function Login() {
  const nav = useNavigate()
  const { login } = useAuth()
  const [phone, setPhone] = useState('+380')
  const [name, setName] = useState('')
  const [channel, setChannel] = useState<ChannelId>('Sms')
  const [contact, setContact] = useState('')
  const [code, setCode] = useState('')
  const [sent, setSent] = useState(false)
  const [info, setInfo] = useState('')
  const [err, setErr] = useState('')
  const [adminMode, setAdminMode] = useState(false)
  const [password, setPassword] = useState('')

  const requestCodeFn = async () => {
    setErr('')
    try {
      const r = await requestCode({ phone, channel, contact: contact || null })
      setSent(true)
      setInfo(r.devCode ? `DEV-режим: код — ${r.devCode}` : r.info || 'Код надіслано')
    } catch (e) { setErr(errMsg(e)) }
  }

  const verify = async () => {
    setErr('')
    try {
      const r = await verifyCode({ phone, code, name, contact: contact || null })
      login(r.token, r.user)
      nav(r.user.role === 'Admin' ? '/admin' : '/cabinet')
    } catch (e) { setErr(errMsg(e)) }
  }

  const adminLoginFn = async () => {
    setErr('')
    try {
      const r = await adminLogin({ phone, password })
      login(r.token, r.user)
      nav('/admin')
    } catch (e) { setErr(errMsg(e)) }
  }

  return (
    <div className="form-page">
      <h2>Вхід</h2>
      <label>Телефон</label>
      <input value={phone} onChange={e => setPhone(normalizePhone(e.target.value))} placeholder="+380..." />
      {!adminMode && !sent && (
        <>
          <label>Отримати код через</label>
          <select value={channel} onChange={e => setChannel(e.target.value as ChannelId)}>
            {CHANNELS.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
          </select>
          <label>{channel === 'Email' ? 'Email' : channel === 'Telegram' ? 'Telegram (номер або chat id)' : 'Контакт (необовʼязково)'}</label>
          <input value={contact} onChange={e => setContact(e.target.value)} />
          <button className="btn primary" onClick={requestCodeFn}>Отримати код</button>
        </>
      )}
      {!adminMode && sent && (
        <>
          <p className="muted">{info}</p>
          <label>Код підтвердження</label>
          <input value={code} onChange={e => setCode(e.target.value)} placeholder="0000" />
          <label>Імʼя (для нових клієнтів)</label>
          <input value={name} onChange={e => setName(e.target.value)} />
          <button className="btn primary" onClick={verify}>Увійти</button>
        </>
      )}
      {adminMode && (
        <>
          <label>Пароль адміна</label>
          <input type="password" value={password} onChange={e => setPassword(e.target.value)} />
          <button className="btn primary" onClick={adminLoginFn}>Увійти як адмін</button>
        </>
      )}
      <button className="link" onClick={() => setAdminMode(!adminMode)}>
        {adminMode ? '← Вхід для клієнтів' : 'Вхід для адміністратора'}
      </button>
      {err && <p className="error">{err}</p>}
    </div>
  )
}
