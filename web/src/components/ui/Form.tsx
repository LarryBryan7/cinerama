import { useId, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes, forwardRef } from 'react'
import { cn } from './cn'

interface FieldProps {
  label: string
  error?: string
  hint?: string
  required?: boolean
  className?: string
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby'?: string }) => ReactNode
}

/** Etiqueta + control + ayuda + error, con los atributos de accesibilidad conectados (el lector de pantalla anuncia el error del campo). */
export function Field({ label, error, hint, required, className, children }: FieldProps) {
  const id = useId()
  const describedBy = [error ? `${id}-error` : null, hint ? `${id}-hint` : null].filter(Boolean).join(' ') || undefined
  return (
    <div className={cn('flex flex-col gap-1', className)}>
      <label htmlFor={id} className="text-sm font-medium text-slate-800">
        {label}
        {required && <span aria-hidden className="text-red-600"> *</span>}
      </label>
      {children({ id, 'aria-invalid': Boolean(error), 'aria-describedby': describedBy })}
      {hint && !error && <p id={`${id}-hint`} className="text-xs text-slate-500">{hint}</p>}
      {error && <p id={`${id}-error`} role="alert" className="text-xs font-medium text-red-700">{error}</p>}
    </div>
  )
}

const control = 'min-h-11 w-full rounded-lg border border-slate-300 bg-white px-3 text-sm text-slate-900 placeholder:text-slate-400 disabled:bg-slate-100 aria-[invalid=true]:border-red-500'

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(function Input({ className, ...rest }, ref) {
  return <input ref={ref} className={cn(control, className)} {...rest} />
})

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(function Select({ className, children, ...rest }, ref) {
  return <select ref={ref} className={cn(control, className)} {...rest}>{children}</select>
})

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(function Textarea({ className, ...rest }, ref) {
  return <textarea ref={ref} className={cn(control, 'min-h-24 py-2', className)} {...rest} />
})

export const Checkbox = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement> & { label: string }>(function Checkbox({ label, className, ...rest }, ref) {
  return (
    <label className={cn('inline-flex min-h-11 cursor-pointer items-center gap-2 text-sm text-slate-800', className)}>
      <input ref={ref} type="checkbox" className="size-4 accent-lime-dark" {...rest} />
      {label}
    </label>
  )
})
