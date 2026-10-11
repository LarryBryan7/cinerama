import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Controller, useForm } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { z } from 'zod'
import { genres as genresApi, movies as api } from '@/api/endpoints'
import type { Movie, MovieInput } from '@/api/types'
import { Button } from '@/components/ui/Button'
import { ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback'
import { Checkbox, Field, Input, Textarea } from '@/components/ui/Form'
import { useToast } from '@/components/ui/Toast'
import { applyServerErrors, blankToNull } from '@/lib/forms'
import { toApiError } from '@/lib/errors'
import { IMAGE_EXTENSIONS, ISO_DATE, VIDEO_EXTENSIONS, isValidMediaPath, parseYouTubeId } from '@/lib/validation'

const schema = z
  .object({
    title: z.string().trim().min(1, 'Escribe el título.').max(200, 'Máximo 200 caracteres.'),
    synopsis: z.string().max(2000, 'Máximo 2000 caracteres.'),
    durationMinutes: z.number({ error: 'Escribe la duración en minutos.' }).int('Debe ser un número entero.').min(1, 'Mínimo 1 minuto.').max(400, 'Máximo 400 minutos.'),
    classification: z.string().trim().min(1, 'Escribe la clasificación (por ejemplo +14).').max(20, 'Máximo 20 caracteres.'),
    genreIds: z.array(z.number()).min(1, 'Elige al menos un género.'),
    posterUrl: z.string().refine((v) => isValidMediaPath(v, IMAGE_EXTENSIONS), 'Usa una ruta como posters/mi-pelicula.jpg o una URL https de una imagen .jpg, .png o .webp.'),
    trailerUrl: z.string().refine((v) => v.trim() === '' || parseYouTubeId(v) !== null, 'Debe ser un enlace de YouTube válido.'),
    previewVideoUrl: z.string().refine((v) => isValidMediaPath(v, VIDEO_EXTENSIONS), 'Usa una ruta como posters/mi-pelicula.mp4 o una URL https de un archivo .mp4.'),
    releaseDate: z.string().regex(ISO_DATE, 'Elige la fecha de estreno.'),
    preSaleStartsOn: z.string().refine((v) => v === '' || ISO_DATE.test(v), 'La fecha no es válida.'),
    isActive: z.boolean(),
  })
  .refine((v) => v.preSaleStartsOn === '' || !ISO_DATE.test(v.releaseDate) || v.preSaleStartsOn <= v.releaseDate, {
    path: ['preSaleStartsOn'], message: 'La preventa no puede abrir después del estreno.',
  })
type Values = z.infer<typeof schema>

const FIELDS = ['title', 'synopsis', 'durationMinutes', 'classification', 'genreIds', 'posterUrl', 'trailerUrl', 'previewVideoUrl', 'releaseDate', 'preSaleStartsOn'] as const

const toValues = (m: Movie | undefined): Values => ({
  title: m?.title ?? '', synopsis: m?.synopsis ?? '', durationMinutes: m?.durationMinutes ?? (undefined as unknown as number), classification: m?.classification ?? '',
  genreIds: m?.genreIds ?? [], posterUrl: m?.posterUrl ?? '', trailerUrl: m?.trailerUrl ?? '', previewVideoUrl: m?.previewVideoUrl ?? '',
  releaseDate: m?.releaseDate ?? '', preSaleStartsOn: m?.preSaleStartsOn ?? '', isActive: m?.isActive ?? true,
})

const toInput = (v: Values): MovieInput => ({
  title: v.title.trim(), synopsis: blankToNull(v.synopsis), durationMinutes: v.durationMinutes, classification: v.classification.trim(), genreIds: v.genreIds,
  posterUrl: blankToNull(v.posterUrl), trailerUrl: blankToNull(v.trailerUrl), previewVideoUrl: blankToNull(v.previewVideoUrl),
  releaseDate: v.releaseDate, preSaleStartsOn: blankToNull(v.preSaleStartsOn), isActive: v.isActive,
})

export default function MovieFormPage() {
  const { id } = useParams()
  const movieId = id ? Number(id) : null
  const genres = useQuery({ queryKey: ['genres'], queryFn: genresApi.list })
  const movie = useQuery({ queryKey: ['movie', movieId], queryFn: () => api.get(movieId!), enabled: movieId !== null })

  if (genres.isPending || (movieId !== null && movie.isPending)) return <Spinner />
  const failed = genres.error ?? movie.error
  if (failed) return <ErrorState error={failed} onRetry={() => { void genres.refetch(); void movie.refetch() }} />
  return <MovieForm movie={movie.data} genres={genres.data ?? []} />
}

function MovieForm({ movie, genres }: { movie: Movie | undefined; genres: { id: number; name: string }[] }) {
  const navigate = useNavigate()
  const client = useQueryClient()
  const toast = useToast()
  const {
    register, control, handleSubmit, setError, watch, formState: { errors },
  } = useForm<Values>({ resolver: zodResolver(schema), defaultValues: toValues(movie) })

  const poster = watch('posterUrl')
  const previewUrl = /^https:\/\//.test(poster.trim()) ? poster.trim() : movie?.posterPreviewUrl && poster.trim() === (movie.posterUrl ?? '') ? movie.posterPreviewUrl : null

  const save = useMutation({
    mutationFn: (values: Values) => (movie ? api.update(movie.id, toInput(values)) : api.create(toInput(values))),
    onSuccess: async () => {
      await Promise.all([client.invalidateQueries({ queryKey: ['movies'] }), client.invalidateQueries({ queryKey: ['movie'] }), client.invalidateQueries({ queryKey: ['genres'] })])
      toast.success(movie ? 'Película actualizada.' : 'Película creada.')
      navigate('/movies')
    },
    onError: (error) => {
      if (!applyServerErrors(error, setError, FIELDS)) toast.error(error)
      else if (toApiError(error).status !== 400) toast.error(error)
    },
  })

  return (
    <>
      <PageHeader title={movie ? `Editar: ${movie.title}` : 'Nueva película'} subtitle="Los campos con * son obligatorios." />
      <form noValidate onSubmit={handleSubmit((values) => save.mutate(values))} className="grid max-w-4xl gap-5 rounded-xl border border-slate-200 bg-white p-5 lg:grid-cols-2">
        <Field label="Título" error={errors.title?.message} required className="lg:col-span-2">{(p) => <Input {...p} {...register('title')} />}</Field>

        <Field label="Duración (minutos)" error={errors.durationMinutes?.message} required>
          {(p) => <Input {...p} type="number" inputMode="numeric" min={1} max={400} {...register('durationMinutes', { valueAsNumber: true })} />}
        </Field>
        <Field label="Clasificación" error={errors.classification?.message} required hint="Por ejemplo: +14, +18, Todo espectador.">{(p) => <Input {...p} {...register('classification')} />}</Field>

        <Field label="Sinopsis" error={errors.synopsis?.message} className="lg:col-span-2">{(p) => <Textarea {...p} {...register('synopsis')} />}</Field>

        <fieldset className="lg:col-span-2" aria-describedby="genres-error">
          <legend className="mb-1 text-sm font-medium text-slate-800">Géneros <span aria-hidden className="text-red-600">*</span></legend>
          {genres.length === 0 && <p className="text-sm text-slate-600">No hay géneros todavía. <Link to="/genres" className="underline">Crea uno primero</Link>.</p>}
          <Controller
            control={control}
            name="genreIds"
            render={({ field }) => (
              <div className="flex flex-wrap gap-x-4">
                {genres.map((g) => (
                  <Checkbox
                    key={g.id} label={g.name} checked={field.value.includes(g.id)}
                    onChange={(e) => field.onChange(e.target.checked ? [...field.value, g.id] : field.value.filter((x) => x !== g.id))}
                  />
                ))}
              </div>
            )}
          />
          {errors.genreIds && <p id="genres-error" role="alert" className="text-xs font-medium text-red-700">{errors.genreIds.message}</p>}
        </fieldset>

        <Field label="Fecha de estreno" error={errors.releaseDate?.message} required>{(p) => <Input {...p} type="date" {...register('releaseDate')} />}</Field>
        <Field label="Abre la preventa" error={errors.preSaleStartsOn?.message} hint="Opcional. Debe ser el día del estreno o antes.">{(p) => <Input {...p} type="date" {...register('preSaleStartsOn')} />}</Field>

        <Field label="Póster" error={errors.posterUrl?.message} hint="Ruta dentro de la carpeta de medios de la API o URL https." className="lg:col-span-2">
          {(p) => <Input {...p} placeholder="posters/mi-pelicula.jpg" {...register('posterUrl')} />}
        </Field>
        {previewUrl && <img src={previewUrl} alt="Vista previa del póster" className="h-40 w-28 rounded-lg border border-slate-200 object-cover" />}

        <Field label="Tráiler (YouTube)" error={errors.trailerUrl?.message} hint="Enlace del video. Se guarda limpio, sin parámetros como &t=30s." className="lg:col-span-2">
          {(p) => <Input {...p} placeholder="https://www.youtube.com/watch?v=…" {...register('trailerUrl')} />}
        </Field>
        <Field label="Video corto de la tarjeta" error={errors.previewVideoUrl?.message} hint="Opcional. Un archivo .mp4 sin sonido." className="lg:col-span-2">
          {(p) => <Input {...p} placeholder="posters/mi-pelicula.mp4" {...register('previewVideoUrl')} />}
        </Field>

        <Controller control={control} name="isActive" render={({ field }) => <Checkbox label="Activa (visible para los clientes)" checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />} />

        <div className="flex flex-wrap gap-2 lg:col-span-2">
          <Button type="submit" loading={save.isPending}>{movie ? 'Guardar cambios' : 'Crear película'}</Button>
          <Link to="/movies" className="inline-flex min-h-11 items-center rounded-lg border border-slate-300 bg-white px-4 text-sm hover:bg-slate-50">Cancelar</Link>
        </div>
      </form>
    </>
  )
}
