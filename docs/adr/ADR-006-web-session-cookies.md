# ADR-006: Sesión del panel web con access token en memoria y refresh token en cookie HttpOnly

- **Estado:** Aceptada
- **Fecha:** 2026-10-02
- **Tipo:** decisión de seguridad

## Contexto

El panel de administración es una SPA en React que consume la API. Necesita mantener la sesión del administrador con un access token de vida corta (15 minutos) y un refresh token de vida larga (7 días).

Guardar los tokens en `localStorage` es habitual, pero expone ambos a cualquier script que se ejecute en la página: una vulnerabilidad XSS, una dependencia comprometida o una extensión del navegador. Un refresh token robado de esa forma da acceso durante días.

## Decisión

| Elemento | Dónde vive | Detalle |
|---|---|---|
| Access token | **Solo en memoria** (variable del módulo del cliente HTTP) | Se pierde al recargar la página; se recupera con un refresh. |
| Refresh token | **Cookie** `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth` | JavaScript no puede leerlo. El navegador solo lo envía a los endpoints de autenticación. |
| `localStorage` / `sessionStorage` | **No se guarda ningún token** | Solo preferencias de interfaz. |

**Flujo:**
1. `POST /auth/login` con `useCookie: true` → el cuerpo trae solo el access token y el refresh token llega en `Set-Cookie`.
2. Al cargar la aplicación, o ante un `401 token_expired`, el cliente llama a `POST /auth/refresh` con `withCredentials: true`. Hay un solo refresh en curso a la vez y las solicitudes concurrentes esperan su resultado.
3. `POST /auth/logout` revoca el token en la base y borra la cookie.

**CORS:** `WithOrigins(<orígenes configurados>)` + `AllowCredentials()`. Nunca `AllowAnyOrigin` junto con credenciales: ASP.NET Core lo prohíbe y sería inseguro. Los orígenes se leen de configuración (`CORS__ALLOWEDORIGINS`).

**CSRF:**
- `SameSite=Strict` impide que otro sitio provoque el envío de la cookie.
- Además, `/auth/refresh` solo devuelve un access token en el cuerpo, y CORS impide que un origen no autorizado lo lea.
- Los endpoints de negocio no usan cookies, sino `Authorization: Bearer`, así que no son vulnerables a CSRF.

**App móvil:** no usa cookies. Envía el refresh token en el cuerpo y lo guarda en `flutter_secure_storage` (ADR-007).

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Ambos tokens en `localStorage` | Accesibles para cualquier script. Es la opción más simple, pero un XSS compromete la sesión durante 7 días. |
| Access token también en cookie | Todos los endpoints quedarían expuestos a CSRF y harían falta tokens anti-CSRF en cada formulario. |
| Patrón BFF (backend-for-frontend con sesión en servidor) | Es el más robusto, pero agrega otro servicio. Es desproporcionado para un panel interno. Queda como mejora futura. |

## Consecuencias

- Un XSS puede usar la sesión mientras la pestaña esté abierta, pero **no puede extraer el refresh token** para usarlo desde otro lugar.
- **Requisito de despliegue:** el panel y la API deben compartir el mismo *site* (por ejemplo `admin.cinerama.example` y `api.cinerama.example`) para que `SameSite=Strict` funcione. Si estuvieran en dominios distintos haría falta `SameSite=None`, con su análisis de CSRF correspondiente.
- **Desarrollo local:** `localhost:5173` y `localhost:8090` son el mismo *site* (el puerto no cuenta). Safari no acepta cookies `Secure` sobre `http://localhost`, así que en Compose y en desarrollo `WebSession:RefreshCookieSecure` es `false` (ver [ADR-014](ADR-014-local-environment-and-startup.md)). El valor por defecto es `true` y un test verifica que la cookie sale con `Secure`. También hay un perfil HTTPS (`dotnet run --launch-profile https`, con `dotnet dev-certs https --trust`).
- La API soporta dos formas de transportar el refresh token (cookie y cuerpo). Está cubierto por tests de integración para ambas.

## Motivación de portafolio

**Producto.** Es la práctica recomendada para SPAs que manejan sesiones de larga duración. Además, da un uso real a CORS con credenciales, que de otro modo sería solo configuración.
