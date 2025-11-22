using System;
using Microsoft.Extensions.DependencyInjection;
using Quizz.Auth;

namespace Quizz.Auth
{
    /// <summary>
    /// Extension methods for registering authentication services.
    /// </summary>
    public static class AuthServiceExtensions
    {
        /// <summary>
        /// Registers IApiKeyService as a scoped service for dependency injection.
        /// </summary>
        public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services)
        {
            services.AddScoped<IApiKeyService, ApiKeyService>();
            return services;
        }
    }
}
