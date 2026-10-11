import { lazy } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { LoginPage } from '@/auth/LoginPage'
import { RequireAdmin } from '@/auth/RequireAdmin'
import { AppShell } from '@/components/layout/AppShell'

// Cada sección se carga bajo demanda: el login no descarga los gráficos ni el editor de salas.
const DashboardPage = lazy(() => import('@/features/dashboard/DashboardPage'))
const MoviesPage = lazy(() => import('@/features/movies/MoviesPage'))
const MovieFormPage = lazy(() => import('@/features/movies/MovieFormPage'))
const GenresPage = lazy(() => import('@/features/genres/GenresPage'))
const CinemasPage = lazy(() => import('@/features/cinemas/CinemasPage'))
const RoomsPage = lazy(() => import('@/features/rooms/RoomsPage'))
const RoomLayoutPage = lazy(() => import('@/features/rooms/RoomLayoutPage'))
const ShowtimesPage = lazy(() => import('@/features/showtimes/ShowtimesPage'))
const BookingsPage = lazy(() => import('@/features/bookings/BookingsPage'))
const UsersPage = lazy(() => import('@/features/users/UsersPage'))
const ReportsPage = lazy(() => import('@/features/reports/ReportsPage'))

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAdmin />}>
        <Route element={<AppShell />}>
          <Route index element={<DashboardPage />} />
          <Route path="movies" element={<MoviesPage />} />
          <Route path="movies/new" element={<MovieFormPage />} />
          <Route path="movies/:id" element={<MovieFormPage />} />
          <Route path="genres" element={<GenresPage />} />
          <Route path="cinemas" element={<CinemasPage />} />
          <Route path="rooms" element={<RoomsPage />} />
          <Route path="rooms/:id/layout" element={<RoomLayoutPage />} />
          <Route path="showtimes" element={<ShowtimesPage />} />
          <Route path="bookings" element={<BookingsPage />} />
          <Route path="users" element={<UsersPage />} />
          <Route path="reports" element={<ReportsPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Route>
    </Routes>
  )
}
