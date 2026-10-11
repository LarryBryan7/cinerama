import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { Button } from '@/components/ui/Button'
import { Field, Input } from '@/components/ui/Form'
import { Spinner } from '@/components/ui/Feedback'
import { toApiError } from '@/lib/errors'
import { useAuth } from './AuthContext'

const schema = z.object({
  email: z.string().trim().min(1, 'Escribe tu email.').pipe(z.email('El email no es válido.')),
  password: z.string().min(1, 'Escribe tu contraseña.'),
})
type Values = z.infer<typeof schema>

export function LoginPage() {
  const { status, login, sessionLost } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register, handleSubmit, formState: { errors, isSubmitting },
  } = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { email: '', password: '' } })

  if (status === 'loading') return <Spinner label="Comprobando la sesión…" />
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  if (status === 'authenticated') return <Navigate to={from} replace />

  const onSubmit = handleSubmit(async (values) => {
    setFormError(null)
    try {
      await login(values.email, values.password)
      navigate(from, { replace: true })
    } catch (error) {
      const e = toApiError(error)
      // Un 401 en el login es "credenciales incorrectas". El mensaje es el mismo para un email que no existe y para una contraseña
      // errónea: no se revela cuál de los dos falló.
      setFormError(
        e.code === 'not_admin' ? e.message
        : e.status === 401 ? 'El email o la contraseña no son correctos.'
        : e.status === 429 ? 'Demasiados intentos seguidos. Espera un momento e inténtalo de nuevo.'
        : e.isNetwork ? 'No hay conexión con el servidor. Revisa tu conexión e inténtalo de nuevo.'
        : 'No se pudo iniciar sesión. Inténtalo de nuevo.',
      )
    }
  })

  return (
    <main className="flex min-h-full items-center justify-center bg-panel p-4">
      <form onSubmit={onSubmit} noValidate className="w-full max-w-sm rounded-2xl bg-white p-6 shadow-xl" aria-labelledby="login-title">
        <h1 id="login-title" className="mb-1 text-2xl font-bold text-slate-900">Cinerama</h1>
        <p className="mb-5 text-sm text-slate-600">Panel de administración</p>

        {sessionLost && !formError && (
          <p role="status" className="mb-4 rounded-lg bg-amber-50 p-3 text-sm text-amber-900">Tu sesión terminó. Inicia sesión de nuevo.</p>
        )}
        {formError && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm font-medium text-red-800">{formError}</p>}

        <div className="flex flex-col gap-4">
          <Field label="Email" error={errors.email?.message} required>
            {(p) => <Input {...p} type="email" autoComplete="username" autoFocus {...register('email')} />}
          </Field>
          <Field label="Contraseña" error={errors.password?.message} required>
            {(p) => <Input {...p} type="password" autoComplete="current-password" {...register('password')} />}
          </Field>
          <Button type="submit" loading={isSubmitting}>Entrar</Button>
        </div>
      </form>
    </main>
  )
}
