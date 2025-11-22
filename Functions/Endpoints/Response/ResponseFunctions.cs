using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Npgsql;
using Quizz.Auth;
using Quizz.DataAccess;
using Quizz.DataModel.ApiModels;
using Quizz.Functions.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Quizz.Functions.Endpoints.Response
{
    public class ResponseFunctions
    {
        private readonly IDbService _dbService;
        private readonly IApiKeyService _apiKeyService;
        private readonly ILogger<ResponseFunctions> _logger;

        public ResponseFunctions(
            IDbService dbService,
            IApiKeyService apiKeyService,
            ILogger<ResponseFunctions> logger)
        {
            _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
            _apiKeyService = apiKeyService ?? throw new ArgumentNullException(nameof(apiKeyService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function("SubmitAnswer")]
        [OpenApiOperation(
            operationId: "SubmitAnswer",
            tags: new[] { "Responses" },
            Summary = "Submit an answer to a question",
            Description = "Records a user's answer to a question in an attempt. Requires API key.")]
        [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
        [OpenApiRequestBody(
            contentType: "application/json",
            bodyType: typeof(SubmitAnswerRequest),
            Required = true)]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.Created,
            contentType: "application/json",
            bodyType: typeof(Quizz.DataModel.ApiModels.Response),
            Description = "Answer submitted successfully")]
        public async Task<HttpResponseData> SubmitAnswer(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responses")] HttpRequestData req)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // API Key Authentication (Commented out for LMS integration)
                // Uncomment when you want to use API key authentication instead of LMS session auth
                // var (validation, errorResponse) = await AuthHelper.ValidateApiKeyAsync(
                //     req, _apiKeyService, "response:write", stopwatch);
                // if (errorResponse != null) return errorResponse;

                // TODO: Add user role validation when LMS authentication is integrated
                // Expected roles: student (own responses), tutor, admin

                SubmitAnswerRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<SubmitAnswerRequest>(req.Body);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid JSON in request body");
                    return await ResponseHelper.BadRequestAsync(req, "Invalid JSON format");
                }

                if (request == null)
                {
                    return await ResponseHelper.BadRequestAsync(req, "Request body is required");
                }

                var responseId = Guid.NewGuid();
                var answerJson = JsonSerializer.Serialize(request.AnswerPayload);

                // Fetch the correct answer AND points from questions table for auto-grading
                // SECURITY: Don't trust points from frontend - always fetch from database
                var questionSql = @"
                    SELECT points, content, question_type 
                    FROM quiz.questions 
                    WHERE question_id = @question_id";

                decimal pointsEarned = 0;
                decimal pointsPossible = 0;
                bool isCorrect = false;

                using (var questionReader = await _dbService.ExecuteQueryAsync(questionSql,
                    new NpgsqlParameter("question_id", request.QuestionId)))
                {
                    if (await questionReader.ReadAsync())
                    {
                        // Get authoritative points value from database (column 0)
                        pointsPossible = questionReader.IsDBNull(0) ? 10.0m : questionReader.GetDecimal(0);
                        
                        var contentJson = questionReader.GetString(1);
                        var questionType = questionReader.GetString(2);
                        
                        var questionContent = JsonSerializer.Deserialize<JsonElement>(contentJson);
                        
                        // Auto-grade based on question type
                        _logger.LogInformation($"Checking answer for question {request.QuestionId}, type: {questionType}");
                        _logger.LogInformation($"Student answer: {JsonSerializer.Serialize(request.AnswerPayload)}");
                        _logger.LogInformation($"Correct answer from DB: {contentJson}");
                        
                        isCorrect = CheckAnswer(questionContent, request.AnswerPayload, questionType);
                        pointsEarned = isCorrect ? pointsPossible : 0;
                        
                        _logger.LogInformation($"Answer check result: isCorrect={isCorrect}, pointsEarned={pointsEarned}, pointsPossible={pointsPossible}");
                    }
                    else
                    {
                        _logger.LogError($"Question {request.QuestionId} not found in database");
                        return await ResponseHelper.NotFoundAsync(req, $"Question {request.QuestionId} not found");
                    }
                }

                var sql = @"
                    INSERT INTO quiz.responses (
                        response_id, attempt_id, question_id, answer_payload, 
                        submitted_at, points_possible, points_earned, is_correct, graded_at
                    )
                    VALUES (
                        @response_id, @attempt_id, @question_id, @answer_payload::jsonb, 
                        CURRENT_TIMESTAMP, @points_possible, @points_earned, @is_correct, CURRENT_TIMESTAMP
                    )
                    RETURNING response_id, attempt_id, question_id, answer_payload, submitted_at,
                              points_possible, points_earned, is_correct, grading_details, graded_at";

                using var reader = await _dbService.ExecuteQueryAsync(sql,
                    new NpgsqlParameter("response_id", responseId),
                    new NpgsqlParameter("attempt_id", request.AttemptId),
                    new NpgsqlParameter("question_id", request.QuestionId),
                    new NpgsqlParameter("answer_payload", answerJson),
                    new NpgsqlParameter("points_possible", pointsPossible),
                    new NpgsqlParameter("points_earned", pointsEarned),
                    new NpgsqlParameter("is_correct", isCorrect));

                if (!await reader.ReadAsync())
                {
                    return await ResponseHelper.InternalServerErrorAsync(req, "Failed to submit answer");
                }

                var answerResult = reader.GetString(3);
                var gradingResult = reader.IsDBNull(8) ? null : reader.GetString(8);

                var response = new Quizz.DataModel.ApiModels.Response
                {
                    ResponseId = reader.GetGuid(0),
                    AttemptId = reader.GetGuid(1),
                    QuestionId = reader.GetGuid(2),
                    AnswerPayload = JsonSerializer.Deserialize<object>(answerResult) ?? new { },
                    SubmittedAt = reader.GetDateTime(4),
                    PointsPossible = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    PointsEarned = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    IsCorrect = reader.IsDBNull(7) ? null : reader.GetBoolean(7),
                    GradingDetails = gradingResult != null ? JsonSerializer.Deserialize<object>(gradingResult) : null,
                    GradedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                    ScorePercentage = null // Calculated on the frontend or in CompleteAttempt
                };

                // API Key Usage Logging (Commented out for LMS integration)
                // if (validation?.ApiKey != null)
                // {
                //     await AuthHelper.LogSuccessfulUsageAsync(req, _apiKeyService, validation.ApiKey.ApiKeyId, "SubmitAnswer", 201, stopwatch);
                // }

                _logger.LogInformation($"Submitted answer {responseId} in {stopwatch.ElapsedMilliseconds}ms");
                return await ResponseHelper.CreatedAsync(req, response, $"/api/responses/{responseId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting answer");
                return await ResponseHelper.InternalServerErrorAsync(req, "Failed to submit answer");
            }
        }

        [Function("GetAttemptResponses")]
        [OpenApiOperation(
            operationId: "GetAttemptResponses",
            tags: new[] { "Responses" },
            Summary = "Get all responses for an attempt",
            Description = "Retrieves all responses submitted for a specific attempt. No API key required.")]
        [OpenApiParameter(
            name: "attemptId",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(Guid))]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(object),
            Description = "Successfully retrieved responses")]
        public async Task<HttpResponseData> GetAttemptResponses(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "attempts/{attemptId}/responses")] HttpRequestData req,
            string attemptId)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (!Guid.TryParse(attemptId, out var guid))
                {
                    return await ResponseHelper.BadRequestAsync(req, "Invalid attempt ID format");
                }

                var sql = @"
                    SELECT response_id, attempt_id, question_id, answer_payload, submitted_at,
                           points_earned, points_possible, is_correct, grading_details, graded_at
                    FROM quiz.responses
                    WHERE attempt_id = @attempt_id
                    ORDER BY submitted_at";

                using var reader = await _dbService.ExecuteQueryAsync(sql,
                    new NpgsqlParameter("attempt_id", guid));

                var responses = new List<Quizz.DataModel.ApiModels.Response>();
                while (await reader.ReadAsync())
                {
                    var answerResult = reader.GetString(3);
                    var gradingResult = reader.IsDBNull(8) ? null : reader.GetString(8);
                    decimal? pointsEarned = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5);
                    decimal? pointsPossible = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6);
                    decimal? scorePercentage = null;
                    if (pointsEarned.HasValue && pointsPossible.HasValue && pointsPossible.Value > 0)
                    {
                        scorePercentage = (pointsEarned.Value / pointsPossible.Value) * 100;
                    }

                    responses.Add(new Quizz.DataModel.ApiModels.Response
                    {
                        ResponseId = reader.GetGuid(0),
                        AttemptId = reader.GetGuid(1),
                        QuestionId = reader.GetGuid(2),
                        AnswerPayload = JsonSerializer.Deserialize<object>(answerResult) ?? new { },
                        SubmittedAt = reader.GetDateTime(4),
                        PointsEarned = pointsEarned,
                        PointsPossible = pointsPossible,
                        IsCorrect = reader.IsDBNull(7) ? null : reader.GetBoolean(7),
                        GradingDetails = gradingResult != null ? JsonSerializer.Deserialize<object>(gradingResult) : null,
                        GradedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                        ScorePercentage = scorePercentage
                    });
                }

                var result = new
                {
                    attemptId = guid,
                    responses,
                    count = responses.Count
                };

                _logger.LogInformation($"Retrieved {responses.Count} responses for attempt {attemptId} in {stopwatch.ElapsedMilliseconds}ms");
                return await ResponseHelper.OkAsync(req, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving responses for attempt {attemptId}");
                return await ResponseHelper.InternalServerErrorAsync(req, "Failed to retrieve responses");
            }
        }

        [Function("GetResponseById")]
        [OpenApiOperation(
            operationId: "GetResponseById",
            tags: new[] { "Responses" },
            Summary = "Get response by ID",
            Description = "Retrieves a specific response by its ID. No API key required.")]
        [OpenApiParameter(
            name: "responseId",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(Guid))]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(Quizz.DataModel.ApiModels.Response),
            Description = "Successfully retrieved response")]
        public async Task<HttpResponseData> GetResponseById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "responses/{responseId}")] HttpRequestData req,
            string responseId)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (!Guid.TryParse(responseId, out var guid))
                {
                    return await ResponseHelper.BadRequestAsync(req, "Invalid response ID format");
                }

                var sql = @"
                    SELECT response_id, attempt_id, question_id, answer_payload, submitted_at,
                           points_earned, points_possible, is_correct, grading_details, graded_at
                    FROM quiz.responses
                    WHERE response_id = @response_id";

                using var reader = await _dbService.ExecuteQueryAsync(sql,
                    new NpgsqlParameter("response_id", guid));

                if (!await reader.ReadAsync())
                {
                    return await ResponseHelper.NotFoundAsync(req, $"Response with ID '{responseId}' not found");
                }

                var answerResult = reader.GetString(3);
                var gradingResult = reader.IsDBNull(8) ? null : reader.GetString(8);
                
                // Read points
                decimal? pointsEarned = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5);
                decimal? pointsPossible = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6);
                
                // Calculate score percentage if both values are available
                decimal? scorePercentage = null;
                if (pointsEarned.HasValue && pointsPossible.HasValue && pointsPossible.Value > 0)
                {
                    scorePercentage = (pointsEarned.Value / pointsPossible.Value) * 100;
                }

                var response = new Quizz.DataModel.ApiModels.Response
                {
                    ResponseId = reader.GetGuid(0),
                    AttemptId = reader.GetGuid(1),
                    QuestionId = reader.GetGuid(2),
                    AnswerPayload = JsonSerializer.Deserialize<object>(answerResult) ?? new { },
                    SubmittedAt = reader.GetDateTime(4),
                    PointsEarned = pointsEarned,
                    PointsPossible = pointsPossible,
                    IsCorrect = reader.IsDBNull(7) ? null : reader.GetBoolean(7),
                    GradingDetails = gradingResult != null ? JsonSerializer.Deserialize<object>(gradingResult) : null,
                    GradedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                    ScorePercentage = scorePercentage
                };

                _logger.LogInformation($"Retrieved response {responseId} in {stopwatch.ElapsedMilliseconds}ms");
                return await ResponseHelper.OkAsync(req, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving response {responseId}");
                return await ResponseHelper.InternalServerErrorAsync(req, "Failed to retrieve response");
            }
        }

        [Function("GradeResponse")]
        [OpenApiOperation(
            operationId: "GradeResponse",
            tags: new[] { "Responses" },
            Summary = "Grade a response",
            Description = "Grades a response and updates the score. Requires API key with 'response:grade' scope.")]
        [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "X-API-Key")]
        [OpenApiParameter(
            name: "responseId",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(Guid))]
        [OpenApiRequestBody(
            contentType: "application/json",
            bodyType: typeof(GradingResult),
            Required = true)]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(Quizz.DataModel.ApiModels.Response),
            Description = "Response graded successfully")]
        public async Task<HttpResponseData> GradeResponse(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responses/{responseId}/grade")] HttpRequestData req,
            string responseId)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // API Key Authentication (Commented out for LMS integration)
                // Uncomment when you want to use API key authentication instead of LMS session auth
                // var (validation, errorResponse) = await AuthHelper.ValidateApiKeyAsync(
                //     req, _apiKeyService, "response:grade", stopwatch);
                // if (errorResponse != null) return errorResponse;

                // TODO: Add user role validation when LMS authentication is integrated
                // Expected roles: tutor, admin (grading permissions)

                if (!Guid.TryParse(responseId, out var guid))
                {
                    return await ResponseHelper.BadRequestAsync(req, "Invalid response ID format");
                }

                GradingResult? gradingResult;
                try
                {
                    gradingResult = await JsonSerializer.DeserializeAsync<GradingResult>(req.Body);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid JSON in request body");
                    return await ResponseHelper.BadRequestAsync(req, "Invalid JSON format");
                }

                if (gradingResult == null)
                {
                    return await ResponseHelper.BadRequestAsync(req, "Grading result is required");
                }

                var scorePercentage = gradingResult.PointsPossible > 0 
                    ? (gradingResult.PointsEarned / gradingResult.PointsPossible) * 100 
                    : 0;

                var gradingDetailsJson = gradingResult.GradingDetails != null 
                    ? JsonSerializer.Serialize(gradingResult.GradingDetails) 
                    : null;

                var sql = @"
                    UPDATE quiz.responses
                    SET points_earned = @points_earned,
                        points_possible = @points_possible,
                        is_correct = @is_correct,
                        grading_details = @grading_details::jsonb,
                        graded_at = CURRENT_TIMESTAMP,
                        score_percentage = @score_percentage
                    WHERE response_id = @response_id
                    RETURNING response_id, attempt_id, question_id, answer_payload, submitted_at,
                              points_earned, points_possible, is_correct, grading_details, graded_at, score_percentage";

                using var reader = await _dbService.ExecuteQueryAsync(sql,
                    new NpgsqlParameter("response_id", guid),
                    new NpgsqlParameter("points_earned", gradingResult.PointsEarned),
                    new NpgsqlParameter("points_possible", gradingResult.PointsPossible),
                    new NpgsqlParameter("is_correct", gradingResult.IsCorrect),
                    new NpgsqlParameter("grading_details", (object?)gradingDetailsJson ?? DBNull.Value),
                    new NpgsqlParameter("score_percentage", scorePercentage));

                if (!await reader.ReadAsync())
                {
                    return await ResponseHelper.NotFoundAsync(req, $"Response with ID '{responseId}' not found");
                }

                var answerResult = reader.GetString(3);
                var gradingDetailsResult = reader.IsDBNull(8) ? null : reader.GetString(8);

                var response = new Quizz.DataModel.ApiModels.Response
                {
                    ResponseId = reader.GetGuid(0),
                    AttemptId = reader.GetGuid(1),
                    QuestionId = reader.GetGuid(2),
                    AnswerPayload = JsonSerializer.Deserialize<object>(answerResult) ?? new { },
                    SubmittedAt = reader.GetDateTime(4),
                    PointsEarned = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    PointsPossible = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    IsCorrect = reader.IsDBNull(7) ? null : reader.GetBoolean(7),
                    GradingDetails = gradingDetailsResult != null ? JsonSerializer.Deserialize<object>(gradingDetailsResult) : null,
                    GradedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                    ScorePercentage = reader.IsDBNull(10) ? null : reader.GetDecimal(10)
                };

                // API Key Usage Logging (Commented out for LMS integration)
                // if (validation?.ApiKey != null)
                // {
                //     await AuthHelper.LogSuccessfulUsageAsync(req, _apiKeyService, validation.ApiKey.ApiKeyId, "GradeResponse", 200, stopwatch);
                // }

                _logger.LogInformation($"Graded response {responseId} in {stopwatch.ElapsedMilliseconds}ms");
                return await ResponseHelper.OkAsync(req, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error grading response {responseId}");
                return await ResponseHelper.InternalServerErrorAsync(req, "Failed to grade response");
            }
        }

        /// <summary>
        /// Auto-grade answer by comparing student response with correct answer
        /// </summary>
        private bool CheckAnswer(JsonElement questionContent, object studentAnswer, string questionType)
        {
            try
            {
                var studentAnswerJson = JsonSerializer.Serialize(studentAnswer);
                var studentAnswerElement = JsonSerializer.Deserialize<JsonElement>(studentAnswerJson);

                switch (questionType.ToLower())
                {
                    case "multiple_choice_single":
                        // Compare selected option ID
                        // Frontend sends either "a" (string) or {"selectedOptionId": "a"} (object)
                        if (questionContent.TryGetProperty("correct_answer", out var correctAnswer))
                        {
                            string studentSelection = null;
                            
                            // Check if it's a string first (frontend sends raw string)
                            if (studentAnswerElement.ValueKind == JsonValueKind.String)
                            {
                                studentSelection = studentAnswerElement.GetString();
                            }
                            // If it's an object, try to get the "selectedOptionId" property
                            else if (studentAnswerElement.ValueKind == JsonValueKind.Object &&
                                     studentAnswerElement.TryGetProperty("selectedOptionId", out var selectedOption))
                            {
                                studentSelection = selectedOption.GetString();
                            }
                            
                            if (studentSelection != null)
                            {
                                return correctAnswer.GetString() == studentSelection;
                            }
                        }
                        break;

                    case "multiple_choice_multi":
                        // Compare array of selected option IDs
                        // Frontend sends either ["a","b"] (array) or {"selectedOptionIds": ["a","b"]} (object)
                        if (questionContent.TryGetProperty("correct_answers", out var correctAnswers))
                        {
                            JsonElement selectedOptionsElement;
                            
                            // Check if it's already an array first (frontend sends raw array)
                            if (studentAnswerElement.ValueKind == JsonValueKind.Array)
                            {
                                selectedOptionsElement = studentAnswerElement;
                            }
                            // If it's an object, try to get the "selectedOptionIds" property
                            else if (studentAnswerElement.ValueKind == JsonValueKind.Object &&
                                     studentAnswerElement.TryGetProperty("selectedOptionIds", out var selectedOptionsObj))
                            {
                                selectedOptionsElement = selectedOptionsObj;
                            }
                            else
                            {
                                break;
                            }
                            
                            var correctSet = correctAnswers.EnumerateArray()
                                .Select(x => x.GetString())
                                .OrderBy(x => x)
                                .ToList();
                            var studentSet = selectedOptionsElement.EnumerateArray()
                                .Select(x => x.GetString())
                                .OrderBy(x => x)
                                .ToList();
                            return correctSet.SequenceEqual(studentSet);
                        }
                        break;

                    case "true_false":
                        // Compare boolean value
                        // Frontend sends either true/false (boolean) or {"answer": true/false} (object)
                        if (questionContent.TryGetProperty("correctAnswer", out var correctTF))
                        {
                            bool? studentTFValue = null;
                            
                            // Check if it's a boolean first (frontend sends raw boolean)
                            if (studentAnswerElement.ValueKind == JsonValueKind.True || 
                                studentAnswerElement.ValueKind == JsonValueKind.False)
                            {
                                studentTFValue = studentAnswerElement.GetBoolean();
                            }
                            // If it's an object, try to get the "answer" property
                            else if (studentAnswerElement.ValueKind == JsonValueKind.Object &&
                                     studentAnswerElement.TryGetProperty("answer", out var studentTFObj))
                            {
                                studentTFValue = studentTFObj.GetBoolean();
                            }
                            
                            if (studentTFValue.HasValue)
                            {
                                return correctTF.GetBoolean() == studentTFValue.Value;
                            }
                        }
                        break;

                    case "matching":
                        // Compare pairs array
                        if ((questionContent.TryGetProperty("correctPairs", out var correctPairs) || 
                             questionContent.TryGetProperty("correct_pairs", out correctPairs)) &&
                            studentAnswerElement.TryGetProperty("pairs", out var studentPairs))
                        {
                            var correctPairsList = correctPairs.EnumerateArray()
                                .Select(p => new { 
                                    Left = p.GetProperty("left").GetString(), 
                                    Right = p.GetProperty("right").GetString() 
                                })
                                .OrderBy(p => p.Left)
                                .ToList();
                            var studentPairsList = studentPairs.EnumerateArray()
                                .Select(p => new { 
                                    Left = p.GetProperty("left").GetString(), 
                                    Right = p.GetProperty("right").GetString() 
                                })
                                .OrderBy(p => p.Left)
                                .ToList();
                            
                            if (correctPairsList.Count != studentPairsList.Count) return false;
                            
                            for (int i = 0; i < correctPairsList.Count; i++)
                            {
                                if (correctPairsList[i].Left != studentPairsList[i].Left ||
                                    correctPairsList[i].Right != studentPairsList[i].Right)
                                {
                                    return false;
                                }
                            }
                            return true;
                        }
                        break;

                    case "ordering":
                        // Compare order array
                        if ((questionContent.TryGetProperty("correctOrder", out var correctOrder) ||
                             questionContent.TryGetProperty("correct_order", out correctOrder)) &&
                            studentAnswerElement.TryGetProperty("order", out var studentOrder))
                        {
                            var correctList = correctOrder.EnumerateArray()
                                .Select(x => x.GetString())
                                .ToList();
                            var studentList = studentOrder.EnumerateArray()
                                .Select(x => x.GetString())
                                .ToList();
                            return correctList.SequenceEqual(studentList);
                        }
                        break;

                    case "fill_in_blank":
                        // Compare text answers (case-insensitive)
                        // Frontend sends either ["on","4","1"] (array) or {"answers": ["on","4","1"]} (object)
                        if (questionContent.TryGetProperty("blanks", out var blanks))
                        {
                            JsonElement studentAnswersElement;
                            
                            // Check if it's already an array first (frontend sends raw array)
                            if (studentAnswerElement.ValueKind == JsonValueKind.Array)
                            {
                                studentAnswersElement = studentAnswerElement;
                            }
                            // If it's an object, try to get the "answers" property
                            else if (studentAnswerElement.ValueKind == JsonValueKind.Object &&
                                     studentAnswerElement.TryGetProperty("answers", out var answersObj))
                            {
                                studentAnswersElement = answersObj;
                            }
                            else
                            {
                                break;
                            }
                            
                            var blanksArray = blanks.EnumerateArray().ToList();
                            var studentAnswersArray = studentAnswersElement.EnumerateArray().ToList();
                            
                            if (blanksArray.Count != studentAnswersArray.Count) return false;
                            
                            for (int i = 0; i < blanksArray.Count; i++)
                            {
                                var blank = blanksArray[i];
                                var studentAns = studentAnswersArray[i].GetString()?.Trim().ToLower();
                                
                                if (blank.TryGetProperty("accepted_answers", out var acceptedAnswers))
                                {
                                    var acceptedList = acceptedAnswers.EnumerateArray()
                                        .Select(x => x.GetString()?.Trim().ToLower())
                                        .ToList();
                                    
                                    if (!acceptedList.Contains(studentAns))
                                    {
                                        return false;
                                    }
                                }
                            }
                            return true;
                        }
                        break;

                    case "short_answer":
                    case "essay":
                        // These require manual grading
                        return false;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Error checking answer for question type {questionType}");
                return false;
            }
        }
    }
}
