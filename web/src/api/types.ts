// Tipos del contrato de la API (docs/api.md). Fechas en ISO 8601 UTC; las "fechas locales del cine" son yyyy-MM-dd.

export type Role = 'Admin' | 'Customer'

export interface User {
  id: number
  fullName: string
  email: string
  role: Role
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  user: User
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

// ── Catálogo ────────────────────────────────────────────────────────────────

export interface Genre {
  id: number
  name: string
  movieCount: number
}

export type MovieStatus = 'NowShowing' | 'PreSale' | 'ComingSoon'

export interface MovieListItem {
  id: number
  title: string
  durationMinutes: number
  classification: string
  genres: string[]
  posterPreviewUrl: string | null
  releaseDate: string
  preSaleStartsOn: string | null
  status: MovieStatus
  isActive: boolean
  upcomingShowtimes: number
}

export interface Movie {
  id: number
  title: string
  synopsis: string | null
  durationMinutes: number
  classification: string
  genreIds: number[]
  genres: string[]
  posterUrl: string | null
  posterPreviewUrl: string | null
  trailerUrl: string | null
  previewVideoUrl: string | null
  releaseDate: string
  preSaleStartsOn: string | null
  status: MovieStatus
  isActive: boolean
  upcomingShowtimes: number
}

export interface MovieInput {
  title: string
  synopsis: string | null
  durationMinutes: number
  classification: string
  genreIds: number[]
  posterUrl: string | null
  trailerUrl: string | null
  previewVideoUrl: string | null
  releaseDate: string
  preSaleStartsOn: string | null
  isActive: boolean
}

export interface Cinema {
  id: number
  name: string
  address: string
  city: string
  region: string
  latitude: number
  longitude: number
  logoUrl: string | null
  logoPreviewUrl: string | null
  isActive: boolean
  roomCount: number
  upcomingShowtimes: number
}

export interface CinemaInput {
  name: string
  address: string
  city: string
  region: string
  latitude: number
  longitude: number
  logoUrl: string | null
  isActive: boolean
}

// ── Salas y funciones ───────────────────────────────────────────────────────

export interface Room {
  id: number
  cinemaId: number
  cinemaName: string
  name: string
  isActive: boolean
  seatCount: number
  rows: number
  positions: number
  upcomingShowtimes: number
}

export interface RoomLayout {
  rows: number
  positions: number
  emptyCells: string[]
}

export interface RoomSeats {
  roomId: number
  roomName: string
  rows: number
  positions: number
  seats: { id: number; row: string; number: number }[]
  emptyCells: string[]
}

export type ShowtimeStatus = 'Scheduled' | 'Cancelled'

export interface Showtime {
  id: number
  movieId: number
  movieTitle: string
  cinemaId: number
  cinemaName: string
  cinemaRoomId: number
  roomName: string
  startsAt: string
  endsAt: string
  price: number
  status: ShowtimeStatus
  capacity: number
  seatsSold: number
  confirmedBookings: number
  hasBookings: boolean
  started: boolean
}

export interface ShowtimeInput {
  movieId: number
  cinemaRoomId: number
  startsAt: string
  price: number
}

// ── Reservas y usuarios ─────────────────────────────────────────────────────

export type BookingStatus = 'Confirmed' | 'Cancelled'

export interface AdminBooking {
  id: number
  code: string
  status: BookingStatus
  userId: number
  customerName: string
  customerEmail: string
  showtimeId: number
  movieId: number
  movieTitle: string
  cinemaName: string
  roomName: string
  startsAt: string
  seats: string[]
  totalAmount: number
  createdAt: string
  cancelledAt: string | null
}

export interface AdminUser {
  id: number
  fullName: string
  email: string
  role: Role
  isActive: boolean
  createdAt: string
}

// ── Reportes ────────────────────────────────────────────────────────────────

export interface SalesSummary {
  from: string
  to: string
  revenue: number
  bookings: number
  ticketsSold: number
  averageTicket: number
  /** De 0 a 1. */
  averageOccupancy: number
  showtimes: number
}

export interface DailySales {
  date: string
  revenue: number
  bookings: number
  ticketsSold: number
  showtimes: number
  occupancy: number
}

export interface MovieSales {
  movieId: number
  title: string
  revenue: number
  bookings: number
  ticketsSold: number
  showtimes: number
  occupancy: number
}

export interface OccupancyRow {
  id: number
  title: string
  subtitle: string | null
  startsAt: string | null
  capacity: number
  seatsSold: number
  occupancy: number
  showtimes: number
}

export interface DateRange {
  from: string
  to: string
}
