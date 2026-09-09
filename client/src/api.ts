import type {
  AppointmentDto,
  AuthResponse,
  BookingConfirmResponse,
  BookingCreateResponse,
  ChannelId,
  DevCodeResponse,
  MasterDetailDto,
  MasterDto,
  MasterScheduleDayDto,
  DayOverride,
  RotationPreviewDayDto,
  RotationType,
  SalonHoursDto,
  ServiceDto,
  SlotDto,
  UserInfo
} from './types'

const API = import.meta.env.VITE_API_URL || ''

// `token` лишається модульною змінною — потрібна для fetch-інтерсептора нижче,
// який працює поза React-деревом. Джерело істини для `user` — AuthContext
// (див. auth.tsx), бо React-компоненти мають реагувати на його зміну.
export let token: string | null = localStorage.getItem('token')

export function readStoredUser(): UserInfo | null {
  try { return JSON.parse(localStorage.getItem('user') || 'null') as UserInfo | null }
  catch { return null }
}

export function setAuth(t: string | null, u: UserInfo | null) {
  token = t
  if (t) localStorage.setItem('token', t); else localStorage.removeItem('token')
  if (u) localStorage.setItem('user', JSON.stringify(u)); else localStorage.removeItem('user')
}

export async function api<T = unknown>(path: string, opts: { method?: string; body?: unknown } = {}): Promise<T> {
  const res = await fetch(API + path, {
    method: opts.method || 'GET',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined
  })
  const data = await res.json().catch(() => ({})) as { error?: string }
  if (!res.ok) throw new Error(data.error || `Помилка ${res.status}`)
  return data as T
}

/** Витягує повідомлення з невідомої помилки (замість catch (e: any)). */
export function errMsg(e: unknown): string {
  return e instanceof Error ? e.message : String(e)
}

// типізовані обгортки основних ендпоінтів
export const fetchServices = () => api<ServiceDto[]>('/api/catalog/services')
export const fetchMasters = () => api<MasterDto[]>('/api/catalog/masters')
export const fetchSlots = (masterId: number | null, serviceId: number) =>
  api<SlotDto[]>('/api/booking/slots', { method: 'POST', body: { masterId, serviceId } })
export const fetchAppointments = () => api<AppointmentDto[]>('/api/account/appointments')
export const requestCode = (body: { phone: string; channel: ChannelId; contact: string | null }) =>
  api<DevCodeResponse>('/api/auth/request-code', { method: 'POST', body })
export const verifyCode = (body: { phone: string; code: string; name: string; contact: string | null }) =>
  api<AuthResponse>('/api/auth/verify-code', { method: 'POST', body })
export const adminLogin = (body: { phone: string; password: string }) =>
  api<AuthResponse>('/api/auth/admin-login', { method: 'POST', body })
export const createBooking = (body: { masterId: number | null; serviceId: number | null; startTime: string | null; channel?: string | null; contact?: string | null; phone?: string; name?: string | null }) =>
  api<BookingCreateResponse>('/api/booking/create', { method: 'POST', body })
export const confirmBooking = (body: { appointmentId: number; code: string; phone?: string }) =>
  api<BookingConfirmResponse>('/api/booking/confirm', { method: 'POST', body })

// ---- Сторінка майстра (Фаза 5/6) ----
export const fetchMaster = (id: number) => api<MasterDetailDto>(`/api/catalog/masters/${id}`)
export const fetchMasterSchedule = (id: number) =>
  api<MasterScheduleDayDto[]>(`/api/catalog/masters/${id}/schedule`)

/** Перетворює відносний шлях сервера (/uploads/...) на повний URL у середовищі клієнта. */
export const resolvePhotoUrl = (path: string) => (path.startsWith('http') ? path : `${API}${path}`)

// ---- Адмін: фото та прев'ю ротації (використовуються в блоці E) ----
/** Завантаження фото майстра. Content-Type НЕ ставимо — boundary додасть браузер. */
export async function uploadMasterPhoto(id: number, file: File): Promise<{ photoUrl: string }> {
  const fd = new FormData()
  fd.append('file', file)
  const res = await fetch(`${API}/api/admin/masters/${id}/photo`, {
    method: 'POST',
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: fd
  })
  const data = await res.json().catch(() => ({})) as { error?: string; photoUrl?: string }
  if (!res.ok) throw new Error(data.error || `Помилка ${res.status}`)
  return { photoUrl: data.photoUrl! }
}
export const deleteMasterPhoto = (id: number) =>
  api<{ ok: boolean }>(`/api/admin/masters/${id}/photo`, { method: 'DELETE' })
export const previewRotation = (body: { rotationType: RotationType; rotationAnchor: string }) =>
  api<RotationPreviewDayDto[]>('/api/admin/rotation-preview', { method: 'POST', body })

// ---- Графік закладу (єдиний на всі дні) ----
export const fetchSalonHours = () => api<SalonHoursDto>('/api/admin/salon-hours')
export const updateSalonHours = (body: { openTime: string; closeTime: string }) =>
  api<SalonHoursDto>('/api/admin/salon-hours', { method: 'PUT', body })

// ---- Адмін: видалення запису (hard-delete) ----
export const deleteAppointment = (id: number) =>
  api<{ ok: boolean }>(`/api/admin/appointments/${id}`, { method: 'DELETE' })

// ---- Адмін: відхилення графіка майстра (override) ----
export const fetchMasterDayOverrides = (id: number) =>
  api<DayOverride[]>(`/api/admin/masters/${id}/day-overrides`)
export const putMasterDayOverride = (id: number, body: { date: string; isWorking: boolean; start?: string | null; end?: string | null }) =>
  api<{ ok: boolean }>(`/api/admin/masters/${id}/day-override`, { method: 'PUT', body })
export const deleteMasterDayOverride = (id: number, date: string) =>
  api<{ ok: boolean }>(`/api/admin/masters/${id}/day-override?date=${date}`, { method: 'DELETE' })

export const CHANNELS: { id: ChannelId; label: string }[] = [
  { id: 'Sms', label: 'SMS' },
  { id: 'Telegram', label: 'Telegram' },
  { id: 'WhatsApp', label: 'WhatsApp' },
  { id: 'Viber', label: 'Viber' },
  { id: 'Email', label: 'Email' }
]

// Дні тижня — лише з понеділка (Пн–Нд), як сітка BookingCalendar (MONDAY_FIRST).
// Неділя-перший порядок (0 = Нд, конвенція JS getDay / DayOfWeek бекенда) в UI не використовуємо.
export const DAYS = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Нд']
