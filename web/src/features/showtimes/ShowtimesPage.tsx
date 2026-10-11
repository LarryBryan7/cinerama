import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { cinemas as cinemasApi, movies as moviesApi, rooms as roomsApi, showtimes as api } from '@/api/endpoints'
import type { Showtime } from '@/api/types'
import { Button } from '@/components/ui/Button'
import { Badge, EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Checkbox, Field, Input, Select } from '@/components/ui/Form'
import { ConfirmDialog, Modal } from '@/components/ui/Modal'
import { Pagination } from '@/components/ui/Pagination'
import { Table, Td, Th, Thead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { toApiError } from '@/lib/errors'
import { formatDateTime, formatMoney } from '@/lib/format'
import { cinemaLocalToUtcIso, utcIsoToCinemaLocal } from '@/lib/time'

const PAGE_SIZE = 20

export default function ShowtimesPage() {
  const [movieId, setMovieId] = useState('')
  const [cinemaId, setCinemaId] = useState('')
  const [status, setStatus] = useState('')
  const [upcomingOnly, setUpcomingOnly] = useState(true)
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<Showtime | 'new' | null>(null)
  const [cancelling, setCancelling] = useState<Showtime | null>(null)

  const movies = useQuery({ queryKey: ['movies', 'options'], queryFn: () => moviesApi.list({ pageSize: 100 }) })
  const cinemas = useQuery({ queryKey: ['cinemas'], queryFn: cinemasApi.list })
  const filters = {
    movieId: movieId ? Number(movieId) : undefined, cinemaId: cinemaId ? Number(cinemaId) : undefined, status: status || undefined,
    upcomingOnly: upcomingOnly || undefined, from: from || undefined, to: to || undefined, page, pageSize: PAGE_SIZE,
  }
  const query = useQuery({ queryKey: ['showtimes', filters], queryFn: () => api.list(filters), placeholderData: (previous) => previous })
  const reset = (apply: () => void) => { apply(); setPage(1) }

  return (
    <>
      <PageHeader title="Funciones" subtitle="La programación de las salas. Las horas se muestran en la hora del cine." actions={<Button onClick={() => setEditing('new')}>Programar función</Button>} />

      <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Select aria-label="Película" value={movieId} onChange={(e) => reset(() => setMovieId(e.target.value))}>
          <option value="">Todas las películas</option>
          {movies.data?.items.map((m) => <option key={m.id} value={m.id}>{m.title}</option>)}
        </Select>
        <Select aria-label="Cine" value={cinemaId} onChange={(e) => reset(() => setCinemaId(e.target.value))}>
          <option value="">Todos los cines</option>
          {cinemas.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </Select>
        <Select aria-label="Estado" value={status} onChange={(e) => reset(() => setStatus(e.target.value))}>
          <option value="">Programadas y canceladas</option>
          <option value="scheduled">Solo programadas</option>
          <option value="cancelled">Solo canceladas</option>
        </Select>
        <Field label="Desde">{(p) => <Input {...p} type="date" value={from} onChange={(e) => reset(() => setFrom(e.target.value))} />}</Field>
        <Field label="Hasta">{(p) => <Input {...p} type="date" value={to} onChange={(e) => reset(() => setTo(e.target.value))} />}</Field>
        <Checkbox label="Solo las que no han empezado" checked={upcomingOnly} onChange={(e) => reset(() => setUpcomingOnly(e.target.checked))} />
      </div>

      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.items.length === 0 && <EmptyState title="No hay funciones con esos filtros" hint="Cambia los filtros o programa una función nueva." />}
      {query.data && query.data.items.length > 0 && (
        <>
          <Table caption="Funciones">
            <Thead><Tr><Th>Película</Th><Th>Sala</Th><Th>Inicio</Th><Th>Precio</Th><Th>Vendidas</Th><Th>Estado</Th><Th className="text-right">Acciones</Th></Tr></Thead>
            <tbody>
              {query.data.items.map((s) => {
                const cancellable = s.status === 'Scheduled' && !s.started
                const editable = cancellable && !s.hasBookings
                return (
                  <Tr key={s.id} className={s.status === 'Cancelled' ? 'bg-slate-50 text-slate-500' : ''}>
                    <Td className="font-medium">{s.movieTitle}</Td>
                    <Td>{s.cinemaName} · {s.roomName}</Td>
                    <Td className="whitespace-nowrap">{formatDateTime(s.startsAt)}</Td>
                    <Td>{formatMoney(s.price)}</Td>
                    <Td>{s.seatsSold} / {s.capacity}</Td>
                    <Td>
                      {s.status === 'Cancelled' ? <Badge tone="red">Cancelada</Badge> : s.started ? <Badge tone="gray">Ya empezó</Badge> : <Badge tone="green">Programada</Badge>}
                    </Td>
                    <Td className="text-right">
                      <div className="flex justify-end gap-2">
                        <Button
                          variant="secondary" size="sm" disabled={!editable} onClick={() => setEditing(s)} aria-label={`Editar la función de ${s.movieTitle} del ${formatDateTime(s.startsAt)}`}
                          title={editable ? undefined : s.hasBookings ? 'Tiene reservas: cancélala en lugar de editarla.' : 'Solo se editan funciones programadas que no empezaron.'}
                        >
                          Editar
                        </Button>
                        <Button variant="ghost" size="sm" disabled={!cancellable} onClick={() => setCancelling(s)} aria-label={`Cancelar la función de ${s.movieTitle} del ${formatDateTime(s.startsAt)}`}>Cancelar</Button>
                      </div>
                    </Td>
                  </Tr>
                )
              })}
            </tbody>
          </Table>
          <Pagination page={page} pageSize={PAGE_SIZE} totalCount={query.data.totalCount} onPage={setPage} />
        </>
      )}

      {editing && <ShowtimeForm showtime={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {cancelling && <CancelShowtime showtime={cancelling} onClose={() => setCancelling(null)} />}
    </>
  )
}

const schema = z.object({
  movieId: z.number({ error: 'Elige la película.' }).int().min(1, 'Elige la película.'),
  cinemaId: z.number({ error: 'Elige el cine.' }).int().min(1, 'Elige el cine.'),
  cinemaRoomId: z.number({ error: 'Elige la sala.' }).int().min(1, 'Elige la sala.'),
  startsAtLocal: z.string().regex(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/, 'Elige el día y la hora de inicio.'),
  price: z.number({ error: 'Escribe el precio.' }).min(0, 'El precio no puede ser negativo.').max(99_999_999, 'El precio es demasiado alto.')
    .refine((v) => Math.abs(v * 100 - Math.round(v * 100)) < 1e-6, 'Máximo dos decimales.'),
})
type Values = z.infer<typeof schema>

function ShowtimeForm({ showtime, onClose }: { showtime: Showtime | null; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const movies = useQuery({ queryKey: ['movies', 'active-options'], queryFn: () => moviesApi.list({ isActive: true, pageSize: 100 }) })
  const cinemas = useQuery({ queryKey: ['cinemas'], queryFn: cinemasApi.list })
  const rooms = useQuery({ queryKey: ['rooms', 'all'], queryFn: () => roomsApi.list() })

  const { register, handleSubmit, setError, watch, setValue, formState: { errors } } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      movieId: showtime?.movieId ?? (undefined as unknown as number),
      cinemaId: showtime?.cinemaId ?? (undefined as unknown as number),
      cinemaRoomId: showtime?.cinemaRoomId ?? (undefined as unknown as number),
      startsAtLocal: showtime ? utcIsoToCinemaLocal(showtime.startsAt) : '',
      price: showtime?.price ?? (undefined as unknown as number),
    },
  })
  const cinemaId = watch('cinemaId')
  const roomOptions = (rooms.data ?? []).filter((r) => r.cinemaId === cinemaId && (r.isActive || r.id === showtime?.cinemaRoomId))

  const save = useMutation({
    mutationFn: (v: Values) => {
      // La hora que escribe el administrador es la del cine: se convierte a UTC con la zona del cine, no con la del navegador.
      const input = { movieId: v.movieId, cinemaRoomId: v.cinemaRoomId, startsAt: cinemaLocalToUtcIso(v.startsAtLocal), price: v.price }
      return showtime ? api.update(showtime.id, input) : api.create(input)
    },
    onSuccess: async () => {
      await Promise.all(['showtimes', 'rooms', 'cinemas', 'movies'].map((key) => client.invalidateQueries({ queryKey: [key] })))
      toast.success(showtime ? 'Función actualizada.' : 'Función programada.')
      onClose()
    },
    onError: (error) => {
      const e = toApiError(error)
      const fieldOf: Record<string, keyof Values> = {
        showtime_overlap: 'startsAtLocal', showtime_in_past: 'startsAtLocal', showtime_before_release: 'startsAtLocal',
        movie_inactive: 'movieId', room_inactive: 'cinemaRoomId', room_has_no_seats: 'cinemaRoomId',
      }
      const field = fieldOf[e.code]
      if (field) setError(field, { type: 'server', message: describeCode(e.code) })
      else toast.error(error)
    },
  })

  const loading = movies.isPending || cinemas.isPending || rooms.isPending
  const failed = movies.error ?? cinemas.error ?? rooms.error

  return (
    <Modal
      title={showtime ? 'Editar función' : 'Programar función'} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose} disabled={save.isPending}>Cancelar</Button><Button type="submit" form="showtime-form" loading={save.isPending} disabled={loading || Boolean(failed)}>Guardar</Button></>}
    >
      {loading && <Spinner />}
      {failed && <ErrorState error={failed} />}
      {!loading && !failed && (
        <form id="showtime-form" noValidate onSubmit={handleSubmit((v) => save.mutate(v))} className="grid gap-4 sm:grid-cols-2">
          <Field label="Película" error={errors.movieId?.message} required className="sm:col-span-2">
            {(p) => (
              <Select {...p} data-autofocus {...register('movieId', { valueAsNumber: true })}>
                <option value="">Elige una película…</option>
                {movies.data?.items.map((m) => <option key={m.id} value={m.id}>{m.title}</option>)}
              </Select>
            )}
          </Field>
          <Field label="Cine" error={errors.cinemaId?.message} required>
            {(p) => (
              <Select {...p} {...register('cinemaId', { valueAsNumber: true, onChange: () => setValue('cinemaRoomId', undefined as unknown as number) })}>
                <option value="">Elige un cine…</option>
                {cinemas.data?.filter((c) => c.isActive || c.id === showtime?.cinemaId).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </Select>
            )}
          </Field>
          <Field label="Sala" error={errors.cinemaRoomId?.message} required>
            {(p) => (
              <Select {...p} disabled={!cinemaId} {...register('cinemaRoomId', { valueAsNumber: true })}>
                <option value="">{cinemaId ? 'Elige una sala…' : 'Elige primero el cine'}</option>
                {roomOptions.map((r) => <option key={r.id} value={r.id}>{r.name} ({r.seatCount} asientos)</option>)}
              </Select>
            )}
          </Field>
          <Field label="Inicio (hora del cine)" error={errors.startsAtLocal?.message} required hint="El fin se calcula solo: inicio + duración + limpieza.">
            {(p) => <Input {...p} type="datetime-local" {...register('startsAtLocal')} />}
          </Field>
          <Field label="Precio (S/)" error={errors.price?.message} required>
            {(p) => <Input {...p} type="number" step="0.01" min={0} inputMode="decimal" {...register('price', { valueAsNumber: true })} />}
          </Field>
        </form>
      )}
    </Modal>
  )
}

function describeCode(code: string) {
  return ({
    showtime_overlap: 'La sala ya tiene otra función en ese horario (incluye la limpieza).',
    showtime_in_past: 'La función debe programarse en el futuro.',
    showtime_before_release: 'La película todavía no se estrena en esa fecha.',
    movie_inactive: 'La película está dada de baja.',
    room_inactive: 'La sala o su cine están desactivados.',
    room_has_no_seats: 'La sala no tiene asientos: define su distribución.',
  } as Record<string, string>)[code] ?? 'Revisa este campo.'
}

function CancelShowtime({ showtime, onClose }: { showtime: Showtime; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const cancel = useMutation({
    mutationFn: () => api.cancel(showtime.id),
    onSuccess: async () => {
      await Promise.all(['showtimes', 'bookings', 'movies', 'rooms'].map((key) => client.invalidateQueries({ queryKey: [key] })))
      toast.success('Función cancelada.')
      onClose()
    },
    onError: async (error) => {
      toast.error(error) // por ejemplo showtime_already_started: empezó mientras se decidía
      await client.invalidateQueries({ queryKey: ['showtimes'] })
      onClose()
    },
  })

  // La lista puede llevar horas abierta: mientras tanto un cliente pudo reservar. La advertencia dice cuántas reservas se cancelarán,
  // así que se consulta la función de nuevo al abrir el diálogo y no se puede confirmar hasta tener ese dato.
  const fresh = useQuery({ queryKey: ['showtime', showtime.id], queryFn: () => api.get(showtime.id), staleTime: 0, gcTime: 0 })
  const n = fresh.data?.confirmedBookings ?? 0
  return (
    <ConfirmDialog
      title="Cancelar la función" confirmLabel="Cancelar la función" loading={cancel.isPending} confirmDisabled={!fresh.isSuccess}
      onCancel={onClose} onConfirm={() => cancel.mutate()}
      message={
        <>
          <p>¿Cancelar <strong>{showtime.movieTitle}</strong> del <strong>{formatDateTime(showtime.startsAt)}</strong> en {showtime.cinemaName} · {showtime.roomName}?</p>
          {fresh.isPending && <Spinner label="Comprobando las reservas…" />}
          {fresh.isError && <ErrorState error={fresh.error} onRetry={() => void fresh.refetch()} />}
          {fresh.isSuccess && (n > 0
            ? <p className="mt-2 rounded-lg bg-amber-50 p-3 text-amber-900">Se cancelarán <strong>{n}</strong> {n === 1 ? 'reserva confirmada' : 'reservas confirmadas'} y se liberarán todos los asientos. Los clientes verán su reserva como cancelada.</p>
            : <p className="mt-2">No tiene reservas confirmadas. Se liberarán los asientos que alguien tenga bloqueados.</p>)}
          <p className="mt-2">No se puede deshacer.</p>
        </>
      }
    />
  )
}
