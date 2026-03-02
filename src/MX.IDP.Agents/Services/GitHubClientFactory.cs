using System.Security.Cryptography;

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Octokit;

namespace MX.IDP.Agents.Services;

public interface IGitHubClientFactory
{
    Task<GitHubClient> CreateClientAsync();
}

public class GitHubClientFactory : IGitHubClientFactory
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitHubClientFactory> _logger;
    private GitHubClient? _cachedClient;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public GitHubClientFactory(IConfiguration configuration, ILogger<GitHubClientFactory> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GitHubClient> CreateClientAsync()
    {
        if (_cachedClient is not null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            return _cachedClient;

        var appId = _configuration["GitHubApp:AppId"] ?? _configuration["GitHubApp__AppId"]
            ?? throw new InvalidOperationException("GitHubApp:AppId is not configured.");
        var installationId = _configuration["GitHubApp:InstallationId"] ?? _configuration["GitHubApp__InstallationId"]
            ?? throw new InvalidOperationException("GitHubApp:InstallationId is not configured.");
        var pemSecretName = _configuration["GitHubApp:PemSecretName"] ?? _configuration["GitHubApp__PemSecretName"]
            ?? throw new InvalidOperationException("GitHubApp:PemSecretName is not configured.");
        var keyVaultUri = _configuration["KeyVault:Uri"] ?? _configuration["KeyVault__Uri"]
            ?? throw new InvalidOperationException("KeyVault:Uri is not configured.");

        _logger.LogInformation("Creating GitHub App client for app {AppId}, installation {InstallationId}", appId, installationId);

        var secretClient = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
        var secret = await secretClient.GetSecretAsync(pemSecretName);
        var pemKey = secret.Value.Value;

        var jwt = GenerateJwt(int.Parse(appId), pemKey);

        var appClient = new GitHubClient(new ProductHeaderValue("idp-agents"))
        {
            Credentials = new Credentials(jwt, AuthenticationType.Bearer)
        };

        var token = await appClient.GitHubApps.CreateInstallationToken(long.Parse(installationId));

        _cachedClient = new GitHubClient(new ProductHeaderValue("idp-agents"))
        {
            Credentials = new Credentials(token.Token)
        };
        _tokenExpiry = token.ExpiresAt;

        _logger.LogInformation("GitHub App installation token created, expires at {Expiry}", _tokenExpiry);
        return _cachedClient;
    }

    private static string GenerateJwt(int appId, string pemKey)
    {
        var now = DateTimeOffset.UtcNow;
        var rsa = RSA.Create();
        rsa.ImportFromPem(pemKey.ToCharArray());

        var header = Base64UrlEncode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64UrlEncode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = now.AddSeconds(-60).ToUnixTimeSeconds(),
            exp = now.AddMinutes(10).ToUnixTimeSeconds(),
            iss = appId
        }));

        var dataToSign = System.Text.Encoding.UTF8.GetBytes($"{header}.{payload}");
        var signature = Base64UrlEncode(rsa.SignData(dataToSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        return $"{header}.{payload}.{signature}";
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
