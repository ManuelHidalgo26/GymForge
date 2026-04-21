using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Companies",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                LegalName = t.Column<string>(maxLength: 200, nullable: false),
                TaxId = t.Column<string>(maxLength: 20, nullable: false),
                LogoUrl = t.Column<string>(maxLength: 500, nullable: true),
                PrimaryLanguage = t.Column<string>(maxLength: 10, nullable: false, defaultValue: "es-AR"),
                Currency = t.Column<string>(maxLength: 5, nullable: false, defaultValue: "ARS"),
                Timezone = t.Column<string>(maxLength: 50, nullable: false, defaultValue: "Argentina Standard Time"),
                FiscalConfigJson = t.Column<string>(nullable: true),
                BrandColorHex = t.Column<string>(maxLength: 10, nullable: false, defaultValue: "#6366F1"),
                IsActive = t.Column<bool>(nullable: false, defaultValue: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_Companies", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Exercises",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                Name = t.Column<string>(maxLength: 150, nullable: false),
                Description = t.Column<string>(nullable: true),
                Instructions = t.Column<string>(nullable: true),
                PrimaryMuscleGroup = t.Column<string>(maxLength: 20, nullable: false),
                SecondaryMuscleGroupsJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                Equipment = t.Column<string>(maxLength: 20, nullable: false),
                MovementType = t.Column<string>(maxLength: 20, nullable: false),
                Difficulty = t.Column<int>(nullable: false, defaultValue: 3),
                VideoUrl = t.Column<string>(maxLength: 500, nullable: true),
                ImageUrl = t.Column<string>(maxLength: 500, nullable: true),
                AnimatedGifUrl = t.Column<string>(maxLength: 500, nullable: true),
                IsUnilateral = t.Column<bool>(nullable: false),
                IsTimed = t.Column<bool>(nullable: false),
                TenantId = t.Column<Guid>(nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_Exercises", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Sites",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                Name = t.Column<string>(maxLength: 150, nullable: false),
                Address = t.Column<string>(maxLength: 300, nullable: false),
                Phone = t.Column<string>(maxLength: 30, nullable: true),
                ManagerStaffId = t.Column<Guid>(nullable: true),
                OpenHoursJson = t.Column<string>(nullable: true),
                GeoLat = t.Column<double>(nullable: true),
                GeoLng = t.Column<double>(nullable: true),
                BrandColorHex = t.Column<string>(maxLength: 10, nullable: false),
                IsActive = t.Column<bool>(nullable: false, defaultValue: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Sites", x => x.Id);
                t.ForeignKey("FK_Sites_Companies_CompanyId", x => x.CompanyId, "Companies", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Staff",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                SiteIdsJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                FirstName = t.Column<string>(maxLength: 100, nullable: false),
                LastName = t.Column<string>(maxLength: 100, nullable: false),
                Email = t.Column<string>(maxLength: 254, nullable: true),
                Mobile = t.Column<string>(maxLength: 30, nullable: true),
                Role = t.Column<string>(maxLength: 20, nullable: false),
                SpecialtiesJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                CertificationsJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                PermissionsJson = t.Column<string>(nullable: false, defaultValue: "{}"),
                PinCodeHash = t.Column<string>(maxLength: 200, nullable: false),
                ColorHex = t.Column<string>(maxLength: 10, nullable: false),
                CommissionPctMemberships = t.Column<decimal>(type: "DECIMAL(5,2)", nullable: false),
                CommissionPctPt = t.Column<decimal>(type: "DECIMAL(5,2)", nullable: false),
                CommissionPctProducts = t.Column<decimal>(type: "DECIMAL(5,2)", nullable: false),
                IsActive = t.Column<bool>(nullable: false, defaultValue: true),
                AvatarUrl = t.Column<string>(maxLength: 500, nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Staff", x => x.Id);
                t.ForeignKey("FK_Staff_Companies_CompanyId", x => x.CompanyId, "Companies", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MembershipTypes",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                Name = t.Column<string>(maxLength: 100, nullable: false),
                Basis = t.Column<string>(maxLength: 20, nullable: false),
                DurationValue = t.Column<int>(nullable: false),
                DurationUnit = t.Column<string>(nullable: false),
                Price = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                SignupFee = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                BillingCycle = t.Column<string>(nullable: false),
                AllowedDoorIdsJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                AllowedClassTypesJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                ClassCreditsIncluded = t.Column<int>(nullable: true),
                PtSessionsIncluded = t.Column<int>(nullable: false),
                ScheduleRestrictionJson = t.Column<string>(nullable: true),
                AgeMin = t.Column<int>(nullable: true),
                AgeMax = t.Column<int>(nullable: true),
                GenderRestriction = t.Column<string>(maxLength: 20, nullable: true),
                BenefitIdsJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                IsActive = t.Column<bool>(nullable: false, defaultValue: true),
                Description = t.Column<string>(nullable: true),
                ColorHex = t.Column<string>(maxLength: 10, nullable: false),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_MembershipTypes", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Members",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                SiteId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                FirstName = t.Column<string>(maxLength: 100, nullable: false),
                LastName = t.Column<string>(maxLength: 100, nullable: false),
                DocumentType = t.Column<string>(maxLength: 10, nullable: false),
                DocumentNumber = t.Column<string>(maxLength: 20, nullable: false),
                Email = t.Column<string>(maxLength: 254, nullable: true),
                Mobile = t.Column<string>(maxLength: 30, nullable: true),
                BirthDate = t.Column<string>(type: "TEXT", nullable: true),
                Gender = t.Column<string>(maxLength: 20, nullable: false),
                PhotoUrl = t.Column<string>(maxLength: 500, nullable: true),
                SignatureUrl = t.Column<string>(maxLength: 500, nullable: true),
                TagSerial = t.Column<string>(maxLength: 50, nullable: true),
                FingerprintTemplate = t.Column<byte[]>(type: "BLOB", nullable: true),
                EmergencyName = t.Column<string>(nullable: true),
                EmergencyPhone = t.Column<string>(nullable: true),
                EmergencyRelation = t.Column<string>(nullable: true),
                MedicalConditions = t.Column<string>(nullable: true),
                Medications = t.Column<string>(nullable: true),
                Allergies = t.Column<string>(nullable: true),
                BloodType = t.Column<string>(maxLength: 10, nullable: false),
                WaiverSignedAt = t.Column<DateTime>(nullable: true),
                ParQId = t.Column<Guid>(nullable: true),
                ReferredByMemberId = t.Column<Guid>(nullable: true),
                Source = t.Column<string>(maxLength: 30, nullable: false),
                SalesRepId = t.Column<Guid>(nullable: true),
                Status = t.Column<string>(maxLength: 20, nullable: false),
                JoinDate = t.Column<string>(type: "TEXT", nullable: true),
                CustomFieldsJson = t.Column<string>(nullable: true),
                MarketingConsent = t.Column<bool>(nullable: false),
                Observations = t.Column<string>(nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Members", x => x.Id);
                t.ForeignKey("FK_Members_Sites_SiteId", x => x.SiteId, "Sites", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Memberships",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MemberId = t.Column<Guid>(nullable: false),
                MembershipTypeId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                SiteId = t.Column<Guid>(nullable: false),
                StartDate = t.Column<string>(type: "TEXT", nullable: false),
                EndDate = t.Column<string>(type: "TEXT", nullable: true),
                Status = t.Column<string>(maxLength: 30, nullable: false),
                FreezeStart = t.Column<string>(type: "TEXT", nullable: true),
                FreezeEnd = t.Column<string>(type: "TEXT", nullable: true),
                FreezeReason = t.Column<string>(maxLength: 500, nullable: true),
                FreezeCountUsed = t.Column<int>(nullable: false),
                NextBillingDate = t.Column<string>(type: "TEXT", nullable: true),
                AutoRenew = t.Column<bool>(nullable: false),
                ContractPdfUrl = t.Column<string>(maxLength: 500, nullable: true),
                SignedAt = t.Column<DateTime>(nullable: true),
                DiscountId = t.Column<Guid>(nullable: true),
                VisitsRemaining = t.Column<int>(nullable: true),
                SoldByStaffId = t.Column<Guid>(nullable: true),
                CommissionAmount = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                CancelDate = t.Column<DateTime>(nullable: true),
                CancelReason = t.Column<string>(maxLength: 500, nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Memberships", x => x.Id);
                t.ForeignKey("FK_Memberships_Members_MemberId", x => x.MemberId, "Members", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_Memberships_MembershipTypes_MembershipTypeId", x => x.MembershipTypeId, "MembershipTypes", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Charges",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MemberId = t.Column<Guid>(nullable: false),
                MembershipId = t.Column<Guid>(nullable: true),
                CompanyId = t.Column<Guid>(nullable: false),
                SiteId = t.Column<Guid>(nullable: false),
                ConceptType = t.Column<string>(maxLength: 30, nullable: false),
                Description = t.Column<string>(maxLength: 300, nullable: false),
                Amount = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                TaxAmount = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                AmountPaid = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                DueDate = t.Column<string>(type: "TEXT", nullable: false),
                Status = t.Column<string>(maxLength: 20, nullable: false),
                InvoiceNumber = t.Column<string>(maxLength: 20, nullable: true),
                FiscalCae = t.Column<string>(maxLength: 20, nullable: true),
                FiscalXmlUrl = t.Column<string>(maxLength: 500, nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Charges", x => x.Id);
                t.ForeignKey("FK_Charges_Members_MemberId", x => x.MemberId, "Members", "Id", onDelete: ReferentialAction.Restrict);
                t.ForeignKey("FK_Charges_Memberships_MembershipId", x => x.MembershipId, "Memberships", "Id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "Payments",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MemberId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                SiteId = t.Column<Guid>(nullable: false),
                Amount = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                Method = t.Column<string>(maxLength: 20, nullable: false),
                Processor = t.Column<string>(maxLength: 50, nullable: true),
                ProcessorTxId = t.Column<string>(maxLength: 100, nullable: true),
                CardLast4 = t.Column<string>(maxLength: 4, nullable: true),
                CardBrand = t.Column<string>(maxLength: 20, nullable: true),
                ReceivedAt = t.Column<DateTime>(nullable: false),
                ShiftId = t.Column<Guid>(nullable: true),
                CashierId = t.Column<Guid>(nullable: false),
                Status = t.Column<string>(maxLength: 20, nullable: false),
                Notes = t.Column<string>(nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Payments", x => x.Id);
                t.ForeignKey("FK_Payments_Members_MemberId", x => x.MemberId, "Members", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PaymentAllocations",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                PaymentId = t.Column<Guid>(nullable: false),
                ChargeId = t.Column<Guid>(nullable: false),
                Amount = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                t.ForeignKey("FK_PaymentAllocations_Payments_PaymentId", x => x.PaymentId, "Payments", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_PaymentAllocations_Charges_ChargeId", x => x.ChargeId, "Charges", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AccessLogs",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MemberId = t.Column<Guid>(nullable: false),
                MembershipId = t.Column<Guid>(nullable: true),
                DoorId = t.Column<int>(nullable: false),
                SiteId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                SwipedAt = t.Column<DateTime>(nullable: false),
                Method = t.Column<string>(maxLength: 20, nullable: false),
                TagSerial = t.Column<string>(maxLength: 50, nullable: true),
                AccessGranted = t.Column<bool>(nullable: false),
                DenialReason = t.Column<string>(maxLength: 30, nullable: true),
                Direction = t.Column<string>(maxLength: 10, nullable: false),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_AccessLogs", x => x.Id);
                t.ForeignKey("FK_AccessLogs_Members_MemberId", x => x.MemberId, "Members", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                UserId = t.Column<Guid>(nullable: true),
                EntityType = t.Column<string>(maxLength: 100, nullable: false),
                EntityId = t.Column<Guid>(nullable: false),
                Action = t.Column<string>(maxLength: 50, nullable: false),
                DiffJson = t.Column<string>(nullable: true),
                Ip = t.Column<string>(maxLength: 45, nullable: true),
                Timestamp = t.Column<DateTime>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_AuditLogs", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Shifts",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                RegisterId = t.Column<Guid>(nullable: true),
                CashierId = t.Column<Guid>(nullable: false),
                SiteId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                OpenedAt = t.Column<DateTime>(nullable: false),
                ClosedAt = t.Column<DateTime>(nullable: true),
                OpeningCash = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                ClosingCashDeclared = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                ClosingCashSystem = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                Status = t.Column<string>(maxLength: 20, nullable: false),
                Notes = t.Column<string>(maxLength: 1000, nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_Shifts", x => x.Id));

        migrationBuilder.CreateTable(
            name: "CashMovements",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                ShiftId = t.Column<Guid>(nullable: false),
                Type = t.Column<string>(maxLength: 20, nullable: false),
                Category = t.Column<string>(maxLength: 30, nullable: false),
                Amount = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                ReferenceId = t.Column<Guid>(nullable: true),
                Notes = t.Column<string>(maxLength: 500, nullable: true),
                MovedAt = t.Column<DateTime>(nullable: false),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_CashMovements", x => x.Id);
                t.ForeignKey("FK_CashMovements_Shifts_ShiftId", x => x.ShiftId, "Shifts", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Products",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                Sku = t.Column<string>(maxLength: 50, nullable: false),
                Barcode = t.Column<string>(maxLength: 50, nullable: true),
                Name = t.Column<string>(maxLength: 200, nullable: false),
                Description = t.Column<string>(nullable: true),
                CategoryId = t.Column<Guid>(nullable: true),
                Brand = t.Column<string>(nullable: true),
                Unit = t.Column<string>(maxLength: 20, nullable: false),
                CostPrice = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                SalePrice = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                TaxRate = t.Column<decimal>(type: "DECIMAL(5,4)", nullable: false),
                ImageUrl = t.Column<string>(nullable: true),
                IsActive = t.Column<bool>(nullable: false, defaultValue: true),
                IsSellableOnline = t.Column<bool>(nullable: false),
                CommissionStaffPct = t.Column<decimal>(nullable: false),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_Products", x => x.Id));

        migrationBuilder.CreateTable(
            name: "StockBySite",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                ProductId = t.Column<Guid>(nullable: false),
                SiteId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                Qty = t.Column<decimal>(type: "DECIMAL(12,3)", nullable: false),
                ReorderPoint = t.Column<decimal>(type: "DECIMAL(12,3)", nullable: false),
                SupplierId = t.Column<Guid>(nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_StockBySite", x => x.Id);
                t.ForeignKey("FK_StockBySite_Products_ProductId", x => x.ProductId, "Products", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Routines",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MemberId = t.Column<Guid>(nullable: false),
                TrainerId = t.Column<Guid>(nullable: true),
                CompanyId = t.Column<Guid>(nullable: false),
                Name = t.Column<string>(maxLength: 150, nullable: false),
                Goal = t.Column<string>(maxLength: 20, nullable: false),
                StartDate = t.Column<string>(type: "TEXT", nullable: false),
                EndDate = t.Column<string>(type: "TEXT", nullable: true),
                FrequencyPerWeek = t.Column<int>(nullable: false),
                Difficulty = t.Column<int>(nullable: false),
                Notes = t.Column<string>(nullable: true),
                TemplateId = t.Column<Guid>(nullable: true),
                IsActive = t.Column<bool>(nullable: false, defaultValue: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Routines", x => x.Id);
                t.ForeignKey("FK_Routines_Members_MemberId", x => x.MemberId, "Members", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BodyMeasurements",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MemberId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                MeasuredAt = t.Column<DateTime>(nullable: false),
                TakenByStaffId = t.Column<Guid>(nullable: true),
                Method = t.Column<string>(maxLength: 20, nullable: false),
                WeightKg = t.Column<decimal>(type: "DECIMAL(6,2)", nullable: true),
                HeightCm = t.Column<decimal>(type: "DECIMAL(6,2)", nullable: true),
                BodyFatPct = t.Column<decimal>(type: "DECIMAL(5,2)", nullable: true),
                BodyFatMethod = t.Column<string>(nullable: true),
                MuscleMassKg = t.Column<decimal>(type: "DECIMAL(6,2)", nullable: true),
                BodyWaterPct = t.Column<decimal>(nullable: true),
                VisceralFat = t.Column<int>(nullable: true),
                BasalMetabolicRate = t.Column<int>(nullable: true),
                NeckCm = t.Column<decimal>(nullable: true),
                ShouldersCm = t.Column<decimal>(nullable: true),
                ChestCm = t.Column<decimal>(nullable: true),
                WaistCm = t.Column<decimal>(nullable: true),
                AbdomenCm = t.Column<decimal>(nullable: true),
                HipsCm = t.Column<decimal>(nullable: true),
                LeftBicepCm = t.Column<decimal>(nullable: true),
                RightBicepCm = t.Column<decimal>(nullable: true),
                LeftForearmCm = t.Column<decimal>(nullable: true),
                RightForearmCm = t.Column<decimal>(nullable: true),
                LeftThighCm = t.Column<decimal>(nullable: true),
                RightThighCm = t.Column<decimal>(nullable: true),
                LeftCalfCm = t.Column<decimal>(nullable: true),
                RightCalfCm = t.Column<decimal>(nullable: true),
                TricepsMm = t.Column<decimal>(nullable: true),
                SubscapularMm = t.Column<decimal>(nullable: true),
                SuprailiacMm = t.Column<decimal>(nullable: true),
                AbdominalMm = t.Column<decimal>(nullable: true),
                ThighMm = t.Column<decimal>(nullable: true),
                ChestMm = t.Column<decimal>(nullable: true),
                PhotoFront = t.Column<string>(nullable: true),
                PhotoSide = t.Column<string>(nullable: true),
                PhotoBack = t.Column<string>(nullable: true),
                Notes = t.Column<string>(nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_BodyMeasurements", x => x.Id);
                t.ForeignKey("FK_BodyMeasurements_Members_MemberId", x => x.MemberId, "Members", "Id", onDelete: ReferentialAction.Cascade);
            });

        // ── Classes & Bookings ───────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "ClassDescriptions",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                Name = t.Column<string>(maxLength: 150, nullable: false),
                Category = t.Column<string>(maxLength: 80, nullable: true),
                Description = t.Column<string>(nullable: true),
                ImageUrl = t.Column<string>(maxLength: 500, nullable: true),
                DefaultDurationMin = t.Column<int>(nullable: false, defaultValue: 60),
                DefaultCapacity = t.Column<int>(nullable: false, defaultValue: 20),
                EquipmentNeeded = t.Column<string>(maxLength: 300, nullable: true),
                IsActive = t.Column<bool>(nullable: false, defaultValue: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_ClassDescriptions", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ClassSchedules",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                ClassDescriptionId = t.Column<Guid>(nullable: false),
                InstructorId = t.Column<Guid>(nullable: true),
                SiteId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                RoomId = t.Column<string>(maxLength: 50, nullable: true),
                StartDatetime = t.Column<DateTime>(nullable: false),
                EndDatetime = t.Column<DateTime>(nullable: false),
                Capacity = t.Column<int>(nullable: false),
                WaitlistCapacity = t.Column<int>(nullable: false, defaultValue: 5),
                IsRecurring = t.Column<bool>(nullable: false),
                RecurrenceRule = t.Column<string>(maxLength: 500, nullable: true),
                BookingOpenFrom = t.Column<DateTime>(nullable: true),
                BookingCloseBeforeMin = t.Column<int>(nullable: false, defaultValue: 30),
                CancelDeadlineMin = t.Column<int>(nullable: false, defaultValue: 120),
                LateCancelFee = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                NoShowFee = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                MinAge = t.Column<int>(nullable: true),
                DropInPrice = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                IsCancelled = t.Column<bool>(nullable: false),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_ClassSchedules", x => x.Id);
                t.ForeignKey("FK_ClassSchedules_ClassDescriptions_ClassDescriptionId",
                    x => x.ClassDescriptionId, "ClassDescriptions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Bookings",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MemberId = t.Column<Guid>(nullable: false),
                ClassScheduleId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                BookingType = t.Column<string>(maxLength: 20, nullable: false),
                Status = t.Column<string>(maxLength: 20, nullable: false),
                WaitlistPosition = t.Column<int>(nullable: true),
                BookedAt = t.Column<DateTime>(nullable: false),
                CancelledAt = t.Column<DateTime>(nullable: true),
                CheckedInAt = t.Column<DateTime>(nullable: true),
                BookingChannel = t.Column<string>(maxLength: 20, nullable: false),
                PaymentChargeId = t.Column<Guid>(nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Bookings", x => x.Id);
                t.ForeignKey("FK_Bookings_Members_MemberId", x => x.MemberId, "Members", "Id", onDelete: ReferentialAction.Restrict);
                t.ForeignKey("FK_Bookings_ClassSchedules_ClassScheduleId", x => x.ClassScheduleId, "ClassSchedules", "Id", onDelete: ReferentialAction.Cascade);
            });

        // ── POS ──────────────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "Sales",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MemberId = t.Column<Guid>(nullable: true),
                CashierId = t.Column<Guid>(nullable: false),
                SiteId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                RegisterId = t.Column<Guid>(nullable: true),
                ShiftId = t.Column<Guid>(nullable: true),
                SaleDatetime = t.Column<DateTime>(nullable: false),
                Subtotal = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                DiscountTotal = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                TaxTotal = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                Total = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                PaymentStatus = t.Column<string>(maxLength: 20, nullable: false),
                InvoiceNumber = t.Column<string>(maxLength: 20, nullable: true),
                FiscalCae = t.Column<string>(maxLength: 20, nullable: true),
                FiscalXmlUrl = t.Column<string>(maxLength: 500, nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_Sales", x => x.Id));

        migrationBuilder.CreateTable(
            name: "SaleLines",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                SaleId = t.Column<Guid>(nullable: false),
                ProductId = t.Column<Guid>(nullable: true),
                MembershipTypeId = t.Column<Guid>(nullable: true),
                Description = t.Column<string>(maxLength: 300, nullable: false),
                Quantity = t.Column<decimal>(type: "DECIMAL(12,3)", nullable: false),
                UnitPrice = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                Discount = t.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                TaxRate = t.Column<decimal>(type: "DECIMAL(5,4)", nullable: false),
                CommissionStaffId = t.Column<Guid>(nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_SaleLines", x => x.Id);
                t.ForeignKey("FK_SaleLines_Sales_SaleId", x => x.SaleId, "Sales", "Id", onDelete: ReferentialAction.Cascade);
            });

        // ── Workout logs & Routine sub-tables ────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "RoutineDays",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                RoutineId = t.Column<Guid>(nullable: false),
                DayNumber = t.Column<int>(nullable: false),
                Name = t.Column<string>(maxLength: 100, nullable: false),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_RoutineDays", x => x.Id);
                t.ForeignKey("FK_RoutineDays_Routines_RoutineId", x => x.RoutineId, "Routines", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RoutineItems",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                RoutineDayId = t.Column<Guid>(nullable: false),
                ExerciseId = t.Column<Guid>(nullable: false),
                Rank = t.Column<int>(nullable: false),
                RestSeconds = t.Column<int>(nullable: false, defaultValue: 60),
                Tempo = t.Column<string>(maxLength: 20, nullable: true),
                Technique = t.Column<string>(maxLength: 20, nullable: false),
                Notes = t.Column<string>(maxLength: 500, nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_RoutineItems", x => x.Id);
                t.ForeignKey("FK_RoutineItems_RoutineDays_RoutineDayId", x => x.RoutineDayId, "RoutineDays", "Id", onDelete: ReferentialAction.Cascade);
                t.ForeignKey("FK_RoutineItems_Exercises_ExerciseId", x => x.ExerciseId, "Exercises", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "RoutineItemSets",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                RoutineItemId = t.Column<Guid>(nullable: false),
                SetNumber = t.Column<int>(nullable: false),
                TargetRepsMin = t.Column<int>(nullable: true),
                TargetRepsMax = t.Column<int>(nullable: true),
                TargetWeightKg = t.Column<decimal>(type: "DECIMAL(6,2)", nullable: true),
                TargetDurationSec = t.Column<int>(nullable: true),
                TargetRpe = t.Column<int>(nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_RoutineItemSets", x => x.Id);
                t.ForeignKey("FK_RoutineItemSets_RoutineItems_RoutineItemId", x => x.RoutineItemId, "RoutineItems", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WorkoutLogs",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MemberId = t.Column<Guid>(nullable: false),
                RoutineItemId = t.Column<Guid>(nullable: false),
                CompanyId = t.Column<Guid>(nullable: false),
                PerformedAt = t.Column<DateTime>(nullable: false),
                ActualSetsJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                ActualRepsJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                ActualWeightKgJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                ActualDurationSecJson = t.Column<string>(nullable: false, defaultValue: "[]"),
                Notes = t.Column<string>(nullable: true),
                Rpe = t.Column<int>(nullable: true),
                CreatedAt = t.Column<DateTime>(nullable: false),
                UpdatedAt = t.Column<DateTime>(nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_WorkoutLogs", x => x.Id);
                t.ForeignKey("FK_WorkoutLogs_Members_MemberId", x => x.MemberId, "Members", "Id", onDelete: ReferentialAction.Cascade);
            });

        // ── Indexes ──────────────────────────────────────────────────────────────
        migrationBuilder.CreateIndex("IX_Companies_TaxId", "Companies", "TaxId", unique: true);
        migrationBuilder.CreateIndex("IX_Sites_Company", "Sites", "CompanyId");
        migrationBuilder.CreateIndex("IX_Staff_Email", "Staff", "Email");
        migrationBuilder.CreateIndex("IX_Staff_CompanyRole", "Staff", ["CompanyId", "Role"]);
        migrationBuilder.CreateIndex("IX_Members_Document", "Members", "DocumentNumber");
        migrationBuilder.CreateIndex("IX_Members_Email", "Members", "Email");
        migrationBuilder.CreateIndex("IX_Members_CompanySiteStatus", "Members", ["CompanyId", "SiteId", "Status"]);
        migrationBuilder.CreateIndex("IX_Members_TagSerial", "Members", "TagSerial");
        migrationBuilder.CreateIndex("IX_MembershipTypes_CompanyActive", "MembershipTypes", ["CompanyId", "IsActive"]);
        migrationBuilder.CreateIndex("IX_Memberships_MemberStatus", "Memberships", ["MemberId", "Status"]);
        migrationBuilder.CreateIndex("IX_Memberships_CompanyEndDate", "Memberships", ["CompanyId", "EndDate"]);
        migrationBuilder.CreateIndex("IX_Charges_MemberStatus", "Charges", ["MemberId", "Status"]);
        migrationBuilder.CreateIndex("IX_Charges_DueDateStatus", "Charges", ["CompanyId", "DueDate", "Status"]);
        migrationBuilder.CreateIndex("IX_Payments_Member", "Payments", "MemberId");
        migrationBuilder.CreateIndex("IX_Payments_Shift", "Payments", "ShiftId");
        migrationBuilder.CreateIndex("IX_Allocations_Payment", "PaymentAllocations", "PaymentId");
        migrationBuilder.CreateIndex("IX_Allocations_Charge", "PaymentAllocations", "ChargeId");
        migrationBuilder.CreateIndex("IX_AccessLogs_MemberSwipedAt", "AccessLogs", ["MemberId", "SwipedAt"]);
        migrationBuilder.CreateIndex("IX_AccessLogs_DoorSwipedAt", "AccessLogs", ["DoorId", "SwipedAt"]);
        migrationBuilder.CreateIndex("IX_AccessLogs_SiteSwipedAt", "AccessLogs", ["SiteId", "SwipedAt"]);
        migrationBuilder.CreateIndex("IX_AccessLogs_TagSerial", "AccessLogs", "TagSerial");
        migrationBuilder.CreateIndex("IX_AuditLogs_CompanyTimestamp", "AuditLogs", ["CompanyId", "Timestamp"]);
        migrationBuilder.CreateIndex("IX_AuditLogs_Entity", "AuditLogs", ["EntityType", "EntityId"]);
        migrationBuilder.CreateIndex("IX_Exercises_Tenant", "Exercises", "TenantId");
        migrationBuilder.CreateIndex("IX_Exercises_MuscleGroup", "Exercises", "PrimaryMuscleGroup");
        migrationBuilder.CreateIndex("IX_Routines_Member", "Routines", "MemberId");
        migrationBuilder.CreateIndex("IX_BodyMeasurements_MemberDate", "BodyMeasurements", ["MemberId", "MeasuredAt"]);
        migrationBuilder.CreateIndex("IX_Products_CompanySku", "Products", ["CompanyId", "Sku"], unique: true);
        migrationBuilder.CreateIndex("IX_Products_Barcode", "Products", "Barcode");
        migrationBuilder.CreateIndex("IX_StockBySite_ProductSite", "StockBySite", ["ProductId", "SiteId"], unique: true);
        migrationBuilder.CreateIndex("IX_Shifts_SiteStatus", "Shifts", ["SiteId", "Status"]);
        migrationBuilder.CreateIndex("IX_CashMovements_Shift", "CashMovements", "ShiftId");
        migrationBuilder.CreateIndex("IX_ClassDescriptions_Company", "ClassDescriptions", "CompanyId");
        migrationBuilder.CreateIndex("IX_ClassSchedules_SiteStart", "ClassSchedules", ["SiteId", "StartDatetime"]);
        migrationBuilder.CreateIndex("IX_ClassSchedules_InstructorStart", "ClassSchedules", ["InstructorId", "StartDatetime"]);
        migrationBuilder.CreateIndex("IX_Bookings_MemberStatus", "Bookings", ["MemberId", "Status"]);
        migrationBuilder.CreateIndex("IX_Bookings_ScheduleStatus", "Bookings", ["ClassScheduleId", "Status"]);
        migrationBuilder.CreateIndex("IX_Sales_SiteDate", "Sales", ["SiteId", "SaleDatetime"]);
        migrationBuilder.CreateIndex("IX_Sales_Member", "Sales", "MemberId");
        migrationBuilder.CreateIndex("IX_WorkoutLogs_MemberPerformedAt", "WorkoutLogs", ["MemberId", "PerformedAt"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("PaymentAllocations");
        migrationBuilder.DropTable("Payments");
        migrationBuilder.DropTable("Charges");
        migrationBuilder.DropTable("AccessLogs");
        migrationBuilder.DropTable("AuditLogs");
        migrationBuilder.DropTable("CashMovements");
        migrationBuilder.DropTable("SaleLines");
        migrationBuilder.DropTable("Sales");
        migrationBuilder.DropTable("StockBySite");
        migrationBuilder.DropTable("Products");
        migrationBuilder.DropTable("Bookings");
        migrationBuilder.DropTable("ClassSchedules");
        migrationBuilder.DropTable("ClassDescriptions");
        migrationBuilder.DropTable("BodyMeasurements");
        migrationBuilder.DropTable("WorkoutLogs");
        migrationBuilder.DropTable("RoutineItemSets");
        migrationBuilder.DropTable("RoutineItems");
        migrationBuilder.DropTable("RoutineDays");
        migrationBuilder.DropTable("Routines");
        migrationBuilder.DropTable("Memberships");
        migrationBuilder.DropTable("Members");
        migrationBuilder.DropTable("MembershipTypes");
        migrationBuilder.DropTable("Staff");
        migrationBuilder.DropTable("Sites");
        migrationBuilder.DropTable("Companies");
        migrationBuilder.DropTable("Exercises");
        migrationBuilder.DropTable("Shifts");
    }
}
