namespace GymForge.Application.Interfaces;

/// <summary>
/// Ya existe un socio con ese documento en la misma empresa y sede. Es el error legible de la red de
/// seguridad de la base (índice único); el mensaje se muestra tal cual.
/// </summary>
public class DuplicateMemberDocumentException : InvalidOperationException
{
    public DuplicateMemberDocumentException(Exception inner)
        : base("Ya existe un socio con ese DNI en esta sede.", inner) { }
}
