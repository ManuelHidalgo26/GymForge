namespace GymForge.Domain.Enums;

public enum BookingStatus
{
    Booked,
    Waitlisted,
    Attended,
    NoShow,
    LateCancelled,
    Cancelled
}

public enum BookingType
{
    Class,
    PersonalTraining,
    Resource,
    Equipment
}

public enum BookingChannel
{
    FrontDesk,
    MobileApp,
    MemberPortal
}
