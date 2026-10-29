namespace GymForge.Domain.Enums;

/// <summary>Canal por el que se avisa al socio.</summary>
public enum NotificationChannel { WhatsApp, Sms }

/// <summary>Quién disparó el aviso: el operador a mano o el job automático.</summary>
public enum NotificationTrigger { Manual, Automatic }
