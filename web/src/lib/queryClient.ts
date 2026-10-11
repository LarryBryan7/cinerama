import { QueryClient } from '@tanstack/react-query'
import { toApiError } from './errors'

/**
 * Reintentos: nunca ante un 4xx (el servidor ya respondió y repetir no lo cambia); una vez ante un fallo de red o un 5xx. 30 s de caché
 * evitan peticiones al saltar entre pantallas y al volver a la pestaña se actualiza lo que haya quedado viejo.
 */
export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: true, // otras personas (y los clientes) cambian los datos mientras la pestaña está abierta
        retry: (failureCount, error) => {
          const e = toApiError(error)
          return (e.isNetwork || e.isServerError) && failureCount < 1
        },
      },
      mutations: { retry: false },
    },
  })
}
