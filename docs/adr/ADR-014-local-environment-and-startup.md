# ADR-014: Entorno local y arranque (migraciones, seed y Docker Compose)

- **Estado:** Aceptada
- **Fecha:** 2026-10-02
- **Fase:** 1

## Contexto

`docker compose up` debe dejar una API funcional, con el esquema creado y datos de demostración, sin pasos manuales. Al mismo tiempo, hay compromisos que son aceptables en un entorno local y **no** lo serían en uno real. Esta decisión los separa explícitamente para que no se confundan.

## Decisión

### Arranque

| Tema | Decisión |
|---|---|
| Migraciones | `Database:MigrateOnStartup` (por defecto `false`). Se activa en desarrollo y en Compose. Usa `Database.MigrateAsync()` con hasta 10 reintentos cada 3 s, porque SQL Server puede aceptar conexiones unos segundos después de que su contenedor figure como iniciado. EF Core toma un bloqueo de migración en la base, así que dos instancias que arrancan a la vez (demo de la fase 6) no compiten. |
| Seed | Solo si `Seed:Enabled=true` y la base está vacía. Las contraseñas de las cuentas demo vienen del entorno (`Seed:AdminPassword`, `Seed:CustomerPassword`); sin ellas no se crean usuarios. Datos 100 % ficticios, con semilla fija y dominio `.test`. |
| Configuración | Tipada y validada al arrancar (`ValidateOnStart`): una clave JWT de menos de 32 caracteres, `Cors:AllowedOrigins` con `*` o una zona horaria inexistente **impiden iniciar la API**. Un test por cada caso. |
| Secretos | Fuera de Git: `.env` (ignorado) para Compose y `dotnet user-secrets` para `dotnet run`. `.env.example` solo trae valores ficticios. |
| Build | Versiones de paquetes centralizadas (`Directory.Packages.props`), nulabilidad habilitada y advertencias tratadas como errores. CI verifica además que el modelo y las migraciones estén sincronizados (`dotnet ef migrations has-pending-model-changes`). |

### Compromisos válidos solo en local

| Compromiso | Por qué es aceptable aquí | Qué se haría en un despliegue real |
|---|---|---|
| La API se conecta con la cuenta `sa` | Es una base desechable de un contenedor. | Un login con permisos mínimos y secretos en un gestor de secretos. |
| La cookie de refresh del panel web sale sin `Secure` (`WebSession:RefreshCookieSecure=false` en Compose y desarrollo) | El panel corre en `http://localhost`. El valor por defecto es `true` y un test verifica que la cookie sale `Secure`. | HTTPS siempre. La opción no se desactiva. |
| Swagger habilitado con `Swagger:Enabled=true` fuera de Development | Es la documentación interactiva de la demo. | Deshabilitado, o detrás de autenticación. |
| Las migraciones se aplican al iniciar la API | Un solo comando levanta todo. | Un paso separado del despliegue (`dotnet ef migrations bundle`), con el rol de base de datos adecuado. |

### Los compromisos locales no se heredan en producción

Los valores seguros son los predeterminados: `appsettings.json` trae `RefreshCookieSecure=true`, `MigrateOnStartup=false`, `Seed:Enabled=false`, `Swagger:Enabled=false`, sin cadena de conexión ni claves. Lo local vive en `appsettings.Development.json`, que ASP.NET Core solo carga cuando el entorno es Development, y en `docker-compose.yml`.

Además, una **protección de arranque** (`InsecureSettingsGuard`) impide que un entorno que no sea Development arranque con la cookie de sesión sin `Secure` o conectado con la cuenta `sa`, salvo permiso explícito con `Local:AllowInsecureSettings=true`. Solo Docker Compose y la factory de tests lo activan. Si se permite, el arranque lo registra como advertencia. Está cubierto por tests unitarios (valores por defecto, cada hallazgo, el permiso) y de integración (la API no arranca).

### Puertos y plataforma

- La API se publica en `http://localhost:8090` (configurable con `API_PORT`). El 8080 suele estar ocupado por otros servicios de desarrollo y el fallo es silencioso: otro proceso responde en su lugar.
- SQL Server solo existe para x86. En macOS con Apple Silicon corre emulado con Rosetta (Colima con `--vz-rosetta`, o Docker Desktop con Rosetta). Funciona, pero más lento. En Linux x64 y en CI corre de forma nativa.
- El contenedor de la API corre con el usuario sin privilegios que trae la imagen de ASP.NET.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| `EnsureCreated()` | No usa migraciones: no hay historial ni forma de evolucionar el esquema. |
| Aplicar siempre las migraciones en el arranque, también en producción | Una API con permisos para alterar el esquema, y varias instancias migrando a la vez. |
| Scripts SQL manuales | Se desincronizan del modelo y no se prueban. |
| Un login de aplicación dedicado en Compose | Más piezas (un contenedor de inicialización) sin valor para una base desechable. Se documenta como compromiso. |

## Consecuencias

- El primer arranque con Compose tarda más (descarga de la imagen de SQL Server, migración y seed de ~126 funciones con sus asientos).
- Los tests de integración no usan este camino por defecto: cada suite arranca su propio contenedor efímero y limpia entre tests (ADR-012).

## Motivación de portafolio

**Producto.** Es la configuración mínima para que el proyecto sea reproducible por cualquier persona que lo clone.
