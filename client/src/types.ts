// Спільні типи відповідей API (відповідають DTO сервера, camelCase)

export type Role = 'Client' | 'Admin'
export type RotationType = 'Weekly' | 'TwoTwo'

export type AppointmentStatus =
  | 'PendingVerification'
  | 'Confirmed'
  | 'Cancelled'
  | 'Completed'

export type ChannelId = 'Sms' | 'Telegram' | 'WhatsApp' | 'Viber' | 'Email'

export interface ServiceDto {
  id: number
  name: string
  description?: string | null
  category: 'hair' | 'manicure'
  durationMin: number
  price: number
  isActive: boolean
}

export interface ServiceForm {
  id: number
  name: string
  description: string
  category: 'hair' | 'manicure'
  durationMin: number
  price: number
  isActive: boolean
}

export interface SalonHoursDto {
  /** HH:mm:ss */
  openTime: string
  /** HH:mm:ss */
  closeTime: string
}

/** Відхилення графіка майстра на дату (mirror DayOverrideDto сервера, camelCase). */
export interface DayOverride {
  /** YYYY-MM-DD */
  date: string
  isWorking: boolean
  /** HH:mm:ss або null */
  start?: string | null
  /** HH:mm:ss або null */
  end?: string | null
}

export interface MasterDto {
  id: number
  name: string
  specialty?: string | null
  bio?: string | null
  photoUrl?: string | null
  /** Повертає лише /api/admin/masters. */
  isActive?: boolean
  rotationType: RotationType
  /** Повертає лише /api/admin/masters. */
  rotationAnchor?: string
  services: number[]
  worksThisWeek?: boolean
  worksNextWeek?: boolean
  worksToday?: boolean
  worksTomorrow?: boolean
}

export interface MasterForm {
  id: number
  name: string
  specialty: string
  bio: string
  isActive: boolean
  rotationType: RotationType
  rotationAnchor: string
  services: number[]
  photoUrl: string
}

export interface SlotMaster {
  id: number
  name: string
}

export interface SlotDto {
  start: string
  end: string
  /** лише в режимі "будь-який майстер": хто вільний у цей час */
  masters?: SlotMaster[]
}

export interface UserInfo {
  id: number
  phone: string
  name: string
  email?: string | null
  role: Role
  preferredChannel?: string | null
  externalContact?: string | null
}

export interface AuthResponse {
  token: string
  user: UserInfo
}

export interface DevCodeResponse {
  /** Ідентифікатор запиту коду — потрібен для verify-code (S1-5). */
  requestId: string
  mock: boolean
  info: string
  devCode?: string | null
}

export interface BookingCreateResponse {
  appointmentId: number
  /** true — залогінений, запис одразу Confirmed; false — гість, потрібен крок коду */
  confirmed: boolean
  mock: boolean
  info: string
  devCode?: string | null
}

export interface BookingConfirmResponse {
  ok: boolean
  token: string
  user: UserInfo
}

export interface AppointmentDto {
  id: number
  startTime: string
  service: string
  master: string
  price: number
  status: AppointmentStatus
  /** лише в адмін-панелі */
  client?: string
  phone?: string
}

export interface MasterServiceBriefDto {
  id: number
  name: string
  category: 'hair' | 'manicure'
  durationMin: number
  price: number
}

export interface MasterDetailDto {
  id: number
  name: string
  specialty?: string | null
  bio?: string | null
  photoUrl?: string | null
  rotationType: RotationType
  /** YYYY-MM-DD */
  rotationAnchor: string
  worksToday: boolean
  worksTomorrow: boolean
  worksThisWeek: boolean
  worksNextWeek: boolean
  services: MasterServiceBriefDto[]
}

export interface BusyIntervalDto {
  /** ISO-8601 UTC */
  start: string
  end: string
}

export interface MasterScheduleDayDto {
  /** YYYY-MM-DD (дата у таймзоні салону) */
  date: string
  isWorking: boolean
  /** HH:mm:ss або null для вихідного */
  start?: string | null
  end?: string | null
  busy: BusyIntervalDto[]
}

export interface RotationPreviewDayDto {
  date: string
  isWorking: boolean
}

/** Пагінована відповідь GET /api/admin/appointments (S2-3). */
export interface PagedAppointments {
  page: number
  pageSize: number
  total: number
  totalPages: number
  items: AppointmentDto[]
}
