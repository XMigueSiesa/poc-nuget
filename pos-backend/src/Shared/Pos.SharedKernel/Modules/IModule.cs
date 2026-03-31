using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.SharedKernel.Modules;

public interface IModule
{
    static abstract IServiceCollection AddModule(IServiceCollection services, Action<ModuleOptions> configure);
    static abstract WebApplication MapEndpoints(WebApplication app);
}

public sealed record ModuleOptions
{
    public required Action<IServiceProvider> ConfigureDbProvider { get; init; }
    public string ConnectionString { get; init; } = string.Empty;
}
