import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { App } from '@/App'
import { tokenStore } from '@/api/client'
import { AuthProvider } from '@/auth/AuthContext'
import { ToastProvider } from '@/components/ui/Toast'

export const testQueryClient = () => new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 }, mutations: { retry: false } } })

/** Monta la aplicación completa (rutas, sesión, toasts) en una ruta. La sesión se recupera con el refresh simulado por MSW. */
export function renderApp(route = '/') {
  tokenStore.set(null)
  const user = userEvent.setup()
  const utils = render(
    <QueryClientProvider client={testQueryClient()}>
      <ToastProvider>
        <MemoryRouter initialEntries={[route]}>
          <AuthProvider>
            <App />
          </AuthProvider>
        </MemoryRouter>
      </ToastProvider>
    </QueryClientProvider>,
  )
  return { user, ...utils }
}

/** Monta solo un componente con proveedores (para pruebas de formularios o de componentes de interfaz). */
export function renderWithProviders(ui: ReactElement, route = '/') {
  const user = userEvent.setup()
  return { user, ...render(<QueryClientProvider client={testQueryClient()}><ToastProvider><MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter></ToastProvider></QueryClientProvider>) }
}
