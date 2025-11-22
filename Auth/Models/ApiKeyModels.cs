using System;

namespace Quizz.Auth.Models
{
    /// <summary>
    /// Represents an API key stored in the database.
    /// </summary>
    public class ApiKey
    {
        public Guid ApiKeyId { get; set; }
        public string KeyHash { get; set; }
        public string KeyPrefix { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string[] Scopes { get; set; }
        public bool IsAdmin { get; set; }
        public int RateLimitPerHour { get; set; }
        public int RateLimitPerDay { get; set; }
        public bool IsActive { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public string LastUsedIp { get; set; }
        public long UsageCount { get; set; }
        public string Metadata { get; set; } // JSON string
    }

    /// <summary>
    /// Represents an audit log entry for API key usage.
    /// </summary>
    public class ApiKeyAudit
    {
        public Guid AuditId { get; set; }
        public Guid ApiKeyId { get; set; }
        public DateTime Timestamp { get; set; }
        public string HttpMethod { get; set; }
        public string Endpoint { get; set; }
        public string IpAddress { get; set; }
        public string UserAgent { get; set; }
        public string RequestId { get; set; }
        public int? StatusCode { get; set; }
        public int? ResponseTimeMs { get; set; }
        public string RequiredScope { get; set; }
        public bool WasAuthorized { get; set; }
        public bool RateLimitExceeded { get; set; }
        public string ErrorMessage { get; set; }
        public string Metadata { get; set; } // JSON string
    }

    /// <summary>
    /// Result of API key validation.
    /// </summary>
    public class ApiKeyValidationResult
    {
        public bool IsValid { get; set; }
        public ApiKey ApiKey { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsRateLimited { get; set; }
        public bool IsExpired { get; set; }
        public bool HasRequiredScope { get; set; }

        public static ApiKeyValidationResult Success(ApiKey apiKey, bool hasRequiredScope = true)
        {
            return new ApiKeyValidationResult
            {
                IsValid = true,
                ApiKey = apiKey,
                HasRequiredScope = hasRequiredScope
            };
        }

        public static ApiKeyValidationResult Failure(string errorMessage)
        {
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ErrorMessage = errorMessage
            };
        }

        public static ApiKeyValidationResult RateLimited(ApiKey apiKey)
        {
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ApiKey = apiKey,
                IsRateLimited = true,
                ErrorMessage = "Rate limit exceeded"
            };
        }

        public static ApiKeyValidationResult Expired(ApiKey apiKey)
        {
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ApiKey = apiKey,
                IsExpired = true,
                ErrorMessage = "API key has expired"
            };
        }

        public static ApiKeyValidationResult InsufficientScope(ApiKey apiKey, string requiredScope)
        {
            return new ApiKeyValidationResult
            {
                IsValid = false,
                ApiKey = apiKey,
                HasRequiredScope = false,
                ErrorMessage = $"API key does not have required scope: {requiredScope}"
            };
        }
    }

    /// <summary>
    /// Statistics for an API key.
    /// </summary>
    public class ApiKeyStats
    {
        public long TotalRequests { get; set; }
        public long SuccessfulRequests { get; set; }
        public long FailedRequests { get; set; }
        public long UnauthorizedRequests { get; set; }
        public long RateLimitedRequests { get; set; }
        public decimal AvgResponseTimeMs { get; set; }
        public long UniqueIps { get; set; }
    }

    /// <summary>
    /// Request to create a new API key.
    /// </summary>
    public class CreateApiKeyRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string[] Scopes { get; set; }
        public bool IsAdmin { get; set; }
        public int RateLimitPerHour { get; set; } = 1000;
        public int RateLimitPerDay { get; set; } = 10000;
        public DateTime? ExpiresAt { get; set; }
        public Guid? CreatedBy { get; set; }
    }

    /// <summary>
    /// Response containing a newly created API key (includes the actual key).
    /// </summary>
    public class CreateApiKeyResponse
    {
        public Guid ApiKeyId { get; set; }
        public string ApiKey { get; set; } // The actual key - only returned once!
        public string KeyPrefix { get; set; }
        public string Name { get; set; }
        public string[] Scopes { get; set; }
        public bool IsAdmin { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        
        public string Warning { get; set; } = "Store this key securely. It will not be shown again.";
    }
}
