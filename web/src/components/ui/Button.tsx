import type { ButtonHTMLAttributes } from 'react'
import { cn } from './cn'

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost'

const styles: Record<Variant, string> = {
  primary: 'bg-lime text-ink hover:bg-lime-dark font-semibold',
  secondary: 'bg-white text-slate-800 border border-slate-300 hover:bg-slate-50',
  danger: 'bg-red-600 text-white hover:bg-red-700 font-semibold',
  ghost: 'text-slate-700 hover:bg-slate-200/60',
}

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  /** Mientras es verdadero el botón se desactiva y avisa que está trabajando (evita enviar dos veces). */
  loading?: boolean
  size?: 'sm' | 'md'
}

export function Button({ variant = 'primary', loading = false, size = 'md', className, disabled, children, type = 'button', ...rest }: Props) {
  return (
    <button
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cn(
        'inline-flex items-center justify-center gap-2 rounded-lg transition-colors disabled:cursor-not-allowed disabled:opacity-50',
        size === 'sm' ? 'min-h-9 px-3 text-sm' : 'min-h-11 px-4 text-sm', // 44 px de alto: cómodo con el dedo y con el mouse
        styles[variant],
        className,
      )}
      {...rest}
    >
      {loading && <span aria-hidden className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent" />}
      {children}
    </button>
  )
}
