import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { bookings as api, cinemas as cinemasApi, movies as moviesApi } from '@/api/endpoints'
import type { AdminBooking } from '@/api/types'
import { Button } from '@/components/ui/Button'
import { Badge, EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Field, Input, Select } from '@/components/ui/Form'
import { ConfirmDialog } from '@/components/ui/Modal'
import { Pagination } from '@/components/ui/Pagination'
import { Table, Td, Th, Thead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { formatDateTime, formatMoney } from '@/lib/format'
import { useDebounced } from '../shared/useDebounced'

const PAGE_SIZE = 20

export default function BookingsPage() {
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [movieId, setMovieId] = useState('')
  const [cinemaId, setCinemaId] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [page, setPage] = useState(1)
  const [cancelling, setCancelling] = useState<AdminBooking | null>(null)
  const debounced = useDebounced(search)

  const movies = useQuery({ queryKey: ['movies', 'options'], queryFn: () => moviesApi.list({ pageSize: 100 }) })
  const cinemas = useQuery({ queryKey: ['cinemas'], queryFn: cinemasApi.list })
  const filters = {
    search: debounced.trim() || undefined, status: status || undefined, movieId: movieId ? Number(movieId) : undefined,
    cinemaId: cinemaId ? Number(cinemaId) : undefined, from: from || undefined, to: to || undefined, page, pageSize: PAGE_SIZE,
  }
  const query = useQuery({ queryKey: ['bookings', filters], queryFn: () => api.list(filters), placeholderData: (previous) => previous })
  const reset = (apply: () => void) => { apply(); setPage(1) }

  return (
    <>
      <PageHeader title="Reservas" subtitle="Las reservas de todos los clientes. Puedes cancelar una hasta el inicio de su función." />

      <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Input type="search" aria-label="Buscar reserva" placeholder="Código, cliente o email" value={search} onChange={(e) => reset(() => setSearch(e.target.value))} />
        <Select aria-label="Estado" value={status} onChange={(e) => reset(() => setStatus(e.target.value))}>
          <option value="">Confirmadas y canceladas</option>
          <option value="confirmed">Solo confirmadas</option>
          <option value="cancelled">Solo canceladas</option>
        </Select>
        <Select aria-label="Película" value={movieId} onChange={(e) => reset(() => setMovieId(e.target.value))}>
          <option value="">Todas las películas</option>
          {movies.data?.items.map((m) => <option key={m.id} value={m.id}>{m.title}</option>)}
        </Select>
        <Select aria-label="Cine" value={cinemaId} onChange={(e) => reset(() => setCinemaId(e.target.value))}>
          <option value="">Todos los cines</option>
          {cinemas.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </Select>
        <Field label="Función desde">{(p) => <Input {...p} type="date" value={from} onChange={(e) => reset(() => setFrom(e.target.value))} />}</Field>
        <Field label="Función hasta">{(p) => <Input {...p} type="date" value={to} onChange={(e) => reset(() => setTo(e.target.value))} />}</Field>
      </div>

      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.items.length === 0 && <EmptyState title="No hay reservas con esos filtros" />}
      {query.data && query.data.items.length > 0 && (
        <>
          <Table caption="Reservas">
            <Thead><Tr><Th>Código</Th><Th>Cliente</Th><Th>Función</Th><Th>Asientos</Th><Th>Total</Th><Th>Estado</Th><Th className="text-right">Acciones</Th></Tr></Thead>
            <tbody>
              {query.data.items.map((b) => {
                const canCancel = b.status === 'Confirmed' && new Date(b.startsAt) > new Date()
                return (
                  <Tr key={b.id} className={b.status === 'Cancelled' ? 'bg-slate-50 text-slate-500' : ''}>
                    <Td className="font-mono text-xs">{b.code}</Td>
                    <Td><p className="font-medium">{b.customerName}</p><p className="text-xs text-slate-500">{b.customerEmail}</p></Td>
                    <Td><p className="font-medium">{b.movieTitle}</p><p className="text-xs text-slate-500">{b.cinemaName} · {b.roomName} · {formatDateTime(b.startsAt)}</p></Td>
                    <Td>{b.seats.join(', ')}</Td>
                    <Td className="whitespace-nowrap">{formatMoney(b.totalAmount)}</Td>
                    <Td>{b.status === 'Confirmed' ? <Badge tone="green">Confirmada</Badge> : <Badge tone="red">Cancelada</Badge>}</Td>
                    <Td className="text-right">
                      <Button
                        variant="ghost" size="sm" disabled={!canCancel} onClick={() => setCancelling(b)} aria-label={`Cancelar la reserva ${b.code}`}
                        title={canCancel ? undefined : b.status === 'Cancelled' ? 'Ya está cancelada.' : 'La función ya empezó.'}
                      >
                        Cancelar
                      </Button>
                    </Td>
                  </Tr>
                )
              })}
            </tbody>
          </Table>
          <Pagination page={page} pageSize={PAGE_SIZE} totalCount={query.data.totalCount} onPage={setPage} />
        </>
      )}

      {cancelling && <CancelBooking booking={cancelling} onClose={() => setCancelling(null)} />}
    </>
  )
}

function CancelBooking({ booking, onClose }: { booking: AdminBooking; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const cancel = useMutation({
    mutationFn: () => api.cancel(booking.id),
    onSuccess: async () => {
      await Promise.all(['bookings', 'showtimes'].map((key) => client.invalidateQueries({ queryKey: [key] })))
      toast.success(`La reserva ${booking.code} se canceló y sus asientos quedaron libres.`)
      onClose()
    },
    onError: async (error) => {
      toast.error(error) // por ejemplo booking_already_cancelled o showtime_already_started
      await client.invalidateQueries({ queryKey: ['bookings'] })
      onClose()
    },
  })

  return (
    <ConfirmDialog
      title="Cancelar la reserva" confirmLabel="Cancelar la reserva" loading={cancel.isPending} onCancel={onClose} onConfirm={() => cancel.mutate()}
      message={
        <>
          <p>¿Cancelar la reserva <strong>{booking.code}</strong> de {booking.customerName}?</p>
          <p className="mt-2">{booking.movieTitle} · {formatDateTime(booking.startsAt)} · asientos {booking.seats.join(', ')} ({formatMoney(booking.totalAmount)}).</p>
          <p className="mt-2">Los asientos quedarán libres y el cliente la verá como cancelada. No se puede deshacer.</p>
        </>
      }
    />
  )
}
