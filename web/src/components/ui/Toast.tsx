import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
import { describeError } from '@/lib/errors'
import { cn } from './cn'

type Tone = 'success' | 'error' | 'info'
interface ToastItem { id: number; tone: Tone; message: string }

interface ToastApi {
  success: (message: string) => void
  info: (message: string) => void
  /** Acepta un mensaje o el error tal cual: lo traduce por su código (lib/errors). */
  error: (messageOrError: unknown) => void
}

const ToastContext = createContext<ToastApi | null>(null)

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([])
  const next = useRef(1)

  const push = useCallback((tone: Tone, message: string) => {
    const id = next.current++
    setItems((current) => [...current.slice(-3), { id, tone, message }])
    setTimeout(() => setItems((current) => current.filter((t) => t.id !== id)), tone === 'error' ? 8000 : 4500)
  }, [])

  const api = useMemo<ToastApi>(
    () => ({
      success: (m) => push('success', m),
      info: (m) => push('info', m),
      error: (e) => push('error', typeof e === 'string' ? e : describeError(e)),
    }),
    [push],
  )

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="pointer-events-none fixed inset-x-0 bottom-4 z-[60] flex flex-col items-center gap-2 px-4" aria-live="polite">
        {items.map((t) => (
          <div
            key={t.id}
            role={t.tone === 'error' ? 'alert' : 'status'}
            className={cn(
              'pointer-events-auto w-full max-w-md rounded-lg px-4 py-3 text-sm font-medium shadow-lg',
              t.tone === 'success' && 'bg-emerald-600 text-white', t.tone === 'error' && 'bg-red-600 text-white', t.tone === 'info' && 'bg-slate-800 text-white',
            )}
          >
            {t.message}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast debe usarse dentro de <ToastProvider>.')
  return ctx
}
