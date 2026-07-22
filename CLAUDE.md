# GymForge — CLAUDE.md

Sistema de gestión de gimnasios desktop offline-first para Windows. Competidor de ControlFit con
tecnología .NET 9 + Avalonia UI 11. Mercado objetivo: gimnasios AR/LATAM con 100-2000 socios.

---

## Stack (NO modificar sin ADR)

| Capa | Tecnología |
|------|-----------|
| UI Desktop | Avalonia UI 11 + MVVM Toolkit + FluentAvalonia |
| Business Logic | .NET 9 + MediatR + FluentValidation |
| ORM | EF Core 9 — SQLite local (WAL) / PostgreSQL cloud |
| Hardware sidecars | FiscalBroker :12000 · BioBroker :12001 · AccessBroker :12002 |
| Reporting | QuestPDF (tickets/recibos) |
| Logging | Serilog → archivo local |
| Tests | xUnit + FluentAssertions + NSubstitute + Testcontainers |

---

## Arquitectura

```
GymForge.Domain          → entidades, value objects, enums, eventos de dominio
GymForge.Application     → use cases (MediatR), DTOs, interfaces de repositorios
GymForge.Infrastructure  → EF Core, repos, seed, servicios
GymForge.Hardware        → interfaces IFiscalPrinter / IBiometricReader / IAccessController
GymForge.Hardware.Fiscal → sidecar x86 :12000 (Hasar/Epson via COM OCX)
GymForge.Hardware.Bio    → sidecar x86 :12001 (ZKTeco libzkfpcsharp)
GymForge.Hardware.Access → sidecar :12002 (TCP ZKTeco C3 / Hikvision)
GymForge.Desktop         → Avalonia app (Program.cs → App.axaml → MainWindow)
GymForge.Api             → Minimal API Kestrel localhost:5000
```

---

## Reglas absolutas

1. **Offline-first**: toda operación persiste en SQLite ANTES de intentar sync cloud.
2. **Multi-tenant**: SIEMPRE filtrar por `CompanyId` + `SiteId`. Nunca datos cross-tenant.
3. **AccessLog y AuditLog son append-only**: NUNCA UPDATE ni DELETE en esas tablas.
4. **FingerprintTemplate**: solo como `byte[]` en formato binario ZKTeco. Nunca base64 en DB.
5. **PAN de tarjetas**: nunca en DB. Solo `CardLast4` + token del procesador.
6. **Tests antes de commit**: `dotnet test` debe pasar. Si falla, no avanzar.
7. **Un commit por feature**: `feat: [módulo] descripción` / `fix:` / `chore:` / `test:`.

---

## Comandos frecuentes

```powershell
# Correr la app de escritorio (la real; el mockup/ es solo diseño)
.\scripts\run.ps1
# Correr desde cero (borra la DB local y re-siembra; PIN admin: 1234)
.\scripts\run.ps1 -Reset

# Build completo + tests
.\scripts\build.ps1

# Solo tests
dotnet test GymForge.sln

# Reset DB + seed
.\scripts\seed.ps1

# Agregar migración EF (requiere .NET 9 SDK; usa el design-time factory de Infrastructure)
dotnet ef migrations add NombreMigracion --project src/GymForge.Infrastructure --startup-project src/GymForge.Infrastructure

# Aplicar migraciones
dotnet ef database update --project src/GymForge.Infrastructure --startup-project src/GymForge.Api
```

---

## Estado del Sprint 1 (en curso)

### ✅ Completado
- Estructura completa de solución (10 proyectos + 3 test projects)
- Domain entities: Company, Site, Staff, Member, MembershipType, Membership, Charge, Payment, PaymentAllocation, AccessLog, AuditLog, Exercise, Routine (3 niveles), BodyMeasurement, ClassSchedule, Booking, Product, StockBySite, Sale, Shift, CashMovement
- Enums, ValueObjects (Money, Cuit, PhoneNumber), Domain Events
- EF Core: DbContext + todas las configuraciones Fluent API + migración InitialCreate manual
- Seed: 80+ ejercicios globales + company/site/staff/planes de ejemplo
- Application: MediatR handlers (CreateMember, GetMembers, SearchMembers, CreateMembership, FreezeMembership, CancelMembership, CreateCharge, ProcessPayment)
- Algoritmo Gatekeeper (ValidateSwipeUseCase) — 8 validaciones en secuencia
- Repositories: MemberRepository, MembershipRepository, ChargeRepository, AccessLogRepository
- Infrastructure services: SystemClock, InProcessEventBus
- Hardware interfaces + sidecars stub (FiscalBroker, BioBroker, AccessBroker)
- Avalonia Desktop: MainWindow (sidebar + topbar), MembersListView, CheckInKioskView
- ViewModels: MainWindowViewModel, MembersListViewModel, CreateMemberViewModel, CheckInKioskViewModel
- Converters: SidebarWidthConverter, CollapsedIconConverter, StatusColorConverter, NavSectionActiveConverter
- GymForge.Api: Minimal API con endpoints /members, /memberships, /access/swipe
- Tests: GatekeeperTests (6 casos), MembershipStateMachineTests (8 casos), MemberRepositoryIntegrationTests (4 casos)
- Router de navegación: ContentControl + DataTemplates, Stack historial, Alt+← para volver
- Dark/Light theme toggle: OS auto-detect via PlatformSettings + toggle manual
- NavButton.active CSS class via NavSectionActiveConverter
- DashboardView: 4 KPIs (socios activos, check-ins, mora, recaudación)
- PlaceholderView: panel "próximamente" para secciones sin implementar
- ChargesView: DataGrid cobros + badge de estado + total saldo
- PaymentModalViewModel: monto libre / cobro específico, N:M allocation via ProcessPaymentCommand
- MemberDetailView: 5 tabs (Datos / Membresías / Cobros / Accesos / Rutinas)
- CreateMemberView: formulario 2 columnas, foto + huella mock (Sprint 2: hardware real)

### ✅ Sprint 1 — COMPLETADO

### ✅ Fundamentos (post-Sprint 1)
- [x] Build verde: tests compilan (`<Using Include="Xunit"/>`) y sidecars con `Serilog.Sinks.Console`
- [x] CI en GitHub Actions (`.github/workflows/ci.yml`): build + test en windows-latest
- [x] Migración EF real con `dotnet ef` (carpeta `Migrations/`) + arranque con `MigrateAsync()`
- [x] Tests de handlers (CreateMember, ProcessPayment) + aislamiento multi-tenant — 29/29 verdes

### 🔄 Sprint 2 — en curso
- [x] Caja: apertura/cierre/arqueo + CashMovement (handlers + UI CashView)
- [x] Sesión de cajero: login por PIN (PBKDF2) + ShiftId en SessionContext
- [x] Multi-sede: selector de sede en topbar + SessionContext (tenant real)
- [x] Cobro unificado: pago persiste + impacta caja (ICashRegister); modal de
      cobro/venta (membresía → socio+plan / producto → socio+producto+stock);
      recaudación del Dashboard desde pagos reales
- [x] POS: venta a no socio (Payment.MemberId nullable + Payment.SaleId) con
      toggle "consumidor final" en Caja; recibo de venta con detalle armado
      desde Sale.Lines (con IVA por línea). Pendiente: scanner HID.
- [ ] Facturación AFIP WSFE (SOAP) — FiscalBroker completo
- [ ] WhatsApp via Twilio/Wassenger (notificaciones cobros)
- [ ] Dunning automático (job nocturno 5 etapas)
- [ ] Webcam (foto socio) + biometría ZKTeco real (BioBroker /enroll)
- [x] UI del mockup: Planes (CRUD), Reportes (recaudación por período),
      Config (gimnasio+sedes), Clases v1 (catálogo) y Rutinas v1 (biblioteca
      de ejercicios con búsqueda/filtro)
- [x] Clases v2: agenda semanal de horarios + reservas de socios (cupo,
      reserva/cancelación) · Rutinas v2: armador por socio (rutina → días →
      ejercicios de la biblioteca con series/reps). "Rutinas" es un hub con
      pestañas Armador | Biblioteca.
- [x] Vista Productos: catálogo + stock por sede (ABM + ajuste de stock con
      punto de reposición y aviso "Reponer")
- [x] Recibo PDF al cobrar (QuestPDF): se genera y abre solo al confirmar
      cualquier cobro (modal de pago y de venta); queda en
      `%LOCALAPPDATA%\GymForge\recibos\AAAA-MM\`. Reimpresión desde el historial
      de pagos (Reportes → botón "Recibo"): regenera el PDF con BuildReceiptQuery.
- [x] Licenciamiento v1 (detalle en Sprint 3)
- [x] Dashboard premium: KPIs con tendencia y subtítulos reales, gráfico de
      recaudación de 30 días (XAML nativo), vencimientos de la semana y
      actividad del día con avatares; verificado en light y dark
      (capturas 01 y 14 de tools/GymForge.Screenshots)
- [x] Alta + cobro en un paso: "Nuevo socio" puede vender el plan y cobrar la
      primera cuota ahí mismo (sección "Membresía inicial"); abre el recibo PDF
      y si el cobro falla el reintento no duplica al socio

### 🚀 Puesta en marcha del piloto (gimnasio real, sin AFIP ni biometría)
- [x] Cambio de PIN del cajero/admin desde Configuración → Seguridad
      (`ChangePinCommand`, PBKDF2). Aviso visible mientras siga activo el PIN de
      fábrica (1234) vía `CheckDefaultPinQuery`. El login es por PIN, así que el
      PIN actual identifica al staff y se valida que no choque con otro.
- [x] Importación del padrón por CSV (Socios → "Importar CSV"): `MemberCsvParser`
      (autodetecta separador `,`/`;`, columnas por nombre sin acentos; requeridas
      nombre/apellido/dni) + `ImportMembersCommand` (omite duplicados por DNI en
      base y dentro del archivo, respeta el límite de licencia y no aborta ante
      filas inválidas). Los socios importados quedan activos.
- [x] Red de seguridad ante excepciones: handlers globales de `AppDomain` y
      `TaskScheduler.UnobservedTaskException` (Program.cs) + `Dispatcher.UIThread`
      (App.axaml.cs) → quedan registradas en `%LOCALAPPDATA%\GymForge\logs` en vez
      de cerrar la app de recepción.

### 🔲 Distribución y licencias (Sprint 3)
- [x] Ejecutable único autocontenido (`scripts/publish.ps1` → `dist/GymForge.exe`)
- [x] Ícono de la app (.ico multi-resolución, regenerable con `scripts/make-icon.ps1`)
      + metadata del exe (GymForge v0.2.0, empresa, descripción) + ícono de ventana
- [x] Kit de instalación en pendrive: `scripts/make-usb.ps1 -Destino F:\` arma la carpeta
      con Setup + `.cer` + `trust-cert.ps1` + instructivo + CHECKSUMS (y nunca copia el
      `.pfx`; avisa si detecta una clave privada en el pendrive). Ver `docs/DISTRIBUCION.md`.
- [ ] Distribución web: GitHub Releases (`vpk upload github`) primero; landing después
- [x] Onboarding de primer arranque: en una instalación nueva la base queda limpia
      (solo la biblioteca global de ejercicios) y aparece un asistente (`OnboardingWindow`)
      que crea el gimnasio real (nombre+CUIT, sede, responsable+PIN, color, planes de
      ejemplo) vía `CompleteOnboardingCommand`. El gimnasio demo (PIN 1234) quedó detrás
      de `GYMFORGE_SEED_COMPANY=1` (lo setean `run.ps1`, `seed.ps1` y el tool de capturas).
- [x] Pasada visual premium: design system (elevación, cifras tabulares), branding por
      gimnasio (logo + color de acento que re-tinta toda la UI, en Config → Marca y en el
      recibo), shell agrupado, kiosk de check-in y gráfico de recaudación tipo área.
- [x] Licenciamiento v1 (offline): claves firmadas ECDSA P-256 (`GYMF.payload.firma`)
      verificadas con la clave pública embebida en `LicenseService`; tier Free
      (1 sede / 50 socios) aplicado en alta de socio y de sede; activación en
      Config → Licencia; 15 días de gracia tras vencer y luego degrada a Free.
      Emisión de claves: `.\scripts\licencia.ps1` (asistente: gimnasio, plan, CUIT, meses;
      copia la clave al portapapeles, deja el mensaje para el cliente y registra la venta
      en `licencias-emitidas.csv`). Subcomandos: `list`, `renew` (encadena desde el
      vencimiento anterior), `show <clave>` (verifica con el mismo LicenseService que la
      app). Presets: Basico 1/300 · Pro 3/1000 · Ilimitado 99/100000.
      (la clave privada vive en `%LOCALAPPDATA%\GymForge\vendor\` — NO está en el
      repo; respaldarla. Si se pierde: `init-keys` + re-embeber la pública + rebuild).
- [ ] Licenciamiento v2: validación/revocación online + venta vía Mercado Pago.
- [x] Firma de código v1 — **autofirmada (gratis)**: `scripts/sign-selfsigned.ps1` genera el
      certificado (5 años, store del usuario, respaldo `.pfx` con `-BackupPfx`) y exporta el
      `.cer` a `%LOCALAPPDATA%\GymForge\vendor\`. `pack.ps1` y `publish.ps1` firman si está
      `GYMFORGE_SIGN_THUMBPRINT`. En la PC del gimnasio se corre `scripts/trust-cert.ps1`
      (admin, una vez) y el instalador pasa a mostrar "GymForge" como editor verificado.
      Instructivo para el cliente: `scripts/LEEME-certificado.txt`.
- [ ] Firma de código v2: certificado de CA (Azure Trusted Signing ~USD 10/mes) para que no
      haga falta instalar nada en PCs ajenas. Ver `docs/CODE-SIGNING.md`.

> Nota (dev): `run.ps1`/`seed.ps1` setean `GYMFORGE_SEED_COMPANY=1`, así que siembran el
> gimnasio demo (PIN admin **1234**, 2 sedes). Un `dotnet run` **sin** esa env var deja la
> base limpia y muestra el onboarding. Si tu `%LOCALAPPDATA%\GymForge\gymforge.db` viene de
> una versión anterior, el arranque la adopta (baseline) pero conserva el PIN viejo; borrala
> para regenerarla (o para probar el onboarding desde cero).

---

## Convenciones de código

```csharp
// Namespaces file-scoped
namespace GymForge.Domain.Entities;

// Constructores privados + factory methods estáticos
private Member() { }
public static Member Create(...) => new Member { ... };

// Properties con private setters
public string FirstName { get; private set; } = string.Empty;

// Commands/Queries MediatR como records
public record CreateMemberCommand(...) : IRequest<MemberDto>;

// ViewModels con MVVM Toolkit
[ObservableProperty] private string _firstName = string.Empty;
[RelayCommand] private async Task SaveAsync() { ... }
```

---

## Sidecars — contratos HTTP

| Sidecar | Puerto | Rutas principales |
|---------|--------|-------------------|
| FiscalBroker | 12000 | POST /ticket · POST /cancel · GET /status · POST /z-report |
| BioBroker | 12001 | POST /enroll · POST /verify · WS /swipes |
| AccessBroker | 12002 | POST /open-door · GET /doors · WS /events |

---

## Contexto de negocio

- Moneda: ARS (pesos argentinos). Formato: `$35.000,00`
- Zona horaria default: `Argentina Standard Time` (UTC-3, sin DST)
- CUIT: validación de dígito verificador obligatoria (ver `Cuit.cs`)
- Facturación: AFIP Webservice WSFE para Facturas A/B/C con CAE
- Anti-passback: 5 minutos configurable por `GatekeeperConfig`
- Freeze de membresía: máx 90 días/año configurables por Company
