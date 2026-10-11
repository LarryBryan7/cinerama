import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { cinemas, movies, showtimes } from '@/api/endpoints'
import { useAuth } from '@/auth/AuthContext'
import { ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { formatDateTime } from '@/lib/format'
import { addDays, cinemaToday } from '@/lib/time'
import { SummaryCards } from '../reports/ReportsPage'

export default function DashboardPage() {
  const { user } = useAuth()
  const today = cinemaToday()
  const range = { from: today, to: today }

  const upcoming = useQuery({
    queryKey: ['showtimes', 'dashboard'],
    queryFn: () => showtimes.list({ upcomingOnly: true, status: 'scheduled', to: addDays(today, 1), page: 1, pageSize: 8 }),
  })
  const activeMovies = useQuery({ queryKey: ['movies', 'count'], queryFn: () => movies.list({ isActive: true, pageSize: 1 }) })
  const cinemaList = useQuery({ queryKey: ['cinemas'], queryFn: cinemas.list })

  return (
    <>
      <PageHeader title={`Hola, ${user?.fullName.split(' ')[0] ?? ''}`} subtitle="El estado del día en la hora del cine." />

      <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-500">Hoy</h2>
      <SummaryCards range={range} />

      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        <section aria-labelledby="next-title" className="rounded-xl border border-slate-200 bg-white p-4 lg:col-span-2">
          <h2 id="next-title" className="mb-3 font-semibold">Próximas funciones</h2>
          {upcoming.isPending && <Spinner />}
          {upcoming.isError && <ErrorState error={upcoming.error} onRetry={() => void upcoming.refetch()} />}
          {upcoming.data && upcoming.data.items.length === 0 && <p className="text-sm text-slate-500">No hay funciones programadas para hoy ni mañana.</p>}
          {upcoming.data && upcoming.data.items.length > 0 && (
            <ul className="divide-y divide-slate-100">
              {upcoming.data.items.map((s) => (
                <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm">
                  <span><strong>{s.movieTitle}</strong> · {s.cinemaName} · {s.roomName}</span>
                  <span className="text-slate-600">{formatDateTime(s.startsAt)} · {s.seatsSold}/{s.capacity}</span>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section aria-labelledby="quick-title" className="rounded-xl border border-slate-200 bg-white p-4">
          <h2 id="quick-title" className="mb-3 font-semibold">Catálogo</h2>
          <p className="text-sm text-slate-600">{activeMovies.data ? `${activeMovies.data.totalCount} películas activas` : '…'} · {cinemaList.data ? `${cinemaList.data.length} cines` : '…'}</p>
          <nav aria-label="Accesos rápidos" className="mt-3 flex flex-col gap-2 text-sm font-medium text-sky-700">
            <Link to="/showtimes" className="hover:underline">Programar una función</Link>
            <Link to="/movies/new" className="hover:underline">Agregar una película</Link>
            <Link to="/bookings" className="hover:underline">Ver reservas</Link>
            <Link to="/reports" className="hover:underline">Reportes</Link>
          </nav>
        </section>
      </div>
    </>
  )
}
