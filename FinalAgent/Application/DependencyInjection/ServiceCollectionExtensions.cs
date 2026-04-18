using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Services;
using FinalAgent.Domain.Abstractions;
using FinalAgent.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FinalAgent.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IApplicationRunner, GreetingApplicationRunner>();
        services.AddSingleton<IGreetingComposer, GreetingComposer>();

        return services;
    }
}
