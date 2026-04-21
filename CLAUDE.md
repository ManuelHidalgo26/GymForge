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
| Reporting | QuestPDF (tickets) + ClosedXML (Excel) |
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
GymForge.Sync            → motor de sync cloud (Sprint 2)
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
# Build completo + tests
.\scripts\build.ps1

# Solo tests
dotnet test GymForge.sln

# Reset DB + seed
.\scripts\seed.ps1

# Agregar migración EF (requiere .NET 9 SDK)
dotnet ef migrations add NombreMigracion --project src/GymForge.Infrastructure --startup-project src/GymForge.Api

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
- Converters: SidebarWidthConverter, CollapsedIconConverter, StatusColorConverter
- GymForge.Api: Minimal API con endpoints /members, /memberships, /access/swipe
- Tests: GatekeeperTests (6 casos), MembershipStateMachineTests (8 casos), MemberRepositoryIntegrationTests (4 casos)

### 🔲 Pendiente Sprint 1
- [ ] Módulo Cobros UI (listado charges + modal de pago)
- [ ] Ficha de socio con tabs (datos, membresías, cobros, accesos, rutinas)
- [ ] Formulario alta socio — webcam + huella mock (Sprint 2 hardware real)
- [ ] Atajos de teclado globales en MainWindow
- [ ] Router de navegación Desktop (conectar sidebar a vistas)
- [ ] Dark/Light theme toggle automático

### 🔲 Sprint 2
- [ ] Caja: apertura/cierre/arqueo
- [ ] POS: venta productos, scanner HID
- [ ] Facturación AFIP WSFE (SOAP) — FiscalBroker completo
- [ ] WhatsApp via Twilio/Wassenger
- [ ] Dunning automático (job nocturno 5 etapas)
- [ ] Webcam + biometría ZKTeco real

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
