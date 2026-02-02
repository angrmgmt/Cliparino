namespace Cliparino.Core.Services;

public interface ITwitchOAuthService {
    Task<bool> IsAuthenticatedAsync();
    Task<string> StartAuthFlowAsync();
    Task<bool> CompleteAuthFlowAsync(string authCode);
    Task<bool> RefreshTokenAsync();
    Task<string?> GetValidAccessTokenAsync();
    Task LogoutAsync();
}