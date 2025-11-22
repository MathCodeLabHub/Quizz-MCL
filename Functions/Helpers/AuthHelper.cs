using Microsoft.Azure.Functions.Worker.Http;
using Quizz.Auth;
using Quizz.Auth.Models;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Quizz.Functions.Helpers
{
    /// <summary>
    /// Helper methods for API key authentication in Azure Functions.
    /// </summary>
    public static class AuthHelper
    {
        /// <summary>
        /// Validates API key from request headers and checks required scope.
        /// Returns error response if validation fails, otherwise returns null.
        /// </summary>
        public static async Task<(ApiKeyValidationResult? validation, HttpResponseData? errorResponse)> ValidateApiKeyAsync(
            HttpRequestData req,
            IApiKeyService apiKeyService,
            string requiredScope,
            Stopwatch stopwatch)
        {
            // Extract API key from header
            if (!req.Headers.TryGetValues("X-API-Key", out var apiKeyValues))
            {
                var response = req.CreateResponse(HttpStatusCode.Unauthorized);
                await response.WriteAsJsonAsync(new { error = "API key required in X-API-Key header" });
                return (null, response);
            }

            var apiKey = apiKeyValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var response = req.CreateResponse(HttpStatusCode.Unauthorized);
                await response.WriteAsJsonAsync(new { error = "API key cannot be empty" });
                return (null, response);
            }

            // Validate key and check scope
            var validation = await apiKeyService.ValidateKeyAsync(apiKey, requiredScope);

            if (!validation.IsValid)
            {
                // Log failed attempt
                if (validation.ApiKey != null)
                {
                    await apiKeyService.LogApiKeyUsageAsync(
                        validation.ApiKey.ApiKeyId,
                        req.Method,
                        req.Url.AbsolutePath,
                        GetIpAddress(req),
                        GetUserAgent(req),
                        validation.IsRateLimited ? 429 : 401,
                        (int)stopwatch.ElapsedMilliseconds,
                        requiredScope,
                        false,
                        validation.IsRateLimited,
                        validation.ErrorMessage,
                        GetRequestId(req)
                    );
                }

                HttpStatusCode statusCode = validation.IsRateLimited 
                    ? HttpStatusCode.TooManyRequests 
                    : HttpStatusCode.Unauthorized;

                var response = req.CreateResponse(statusCode);
                await response.WriteAsJsonAsync(new 
                { 
                    error = validation.ErrorMessage,
                    isRateLimited = validation.IsRateLimited,
                    isExpired = validation.IsExpired,
                    hasRequiredScope = validation.HasRequiredScope
                });

                return (validation, response);
            }

            return (validation, null);
        }

        /// <summary>
        /// Logs successful API key usage after request completion.
        /// </summary>
        public static async Task LogSuccessfulUsageAsync(
            HttpRequestData req,
            IApiKeyService apiKeyService,
            Guid apiKeyId,
            string requiredScope,
            int statusCode,
            Stopwatch stopwatch)
        {
            await apiKeyService.LogApiKeyUsageAsync(
                apiKeyId,
                req.Method,
                req.Url.AbsolutePath,
                GetIpAddress(req),
                GetUserAgent(req),
                statusCode,
                (int)stopwatch.ElapsedMilliseconds,
                requiredScope,
                true,
                false,
                null!,
                GetRequestId(req)
            );
        }

        /// <summary>
        /// Gets the client IP address from the request.
        /// </summary>
        public static string GetIpAddress(HttpRequestData req)
        {
            // Try to get from X-Forwarded-For header (behind load balancer/proxy)
            if (req.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
            {
                var ip = forwardedFor.FirstOrDefault()?.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(ip))
                    return ip;
            }

            // Try X-Real-IP
            if (req.Headers.TryGetValues("X-Real-IP", out var realIp))
            {
                var ip = realIp.FirstOrDefault();
                if (!string.IsNullOrEmpty(ip))
                    return ip;
            }

            return "unknown";
        }

        /// <summary>
        /// Gets the User-Agent from request headers.
        /// </summary>
        public static string GetUserAgent(HttpRequestData req)
        {
            if (req.Headers.TryGetValues("User-Agent", out var userAgent))
            {
                return userAgent.FirstOrDefault() ?? "unknown";
            }
            return "unknown";
        }

        /// <summary>
        /// Gets or generates a request ID for tracing.
        /// </summary>
        public static string GetRequestId(HttpRequestData req)
        {
            if (req.Headers.TryGetValues("X-Request-ID", out var requestId))
            {
                return requestId.FirstOrDefault() ?? string.Empty;
            }

            // Generate new request ID
            return Guid.NewGuid().ToString("N");
        }
    }
}
