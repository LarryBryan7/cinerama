// Validaciones del lado del cliente: dan respuesta inmediata, pero NO reemplazan las del servidor (que es la autoridad).
// Reflejan las reglas de la API: RN-09, RN-48, RN-51. Si el servidor rechaza algo que aquí pasó, el error aparece igual en el campo.

const RELATIVE = /^[A-Za-z0-9][A-Za-z0-9_\-/]*\.[A-Za-z0-9]+$/

export const IMAGE_EXTENSIONS = ['jpg', 'jpeg', 'png', 'webp']
export const VIDEO_EXTENSIONS = ['mp4']

/** Una ruta relativa a la carpeta de medios de la API (posters/mi-pelicula.jpg) o una URL https, con una extensión permitida. Vacío es válido. */
export function isValidMediaPath(value: string | null | undefined, extensions: string[]): boolean {
  const v = (value ?? '').trim()
  if (v === '') return true
  if (v.length > 500) return false
  const hasExtension = (path: string) => extensions.includes(path.slice(path.lastIndexOf('.') + 1).toLowerCase()) && path.includes('.')

  try {
    const url = new URL(v)
    return url.protocol === 'https:' && url.hostname !== '' && hasExtension(url.pathname)
  } catch {
    // No es una URL absoluta: se trata como ruta relativa.
  }
  return !v.includes('..') && !v.includes('//') && !v.includes('\\') && RELATIVE.test(v) && hasExtension(v)
}

const YOUTUBE_HOSTS = new Set(['youtu.be', 'youtube.com', 'www.youtube.com', 'm.youtube.com', 'youtube-nocookie.com', 'www.youtube-nocookie.com'])

/** El código de 11 caracteres de un video de YouTube, o null. Acepta el código solo o una URL de YouTube (watch, youtu.be, embed, shorts, live). */
export function parseYouTubeId(value: string | null | undefined): string | null {
  const v = (value ?? '').trim()
  if (v === '') return null
  if (/^[A-Za-z0-9_-]{11}$/.test(v)) return v
  let url: URL
  try {
    url = new URL(v)
  } catch {
    return null
  }
  if (!['http:', 'https:'].includes(url.protocol) || !YOUTUBE_HOSTS.has(url.hostname.toLowerCase())) return null
  if (url.hostname.toLowerCase() === 'youtu.be') {
    const first = url.pathname.split('/').filter(Boolean)[0]
    return first && /^[A-Za-z0-9_-]{11}$/.test(first) ? first : null
  }
  const fromQuery = url.searchParams.get('v')
  if (fromQuery && /^[A-Za-z0-9_-]{11}$/.test(fromQuery)) return fromQuery
  return /\/(?:embed|shorts|live)\/([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])/.exec(url.pathname)?.[1] ?? null
}

export const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/
