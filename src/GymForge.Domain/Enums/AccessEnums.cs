namespace GymForge.Domain.Enums;

public enum AccessMethod
{
    Fingerprint,
    RfidCard,
    Qr,
    KeypadPin,
    MobileApp,
    Facial,
    Manual
}

public enum AccessDirection
{
    In,
    Out
}

public enum AccessDenialReason
{
    TagUnknown,
    IncompleteMembership,
    Expired,
    Owing,
    OutOfHours,
    DoorNotAllowed,
    GenderRestricted,
    AlreadyInside,
    NoVisitsLeft,
    Suspended
}
