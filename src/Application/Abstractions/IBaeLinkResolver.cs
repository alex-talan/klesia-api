namespace Application.Abstractions;

public interface IBaeLinkResolver
{
    /// <summary>Turns the stored document reference into the HTTPS URL returned to the client.</summary>
    string Resolve(string storedLink);
}
