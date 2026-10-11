import { http } from './client'
import type {
  AdminBooking, AdminUser, AuthResponse, Cinema, CinemaInput, DailySales, DateRange, Genre, Movie, MovieInput, MovieListItem, MovieSales,
  OccupancyRow, Paged, Role, Room, RoomLayout, RoomSeats, SalesSummary, Showtime, ShowtimeInput,
} from './types'

/** Quita los parámetros vacíos para no enviar `?search=&status=`. */
export function clean<T extends Record<string, unknown>>(params: T): Partial<T> {
  return Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== '')) as Partial<T>
}

const get = async <T>(url: string, params?: Record<string, unknown>) => (await http.get<T>(url, { params: params && clean(params) })).data

// ── Autenticación ───────────────────────────────────────────────────────────

export const auth = {
  /** `useCookie: true`: el refresh token llega solo en una cookie HttpOnly y el cuerpo trae únicamente el access token (ADR-006). */
  login: async (email: string, password: string) => (await http.post<AuthResponse>('/auth/login', { email, password, useCookie: true })).data,
  logout: async () => {
    await http.post('/auth/logout')
  },
}

// ── Géneros ─────────────────────────────────────────────────────────────────

export const genres = {
  list: () => get<Genre[]>('/admin/genres'),
  create: async (name: string) => (await http.post<Genre>('/genres', { name })).data,
  update: async (id: number, name: string) => (await http.put<Genre>(`/genres/${id}`, { name })).data,
  remove: async (id: number) => {
    await http.delete(`/genres/${id}`)
  },
}

// ── Películas ───────────────────────────────────────────────────────────────

export interface MovieFilters {
  search?: string
  isActive?: boolean
  status?: string
  page?: number
  pageSize?: number
}

export const movies = {
  list: (filters: MovieFilters) => get<Paged<MovieListItem>>('/admin/movies', { ...filters }),
  get: (id: number) => get<Movie>(`/admin/movies/${id}`),
  create: async (input: MovieInput) => (await http.post<Movie>('/movies', input)).data,
  update: async (id: number, input: MovieInput) => (await http.put<Movie>(`/movies/${id}`, input)).data,
  /** Baja lógica (RN-08). */
  remove: async (id: number) => {
    await http.delete(`/movies/${id}`)
  },
}

// ── Cines, salas y asientos ─────────────────────────────────────────────────

export const cinemas = {
  list: () => get<Cinema[]>('/admin/cinemas'),
  create: async (input: CinemaInput) => (await http.post<Cinema>('/cinemas', input)).data,
  update: async (id: number, input: CinemaInput) => (await http.put<Cinema>(`/cinemas/${id}`, input)).data,
}

export const rooms = {
  list: (cinemaId?: number) => get<Room[]>('/cinema-rooms', { cinemaId }),
  create: async (input: { cinemaId: number; name: string; layout: RoomLayout }) => (await http.post<Room>('/cinema-rooms', input)).data,
  update: async (id: number, input: { name: string; isActive: boolean }) => (await http.put<Room>(`/cinema-rooms/${id}`, input)).data,
  seats: (id: number) => get<RoomSeats>(`/cinema-rooms/${id}/seats`),
  updateLayout: async (id: number, layout: RoomLayout) => (await http.put<RoomSeats>(`/cinema-rooms/${id}/layout`, layout)).data,
}

// ── Funciones ───────────────────────────────────────────────────────────────

export interface ShowtimeFilters {
  movieId?: number
  cinemaId?: number
  roomId?: number
  status?: string
  from?: string
  to?: string
  upcomingOnly?: boolean
  page?: number
  pageSize?: number
}

export const showtimes = {
  list: (filters: ShowtimeFilters) => get<Paged<Showtime>>('/admin/showtimes', { ...filters }),
  get: (id: number) => get<Showtime>(`/admin/showtimes/${id}`),
  create: async (input: ShowtimeInput) => (await http.post<Showtime>('/showtimes', input)).data,
  update: async (id: number, input: ShowtimeInput) => (await http.put<Showtime>(`/showtimes/${id}`, input)).data,
  cancel: async (id: number) => (await http.post<Showtime>(`/showtimes/${id}/cancel`)).data,
}

// ── Reservas y usuarios ─────────────────────────────────────────────────────

export interface BookingFilters {
  status?: string
  movieId?: number
  cinemaId?: number
  from?: string
  to?: string
  search?: string
  page?: number
  pageSize?: number
}

export const bookings = {
  list: (filters: BookingFilters) => get<Paged<AdminBooking>>('/admin/bookings', { ...filters }),
  /** El administrador cancela hasta el inicio de la función (RN-30). */
  cancel: async (id: number) => (await http.post(`/bookings/${id}/cancel`)).data,
}

export interface UserFilters {
  search?: string
  role?: Role | ''
  isActive?: boolean
  page?: number
  pageSize?: number
}

export const users = {
  list: (filters: UserFilters) => get<Paged<AdminUser>>('/users', { ...filters }),
  setActive: async (id: number, isActive: boolean) => (await http.put<AdminUser>(`/users/${id}/status`, { isActive })).data,
}

// ── Reportes ────────────────────────────────────────────────────────────────

export const reports = {
  summary: (range: DateRange) => get<SalesSummary>('/reports/summary', { ...range }),
  daily: (range: DateRange) => get<DailySales[]>('/reports/sales-daily', { ...range }),
  byMovie: (range: DateRange) => get<MovieSales[]>('/reports/sales-by-movie', { ...range }),
  occupancy: (range: DateRange, groupBy: 'showtime' | 'room', page = 1, pageSize = 20) =>
    get<Paged<OccupancyRow>>('/reports/occupancy', { ...range, groupBy, page, pageSize }),
}
