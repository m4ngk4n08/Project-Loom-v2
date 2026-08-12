using Loom.Web.Api.Interfaces;
using Loom.Web.Api.Services;

namespace Loom.Web.Api.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddServices(this IServiceCollection services)
        {
            services.AddSingleton<IMetricsService, MetricsService>();
            return services;
        }
    }
}
