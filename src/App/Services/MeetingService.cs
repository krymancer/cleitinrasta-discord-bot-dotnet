using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using App.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Services;

public sealed record Meeting(string Url, string Provider);

/// <summary>
/// Creates instant video rooms for screen sharing. Uses the Google Meet REST API when
/// GoogleMeet credentials are configured, otherwise falls back to a random Jitsi Meet room.
/// </summary>
public sealed class MeetingService(IOptions<MeetOptions> options, ILogger<MeetingService> logger) : IDisposable
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string SpacesEndpoint = "https://meet.googleapis.com/v2/spaces";

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? accessToken;
    private DateTimeOffset accessTokenExpiresAt;

    public async Task<Meeting> CreateAsync(CancellationToken cancellationToken = default)
    {
        var meet = options.Value;
        if (meet.IsConfigured)
        {
            try
            {
                return new Meeting(await CreateGoogleMeetAsync(meet, cancellationToken), "Google Meet");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to create Google Meet space, falling back to Jitsi");
            }
        }

        return new Meeting(CreateJitsiUrl(), "Jitsi Meet");
    }

    private async Task<string> CreateGoogleMeetAsync(MeetOptions meet, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(meet, cancellationToken);

        // OPEN = anyone with the link joins without knocking. Some accounts don't allow
        // changing the access type, so retry with the account defaults on 400/403.
        using var response = await PostSpaceAsync(token, new { config = new { accessType = "OPEN" } }, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
        {
            logger.LogWarning("Meet rejected accessType=OPEN ({Status}): {Body}",
                response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));

            using var fallback = await PostSpaceAsync(token, new { }, cancellationToken);
            return await ReadMeetingUriAsync(fallback, cancellationToken);
        }

        return await ReadMeetingUriAsync(response, cancellationToken);
    }

    private Task<HttpResponseMessage> PostSpaceAsync(string token, object body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SpacesEndpoint)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new("Bearer", token);
        return http.SendAsync(request, cancellationToken);
    }

    private static async Task<string> ReadMeetingUriAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Meet API returned {(int)response.StatusCode}: {body}");
        }

        var space = await response.Content.ReadFromJsonAsync<SpaceResponse>(cancellationToken);
        return space?.MeetingUri ?? throw new InvalidOperationException("Meet API response has no meetingUri");
    }

    private async Task<string> GetAccessTokenAsync(MeetOptions meet, CancellationToken cancellationToken)
    {
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (accessToken is not null && DateTimeOffset.UtcNow < accessTokenExpiresAt)
                return accessToken;

            using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = meet.ClientId!,
                ["client_secret"] = meet.ClientSecret!,
                ["refresh_token"] = meet.RefreshToken!,
                ["grant_type"] = "refresh_token",
            }), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Google token endpoint returned {(int)response.StatusCode}: {body}");
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Empty token response");

            accessToken = token.AccessToken;
            // Refresh a minute early to avoid using a token right as it expires.
            accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, token.ExpiresIn - 60));
            return accessToken;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private static string CreateJitsiUrl()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var suffix = RandomNumberGenerator.GetString(alphabet, 12);
        return $"https://meet.jit.si/cleitinrasta-{suffix}";
    }

    public void Dispose()
    {
        http.Dispose();
        tokenLock.Dispose();
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record SpaceResponse(
        [property: JsonPropertyName("meetingUri")] string? MeetingUri);
}
