using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pos.SharedKernel.Auth;

namespace Pos.Host.CloudHub.Auth;

public static class StoreManagementEndpoint
{
    private sealed record CreateStoreRequest(string? StoreId, string? StoreName);

    private sealed record CreateStoreResponse(
        string StoreId,
        string ClientId,
        string ClientSecret);

    private sealed record StoreListItem(
        string Id,
        string StoreId,
        string ClientId,
        string StoreName,
        bool IsActive,
        DateTimeOffset CreatedAt);

    public static WebApplication MapStoreManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/stores")
            .WithTags("Admin")
            .RequireAuthorization("AdminEndpoint");

        group.MapPost("/", HandleCreateStore);
        group.MapGet("/", HandleListStores);
        group.MapPut("/{id}/deactivate", HandleDeactivateStore);

        return app;
    }

    private static async Task<IResult> HandleCreateStore(
        CreateStoreRequest request,
        AuthDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId))
            return Results.BadRequest(new { Error = "storeId is required" });

        if (string.IsNullOrWhiteSpace(request.StoreName))
            return Results.BadRequest(new { Error = "storeName is required" });

        var existingStore = await db.StoreCredentials
            .FirstOrDefaultAsync(c => c.StoreId == request.StoreId, ct);

        if (existingStore is not null)
            return Results.Conflict(new { Error = "Store with this ID already exists" });

        var clientId = Ulid.NewUlid().ToString();
        var clientSecret = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));
        var clientSecretHash = BCrypt.Net.BCrypt.HashPassword(clientSecret);

        var credential = new StoreCredential
        {
            StoreId = request.StoreId,
            ClientId = clientId,
            ClientSecretHash = clientSecretHash,
            StoreName = request.StoreName
        };

        db.StoreCredentials.Add(credential);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/admin/stores/{credential.Id}",
            new CreateStoreResponse(
                StoreId: request.StoreId,
                ClientId: clientId,
                ClientSecret: clientSecret));
    }

    private static async Task<IResult> HandleListStores(
        AuthDbContext db,
        CancellationToken ct)
    {
        var stores = await db.StoreCredentials
            .OrderBy(c => c.CreatedAt)
            .Select(c => new StoreListItem(
                Id: c.Id.ToString(),
                StoreId: c.StoreId,
                ClientId: c.ClientId,
                StoreName: c.StoreName,
                IsActive: c.IsActive,
                CreatedAt: c.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(stores);
    }

    private static async Task<IResult> HandleDeactivateStore(
        string id,
        AuthDbContext db,
        CancellationToken ct)
    {
        if (!Ulid.TryParse(id, out var ulid))
            return Results.BadRequest(new { Error = "Invalid store ID format" });

        var credential = await db.StoreCredentials.FindAsync([ulid], ct);

        if (credential is null)
            return Results.NotFound(new { Error = "Store not found" });

        var deactivated = credential with { IsActive = false };
        db.Entry(credential).CurrentValues.SetValues(deactivated);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { Id = id, IsActive = false });
    }
}
