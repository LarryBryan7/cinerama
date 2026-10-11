import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { cinemas as api } from '@/api/endpoints'
import type { Cinema, CinemaInput } from '@/api/types'
import { Button } from '@/components/ui/Button'
import { Badge, EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Checkbox, Field, Input } from '@/components/ui/Form'
import { Modal } from '@/components/ui/Modal'
import { Table, Td, Th, Thead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { applyServerErrors, blankToNull } from '@/lib/forms'
import { toApiError } from '@/lib/errors'
import { IMAGE_EXTENSIONS, isValidMediaPath } from '@/lib/validation'

const schema = z.object({
  name: z.string().trim().min(1, 'Escribe el nombre del cine.').max(100, 'Máximo 100 caracteres.'),
  address: z.string().trim().min(1, 'Escribe la dirección.').max(200, 'Máximo 200 caracteres.'),
  city: z.string().trim().min(1, 'Escribe la ciudad.').max(100, 'Máximo 100 caracteres.'),
  region: z.string().trim().min(1, 'Escribe la región.').max(100, 'Máximo 100 caracteres.'),
  latitude: z.number({ error: 'Escribe la latitud.' }).min(-90, 'La latitud va de -90 a 90.').max(90, 'La latitud va de -90 a 90.'),
  longitude: z.number({ error: 'Escribe la longitud.' }).min(-180, 'La longitud va de -180 a 180.').max(180, 'La longitud va de -180 a 180.'),
  logoUrl: z.string().refine((v) => isValidMediaPath(v, IMAGE_EXTENSIONS), 'Usa una ruta como cinemas/mi-cine.png o una URL https de una imagen.'),
  isActive: z.boolean(),
})
type Values = z.infer<typeof schema>
const FIELDS = ['name', 'address', 'city', 'region', 'latitude', 'longitude', 'logoUrl'] as const

export default function CinemasPage() {
  const query = useQuery({ queryKey: ['cinemas'], queryFn: api.list })
  const [editing, setEditing] = useState<Cinema | 'new' | null>(null)

  return (
    <>
      <PageHeader title="Cines" subtitle="Las sedes. Cada sala pertenece a un cine, y la ciudad sale de los cines activos." actions={<Button onClick={() => setEditing('new')}>Nuevo cine</Button>} />

      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.length === 0 && <EmptyState title="Todavía no hay cines" hint="Crea uno para poder agregarle salas." />}
      {query.data && query.data.length > 0 && (
        <Table caption="Cines">
          <Thead><Tr><Th>Cine</Th><Th>Ciudad</Th><Th>Ubicación</Th><Th>Salas</Th><Th>Funciones</Th><Th>Estado</Th><Th className="text-right">Acciones</Th></Tr></Thead>
          <tbody>
            {query.data.map((c) => (
              <Tr key={c.id} className={c.isActive ? '' : 'bg-slate-50 text-slate-500'}>
                <Td><p className="font-medium">{c.name}</p><p className="text-xs text-slate-500">{c.address}</p></Td>
                <Td>{c.city}, {c.region}</Td>
                <Td className="text-xs tabular-nums">{c.latitude.toFixed(6)}, {c.longitude.toFixed(6)}</Td>
                <Td>{c.roomCount}</Td>
                <Td>{c.upcomingShowtimes}</Td>
                <Td>{c.isActive ? <Badge tone="green">Activo</Badge> : <Badge tone="red">Desactivado</Badge>}</Td>
                <Td className="text-right"><Button variant="secondary" size="sm" onClick={() => setEditing(c)} aria-label={`Editar ${c.name}`}>Editar</Button></Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}

      {editing && <CinemaForm cinema={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  )
}

function CinemaForm({ cinema, onClose }: { cinema: Cinema | null; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: cinema?.name ?? '', address: cinema?.address ?? '', city: cinema?.city ?? '', region: cinema?.region ?? '',
      latitude: cinema?.latitude ?? (undefined as unknown as number), longitude: cinema?.longitude ?? (undefined as unknown as number),
      logoUrl: cinema?.logoUrl ?? '', isActive: cinema?.isActive ?? true,
    },
  })

  const save = useMutation({
    mutationFn: (v: Values) => {
      const input: CinemaInput = { ...v, name: v.name.trim(), address: v.address.trim(), city: v.city.trim(), region: v.region.trim(), logoUrl: blankToNull(v.logoUrl) }
      return cinema ? api.update(cinema.id, input) : api.create(input)
    },
    onSuccess: async () => {
      await Promise.all(['cinemas', 'rooms'].map((key) => client.invalidateQueries({ queryKey: [key] })))
      toast.success(cinema ? 'Cine actualizado.' : 'Cine creado.')
      onClose()
    },
    onError: (error) => {
      const e = toApiError(error)
      if (e.code === 'cinema_name_taken') setError('name', { type: 'server', message: 'Ya existe un cine con ese nombre.' })
      else if (!applyServerErrors(error, setError, FIELDS)) toast.error(error)
    },
  })

  return (
    <Modal
      title={cinema ? `Editar ${cinema.name}` : 'Nuevo cine'} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose} disabled={save.isPending}>Cancelar</Button><Button type="submit" form="cinema-form" loading={save.isPending}>Guardar</Button></>}
    >
      <form id="cinema-form" noValidate onSubmit={handleSubmit((v) => save.mutate(v))} className="grid gap-4 sm:grid-cols-2">
        <Field label="Nombre" error={errors.name?.message} required className="sm:col-span-2">{(p) => <Input {...p} data-autofocus {...register('name')} />}</Field>
        <Field label="Dirección" error={errors.address?.message} required className="sm:col-span-2">{(p) => <Input {...p} {...register('address')} />}</Field>
        <Field label="Ciudad" error={errors.city?.message} required>{(p) => <Input {...p} {...register('city')} />}</Field>
        <Field label="Región" error={errors.region?.message} required>{(p) => <Input {...p} {...register('region')} />}</Field>
        <Field label="Latitud" error={errors.latitude?.message} required hint="De -90 a 90. Se usa para calcular la distancia desde el teléfono.">
          {(p) => <Input {...p} type="number" step="any" inputMode="decimal" {...register('latitude', { valueAsNumber: true })} />}
        </Field>
        <Field label="Longitud" error={errors.longitude?.message} required hint="De -180 a 180.">
          {(p) => <Input {...p} type="number" step="any" inputMode="decimal" {...register('longitude', { valueAsNumber: true })} />}
        </Field>
        <Field label="Logo" error={errors.logoUrl?.message} hint="Opcional. Ruta de la carpeta de medios o URL https." className="sm:col-span-2">
          {(p) => <Input {...p} placeholder="cinemas/mi-cine.png" {...register('logoUrl')} />}
        </Field>
        <Controller control={control} name="isActive" render={({ field }) => (
          <Checkbox label="Activo (vende funciones)" checked={field.value} onChange={(e) => field.onChange(e.target.checked)} className="sm:col-span-2" />
        )} />
        {cinema && cinema.isActive && <p className="text-xs text-slate-500 sm:col-span-2">Al desactivarlo se cancelan sus funciones futuras. Si alguna tiene reservas confirmadas, se rechaza.</p>}
      </form>
    </Modal>
  )
}
