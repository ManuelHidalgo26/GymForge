# GymForge

Sistema de gestión de gimnasios para Windows: **desktop, offline-first**. Socios,
membresías, control de acceso (gatekeeper), caja con arqueo, cobros y ventas —
todo funciona sin internet, con SQLite local.

> Mercado objetivo: gimnasios de AR/LATAM (100–2000 socios). Moneda ARS,
> facturación AFIP (en desarrollo).

## Probar la app

```powershell
# Correr en modo desarrollo (requiere .NET 9 SDK)
.\scripts\run.ps1
# Desde cero: borra la base local y re-siembra datos de ejemplo (PIN admin: 1234)
.\scripts\run.ps1 -Reset
```

Recorrido sugerido: **Socios → Nuevo socio** → **Caja** (PIN `1234`) → Abrir caja →
**Registrar cobro/venta** → el socio queda activo, la caja suma el movimiento y el
Dashboard refleja la recaudación.

## Generar el ejecutable distribuible

```powershell
.\scripts\publish.ps1   # → dist\GymForge.Desktop.exe (autocontenido, ~65 MB)
```

Ese único `.exe` corre en cualquier Windows x64 sin instalar nada.

## Desarrollo

```powershell
.\scripts\build.ps1      # build + tests
dotnet test GymForge.sln # solo tests
dotnet run --project tools/GymForge.Screenshots  # capturas headless de la UI real
```

| Capa | Tecnología |
|------|-----------|
| UI Desktop | Avalonia 11 + MVVM Toolkit + FluentAvalonia |
| Lógica | .NET 9 + MediatR + FluentValidation |
| Datos | EF Core 9 + SQLite (WAL), PostgreSQL en cloud (futuro) |
| Hardware | Sidecars HTTP: fiscal (:12000), biometría (:12001), accesos (:12002) |
| Tests | xUnit + FluentAssertions + NSubstitute |

La arquitectura, convenciones y backlog viven en [CLAUDE.md](CLAUDE.md).
El `mockup/` es la maqueta HTML de referencia visual (no es la app).
