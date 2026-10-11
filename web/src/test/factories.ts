import type { AdminBooking, AdminUser, Cinema, Genre, MovieListItem, Room, Showtime, User } from '@/api/types'

export const admin: User = { id: 1, fullName: 'Ana Admin', email: 'admin@cinerama.test', role: 'Admin' }
export const customer: User = { id: 2, fullName: 'Carlos Cliente', email: 'cliente@cinerama.test', role: 'Customer' }

export const makeAuth = (user: User = admin) => ({ accessToken: `token-${user.id}`, accessTokenExpiresAt: '2099-01-01T00:00:00Z', user })

export const makeShowtime = (over: Partial<Showtime> = {}): Showtime => ({
  id: 10, movieId: 1, movieTitle: 'Dune', cinemaId: 1, cinemaName: 'Cinerama Centro', cinemaRoomId: 5, roomName: 'Sala 1',
  startsAt: '2099-03-10T20:00:00.000Z', endsAt: '2099-03-10T22:50:00.000Z', price: 25, status: 'Scheduled',
  capacity: 100, seatsSold: 0, confirmedBookings: 0, hasBookings: false, started: false, ...over,
})

export const makeBooking = (over: Partial<AdminBooking> = {}): AdminBooking => ({
  id: 100, code: 'CIN-ABC123', status: 'Confirmed', userId: 2, customerName: 'Carlos Cliente', customerEmail: 'cliente@cinerama.test',
  showtimeId: 10, movieId: 1, movieTitle: 'Dune', cinemaName: 'Cinerama Centro', roomName: 'Sala 1',
  startsAt: '2099-03-10T20:00:00.000Z', seats: ['A1', 'A2'], totalAmount: 50, createdAt: '2099-03-01T10:00:00.000Z', cancelledAt: null, ...over,
})

export const makeUser = (over: Partial<AdminUser> = {}): AdminUser => ({
  id: 2, fullName: 'Carlos Cliente', email: 'cliente@cinerama.test', role: 'Customer', isActive: true, createdAt: '2099-01-05T10:00:00.000Z', ...over,
})

export const makeRoom = (over: Partial<Room> = {}): Room => ({
  id: 5, cinemaId: 1, cinemaName: 'Cinerama Centro', name: 'Sala 1', isActive: true, seatCount: 12, rows: 3, positions: 4, upcomingShowtimes: 0, ...over,
})

export const makeCinema = (over: Partial<Cinema> = {}): Cinema => ({
  id: 1, name: 'Cinerama Centro', address: 'Av. Principal 123', city: 'Lima', region: 'Lima', latitude: -12.04, longitude: -77.03,
  logoUrl: null, logoPreviewUrl: null, isActive: true, roomCount: 1, upcomingShowtimes: 0, ...over,
})

export const makeGenre = (over: Partial<Genre> = {}): Genre => ({ id: 1, name: 'Acción', movieCount: 3, ...over })

export const makeMovieItem = (over: Partial<MovieListItem> = {}): MovieListItem => ({
  id: 1, title: 'Dune', durationMinutes: 155, classification: '+14', genres: ['Ciencia ficción'], posterPreviewUrl: null,
  releaseDate: '2024-03-01', preSaleStartsOn: null, status: 'NowShowing', isActive: true, upcomingShowtimes: 0, ...over,
})

export const paged = <T,>(items: T[], page = 1, pageSize = 20) => ({ items, page, pageSize, totalCount: items.length })
