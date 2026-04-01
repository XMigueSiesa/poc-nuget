using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Pos.Host.CloudHub.Auth;

public static class JwtAuthExtensions
{
    public static IServiceCollection AddPosAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var signingKey = configuration["Auth:JwtSigningKey"]
            ?? throw new InvalidOperationException("Auth:JwtSigningKey is not configured.");

        var issuer = configuration["Auth:Issuer"] ?? "pos-cloud-hub";
        var audience = configuration["Auth:Audience"] ?? "pos-api";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(signingKey)),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("SyncEndpoint", policy =>
                policy.RequireClaim("client_id"))
            .AddPolicy("AdminEndpoint", policy =>
                policy.RequireClaim("role", "admin"));

        return services;
    }
}
