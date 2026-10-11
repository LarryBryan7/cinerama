import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { toApiError } from './errors'

/**
 * Muestra en cada campo el error de validación que devolvió el servidor (400 validation_failed). Devuelve `true` si al menos uno
 * coincidió con un campo del formulario; si no, quien llama muestra el mensaje general (aviso).
 */
export function applyServerErrors<T extends FieldValues>(error: unknown, setError: UseFormSetError<T>, fields: readonly string[]): boolean {
  const e = toApiError(error)
  let applied = false
  for (const [name, messages] of Object.entries(e.fieldErrors)) {
    const field = fields.find((f) => f.toLowerCase() === name.toLowerCase())
    if (field && messages[0]) {
      setError(field as Path<T>, { type: 'server', message: messages[0] })
      applied = true
    }
  }
  return applied
}

/** "" → null: los campos opcionales de texto se envían como null y no como cadena vacía. */
export const blankToNull = (value: string | null | undefined) => (value && value.trim() !== '' ? value.trim() : null)
