import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { z } from 'zod'
import { cinemas as cinemasApi, rooms as api } from '@/api/endpoints'
import type { Room } from '@/api/types'
import { Button } from '@/components/ui/Button'
import { Badge, EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Checkbox, Field, Input, Select } from '@/components/ui/Form'
import { Modal } from '@/components/ui/Modal'
import { Table, Td, Th, Thead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { toApiError } from '@/lib/errors'
import { applyServerErrors } from '@/lib/forms'

export default function RoomsPage() {
  const [cinemaId, setCinemaId] = useState<number | undefined>(undefined)
  const cinemas = useQuery({ queryKey: ['cinemas'], queryFn: cinemasApi.list })
  const query = useQuery({ queryKey: ['rooms', cinemaId ?? 'all'], queryFn: () => api.list(cinemaId) })
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<Room | null>(null)

  return (
    <>
      <PageHeader
        title="Salas y asientos" subtitle="Cada sala tiene su distribución de asientos. Un hueco en una fila es un pasillo."
        actions={<Button onClick={() => setCreating(true)} disabled={!cinemas.data || cinemas.data.length === 0}>Nueva sala</Button>}
      />

      <div className="mb-4 max-w-xs">
        <Select aria-label="Cine" value={cinemaId ?? ''} onChange={(e) => setCinemaId(e.target.value ? Number(e.target.value) : undefined)}>
          <option value="">Todos los cines</option>
          {cinemas.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </Select>
      </div>

      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.length === 0 && <EmptyState title="No hay salas" hint={cinemas.data?.length ? 'Crea una sala con "Nueva sala".' : 'Crea primero un cine.'} />}
      {query.data && query.data.length > 0 && (
        <Table caption="Salas">
          <Thead><Tr><Th>Sala</Th><Th>Cine</Th><Th>Asientos</Th><Th>Cuadrícula</Th><Th>Funciones</Th><Th>Estado</Th><Th className="text-right">Acciones</Th></Tr></Thead>
          <tbody>
            {query.data.map((room) => (
              <Tr key={room.id} className={room.isActive ? '' : 'bg-slate-50 text-slate-500'}>
                <Td className="font-medium">{room.name}</Td>
                <Td>{room.cinemaName}</Td>
                <Td>{room.seatCount}</Td>
                <Td>{room.rows} filas × {room.positions} posiciones</Td>
                <Td>{room.upcomingShowtimes}</Td>
                <Td>{room.isActive ? <Badge tone="green">Activa</Badge> : <Badge tone="red">Desactivada</Badge>}</Td>
                <Td className="text-right">
                  <div className="flex justify-end gap-2">
                    <Link to={`/rooms/${room.id}/layout`} aria-label={`Distribución de ${room.name} (${room.cinemaName})`} className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 text-sm hover:bg-slate-50">Distribución</Link>
                    <Button variant="ghost" size="sm" onClick={() => setEditing(room)} aria-label={`Editar ${room.name} (${room.cinemaName})`}>Editar</Button>
                  </div>
                </Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}

      {creating && cinemas.data && <CreateRoom cinemas={cinemas.data.map((c) => ({ id: c.id, name: c.name }))} defaultCinemaId={cinemaId} onClose={() => setCreating(false)} />}
      {editing && <EditRoom room={editing} onClose={() => setEditing(null)} />}
    </>
  )
}

const createSchema = z.object({
  cinemaId: z.number({ error: 'Elige el cine.' }).int().min(1, 'Elige el cine.'),
  name: z.string().trim().min(1, 'Escribe el nombre de la sala.').max(50, 'Máximo 50 caracteres.'),
  rows: z.number({ error: 'Escribe las filas.' }).int('Debe ser un número entero.').min(1, 'Mínimo 1 fila.').max(26, 'Máximo 26 filas (A–Z).'),
  positions: z.number({ error: 'Escribe las posiciones.' }).int('Debe ser un número entero.').min(1, 'Mínimo 1 posición.').max(40, 'Máximo 40 posiciones por fila.'),
  aisle: z.string().refine((v) => v === '' || (/^\d+$/.test(v) && Number(v) >= 1), 'Debe ser un número de posición, por ejemplo 7.'),
})
type CreateValues = z.infer<typeof createSchema>

function CreateRoom({ cinemas, defaultCinemaId, onClose }: { cinemas: { id: number; name: string }[]; defaultCinemaId?: number; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const { register, handleSubmit, setError, formState: { errors } } = useForm<CreateValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { cinemaId: defaultCinemaId ?? cinemas[0]?.id ?? 0, name: '', rows: 8, positions: 12, aisle: '' },
  })

  const create = useMutation({
    mutationFn: (v: CreateValues) => {
      // Un pasillo central: la misma posición vacía en todas las filas.
      const aisle = v.aisle === '' ? null : Number(v.aisle)
      const emptyCells = aisle !== null && aisle <= v.positions ? Array.from({ length: v.rows }, (_, r) => `${String.fromCharCode(65 + r)}${aisle}`) : []
      return api.create({ cinemaId: v.cinemaId, name: v.name.trim(), layout: { rows: v.rows, positions: v.positions, emptyCells } })
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['rooms'] })
      await client.invalidateQueries({ queryKey: ['cinemas'] })
      toast.success('Sala creada. Puedes ajustar su distribución en «Distribución».')
      onClose()
    },
    onError: (error) => {
      const e = toApiError(error)
      if (e.code === 'room_name_taken') setError('name', { type: 'server', message: 'Ya hay una sala con ese nombre en este cine.' })
      else if (!applyServerErrors(error, setError, ['name'])) toast.error(error)
    },
  })

  return (
    <Modal
      title="Nueva sala" onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose} disabled={create.isPending}>Cancelar</Button><Button type="submit" form="room-create-form" loading={create.isPending}>Crear sala</Button></>}
    >
      <form id="room-create-form" noValidate onSubmit={handleSubmit((v) => create.mutate(v))} className="grid gap-4 sm:grid-cols-2">
        <Field label="Cine" error={errors.cinemaId?.message} required className="sm:col-span-2">
          {(p) => <Select {...p} {...register('cinemaId', { valueAsNumber: true })}>{cinemas.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</Select>}
        </Field>
        <Field label="Nombre" error={errors.name?.message} required className="sm:col-span-2">{(p) => <Input {...p} data-autofocus placeholder="Sala 1" {...register('name')} />}</Field>
        <Field label="Filas" error={errors.rows?.message} required hint="De 1 a 26 (A–Z).">{(p) => <Input {...p} type="number" inputMode="numeric" {...register('rows', { valueAsNumber: true })} />}</Field>
        <Field label="Posiciones por fila" error={errors.positions?.message} required hint="De 1 a 40.">{(p) => <Input {...p} type="number" inputMode="numeric" {...register('positions', { valueAsNumber: true })} />}</Field>
        <Field label="Pasillo en la posición" error={errors.aisle?.message} hint="Opcional. Deja esa posición sin asiento en todas las filas." className="sm:col-span-2">
          {(p) => <Input {...p} inputMode="numeric" placeholder="7" {...register('aisle')} />}
        </Field>
      </form>
    </Modal>
  )
}

const editSchema = z.object({ name: z.string().trim().min(1, 'Escribe el nombre de la sala.').max(50, 'Máximo 50 caracteres.'), isActive: z.boolean() })
type EditValues = z.infer<typeof editSchema>

function EditRoom({ room, onClose }: { room: Room; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<EditValues>({ resolver: zodResolver(editSchema), defaultValues: { name: room.name, isActive: room.isActive } })

  const save = useMutation({
    mutationFn: (v: EditValues) => api.update(room.id, { name: v.name.trim(), isActive: v.isActive }),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['rooms'] })
      toast.success('Sala actualizada.')
      onClose()
    },
    onError: (error) => {
      const e = toApiError(error)
      if (e.code === 'room_name_taken') setError('name', { type: 'server', message: 'Ya hay una sala con ese nombre en este cine.' })
      else toast.error(error) // room_has_future_showtimes al desactivar
    },
  })

  return (
    <Modal
      title={`Editar ${room.name}`} size="sm" onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose} disabled={save.isPending}>Cancelar</Button><Button type="submit" form="room-edit-form" loading={save.isPending}>Guardar</Button></>}
    >
      <form id="room-edit-form" noValidate onSubmit={handleSubmit((v) => save.mutate(v))} className="flex flex-col gap-4">
        <Field label="Nombre" error={errors.name?.message} required>{(p) => <Input {...p} data-autofocus {...register('name')} />}</Field>
        <Controller control={control} name="isActive" render={({ field }) => <Checkbox label="Activa (admite funciones)" checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />} />
        {room.isActive && room.upcomingShowtimes > 0 && <p className="text-xs text-slate-500">Tiene {room.upcomingShowtimes} funciones futuras: no se puede desactivar hasta cancelarlas.</p>}
      </form>
    </Modal>
  )
}
