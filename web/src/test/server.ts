import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { API_URL } from '@/api/client'
import { admin, makeAuth, makeCinema, makeGenre, makeMovieItem, makeRoom, paged } from './factories'

export const url = (path: string) => `${API_URL}${path}`

/** Respuesta de error con el formato de la API (problem+json con `code` estable). */
export const problem = (status: number, code: string, detail = 'Error', errors?: Record<string, string[]>) =>
  HttpResponse.json({ type: 'about:blank', title: detail, status, detail, code, ...(errors ? { errors } : {}) }, { status, headers: { 'Content-Type': 'application/problem+json' } })

/** Respuestas por defecto: una sesión de administrador válida y catálogos pequeños. Cada prueba reemplaza lo que necesita. */
export const defaultHandlers = [
  http.post(url('/auth/refresh'), () => HttpResponse.json(makeAuth(admin))),
  http.post(url('/auth/login'), () => HttpResponse.json(makeAuth(admin))),
  http.post(url('/auth/logout'), () => new HttpResponse(null, { status: 204 })),
  http.get(url('/admin/movies'), () => HttpResponse.json(paged([makeMovieItem()]))),
  http.get(url('/admin/cinemas'), () => HttpResponse.json([makeCinema()])),
  http.get(url('/admin/genres'), () => HttpResponse.json([makeGenre()])),
  http.get(url('/cinema-rooms'), () => HttpResponse.json([makeRoom()])),
]

export const server = setupServer(...defaultHandlers)
