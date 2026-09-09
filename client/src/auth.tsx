import { createContext, useContext, useMemo, useState, type ReactNode } from 'react'
import { readStoredUser, setAuth as persistAuth } from './api'
import type { UserInfo } from './types'

interface AuthState {
  user: UserInfo | null
  /** Встановлює токен і користувача (логін) або (null, null) для виходу. */
  login: (token: string | null, user: UserInfo | null) => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(() => readStoredUser())

  const login = (token: string | null, u: UserInfo | null) => {
    persistAuth(token, u)
    setUser(u)
  }

  const value = useMemo(() => ({ user, login }), [user])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

/** Реактивний доступ до поточного користувача. Компоненти, що його викликають,
 *  автоматично ре-рендеряться при логіні/логауті (на відміну від старого
 *  імпорту `user` напряму з api.ts). */
export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth() використано поза AuthProvider')
  return ctx
}
