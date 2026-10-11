import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { movies as api } from '@/api/endpoints'
import type { MovieListItem, MovieStatus } from '@/api/types'
import { Button } from '@/components/ui/Button'
import { Badge, EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Input, Select } from '@/components/ui/Form'
import { ConfirmDialog } from '@/components/ui/Modal'
import { Pagination } from '@/components/ui/Pagination'
import { Table, Td, Th, Thead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { formatDate, formatMinutes } from '@/lib/format'
import { useDebounced } from '../shared/useDebounced'

const PAGE_SIZE = 20
const STATUS_LABEL: Record<MovieStatus, string> = { NowShowing: 'En cartelera', PreSale: 'Preventa', ComingSoon: 'Próximamente' }
const STATUS_TONE = { NowShowing: 'green', PreSale: 'blue', ComingSoon: 'amber' } as const

export default function MoviesPage() {
  const [search, setSearch] = useState('')
  const [active, setActive] = useState<'' | 'true' | 'false'>('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [deactivating, setDeactivating] = useState<MovieListItem | null>(null)
  const debounced = useDebounced(search)

  const filters = { search: debounced.trim() || undefined, isActive: active === '' ? undefined : active === 'true', status: status || undefined, page, pageSize: PAGE_SIZE }
  const query = useQuery({ queryKey: ['movies', filters], queryFn: () => api.list(filters), placeholderData: (previous) => previous })

  const reset = (apply: () => void) => {
    apply()
    setPage(1)
  }

  return (
    <>
      <PageHeader
        title="Películas" subtitle="El catálogo que ven los clientes. Dar de baja una película la quita de la cartelera sin borrar su historial."
        actions={<Link to="/movies/new" className="inline-flex min-h-11 items-center rounded-lg bg-lime px-4 text-sm font-semibold text-ink hover:bg-lime-dark">Nueva película</Link>}
      />

      <div className="mb-4 grid gap-3 sm:grid-cols-3">
        <Input type="search" aria-label="Buscar por título" placeholder="Buscar por título" value={search} onChange={(e) => reset(() => setSearch(e.target.value))} />
        <Select aria-label="Estado de estreno" value={status} onChange={(e) => reset(() => setStatus(e.target.value))}>
          <option value="">Todos los estados</option>
          <option value="nowShowing">En cartelera</option>
          <option value="presale">Preventa</option>
          <option value="comingSoon">Próximamente</option>
        </Select>
        <Select aria-label="Actividad" value={active} onChange={(e) => reset(() => setActive(e.target.value as '' | 'true' | 'false'))}>
          <option value="">Activas y de baja</option>
          <option value="true">Solo activas</option>
          <option value="false">Solo de baja</option>
        </Select>
      </div>

      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.items.length === 0 && <EmptyState title="No hay películas con esos filtros" hint="Prueba con otra búsqueda o crea una película nueva." />}
      {query.data && query.data.items.length > 0 && (
        <>
          <Table caption="Películas">
            <Thead><Tr><Th>Película</Th><Th>Géneros</Th><Th>Estreno</Th><Th>Estado</Th><Th>Funciones</Th><Th className="text-right">Acciones</Th></Tr></Thead>
            <tbody>
              {query.data.items.map((movie) => (
                <Tr key={movie.id} className={movie.isActive ? '' : 'bg-slate-50 text-slate-500'}>
                  <Td>
                    <div className="flex items-center gap-3">
                      {movie.posterPreviewUrl
                        ? <img src={movie.posterPreviewUrl} alt="" loading="lazy" className="h-14 w-10 rounded object-cover" />
                        : <div aria-hidden className="h-14 w-10 rounded bg-slate-200" />}
                      <div>
                        <p className="font-medium">{movie.title}</p>
                        <p className="text-xs text-slate-500">{formatMinutes(movie.durationMinutes)} · {movie.classification}</p>
                      </div>
                    </div>
                  </Td>
                  <Td>{movie.genres.join(', ')}</Td>
                  <Td>{formatDate(movie.releaseDate)}</Td>
                  <Td>
                    <div className="flex flex-wrap gap-1">
                      <Badge tone={STATUS_TONE[movie.status]}>{STATUS_LABEL[movie.status]}</Badge>
                      {!movie.isActive && <Badge tone="red">De baja</Badge>}
                    </div>
                  </Td>
                  <Td>{movie.upcomingShowtimes}</Td>
                  <Td className="text-right">
                    <div className="flex justify-end gap-2">
                      <Link to={`/movies/${movie.id}`} aria-label={`Editar ${movie.title}`} className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 text-sm hover:bg-slate-50">Editar</Link>
                      {movie.isActive && <Button variant="ghost" size="sm" onClick={() => setDeactivating(movie)} aria-label={`Dar de baja ${movie.title}`}>Dar de baja</Button>}
                    </div>
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          <Pagination page={page} pageSize={PAGE_SIZE} totalCount={query.data.totalCount} onPage={setPage} />
        </>
      )}

      {deactivating && <DeactivateMovie movie={deactivating} onClose={() => setDeactivating(null)} />}
    </>
  )
}

function DeactivateMovie({ movie, onClose }: { movie: MovieListItem; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const remove = useMutation({
    mutationFn: () => api.remove(movie.id),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['movies'] })
      toast.success(`«${movie.title}» se dio de baja.`)
      onClose()
    },
    onError: (error) => {
      toast.error(error) // movie_has_active_bookings: tiene funciones futuras con reservas confirmadas
      onClose()
    },
  })

  return (
    <ConfirmDialog
      title="Dar de baja la película" confirmLabel="Dar de baja" loading={remove.isPending} onCancel={onClose} onConfirm={() => remove.mutate()}
      message={
        <>
          <p>¿Dar de baja <strong>{movie.title}</strong>? Dejará de verse en la cartelera y no admitirá funciones nuevas. Podrás reactivarla después.</p>
          {movie.upcomingShowtimes > 0 && (
            <p className="mt-2 rounded-lg bg-amber-50 p-3 text-amber-900">
              Tiene {movie.upcomingShowtimes} {movie.upcomingShowtimes === 1 ? 'función futura' : 'funciones futuras'}: se cancelarán. Si alguna tiene reservas confirmadas, la baja se rechazará.
            </p>
          )}
        </>
      }
    />
  )
}
