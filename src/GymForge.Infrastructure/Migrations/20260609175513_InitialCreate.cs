using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DiffJson = table.Column<string>(type: "TEXT", nullable: true),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassDescriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DefaultDurationMin = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultCapacity = table.Column<int>(type: "INTEGER", nullable: false),
                    EquipmentNeeded = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassDescriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TaxId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LogoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PrimaryLanguage = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Timezone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FiscalConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    BrandColorHex = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Instructions = table.Column<string>(type: "TEXT", nullable: true),
                    PrimaryMuscleGroup = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SecondaryMuscleGroupsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Equipment = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MovementType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AnimatedGifUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsUnilateral = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsTimed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MembershipTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Basis = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DurationValue = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationUnit = table.Column<string>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    SignupFee = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    BillingCycle = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedDoorIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedClassTypesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ClassCreditsIncluded = table.Column<int>(type: "INTEGER", nullable: true),
                    PtSessionsIncluded = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduleRestrictionJson = table.Column<string>(type: "TEXT", nullable: true),
                    AgeMin = table.Column<int>(type: "INTEGER", nullable: true),
                    AgeMax = table.Column<int>(type: "INTEGER", nullable: true),
                    GenderRestriction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    BenefitIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Barcode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Brand = table.Column<string>(type: "TEXT", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CostPrice = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    SalePrice = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    TaxRate = table.Column<decimal>(type: "DECIMAL(5,4)", nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSellableOnline = table.Column<bool>(type: "INTEGER", nullable: false),
                    CommissionStaffPct = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CashierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegisterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SaleDatetime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Subtotal = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    TaxTotal = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    PaymentStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    FiscalCae = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    FiscalXmlUrl = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegisterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CashierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OpeningCash = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ClosingCashDeclared = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                    ClosingCashSystem = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClassDescriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstructorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    StartDatetime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDatetime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    WaitlistCapacity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRecurring = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecurrenceRule = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BookingOpenFrom = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BookingCloseBeforeMin = table.Column<int>(type: "INTEGER", nullable: false),
                    CancelDeadlineMin = table.Column<int>(type: "INTEGER", nullable: false),
                    LateCancelFee = table.Column<decimal>(type: "TEXT", nullable: true),
                    NoShowFee = table.Column<decimal>(type: "TEXT", nullable: true),
                    MinAge = table.Column<int>(type: "INTEGER", nullable: true),
                    DropInPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassSchedules_ClassDescriptions_ClassDescriptionId",
                        column: x => x.ClassDescriptionId,
                        principalTable: "ClassDescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ManagerStaffId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OpenHoursJson = table.Column<string>(type: "TEXT", nullable: true),
                    GeoLat = table.Column<double>(type: "REAL", nullable: true),
                    GeoLng = table.Column<double>(type: "REAL", nullable: true),
                    BrandColorHex = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sites_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Staff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    Mobile = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SpecialtiesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CertificationsJson = table.Column<string>(type: "TEXT", nullable: false),
                    PermissionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    PinCodeHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CommissionPctMemberships = table.Column<decimal>(type: "DECIMAL(5,2)", nullable: false),
                    CommissionPctPt = table.Column<decimal>(type: "DECIMAL(5,2)", nullable: false),
                    CommissionPctProducts = table.Column<decimal>(type: "DECIMAL(5,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Staff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Staff_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockBySite",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Qty = table.Column<decimal>(type: "DECIMAL(12,3)", nullable: false),
                    ReorderPoint = table.Column<decimal>(type: "DECIMAL(12,3)", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBySite", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockBySite_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MembershipTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(12,3)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    TaxRate = table.Column<decimal>(type: "DECIMAL(5,4)", nullable: false),
                    CommissionStaffId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleLines_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MovedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashMovements_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DocumentNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    Mobile = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    BirthDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Gender = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PhotoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SignatureUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TagSerial = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    FingerprintTemplate = table.Column<byte[]>(type: "BLOB", nullable: true),
                    EmergencyName = table.Column<string>(type: "TEXT", nullable: true),
                    EmergencyPhone = table.Column<string>(type: "TEXT", nullable: true),
                    EmergencyRelation = table.Column<string>(type: "TEXT", nullable: true),
                    MedicalConditions = table.Column<string>(type: "TEXT", nullable: true),
                    Medications = table.Column<string>(type: "TEXT", nullable: true),
                    Allergies = table.Column<string>(type: "TEXT", nullable: true),
                    BloodType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    WaiverSignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ParQId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReferredByMemberId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SalesRepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    JoinDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CustomFieldsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MarketingConsent = table.Column<bool>(type: "INTEGER", nullable: false),
                    Observations = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Members_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MembershipId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DoorId = table.Column<int>(type: "INTEGER", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SwipedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TagSerial = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AccessGranted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DenialReason = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessLogs_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BodyMeasurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MeasuredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TakenByStaffId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    WeightKg = table.Column<decimal>(type: "DECIMAL(6,2)", nullable: true),
                    HeightCm = table.Column<decimal>(type: "DECIMAL(6,2)", nullable: true),
                    BodyFatPct = table.Column<decimal>(type: "DECIMAL(5,2)", nullable: true),
                    BodyFatMethod = table.Column<string>(type: "TEXT", nullable: true),
                    MuscleMassKg = table.Column<decimal>(type: "DECIMAL(6,2)", nullable: true),
                    BodyWaterPct = table.Column<decimal>(type: "TEXT", nullable: true),
                    VisceralFat = table.Column<int>(type: "INTEGER", nullable: true),
                    BasalMetabolicRate = table.Column<int>(type: "INTEGER", nullable: true),
                    NeckCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    ShouldersCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    ChestCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    WaistCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    AbdomenCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    HipsCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    LeftBicepCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    RightBicepCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    LeftForearmCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    RightForearmCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    LeftThighCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    RightThighCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    LeftCalfCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    RightCalfCm = table.Column<decimal>(type: "TEXT", nullable: true),
                    TricepsMm = table.Column<decimal>(type: "TEXT", nullable: true),
                    SubscapularMm = table.Column<decimal>(type: "TEXT", nullable: true),
                    SuprailiacMm = table.Column<decimal>(type: "TEXT", nullable: true),
                    AbdominalMm = table.Column<decimal>(type: "TEXT", nullable: true),
                    ThighMm = table.Column<decimal>(type: "TEXT", nullable: true),
                    ChestMm = table.Column<decimal>(type: "TEXT", nullable: true),
                    PhotoFront = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoSide = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoBack = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BodyMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BodyMeasurements_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClassScheduleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookingType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    WaitlistPosition = table.Column<int>(type: "INTEGER", nullable: true),
                    BookedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CheckedInAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BookingChannel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PaymentChargeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookings_ClassSchedules_ClassScheduleId",
                        column: x => x.ClassScheduleId,
                        principalTable: "ClassSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookings_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MembershipTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    FreezeStart = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FreezeEnd = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FreezeReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FreezeCountUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    NextBillingDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AutoRenew = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContractPdfUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DiscountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VisitsRemaining = table.Column<int>(type: "INTEGER", nullable: true),
                    SoldByStaffId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CancelDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Memberships_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Memberships_MembershipTypes_MembershipTypeId",
                        column: x => x.MembershipTypeId,
                        principalTable: "MembershipTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Processor = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ProcessorTxId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CardLast4 = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                    CardBrand = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CashierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Routines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrainerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Goal = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FrequencyPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Routines_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoutineItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActualSetsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ActualRepsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ActualWeightKgJson = table.Column<string>(type: "TEXT", nullable: false),
                    ActualDurationSecJson = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Rpe = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Charges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MembershipId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConceptType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    FiscalCae = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    FiscalXmlUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AmountPaid = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Charges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Charges_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Charges_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RoutineDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoutineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DayNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineDays_Routines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChargeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Charges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "Charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoutineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoutineDayId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    RestSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Tempo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Technique = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineItems_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoutineItems_RoutineDays_RoutineDayId",
                        column: x => x.RoutineDayId,
                        principalTable: "RoutineDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoutineItemSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoutineItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SetNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetRepsMin = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetRepsMax = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetWeightKg = table.Column<decimal>(type: "DECIMAL(6,2)", nullable: true),
                    TargetDurationSec = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetRpe = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineItemSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineItemSets_RoutineItems_RoutineItemId",
                        column: x => x.RoutineItemId,
                        principalTable: "RoutineItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_DoorSwipedAt",
                table: "AccessLogs",
                columns: new[] { "DoorId", "SwipedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_MemberSwipedAt",
                table: "AccessLogs",
                columns: new[] { "MemberId", "SwipedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_SiteSwipedAt",
                table: "AccessLogs",
                columns: new[] { "SiteId", "SwipedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_TagSerial",
                table: "AccessLogs",
                column: "TagSerial");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CompanyTimestamp",
                table: "AuditLogs",
                columns: new[] { "CompanyId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Entity",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_BodyMeasurements_MemberDate",
                table: "BodyMeasurements",
                columns: new[] { "MemberId", "MeasuredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_MemberStatus",
                table: "Bookings",
                columns: new[] { "MemberId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ScheduleStatus",
                table: "Bookings",
                columns: new[] { "ClassScheduleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_Shift",
                table: "CashMovements",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_DueDateStatus",
                table: "Charges",
                columns: new[] { "CompanyId", "DueDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_MembershipId",
                table: "Charges",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_MemberStatus",
                table: "Charges",
                columns: new[] { "MemberId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassDescriptions_Company",
                table: "ClassDescriptions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedules_ClassDescriptionId",
                table: "ClassSchedules",
                column: "ClassDescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedules_InstructorStart",
                table: "ClassSchedules",
                columns: new[] { "InstructorId", "StartDatetime" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedules_SiteStart",
                table: "ClassSchedules",
                columns: new[] { "SiteId", "StartDatetime" });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_TaxId",
                table: "Companies",
                column: "TaxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_MuscleGroup",
                table: "Exercises",
                column: "PrimaryMuscleGroup");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_Tenant",
                table: "Exercises",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_CompanySiteStatus",
                table: "Members",
                columns: new[] { "CompanyId", "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Members_Document",
                table: "Members",
                column: "DocumentNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Members_Email",
                table: "Members",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Members_SiteId",
                table: "Members",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_TagSerial",
                table: "Members",
                column: "TagSerial");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_CompanyEndDate",
                table: "Memberships",
                columns: new[] { "CompanyId", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_MembershipTypeId",
                table: "Memberships",
                column: "MembershipTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_MemberStatus",
                table: "Memberships",
                columns: new[] { "MemberId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipTypes_CompanyActive",
                table: "MembershipTypes",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_Charge",
                table: "PaymentAllocations",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_Payment",
                table: "PaymentAllocations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Member",
                table: "Payments",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Shift",
                table: "Payments",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Barcode",
                table: "Products",
                column: "Barcode");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanySku",
                table: "Products",
                columns: new[] { "CompanyId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineDays_RoutineId",
                table: "RoutineDays",
                column: "RoutineId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineItems_ExerciseId",
                table: "RoutineItems",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineItems_RoutineDayId",
                table: "RoutineItems",
                column: "RoutineDayId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineItemSets_RoutineItemId",
                table: "RoutineItemSets",
                column: "RoutineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Routines_Member",
                table: "Routines",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_Member",
                table: "Sales",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SiteDate",
                table: "Sales",
                columns: new[] { "SiteId", "SaleDatetime" });

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_SiteStatus",
                table: "Shifts",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Company",
                table: "Sites",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Staff_CompanyRole",
                table: "Staff",
                columns: new[] { "CompanyId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_Staff_Email",
                table: "Staff",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_StockBySite_ProductSite",
                table: "StockBySite",
                columns: new[] { "ProductId", "SiteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_MemberPerformedAt",
                table: "WorkoutLogs",
                columns: new[] { "MemberId", "PerformedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessLogs");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BodyMeasurements");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "PaymentAllocations");

            migrationBuilder.DropTable(
                name: "RoutineItemSets");

            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "Staff");

            migrationBuilder.DropTable(
                name: "StockBySite");

            migrationBuilder.DropTable(
                name: "WorkoutLogs");

            migrationBuilder.DropTable(
                name: "ClassSchedules");

            migrationBuilder.DropTable(
                name: "Shifts");

            migrationBuilder.DropTable(
                name: "Charges");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "RoutineItems");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "ClassDescriptions");

            migrationBuilder.DropTable(
                name: "Memberships");

            migrationBuilder.DropTable(
                name: "Exercises");

            migrationBuilder.DropTable(
                name: "RoutineDays");

            migrationBuilder.DropTable(
                name: "MembershipTypes");

            migrationBuilder.DropTable(
                name: "Routines");

            migrationBuilder.DropTable(
                name: "Members");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropTable(
                name: "Companies");
        }
    }
}
