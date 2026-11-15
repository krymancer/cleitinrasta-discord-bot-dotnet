namespace App.Configuration;

public class MeetOptions
{
    public const string SectionName = "GoogleMeet";

    /// <summary>OAuth client id (Google Cloud Console → Credentials → OAuth client).</summary>
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>
    /// Refresh token for the Google account that will own the rooms.
    /// Needs the scope https://www.googleapis.com/auth/meetings.space.created
    /// </summary>
    public string? RefreshToken { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RefreshToken);
}
