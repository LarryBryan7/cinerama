import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { Spinner } from '@/components/ui/Feedback'
import { useAuth } from './AuthContext'

/** Guardia de rutas: sin sesión de administrador se va al login y, al entrar, se vuelve a donde se quería ir. */
export function RequireAdmin() {
  const { status, user } = useAuth()
  const location = useLocation()

  if (status === 'loading') return <Spinner label="Comprobando la sesión…" />
  if (status !== 'authenticated' || user?.role !== 'Admin') {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }
  return <Outlet />
}
