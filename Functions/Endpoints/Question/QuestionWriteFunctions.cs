using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Npgsql;
using NpgsqlTypes;
using Quizz.Auth;
using Quizz.DataAccess;
using Quizz.DataModel.ApiModels;
using Quizz.Functions.Helpers;
using System;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Quizz.Functions.Endpoints.Question
{
    public class QuestionWriteFunctions
    {
        private readonly IDbService _dbService;
        private readonly IApiKeyService _apiKeyService;
        private readonly ILogger<QuestionWriteFunctions> _logger;

        public QuestionWriteFunctions(
            IDbService dbService,
            IApiKeyService apiKeyService,
            ILogger<QuestionWriteFunctions> logger)
        {
            _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
            _apiKeyService = apiKeyService ?? throw new ArgumentNullException(nameof(apiKeyService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function("CreateQuestion")]
        [OpenApiOperation(
            operationId: "CreateQuestion",
            tags: new[] { "Questions - Write" },
            Summary = "Create a new question",
            Description = "Creates a new question. Requires API key with 'question:write' scope.")]
        [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
        [OpenApiRequestBody(
            contentType: "application/json",
            bodyType: typeof(CreateQuestionRequest),
            Required = true,
            Description = "Question creation request")]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.Created,
            contentType: "application/json",
            bodyType: typeof(Quizz.DataModel.ApiModels.Question),
            Description = "Question successfully created")]
        public async Task<HttpResponseData> CreateQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "questions")] HttpRequestData req)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // API Key Authentication (Commented out for LMS integration)
                // Uncomment when you want to use API key authentication instead of LMS session auth
                // var (validation, errorResponse) = await AuthHelper.ValidateApiKeyAsync(
                //     req, _apiKeyService, "question:write", stopwatch);
                // if (errorResponse != null) return errorResponse;

                // TODO: Add user role validation when LMS authentication is integrated
                // Expected roles: tutor, content_creator, admin

                CreateQuestionRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<CreateQuestionRequest>(req.Body);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid JSON in request body");
                    return await ResponseHelper.BadRequestAsync(req, "Invalid JSON format");
                }

                if (request == null || string.IsNullOrWhiteSpace(request.QuestionType) || string.IsNullOrWhiteSpace(request.QuestionText))
                {
                    return await ResponseHelper.BadRequestAsync(req, "QuestionType and QuestionText are required");
                }

                var questionId = Guid.NewGuid();
                var contentJson = JsonSerializer.Serialize(request.Content);

                var sql = @"
                    INSERT INTO quiz.questions (question_id, question_type, question_text, age_min, age_max,
                                                 difficulty, estimated_seconds, subject, locale, points,
                                                 allow_partial_credit, negative_marking, supports_read_aloud,
                                                 content, version, created_at, updated_at)
                    VALUES (@question_id, @question_type, @question_text, @age_min, @age_max,
                            @difficulty, @estimated_seconds, @subject, @locale, @points,
                            @allow_partial_credit, @negative_marking, @supports_read_aloud,
                            @content::jsonb, @version, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                    RETURNING question_id, question_type, question_text, age_min, age_max,
                              difficulty, estimated_seconds, subject, locale, points,
                              allow_partial_credit, negative_marking, supports_read_aloud,
                              content, version, created_at, updated_at";

                using var reader = await _dbService.ExecuteQueryAsync(sql,
                    new NpgsqlParameter("question_id", questionId),
                    new NpgsqlParameter("question_type", request.QuestionType),
                    new NpgsqlParameter("question_text", request.QuestionText),
                    new NpgsqlParameter("age_min", (object?)request.AgeMin ?? DBNull.Value),
                    new NpgsqlParameter("age_max", (object?)request.AgeMax ?? DBNull.Value),
                    new NpgsqlParameter("difficulty", (object?)request.Difficulty ?? DBNull.Value),
                    new NpgsqlParameter("estimated_seconds", (object?)request.EstimatedSeconds ?? DBNull.Value),
                    new NpgsqlParameter("subject", (object?)request.Subject ?? DBNull.Value),
                    new NpgsqlParameter("locale", request.Locale ?? "en-US"),
                    new NpgsqlParameter("points", request.Points ?? 10.0m),
                    new NpgsqlParameter("allow_partial_credit", request.AllowPartialCredit ?? false),
                    new NpgsqlParameter("negative_marking", request.NegativeMarking ?? false),
                    new NpgsqlParameter("supports_read_aloud", request.SupportsReadAloud ?? true),
                    new NpgsqlParameter("content", contentJson),
                    new NpgsqlParameter("version", 1));

                if (!await reader.ReadAsync())
                {
                    return await ResponseHelper.InternalServerErrorAsync(req, "Failed to create question");
                }

                var contentResult = reader.IsDBNull(13) ? "{}" : reader.GetString(13);
                var createdQuestion = new Quizz.DataModel.ApiModels.Question
                {
                    QuestionId = reader.GetGuid(0),
                    QuestionType = reader.GetString(1),
                    QuestionText = reader.GetString(2),
                    AgeMin = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    AgeMax = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Difficulty = reader.IsDBNull(5) ? null : reader.GetString(5),
                    EstimatedSeconds = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    Subject = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Locale = reader.IsDBNull(8) ? "en-US" : reader.GetString(8),
                    Points = reader.IsDBNull(9) ? 10.0m : reader.GetDecimal(9),
                    AllowPartialCredit = reader.IsDBNull(10) ? false : reader.GetBoolean(10),
                    NegativeMarking = reader.IsDBNull(11) ? false : reader.GetBoolean(11),
                    SupportsReadAloud = reader.IsDBNull(12) ? true : reader.GetBoolean(12),
                    Content = JsonSerializer.Deserialize<object>(contentResult),
                    Version = reader.IsDBNull(14) ? 1 : reader.GetInt32(14),
                    CreatedAt = reader.GetDateTime(15),
                    UpdatedAt = reader.GetDateTime(16)
                };

                // API Key Usage Logging (Commented out for LMS integration)
                // if (validation?.ApiKey != null)
                // {
                //     await AuthHelper.LogSuccessfulUsageAsync(req, _apiKeyService, validation.ApiKey.ApiKeyId, "CreateQuestion", 201, stopwatch);
                // }

                _logger.LogInformation($"Created question {questionId} in {stopwatch.ElapsedMilliseconds}ms");
                return await ResponseHelper.CreatedAsync(req, createdQuestion, $"/api/questions/{questionId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating question");
                return await ResponseHelper.InternalServerErrorAsync(req, "Failed to create question");
            }
        }

        [Function("DeleteQuestion")]
        [OpenApiOperation(
            operationId: "DeleteQuestion",
            tags: new[] { "Questions - Write" },
            Summary = "Delete a question",
            Description = "Soft deletes a question. Requires API key with 'question:delete' scope.")]
        [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
        [OpenApiParameter(
            name: "questionId",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(string),
            Description = "The unique identifier of the question")]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.NoContent,
            contentType: "application/json",
            bodyType: typeof(void),
            Description = "Question successfully deleted")]
        public async Task<HttpResponseData> DeleteQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "questions/{questionId}")] HttpRequestData req,
            string questionId)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // API Key Authentication (Commented out for LMS integration)
                // Uncomment when you want to use API key authentication instead of LMS session auth
                // var (validation, errorResponse) = await AuthHelper.ValidateApiKeyAsync(
                //     req, _apiKeyService, "question:delete", stopwatch);
                // if (errorResponse != null) return errorResponse;

                // TODO: Add user role validation when LMS authentication is integrated
                // Expected roles: tutor, admin

                if (!Guid.TryParse(questionId, out var guid))
                {
                    return await ResponseHelper.BadRequestAsync(req, "Invalid question ID format");
                }

                var sql = @"
                    UPDATE quiz.questions
                    SET deleted_at = CURRENT_TIMESTAMP
                    WHERE question_id = @question_id AND deleted_at IS NULL";

                var rowsAffected = await _dbService.ExecuteNonQueryAsync(sql,
                    new NpgsqlParameter("question_id", guid));

                if (rowsAffected == 0)
                {
                    return await ResponseHelper.NotFoundAsync(req, $"Question with ID '{questionId}' not found");
                }

                // API Key Usage Logging (Commented out for LMS integration)
                // if (validation?.ApiKey != null)
                // {
                //     await AuthHelper.LogSuccessfulUsageAsync(req, _apiKeyService, validation.ApiKey.ApiKeyId, "DeleteQuestion", 204, stopwatch);
                // }

                _logger.LogInformation($"Deleted question {questionId} in {stopwatch.ElapsedMilliseconds}ms");
                return await ResponseHelper.NoContentAsync(req);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting question {questionId}");
                return await ResponseHelper.InternalServerErrorAsync(req, "Failed to delete question");
            }
        }
    }
}
