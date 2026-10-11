# ADR-002: .NET 10 LTS

- **Estado:** Aceptada
- **Fecha:** 2026-10-02

## Contexto

El proyecto empieza en octubre de 2026. Microsoft anunció que .NET 9 (STS) y .NET 8 (LTS) dejan de tener soporte el 10 de noviembre de 2026. .NET 10 es LTS, publicado en noviembre de 2025, con soporte hasta noviembre de 2028.

## Decisión

Usar .NET 10 LTS (SDK 10.0.x) en todos los proyectos del backend, con `<TargetFramework>net10.0</TargetFramework>`.

## Alternativas consideradas

| Alternativa | Motivo del descarte |
|---|---|
| .NET 9 | Quedaría sin parches de seguridad a las pocas semanas de empezar el proyecto. |
| .NET 8 LTS | Misma fecha de fin de soporte que .NET 9. |

## Consecuencias

- Todo lo que el proyecto demuestra (ASP.NET Core, EF Core, SignalR, BackgroundService, HybridCache) existe con la misma forma en .NET 9. El conocimiento es transferible en ambos sentidos.
- Las versiones de los paquetes de EF Core y ASP.NET Core siguen la línea 10.x.

## Motivación de portafolio

**Producto + portafolio.** La plataforma .NET se elige por el perfil profesional buscado; un backend en Node.js o Java habría sido igual de válido para el producto. La versión LTS sí es una decisión de producto: no se inicia un proyecto nuevo sobre una versión a punto de quedar sin soporte.
