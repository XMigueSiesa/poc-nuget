using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pos.SharedKernel.Auth;

namespace Pos.Host.CloudHub.Auth;

public static class TokenEndpoint
{
    private sealed record TokenRequest(string? ClientId, string? ClientSecret);

    private sealed record TokenResponse(
        string AccessToken,
        string TokenType,
        int ExpiresIn);

    public static WebApplication MapTokenEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/token", HandleTokenRequest);

        return app;
    }

    private static async Task<IResult> HandleTokenRequest(
        TokenRequest request,
        AuthDbContext db,
        IConfiguration configuration,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
            return Results.BadRequest(new { Error = "client_id is required" });

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
            return Results.BadRequest(new { Error = "client_secret is required" });

        var credential = await db.StoreCredentials
            .FirstOrDefaultAsync(c => c.ClientId == request.ClientId, ct);

        if (credential is null || !credential.IsActive)
            return Results.Unauthorized();

        if (!BCrypt.Net.BCrypt.Verify(request.ClientSecret, credential.ClientSecretHash))
            return Results.Unauthorized();

        var token = GenerateJwt(credential, configuration);

        return Results.Ok(token);
    }

    private static TokenResponse GenerateJwt(
        StoreCredential credential,
        IConfiguration configuration)
    {
        var signingKey = configuration["Auth:JwtSigningKey"]
            ?? throw new InvalidOperationException("Auth:JwtSigningKey is not configured.");

        var issuer = configuration["Auth:Issuer"] ?? "pos-cloud-hub";
        var audience = configuration["Auth:Audience"] ?? "pos-api";
        var expirationMinutes = int.TryParse(
            configuration["Auth:TokenExpirationMinutes"], out var minutes)
            ? minutes
            : 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, credential.StoreId),
            new Claim("client_id", credential.ClientId),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: signingCredentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return new TokenResponse(
            AccessToken: tokenString,
            TokenType: "Bearer",
            ExpiresIn: expirationMinutes * 60);
    }
}
