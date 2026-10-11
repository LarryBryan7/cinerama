import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { auth } from '@/api/endpoints'
import { onSessionLost, refreshSession, tokenStore } from '@/api/client'
import type { User } from '@/api/types'
import { ApiError, toApiError } from '@/lib/errors'

type Status = 'loading' | 'authenticated' | 'unauthenticated'

interface AuthState {
  status: Status
  user: User | null
  /** Inicia sesión. Si la cuenta existe pero no es de administrador, cierra la sesión que acaba de abrir y lanza `not_admin`. */
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
  /** Por qué se perdió la sesión sin que la persona lo pidiera (para decírselo en el login). */
  sessionLost: boolean
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>('loading')
  const [user, setUser] = useState<User | null>(null)
  const [sessionLost, setSessionLost] = useState(false)
  const queryClient = useQueryClient()

  const clear = useCallback(() => {
    tokenStore.set(null)
    setUser(null)
    setStatus('unauthenticated')
    queryClient.clear() // los datos de administración no quedan en memoria para la siguiente persona
  }, [queryClient])

  // Al cargar la página se intenta recuperar la sesión con la cookie del refresh token (el access token no sobrevive a una recarga).
  useEffect(() => {
    let cancelled = false
    refreshSession()
      .then((response) => {
        if (cancelled) return
        if (response.user.role !== 'Admin') {
          tokenStore.set(null)
          setStatus('unauthenticated')
          return
        }
        setUser(response.user)
        setStatus('authenticated')
      })
      .catch(() => {
        if (!cancelled) setStatus('unauthenticated')
      })
    return () => {
      cancelled = true
    }
  }, [])

  // Si la API dice que la sesión terminó (no se pudo renovar), se vuelve al login.
  useEffect(
    () =>
      onSessionLost(() => {
        setSessionLost(true)
        clear()
      }),
    [clear],
  )

  const login = useCallback(
    async (email: string, password: string) => {
      const response = await auth.login(email, password)
      tokenStore.set(response.accessToken)
      if (response.user.role !== 'Admin') {
        // Una cuenta de cliente no entra al panel: se revoca la sesión que se acaba de abrir (token y cookie).
        await auth.logout().catch(() => undefined)
        tokenStore.set(null)
        throw new ApiError({ status: 403, code: 'not_admin', message: 'Esta cuenta no tiene acceso al panel de administración.' })
      }
      setSessionLost(false)
      setUser(response.user)
      setStatus('authenticated')
    },
    [],
  )

  const logout = useCallback(async () => {
    try {
      await auth.logout()
    } catch (error) {
      // Aunque el servidor no responda, la sesión local se cierra igual.
      void toApiError(error)
    } finally {
      setSessionLost(false)
      clear()
    }
  }, [clear])

  const value = useMemo<AuthState>(() => ({ status, user, login, logout, sessionLost }), [status, user, login, logout, sessionLost])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth debe usarse dentro de <AuthProvider>.')
  return ctx
}
