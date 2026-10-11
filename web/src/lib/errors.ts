import { AxiosError } from 'axios'

/**
 * Error de la API ya interpretado. Las pantallas deciden por `code` (estable), nunca por el texto del mensaje (ADR-007).
 * `fieldErrors` trae los errores de validación por campo (`400 validation_failed`) con las claves en camelCase.
 */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly fieldErrors: Record<string, string[]>
  readonly extensions: Record<string, unknown>

  constructor(init: { status: number; code: string; message: string; fieldErrors?: Record<string, string[]>; extensions?: Record<string, unknown> }) {
    super(init.message)
    this.name = 'ApiError'
    this.status = init.status
    this.code = init.code
    this.fieldErrors = init.fieldErrors ?? {}
    this.extensions = init.extensions ?? {}
  }

  /** Sin respuesta del servidor: sin red, tiempo agotado o servidor caído. */
  get isNetwork() {
    return this.status === 0
  }

  get isServerError() {
    return this.status >= 500
  }
}

/** Convierte lo que lance Axios en un ApiError. Una respuesta application/problem+json trae `code`, `detail` y `errors`. */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error

  if (error instanceof AxiosError) {
    const response = error.response
    if (!response) {
      return new ApiError({ status: 0, code: 'network_error', message: 'No hay conexión con el servidor. Revisa tu conexión e inténtalo de nuevo.' })
    }

    const data = (response.data ?? {}) as Record<string, unknown>
    const errors = data.errors && typeof data.errors === 'object' ? (data.errors as Record<string, string[]>) : {}
    const { type: _type, title, detail, status: _status, errors: _errors, traceId: _traceId, ...extensions } = data
    return new ApiError({
      status: response.status,
      code: typeof data.code === 'string' ? data.code : `http_${response.status}`,
      message: (typeof detail === 'string' && detail) || (typeof title === 'string' && title) || 'Ocurrió un error.',
      fieldErrors: normalizeFieldErrors(errors),
      extensions,
    })
  }

  return new ApiError({ status: -1, code: 'unexpected', message: error instanceof Error ? error.message : 'Ocurrió un error inesperado.' })
}

function normalizeFieldErrors(errors: Record<string, string[]>): Record<string, string[]> {
  const result: Record<string, string[]> = {}
  for (const [key, value] of Object.entries(errors)) {
    // "Layout.EmptyCells" y "layout.emptyCells" son el mismo campo: se deja siempre la primera letra en minúscula.
    const normalized = key.split('.').map((part) => (part ? part[0]!.toLowerCase() + part.slice(1) : part)).join('.')
    result[normalized] = Array.isArray(value) ? value : [String(value)]
  }
  return result
}

/** Mensaje para la persona, decidido por el `code` de negocio (docs/api.md, «Códigos de negocio»). */
const MESSAGES: Record<string, string> = {
  network_error: 'No hay conexión con el servidor. Revisa tu conexión e inténtalo de nuevo.',
  unauthorized: 'Tu sesión terminó. Inicia sesión de nuevo.',
  token_expired: 'Tu sesión terminó. Inicia sesión de nuevo.',
  invalid_token: 'Tu sesión no es válida. Inicia sesión de nuevo.',
  forbidden: 'No tienes permiso para hacer esto.',
  rate_limited: 'Demasiados intentos seguidos. Espera un momento e inténtalo de nuevo.',
  invalid_credentials: 'El email o la contraseña no son correctos.',
  genre_name_taken: 'Ya existe un género con ese nombre.',
  genre_in_use: 'El género tiene películas: quítalo de ellas antes de borrarlo.',
  genre_not_found: 'El género ya no existe.',
  movie_not_found: 'La película ya no existe.',
  movie_has_active_bookings: 'La película tiene funciones futuras con reservas confirmadas. Cancela esas funciones antes de darla de baja.',
  movie_inactive: 'La película está dada de baja y no admite funciones nuevas.',
  cinema_not_found: 'El cine ya no existe.',
  cinema_name_taken: 'Ya existe un cine con ese nombre.',
  cinema_has_active_bookings: 'El cine tiene funciones futuras con reservas confirmadas. Cancela esas funciones antes de desactivarlo.',
  room_not_found: 'La sala ya no existe.',
  room_name_taken: 'Ya hay una sala con ese nombre en este cine.',
  room_has_future_showtimes: 'La sala tiene funciones futuras. Cancélalas antes de cambiar su distribución o desactivarla.',
  room_inactive: 'La sala o su cine están desactivados.',
  room_has_no_seats: 'La sala no tiene asientos. Define primero su distribución.',
  showtime_not_found: 'La función ya no existe.',
  showtime_overlap: 'La sala ya tiene otra función en ese horario (incluye el tiempo de limpieza).',
  showtime_in_past: 'La función debe programarse en el futuro.',
  showtime_before_release: 'La película todavía no se estrena en esa fecha: no puede haber funciones antes.',
  showtime_has_bookings: 'La función ya tiene reservas: cancélala en lugar de editarla.',
  showtime_already_started: 'La función ya empezó.',
  showtime_cancelled: 'La función ya está cancelada.',
  booking_not_found: 'La reserva ya no existe.',
  booking_already_cancelled: 'La reserva ya estaba cancelada.',
  cannot_deactivate_self: 'No puedes desactivar tu propia cuenta.',
  user_not_found: 'El usuario ya no existe.',
  concurrency_conflict: 'Otra persona está haciendo un cambio en lo mismo. Vuelve a intentarlo.',
  validation_failed: 'Revisa los campos marcados.',
}

export function describeError(error: unknown): string {
  const e = toApiError(error)
  if (e.isServerError) return 'El servidor tuvo un problema. Inténtalo de nuevo en un momento.'
  return MESSAGES[e.code] ?? e.message
}
