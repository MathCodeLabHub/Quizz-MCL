using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using Quizz.Auth.Models;
using Quizz.DataAccess;

namespace Quizz.Auth
{
    /// <summary>
    /// Interface for API key operations.
    /// </summary>
    public interface IApiKeyService
    {
        Task<ApiKeyValidationResult> ValidateKeyAsync(string apiKey, string requiredScope = null);
        Task<CreateApiKeyResponse> CreateApiKeyAsync(CreateApiKeyRequest request);
        Task<bool> RevokeApiKeyAsync(Guid apiKeyId);
        Task<ApiKeyStats> GetApiKeyStatsAsync(Guid apiKeyId, int days = 7);
        Task LogApiKeyUsageAsync(Guid apiKeyId, string httpMethod, string endpoint, string ipAddress, 
            string userAgent, int statusCode, int responseTimeMs, string requiredScope, bool wasAuthorized,
            bool rateLimitExceeded = false, string errorMessage = null, string requestId = null);
    }

    /// <summary>
    /// Service for managing and validating API keys.
    /// Uses bcrypt for secure key hashing and validation.
    /// </summary>
    public class ApiKeyService : IApiKeyService
    {
        private readonly IDbService _dbService;
        private const string KeyPrefix = "sk_";
        private const int KeyLength = 32; // 32 bytes = 64 hex characters

        public ApiKeyService(IDbService dbService)
        {
            _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
        }

        /// <summary>
        /// Validates an API key and checks required scope.
        /// </summary>
        public async Task<ApiKeyValidationResult> ValidateKeyAsync(string apiKey, string requiredScope = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return ApiKeyValidationResult.Failure("API key is required");
            }

            // Extract prefix for faster lookup
            var prefix = apiKey.Length >= 8 ? apiKey.Substring(0, 8) : apiKey;

            // Get all active keys with matching prefix
            var sql = @"
                SELECT api_key_id, key_hash, key_prefix, name, description, scopes, is_admin,
                       rate_limit_per_hour, rate_limit_per_day, is_active, expires_at,
                       created_at, created_by, last_used_at, last_used_ip, usage_count, metadata
                FROM quiz.api_keys
                WHERE key_prefix = @prefix 
                  AND is_active = TRUE
                  AND (expires_at IS NULL OR expires_at > NOW())";

            using var reader = await _dbService.ExecuteQueryAsync(sql, 
                new NpgsqlParameter("@prefix", prefix));

            ApiKey matchedKey = null;

            // Check each key with matching prefix
            while (await reader.ReadAsync())
            {
                var keyHash = reader.GetString(1);
                
                // Verify the key against bcrypt hash
                if (BCrypt.Net.BCrypt.Verify(apiKey, keyHash))
                {
                    matchedKey = new ApiKey
                    {
                        ApiKeyId = reader.GetGuid(0),
                        KeyHash = keyHash,
                        KeyPrefix = reader.GetString(2),
                        Name = reader.GetString(3),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Scopes = reader.IsDBNull(5) ? new string[0] : (string[])reader.GetValue(5),
                        IsAdmin = reader.GetBoolean(6),
                        RateLimitPerHour = reader.GetInt32(7),
                        RateLimitPerDay = reader.GetInt32(8),
                        IsActive = reader.GetBoolean(9),
                        ExpiresAt = reader.IsDBNull(10) ? null : (DateTime?)reader.GetDateTime(10),
                        CreatedAt = reader.GetDateTime(11),
                        CreatedBy = reader.IsDBNull(12) ? null : (Guid?)reader.GetGuid(12),
                        LastUsedAt = reader.IsDBNull(13) ? null : (DateTime?)reader.GetDateTime(13),
                        LastUsedIp = reader.IsDBNull(14) ? null : reader.GetString(14),
                        UsageCount = reader.GetInt64(15),
                        Metadata = reader.IsDBNull(16) ? null : reader.GetString(16)
                    };
                    break;
                }
            }

            if (matchedKey == null)
            {
                return ApiKeyValidationResult.Failure("Invalid API key");
            }

            // Check if key is expired
            if (matchedKey.ExpiresAt.HasValue && matchedKey.ExpiresAt.Value <= DateTime.UtcNow)
            {
                return ApiKeyValidationResult.Expired(matchedKey);
            }

            // Check rate limits
            var isRateLimitedHourly = await CheckRateLimitAsync(matchedKey.ApiKeyId, "hourly");
            var isRateLimitedDaily = await CheckRateLimitAsync(matchedKey.ApiKeyId, "daily");

            if (isRateLimitedHourly || isRateLimitedDaily)
            {
                return ApiKeyValidationResult.RateLimited(matchedKey);
            }

            // Check required scope
            if (!string.IsNullOrWhiteSpace(requiredScope))
            {
                var hasScope = matchedKey.IsAdmin || 
                               (matchedKey.Scopes != null && matchedKey.Scopes.Contains(requiredScope));

                if (!hasScope)
                {
                    return ApiKeyValidationResult.InsufficientScope(matchedKey, requiredScope);
                }

                return ApiKeyValidationResult.Success(matchedKey, hasRequiredScope: true);
            }

            return ApiKeyValidationResult.Success(matchedKey);
        }

        /// <summary>
        /// Creates a new API key with secure random generation and bcrypt hashing.
        /// </summary>
        public async Task<CreateApiKeyResponse> CreateApiKeyAsync(CreateApiKeyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ArgumentException("Name is required", nameof(request.Name));
            }

            // Generate secure random key
            var keyBytes = new byte[KeyLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyBytes);
            }

            var keyString = Convert.ToBase64String(keyBytes)
                .Replace("+", "")
                .Replace("/", "")
                .Replace("=", "")
                .Substring(0, 40); // 40 character key

            var apiKey = $"{KeyPrefix}{keyString}";
            var keyPrefix = apiKey.Substring(0, 8);

            // Hash the key with bcrypt
            var keyHash = BCrypt.Net.BCrypt.HashPassword(apiKey, workFactor: 10);

            // Insert into database
            var sql = @"
                INSERT INTO quiz.api_keys (
                    key_hash, key_prefix, name, description, scopes, is_admin,
                    rate_limit_per_hour, rate_limit_per_day, expires_at, created_by
                )
                VALUES (
                    @keyHash, @keyPrefix, @name, @description, @scopes, @isAdmin,
                    @rateLimitPerHour, @rateLimitPerDay, @expiresAt, @createdBy
                )
                RETURNING api_key_id, created_at";

            using var reader = await _dbService.ExecuteQueryAsync(sql,
                new NpgsqlParameter("@keyHash", keyHash),
                new NpgsqlParameter("@keyPrefix", keyPrefix),
                new NpgsqlParameter("@name", request.Name),
                new NpgsqlParameter("@description", request.Description ?? (object)DBNull.Value),
                new NpgsqlParameter("@scopes", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) 
                    { Value = request.Scopes ?? new string[0] },
                new NpgsqlParameter("@isAdmin", request.IsAdmin),
                new NpgsqlParameter("@rateLimitPerHour", request.RateLimitPerHour),
                new NpgsqlParameter("@rateLimitPerDay", request.RateLimitPerDay),
                new NpgsqlParameter("@expiresAt", request.ExpiresAt.HasValue ? (object)request.ExpiresAt.Value : DBNull.Value),
                new NpgsqlParameter("@createdBy", request.CreatedBy.HasValue ? (object)request.CreatedBy.Value : DBNull.Value)
            );

            await reader.ReadAsync();
            var apiKeyId = reader.GetGuid(0);
            var createdAt = reader.GetDateTime(1);

            return new CreateApiKeyResponse
            {
                ApiKeyId = apiKeyId,
                ApiKey = apiKey, // Return the actual key - only time it's visible!
                KeyPrefix = keyPrefix,
                Name = request.Name,
                Scopes = request.Scopes,
                IsAdmin = request.IsAdmin,
                CreatedAt = createdAt,
                ExpiresAt = request.ExpiresAt
            };
        }

        /// <summary>
        /// Revokes an API key (soft delete).
        /// </summary>
        public async Task<bool> RevokeApiKeyAsync(Guid apiKeyId)
        {
            var sql = "UPDATE quiz.api_keys SET is_active = FALSE WHERE api_key_id = @apiKeyId";
            var rowsAffected = await _dbService.ExecuteNonQueryAsync(sql,
                new NpgsqlParameter("@apiKeyId", apiKeyId));
            
            return rowsAffected > 0;
        }

        /// <summary>
        /// Gets usage statistics for an API key.
        /// </summary>
        public async Task<ApiKeyStats> GetApiKeyStatsAsync(Guid apiKeyId, int days = 7)
        {
            var sql = "SELECT * FROM get_api_key_stats(@apiKeyId, @days)";
            
            using var reader = await _dbService.ExecuteQueryAsync(sql,
                new NpgsqlParameter("@apiKeyId", apiKeyId),
                new NpgsqlParameter("@days", days));

            if (await reader.ReadAsync())
            {
                return new ApiKeyStats
                {
                    TotalRequests = reader.GetInt64(0),
                    SuccessfulRequests = reader.GetInt64(1),
                    FailedRequests = reader.GetInt64(2),
                    UnauthorizedRequests = reader.GetInt64(3),
                    RateLimitedRequests = reader.GetInt64(4),
                    AvgResponseTimeMs = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                    UniqueIps = reader.GetInt64(6)
                };
            }

            return new ApiKeyStats();
        }

        /// <summary>
        /// Logs API key usage to audit table.
        /// </summary>
        public async Task LogApiKeyUsageAsync(
            Guid apiKeyId, 
            string httpMethod, 
            string endpoint, 
            string ipAddress,
            string userAgent, 
            int statusCode, 
            int responseTimeMs, 
            string requiredScope, 
            bool wasAuthorized,
            bool rateLimitExceeded = false, 
            string errorMessage = null, 
            string requestId = null)
        {
            var sql = @"
                INSERT INTO quiz.api_key_audit (
                    api_key_id, http_method, endpoint, ip_address, user_agent, request_id,
                    status_code, response_time_ms, required_scope, was_authorized, 
                    rate_limit_exceeded, error_message
                )
                VALUES (
                    @apiKeyId, @httpMethod, @endpoint, @ipAddress::inet, @userAgent, @requestId,
                    @statusCode, @responseTimeMs, @requiredScope, @wasAuthorized,
                    @rateLimitExceeded, @errorMessage
                )";

            await _dbService.ExecuteNonQueryAsync(sql,
                new NpgsqlParameter("@apiKeyId", apiKeyId),
                new NpgsqlParameter("@httpMethod", httpMethod),
                new NpgsqlParameter("@endpoint", endpoint),
                new NpgsqlParameter("@ipAddress", ipAddress ?? (object)DBNull.Value),
                new NpgsqlParameter("@userAgent", userAgent ?? (object)DBNull.Value),
                new NpgsqlParameter("@requestId", requestId ?? (object)DBNull.Value),
                new NpgsqlParameter("@statusCode", statusCode),
                new NpgsqlParameter("@responseTimeMs", responseTimeMs),
                new NpgsqlParameter("@requiredScope", requiredScope ?? (object)DBNull.Value),
                new NpgsqlParameter("@wasAuthorized", wasAuthorized),
                new NpgsqlParameter("@rateLimitExceeded", rateLimitExceeded),
                new NpgsqlParameter("@errorMessage", errorMessage ?? (object)DBNull.Value)
            );
        }

        /// <summary>
        /// Checks if an API key has exceeded its rate limit.
        /// </summary>
        private async Task<bool> CheckRateLimitAsync(Guid apiKeyId, string period)
        {
            var functionName = period == "hourly" 
                ? "is_api_key_rate_limited_hourly" 
                : "is_api_key_rate_limited_daily";

            var sql = $"SELECT {functionName}(@apiKeyId)";
            
            using var reader = await _dbService.ExecuteQueryAsync(sql,
                new NpgsqlParameter("@apiKeyId", apiKeyId));

            if (await reader.ReadAsync())
            {
                return reader.GetBoolean(0);
            }

            return false;
        }
    }
}
