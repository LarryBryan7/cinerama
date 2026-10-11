import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { genres as api } from '@/api/endpoints'
import type { Genre } from '@/api/types'
import { Button } from '@/components/ui/Button'
import { EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Field, Input } from '@/components/ui/Form'
import { ConfirmDialog, Modal } from '@/components/ui/Modal'
import { Table, Td, Th, Thead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { applyServerErrors } from '@/lib/forms'

const schema = z.object({ name: z.string().trim().min(1, 'Escribe el nombre del género.').max(50, 'Máximo 50 caracteres.') })
type Values = z.infer<typeof schema>

export default function GenresPage() {
  const query = useQuery({ queryKey: ['genres'], queryFn: api.list })
  const [editing, setEditing] = useState<Genre | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Genre | null>(null)

  return (
    <>
      <PageHeader title="Géneros" subtitle="Las categorías con las que se clasifican las películas." actions={<Button onClick={() => setEditing('new')}>Nuevo género</Button>} />

      {query.isPending && <Spinner />}
      {query.isError && <ErrorState error={query.error} onRetry={() => void query.refetch()} />}
      {query.data && query.data.length === 0 && <EmptyState title="Todavía no hay géneros" hint="Crea el primero para poder clasificar películas." />}
      {query.data && query.data.length > 0 && (
        <Table caption="Géneros">
          <Thead><Tr><Th>Nombre</Th><Th>Películas</Th><Th className="text-right">Acciones</Th></Tr></Thead>
          <tbody>
            {query.data.map((genre) => (
              <Tr key={genre.id}>
                <Td className="font-medium">{genre.name}</Td>
                <Td>{genre.movieCount}</Td>
                <Td className="text-right">
                  <div className="flex justify-end gap-2">
                    <Button variant="secondary" size="sm" onClick={() => setEditing(genre)} aria-label={`Editar ${genre.name}`}>Editar</Button>
                    <Button
                      variant="ghost" size="sm" onClick={() => setDeleting(genre)} aria-label={`Borrar ${genre.name}`}
                      disabled={genre.movieCount > 0} title={genre.movieCount > 0 ? 'Tiene películas: quítalo de ellas antes de borrarlo.' : undefined}
                    >
                      Borrar
                    </Button>
                  </div>
                </Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}

      {editing && <GenreForm genre={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {deleting && <DeleteGenre genre={deleting} onClose={() => setDeleting(null)} />}
    </>
  )
}

function GenreForm({ genre, onClose }: { genre: Genre | null; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const { register, handleSubmit, setError, formState: { errors } } = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { name: genre?.name ?? '' } })

  const save = useMutation({
    mutationFn: (values: Values) => (genre ? api.update(genre.id, values.name) : api.create(values.name)),
    onSuccess: async () => {
      await Promise.all([client.invalidateQueries({ queryKey: ['genres'] }), client.invalidateQueries({ queryKey: ['movies'] })])
      toast.success(genre ? 'Género actualizado.' : 'Género creado.')
      onClose()
    },
    onError: (error) => {
      // Un nombre repetido (409) se muestra en el propio campo; cualquier otro error, como aviso.
      const e = error as { code?: string }
      if (e.code === 'genre_name_taken') setError('name', { type: 'server', message: 'Ya existe un género con ese nombre.' })
      else if (!applyServerErrors(error, setError, ['name'])) toast.error(error)
    },
  })

  return (
    <Modal
      title={genre ? 'Editar género' : 'Nuevo género'} size="sm" onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose} disabled={save.isPending}>Cancelar</Button><Button type="submit" form="genre-form" loading={save.isPending}>Guardar</Button></>}
    >
      <form id="genre-form" noValidate onSubmit={handleSubmit((values) => save.mutate(values))}>
        <Field label="Nombre" error={errors.name?.message} required>
          {(p) => <Input {...p} data-autofocus autoComplete="off" {...register('name')} />}
        </Field>
      </form>
    </Modal>
  )
}

function DeleteGenre({ genre, onClose }: { genre: Genre; onClose: () => void }) {
  const client = useQueryClient()
  const toast = useToast()
  const remove = useMutation({
    mutationFn: () => api.remove(genre.id),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['genres'] })
      toast.success('Género borrado.')
      onClose()
    },
    onError: async (error) => {
      toast.error(error) // por ejemplo genre_in_use: otra persona le asignó una película mientras tanto
      await client.invalidateQueries({ queryKey: ['genres'] })
      onClose()
    },
  })

  return (
    <ConfirmDialog
      title="Borrar género" confirmLabel="Borrar" loading={remove.isPending} onCancel={onClose} onConfirm={() => remove.mutate()}
      message={<>¿Borrar el género <strong>{genre.name}</strong>? No se puede deshacer.</>}
    />
  )
}
