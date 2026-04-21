# Sprint 1 — Bootstrap & MVP Core

**Objetivo**: tener la solución compilando, con dominio completo, DB migrando y pantallas base funcionales.

## Decisiones técnicas tomadas

### ADR-001 — Avalonia UI vs Electron vs Tauri
Ver `/docs/adr/ADR-001-stack-decision.md`. Decisión: Avalonia UI + C# .NET 9.

### Migración manual vs dotnet ef
El primer commit crea la migración `InitialCreate` manualmente porque .NET SDK no está instalado
en la máquina del desarrollador en el momento del bootstrap. Una vez instalado el SDK:
```
dotnet ef migrations remove  # eliminar la manual
dotnet ef migrations add InitialCreate --project src/GymForge.Infrastructure --startup-project src/GymForge.Api
```
Esto regenerará el archivo con el ModelSnapshot completo.

### Snapshot vacío
`GymForgeDbContextModelSnapshot.cs` tiene el body vacío porque fue generado sin SDK.
EF Core usa el snapshot para calcular diffs entre migraciones — regenerar con SDK antes del Sprint 2.

## Checklist de entrega Sprint 1

- [x] Solución bootstrap: 10 proyectos src + 3 test
- [x] Directory.Packages.props con versiones centralizadas
- [x] .editorconfig + .gitignore
- [x] ADR-001 documentado
- [x] Domain entities completas (22 entidades)
- [x] EF Core DbContext + 8 archivos de configuración Fluent API
- [x] Migración InitialCreate (manual)
- [x] Seed: 80+ ejercicios + company/site/staff/planes default
- [x] MediatR handlers: CRUD socios, membresías, cobros
- [x] Algoritmo Gatekeeper (8 validaciones)
- [x] Repositories: Member, Membership, Charge, AccessLog
- [x] Hardware interfaces + sidecars stub
- [x] Avalonia Desktop: shell layout (sidebar 240/64px + topbar)
- [x] Vista Socios (DataGrid virtualizado + búsqueda)
- [x] Vista Check-in Kiosk (semáforo verde/rojo, countdown 3s)
- [x] Converters Avalonia (status color, sidebar width)
- [x] GymForge.Api: Minimal API endpoints
- [x] Tests: 6 Gatekeeper + 8 MembershipStateMachine + 4 Integration
- [x] CLAUDE.md

## Dependencias instalación

Para compilar se necesita instalar (si no están):
1. [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
2. Ejecutar: `dotnet restore && dotnet build GymForge.sln`
3. Para regenerar migración: `dotnet ef` (EF Core tools: `dotnet tool install --global dotnet-ef`)
