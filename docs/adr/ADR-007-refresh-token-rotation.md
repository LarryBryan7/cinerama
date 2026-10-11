# ADR-007: Refresh tokens con hash, rotación y detección de reutilización

- **Estado:** Aceptada
- **Fecha:** 2026-10-02
- **Tipo:** decisión de seguridad

## Contexto

Los access tokens JWT son stateless: no se pueden revocar antes de que venzan. Por eso su vida útil es corta (15 minutos) y la sesión se mantiene con un refresh token de 7 días que sí se controla en la base.

En un proyecto anterior del autor los refresh tokens se guardaban tal cual y no rotaban. Así, un token filtrado seguía siendo válido hasta su vencimiento, sin forma de detectarlo.

## Decisión

1. **Formato:** el refresh token es un valor aleatorio opaco de 32 bytes (`RandomNumberGenerator`), no un JWT. No necesita llevar información porque siempre se valida contra la base.
2. **Almacenamiento:** se guarda solo `SHA-256(token)` en `RefreshTokens.TokenHash`. Si la base se filtra, los hashes no sirven para iniciar sesión.
3. **Rotación:** cada `POST /auth/refresh` revoca el token presentado (`RevokedAt`, `ReplacedById`) y emite uno nuevo de la misma familia (`FamilyId`).
4. **Detección de reutilización:** si se presenta un token ya revocado por rotación, se asume un robo (el atacante o la víctima usó una copia vieja). Se revoca **toda la familia** y se responde 401 `refresh_token_reused`.
5. **Revocación:** el logout revoca el token actual. Desactivar un usuario revoca todos sus tokens (RN-03). El refresh valida que el usuario siga activo.
6. **Contraseñas:** `PasswordHasher<User>` de `Microsoft.Extensions.Identity.Core` (PBKDF2 con sal y formato versionado que permite subir el costo en el futuro).
7. **Access token:** JWT HS256 con `sub`, `role`, `iss`, `aud` y `exp`. Duración única configurada en `JwtOptions`, validada al arrancar (`ValidateOnStart`), para que login y refresh emitan siempre el mismo TTL.
8. **401 diferenciados:** un access token vencido devuelve `code: token_expired` y los clientes solo intentan el refresh ante ese código.

**Almacenamiento en los clientes:**

| Cliente | Access token | Refresh token |
|---|---|---|
| App móvil | Memoria | `flutter_secure_storage` (Keychain / Keystore) |
| Panel web | Memoria | Cookie HttpOnly (ADR-006) |

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| Refresh token JWT sin estado | No se puede revocar ni detectar su reutilización. |
| Guardar el token en texto plano | Una filtración de la base expone sesiones válidas. |
| ASP.NET Core Identity completo con sus endpoints | Agrega tablas y flujos (confirmación de email, 2FA, claims) que el proyecto no usa, y oculta el flujo JWT que se quiere entender y explicar. |
| Lista de revocación de access tokens | Contradice el modelo stateless. Con 15 minutos de vida, la ventana de riesgo es aceptable. |

## Consecuencias

- Hay una condición de carrera conocida: si el mismo cliente envía dos refresh simultáneos con el mismo token, el segundo se ve como reutilización. Los clientes la evitan con un solo refresh en curso a la vez (patrón *single-flight*), y hay un test que lo cubre.
- Un administrador desactivado pierde el acceso en, como máximo, 15 minutos (lo que dure su access token vigente).

## Implementación (fase 1)

Detalles que el diseño inicial no fijaba y que la implementación y sus tests dejaron resueltos:

- **Reutilización frente a logout.** `ReplacedById` solo se informa cuando un token se revoca *por rotación*. Así, presentar un token rotado (posible robo) revoca la familia y responde `refresh_token_reused`, mientras que presentar uno revocado por logout o por desactivación responde `invalid_refresh_token` sin revocar nada más.
- **Rotación atómica.** Se inserta el token nuevo y se "reclama" el viejo con un `UPDATE ... WHERE Id = @id AND RevokedAt IS NULL` dentro de una transacción. Si dos solicitudes presentan el mismo token a la vez, solo una reclama la fila; la otra obtiene 0 filas, se revierte y se trata como reutilización. Un test lanza ambas solicitudes en paralelo; quitar la condición `RevokedAt IS NULL` lo hace fallar.
- **Logout con un token ajeno.** El token se busca filtrando por el usuario autenticado: el de otra persona no se encuentra, no se revoca y la respuesta es igual (204). Sin token, se revocan todas las sesiones del usuario.
- **Cookie del panel web.** Al rechazar una cookie, el `Set-Cookie` que la borra debe enviarse en la misma respuesta 401. El middleware de excepciones limpia las cabeceras, así que ese caso responde desde el controlador y no lanzando una excepción. Un test lo cubre.
- **Email desconocido y temporización.** El login verifica la contraseña contra un hash ficticio cuando el email no existe, para que ambos casos tarden lo mismo.

## Motivación de portafolio

**Producto.** Es la práctica recomendada para sesiones JWT. Además, aplica directamente una lección aprendida en un proyecto anterior.
