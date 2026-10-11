import axios, { AxiosError, type AxiosRequestConfig, type InternalAxiosRequestConfig } from 'axios'
import { ApiError, toApiError } from '@/lib/errors'
import type { AuthResponse } from './types'

export const API_URL: string = import.meta.env.VITE_API_URL ?? 'http://localhost:8090/api/v1'

/**
 * El access token vive SOLO en memoria (ADR-006): una variable del módulo. Nunca en localStorage ni sessionStorage: un script malicioso
 * (XSS, una dependencia comprometida) no puede leerlo desde ahí. Se pierde al recargar y se recupera con un refresh (la cookie HttpOnly).
 */
let accessToken: string | null = null

export const tokenStore = {
  get: () => accessToken,
  set: (token: string | null) => {
    accessToken = token
  },
}

type SessionListener = () => void
const sessionLostListeners = new Set<SessionListener>()

/** Avisa cuando la sesión terminó de verdad (no se pudo renovar). La pantalla de sesión lleva a la persona al login. */
export function onSessionLost(listener: SessionListener) {
  sessionLostListeners.add(listener)
  return () => {
    sessionLostListeners.delete(listener)
  }
}

export const http = axios.create({
  baseURL: API_URL,
  withCredentials: true, // para que viaje la cookie del refresh token a /auth/*
  timeout: 20_000,
  headers: { 'Content-Type': 'application/json' },
})

const isAuthCall = (url?: string) => /\/auth\/(login|refresh|register)$/.test(url ?? '')

http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  if (accessToken && !isAuthCall(config.url)) config.headers.set('Authorization', `Bearer ${accessToken}`)
  return config
})

// ── Renovación de la sesión ─────────────────────────────────────────────────
// Hay UN solo refresh en curso a la vez: si varias solicitudes reciben 401 token_expired al mismo tiempo, todas esperan el mismo
// resultado y se reintentan con el token nuevo. Dos refresh simultáneos rotarían el token dos veces y el segundo se vería como
// reutilización (RN-05), lo que revocaría toda la sesión.

let refreshing: Promise<AuthResponse> | null = null

export function refreshSession(): Promise<AuthResponse> {
  refreshing ??= http
    .post<AuthResponse>('/auth/refresh', undefined, { _skipRefresh: true } as AxiosRequestConfig)
    .then((response) => {
      accessToken = response.data.accessToken
      return response.data
    })
    .catch((error: unknown) => {
      accessToken = null
      throw toApiError(error)
    })
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

declare module 'axios' {
  interface AxiosRequestConfig {
    _retried?: boolean
    _skipRefresh?: boolean
  }
}

http.interceptors.response.use(
  (response) => response,
  async (error: unknown) => {
    const failure = toApiError(error)
    const config = error instanceof AxiosError ? error.config : undefined

    const canRefresh = failure.status === 401 && failure.code === 'token_expired' && config && !config._retried && !config._skipRefresh && !isAuthCall(config.url)
    if (canRefresh) {
      try {
        await refreshSession()
        config._retried = true
        return await http.request(config)
      } catch (refreshError) {
        const e = toApiError(refreshError)
        if (e.status === 401) sessionLostListeners.forEach((listener) => listener())
        throw e
      }
    }

    // Cualquier otro 401 de una solicitud con sesión (token inválido, o ya reintentada): la sesión se perdió.
    if (failure.status === 401 && config && !isAuthCall(config.url) && !config._skipRefresh) {
      accessToken = null
      sessionLostListeners.forEach((listener) => listener())
    }

    throw failure
  },
)

export { ApiError }
