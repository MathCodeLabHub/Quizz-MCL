using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Net;
using Npgsql;
using Quizz.DataModel.ApiModels;
using Quizz.DataAccess;
using Quizz.Auth;

namespace Quizz.Functions.Endpoints;

/// <summary>
/// Authentication endpoints: Login and Signup
/// </summary>
public class AuthEndpoints
{
    private readonly ILogger<AuthEndpoints> _logger;
    private readonly IDbService _dbService;
    private readonly AuthService _authService;

    public AuthEndpoints(ILogger<AuthEndpoints> logger, IDbService dbService, AuthService authService)
    {
        _logger = logger;
        _dbService = dbService;
        _authService = authService;
    }

    /// <summary>
    /// POST /api/auth/login
    /// Public endpoint for user login
    /// </summary>
    [Function("Login")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req)
    {
        _logger.LogInformation("===== LOGIN ATTEMPT STARTED =====");
        
        try
        {
            // Read request body
            string requestBody;
            using (var streamReader = new StreamReader(req.Body))
            {
                requestBody = await streamReader.ReadToEndAsync();
            }
            
            _logger.LogInformation($"Request body received: {requestBody}");
            
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                _logger.LogWarning("Empty request body");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Request body cannot be empty" });
                return badResponse;
            }

            var loginRequest = JsonSerializer.Deserialize<LoginRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation($"Deserialized login request: Username={loginRequest?.Username}");

            if (loginRequest == null || string.IsNullOrWhiteSpace(loginRequest.Username) || string.IsNullOrWhiteSpace(loginRequest.Password))
            {
                _logger.LogWarning("Invalid login request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Username and password are required" });
                return badResponse;
            }

            // Query user from database
            var sql = @"
                SELECT user_id, username, password, full_name, role, is_active
                FROM quiz.users
                WHERE username = @username 
                AND deleted_at IS NULL
                LIMIT 1";

            await using var conn = await _dbService.GetConnectionAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("username", loginRequest.Username.ToLower());

            await using var dbReader = await cmd.ExecuteReaderAsync();
            
            if (!await dbReader.ReadAsync())
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new { error = "Invalid username or password" });
                return unauthorizedResponse;
            }

            var userId = dbReader.GetGuid(0);
            var username = dbReader.GetString(1);
            var password = dbReader.GetString(2);
            var fullName = dbReader.IsDBNull(3) ? null : dbReader.GetString(3);
            var role = dbReader.GetString(4);
            var isActive = dbReader.GetBoolean(5);

            await dbReader.CloseAsync();

            // Check if user is active
            if (!isActive)
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new { error = "Account is deactivated" });
                return unauthorizedResponse;
            }

            // Verify password (plain text comparison for now)
            if (loginRequest.Password != password)
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new { error = "Invalid username or password" });
                return unauthorizedResponse;
            }

            // Get enrolled levels for students
            var enrolledLevels = new List<UserLevelInfo>();
            if (role == "student")
            {
                var levelsSql = @"
                    SELECT id, level, role
                    FROM quiz.user_levels
                    WHERE user_id = @userId
                    ORDER BY created_at";

                await using var levelsCmd = new NpgsqlCommand(levelsSql, conn);
                levelsCmd.Parameters.AddWithValue("userId", userId);

                await using var levelsReader = await levelsCmd.ExecuteReaderAsync();
                while (await levelsReader.ReadAsync())
                {
                    enrolledLevels.Add(new UserLevelInfo
                    {
                        LevelId = levelsReader.GetGuid(0),
                        LevelCode = levelsReader.GetString(1),
                        LevelName = levelsReader.GetString(1),
                        ProgressPercentage = 0,
                        IsCompleted = false
                    });
                }
            }

            // Update last login
            var updateSql = "UPDATE quiz.users SET last_login_at = NOW() WHERE user_id = @userId";
            await using var updateCmd = new NpgsqlCommand(updateSql, conn);
            updateCmd.Parameters.AddWithValue("userId", userId);
            await updateCmd.ExecuteNonQueryAsync();

            // Generate JWT token
            var (token, expiresAt) = _authService.GenerateJwtToken(userId, username, role);

            var loginResponse = new LoginResponse
            {
                UserId = userId,
                Username = username,
                FullName = fullName,
                Role = role,
                Token = token,
                ExpiresAt = expiresAt,
                EnrolledLevels = enrolledLevels
            };

            _logger.LogInformation("User {Username} logged in successfully", username);
            
            var successResponse = req.CreateResponse(HttpStatusCode.OK);
            await successResponse.WriteAsJsonAsync(loginResponse);
            return successResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred during login" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /api/auth/signup
    /// Admin-only endpoint accessible via secret route /create
    /// Creates new user accounts
    /// </summary>
    [Function("Signup")]
    public async Task<HttpResponseData> Signup(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "auth/signup")] HttpRequestData req)
    {
        _logger.LogInformation("===== SIGNUP ATTEMPT STARTED =====");
        try
        {
            // Verify admin authorization from header
            var authHeader = req.Headers.TryGetValues("Authorization", out var authValues) 
                ? authValues.FirstOrDefault() 
                : null;
            
            _logger.LogInformation($"Authorization header received: {authHeader ?? "NULL"}");
            
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                _logger.LogWarning("Authentication failed: Missing or invalid Authorization header");
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorizedResponse;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            _logger.LogInformation($"Token extracted, length: {token.Length}");
            var role = _authService.GetRoleFromToken(token);
            _logger.LogInformation($"Role extracted from token: {role ?? "NULL"}");
            
            if (role != "admin")
            {
                _logger.LogWarning($"Access denied: User role is '{role}', expected 'admin'");
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenResponse.WriteAsJsonAsync(new { error = "Admin access required" });
                return forbiddenResponse;
            }
            
            _logger.LogInformation("Authorization check passed - user is admin");

            // Parse request body
            string requestBody;
            using (var streamReader = new StreamReader(req.Body))
            {
                requestBody = await streamReader.ReadToEndAsync();
            }
            _logger.LogInformation($"Signup request body received: {requestBody}");
            var signupRequest = JsonSerializer.Deserialize<SignupRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            _logger.LogInformation($"Deserialized signup request: Username={signupRequest?.Username}, Role={signupRequest?.Role}");

            if (signupRequest == null || string.IsNullOrWhiteSpace(signupRequest.Username) || string.IsNullOrWhiteSpace(signupRequest.Password))
            {
                _logger.LogWarning("Signup validation failed: Username or password missing");
                var badResponse1 = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse1.WriteAsJsonAsync(new { error = "Username and password are required" });
                return badResponse1;
            }

            // Validate username length
            if (signupRequest.Username.Length < 3)
            {
                var badResponse2 = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse2.WriteAsJsonAsync(new { error = "Username must be at least 3 characters" });
                return badResponse2;
            }

            // Validate role
            if (!new[] { "student", "tutor", "admin" }.Contains(signupRequest.Role))
            {
                _logger.LogWarning($"Invalid role: {signupRequest.Role}");
                var badResponse3 = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse3.WriteAsJsonAsync(new { error = "Invalid role. Must be student, tutor, or admin" });
                return badResponse3;
            }

            _logger.LogInformation("Opening database connection...");
            await using var conn = await _dbService.GetConnectionAsync();
            _logger.LogInformation("Database connection established");

            // Check if username already exists
            _logger.LogInformation($"Checking if username '{signupRequest.Username}' exists...");
            var checkSql = "SELECT COUNT(*) FROM quiz.users WHERE username = @username";
            await using var checkCmd = new NpgsqlCommand(checkSql, conn);
            checkCmd.Parameters.AddWithValue("username", signupRequest.Username.ToLower());
            
            var exists = (long)(await checkCmd.ExecuteScalarAsync() ?? 0) > 0;
            _logger.LogInformation($"Username exists check result: {exists}");
            if (exists)
            {
                _logger.LogWarning($"Username '{signupRequest.Username}' already exists");
                var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                await conflictResponse.WriteAsJsonAsync(new { error = "Username already exists" });
                return conflictResponse;
            }

            // Insert user (plain text password for now)
            _logger.LogInformation($"Inserting user '{signupRequest.Username}' with role '{signupRequest.Role}'...");
            var insertSql = @"
                INSERT INTO quiz.users (username, password, full_name, role, is_active)
                VALUES (@username, @password, @fullName, @role, true)
                RETURNING user_id";

            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("username", signupRequest.Username.ToLower());
            insertCmd.Parameters.AddWithValue("password", signupRequest.Password);
            insertCmd.Parameters.AddWithValue("fullName", (object?)signupRequest.FullName ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("role", signupRequest.Role);

            var userId = (Guid)(await insertCmd.ExecuteScalarAsync() ?? Guid.Empty);
            _logger.LogInformation($"User created with ID: {userId}");

            // Enroll in levels if provided
            var enrolledLevels = new List<string>();
            if (signupRequest.LevelCodes != null && signupRequest.LevelCodes.Any())
            {
                _logger.LogInformation($"Enrolling user in {signupRequest.LevelCodes.Count()} levels...");
                foreach (var levelCode in signupRequest.LevelCodes)
                {
                    var enrollSql = @"
                        INSERT INTO quiz.user_levels (user_id, role, level)
                        VALUES (@userId, @role, @level)";

                    await using var enrollCmd = new NpgsqlCommand(enrollSql, conn);
                    enrollCmd.Parameters.AddWithValue("userId", userId);
                    enrollCmd.Parameters.AddWithValue("role", signupRequest.Role);
                    enrollCmd.Parameters.AddWithValue("level", levelCode.ToLower());

                    await enrollCmd.ExecuteNonQueryAsync();
                    enrolledLevels.Add(levelCode);
                    _logger.LogInformation($"Enrolled in level: {levelCode}");
                }
            }
            else
            {
                _logger.LogInformation("No levels to enroll");
            }

            var signupResponse = new SignupResponse
            {
                UserId = userId,
                Username = signupRequest.Username.ToLower(),
                Role = signupRequest.Role,
                EnrolledLevels = enrolledLevels,
                Message = $"User {signupRequest.Username} created successfully"
            };

            _logger.LogInformation("User {Username} created with role {Role} and userId {UserId}", signupRequest.Username, signupRequest.Role, userId);
            _logger.LogInformation($"Returning signup response: {System.Text.Json.JsonSerializer.Serialize(signupResponse)}");
            var createdResponse = req.CreateResponse(HttpStatusCode.Created);
            createdResponse.Headers.Add("Location", $"/api/users/{userId}");
            await createdResponse.WriteAsJsonAsync(signupResponse);
            _logger.LogInformation("===== SIGNUP COMPLETED SUCCESSFULLY =====");
            return createdResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during signup");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred during signup" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /api/auth/me
    /// Get current user profile
    /// </summary>
    [Function("GetCurrentUser")]
    public async Task<HttpResponseData> GetCurrentUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/me")] HttpRequestData req)
    {
        try
        {
            var authHeader = req.Headers.TryGetValues("Authorization", out var authValues) 
                ? authValues.FirstOrDefault() 
                : null;
            
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorizedResponse;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var userId = _authService.GetUserIdFromToken(token);
            
            if (userId == null)
            {
                var invalidTokenResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await invalidTokenResponse.WriteAsJsonAsync(new { error = "Invalid token" });
                return invalidTokenResponse;
            }

            await using var conn = await _dbService.GetConnectionAsync();

            var sql = @"
                SELECT user_id, username, email, full_name, role, is_active, last_login_at, created_at
                FROM quiz.users
                WHERE user_id = @userId 
                AND deleted_at IS NULL";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("userId", userId.Value);

            await using var userReader = await cmd.ExecuteReaderAsync();
            
            if (!await userReader.ReadAsync())
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "User not found" });
                return notFoundResponse;
            }

            var profile = new UserProfile
            {
                UserId = userReader.GetGuid(0),
                Username = userReader.GetString(1),
                Email = userReader.IsDBNull(2) ? null : userReader.GetString(2),
                FullName = userReader.IsDBNull(3) ? null : userReader.GetString(3),
                Role = userReader.GetString(4),
                IsActive = userReader.GetBoolean(5),
                LastLoginAt = userReader.IsDBNull(6) ? null : userReader.GetDateTime(6),
                CreatedAt = userReader.GetDateTime(7)
            };

            var successResponse = req.CreateResponse(HttpStatusCode.OK);
            await successResponse.WriteAsJsonAsync(profile);
            return successResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred" });
            return errorResponse;
        }
    }
}
