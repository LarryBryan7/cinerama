import { useEffect, useState } from 'react'

/** Devuelve el valor tras `delay` ms sin cambios: así escribir en un buscador no dispara una petición por tecla. */
export function useDebounced<T>(value: T, delay = 350): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(id)
  }, [value, delay])
  return debounced
}
