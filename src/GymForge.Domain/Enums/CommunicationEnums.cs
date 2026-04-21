namespace GymForge.Domain.Enums;

public enum CommunicationChannel
{
    Email,
    Sms,
    WhatsApp,
    Push,
    InApp
}

public enum CommunicationStatus
{
    Queued,
    Sent,
    Delivered,
    Opened,
    Clicked,
    Failed,
    Bounced
}

public enum ProspectStatus
{
    New,
    Contacted,
    Qualified,
    Trial,
    Won,
    Lost
}

public enum AutoTaskType
{
    BirthdayCall,
    OverdueFollowup,
    WelcomeCall,
    Renewal,
    TrialExpiring
}

public enum MemberSource
{
    WalkIn,
    Referral,
    SocialMedia,
    GoogleAds,
    Website,
    Event,
    Other
}
