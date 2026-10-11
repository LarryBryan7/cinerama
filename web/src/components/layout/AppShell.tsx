import { Suspense, useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '@/auth/AuthContext'
import { Spinner } from '@/components/ui/Feedback'
import { Button } from '@/components/ui/Button'
import { cn } from '@/components/ui/cn'

const NAV = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/movies', label: 'Películas' },
  { to: '/genres', label: 'Géneros' },
  { to: '/cinemas', label: 'Cines' },
  { to: '/rooms', label: 'Salas y asientos' },
  { to: '/showtimes', label: 'Funciones' },
  { to: '/bookings', label: 'Reservas' },
  { to: '/users', label: 'Usuarios' },
  { to: '/reports', label: 'Reportes' },
]

/**
 * Estructura de todas las pantallas: barra lateral fija en pantallas anchas y un menú que se abre en las angostas.
 * El contenido se carga bajo demanda (cada sección es un trozo aparte del paquete).
 */
export function AppShell() {
  const { user, logout } = useAuth()
  const [open, setOpen] = useState(false)

  return (
    <div className="flex min-h-full flex-col md:flex-row">
      <a href="#contenido" className="sr-only focus:not-sr-only focus:fixed focus:left-2 focus:top-2 focus:z-50 focus:rounded focus:bg-white focus:p-2">Saltar al contenido</a>

      <header className="flex items-center justify-between bg-panel px-4 py-2 text-white md:hidden">
        <span className="font-bold">Cinerama</span>
        <button
          type="button"
          aria-expanded={open}
          aria-controls="menu-principal"
          onClick={() => setOpen((v) => !v)}
          className="inline-flex min-h-11 items-center rounded-lg px-3 text-sm hover:bg-panel-2"
        >
          {open ? 'Cerrar menú' : 'Menú'}
        </button>
      </header>

      <aside className={cn('w-full shrink-0 bg-panel text-slate-200 md:block md:w-60', open ? 'block' : 'hidden')}>
        <div className="hidden px-5 py-5 md:block">
          <p className="text-lg font-bold text-white">Cinerama</p>
          <p className="text-xs text-slate-400">Panel de administración</p>
        </div>
        <nav id="menu-principal" aria-label="Principal" className="flex flex-col gap-1 px-3 pb-3">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              onClick={() => setOpen(false)}
              className={({ isActive }) =>
                cn('flex min-h-11 items-center rounded-lg px-3 text-sm', isActive ? 'bg-lime font-semibold text-ink' : 'hover:bg-panel-2')
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="border-t border-panel-2 px-5 py-4 text-sm md:mt-auto">
          <p className="truncate font-medium text-white" title={user?.email}>{user?.fullName}</p>
          <p className="mb-3 truncate text-xs text-slate-400">{user?.email}</p>
          <Button variant="secondary" size="sm" onClick={() => void logout()}>Cerrar sesión</Button>
        </div>
      </aside>

      <main id="contenido" className="min-w-0 flex-1 p-4 sm:p-6 lg:p-8">
        <Suspense fallback={<Spinner />}>
          <Outlet />
        </Suspense>
      </main>
    </div>
  )
}
