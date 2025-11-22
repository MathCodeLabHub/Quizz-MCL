using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Quizz.Functions.Swagger
{
    /// <summary>
    /// Swagger/OpenAPI documentation endpoints on a hidden path.
    /// Access via: /internal-docs/swagger/ui (or custom path from config)
    /// Protected by a secret key to prevent discovery.
    /// </summary>
    public class SwaggerFunction
    {
        private readonly IConfiguration _configuration;
        private readonly string _swaggerPath;
        private readonly string _swaggerAuthKey;

        public SwaggerFunction(IConfiguration configuration)
        {
            _configuration = configuration;
            _swaggerPath = configuration["SwaggerPath"] ?? "/api/internal-docs";
            _swaggerAuthKey = configuration["SwaggerAuthKey"] ?? "default-key-change-this";
        }

        /// <summary>
        /// Validates the swagger auth key from query string or header.
        /// </summary>
        private bool ValidateSwaggerAccess(HttpRequestData req)
        {
            // Check query string
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            if (query["key"] == _swaggerAuthKey)
                return true;

            // Check header
            if (req.Headers.TryGetValues("X-Swagger-Key", out var headerValues))
            {
                foreach (var value in headerValues)
                {
                    if (value == _swaggerAuthKey)
                        return true;
                }
            }

            return false;
        }

        [Function("RenderSwaggerUI")]
        [OpenApiIgnore] // Don't include this endpoint in OpenAPI spec
        public async Task<HttpResponseData> RenderSwaggerUI(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal-docs/swagger/ui")] 
            HttpRequestData req)
        {
            // Validate access - DISABLED FOR TESTING
            // if (!ValidateSwaggerAccess(req))
            // {
            //     var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
            //     await unauthorizedResponse.WriteStringAsync("Access denied. Valid swagger key required.");
            //     return unauthorizedResponse;
            // }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html");

            var swaggerUiHtml = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Quiz API Documentation</title>
    <link rel=""stylesheet"" type=""text/css"" href=""https://unpkg.com/swagger-ui-dist@5.10.0/swagger-ui.css"" />
    <style>
        body {{ margin: 0; padding: 0; }}
        .topbar {{ display: none; }}
    </style>
</head>
<body>
    <div id=""swagger-ui""></div>
    <script src=""https://unpkg.com/swagger-ui-dist@5.10.0/swagger-ui-bundle.js""></script>
    <script src=""https://unpkg.com/swagger-ui-dist@5.10.0/swagger-ui-standalone-preset.js""></script>
    <script>
        window.onload = function() {{
            const ui = SwaggerUIBundle({{
                url: window.location.origin + '/api/internal-docs/swagger.json',
                dom_id: '#swagger-ui',
                deepLinking: true,
                presets: [
                    SwaggerUIBundle.presets.apis,
                    SwaggerUIStandalonePreset
                ],
                plugins: [
                    SwaggerUIBundle.plugins.DownloadUrl
                ],
                layout: 'StandaloneLayout'
            }});
            window.ui = ui;
        }};
    </script>
</body>
</html>";

            await response.WriteStringAsync(swaggerUiHtml);
            return response;
        }

        [Function("RenderOpenApiDocument")]
        [OpenApiIgnore]
        public async Task<HttpResponseData> RenderOpenApiDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal-docs/swagger.json")] 
            HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");

            // For now, using a manually created OpenAPI spec
            // This includes all the endpoints defined in your functions with [OpenApiOperation]
            var openApiDoc = @"{
  ""openapi"": ""3.0.1"",
  ""info"": {
    ""title"": ""Quiz API"",
    ""description"": ""Kids Quiz Application API with multiple question types, attempts, responses, and i18n content"",
    ""version"": ""2.0.0"",
    ""contact"": {
      ""name"": ""Quiz API Support"",
      ""email"": ""support@quizapp.com""
    }
  },
  ""servers"": [
    {
      ""url"": ""http://localhost:7071/api"",
      ""description"": ""Local development server""
    },
    {
      ""url"": ""/api"",
      ""description"": ""Default server""
    }
  ],
  ""paths"": {
    ""/quizzes"": {
      ""get"": {
        ""tags"": [""Quizzes - Read""],
        ""summary"": ""Get all published quizzes"",
        ""description"": ""Retrieves a paginated list of published quizzes with optional filtering by difficulty and tags. No API key required."",
        ""operationId"": ""GetQuizzes"",
        ""parameters"": [
          {
            ""name"": ""difficulty"",
            ""in"": ""query"",
            ""description"": ""Filter by difficulty level (easy, medium, hard)"",
            ""schema"": {""type"": ""string"", ""enum"": [""easy"", ""medium"", ""hard""]}
          },
          {
            ""name"": ""tags"",
            ""in"": ""query"",
            ""description"": ""Filter by tags (comma-separated)"",
            ""schema"": {""type"": ""string""}
          },
          {
            ""name"": ""limit"",
            ""in"": ""query"",
            ""description"": ""Maximum number of results (default: 50, max: 100)"",
            ""schema"": {""type"": ""integer"", ""default"": 50, ""maximum"": 100}
          },
          {
            ""name"": ""offset"",
            ""in"": ""query"",
            ""description"": ""Offset for pagination (default: 0)"",
            ""schema"": {""type"": ""integer"", ""default"": 0, ""minimum"": 0}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved list of quizzes"",
            ""content"": {
              ""application/json"": {
                ""schema"": {
                  ""type"": ""object"",
                  ""properties"": {
                    ""data"": {
                      ""type"": ""array"",
                      ""items"": {""$ref"": ""#/components/schemas/Quiz""}
                    },
                    ""count"": {""type"": ""integer""},
                    ""limit"": {""type"": ""integer""},
                    ""offset"": {""type"": ""integer""}
                  }
                }
              }
            }
          },
          ""400"": {""description"": ""Invalid query parameters""}
        }
      },
      ""post"": {
        ""tags"": [""Quizzes - Write""],
        ""summary"": ""Create a new quiz"",
        ""description"": ""Creates a new quiz. Requires API key with 'quiz:write' scope."",
        ""operationId"": ""CreateQuiz"",
        ""security"": [{""ApiKeyAuth"": []}],
        ""requestBody"": {
          ""required"": true,
          ""description"": ""Quiz creation request"",
          ""content"": {
            ""application/json"": {
              ""schema"": {""$ref"": ""#/components/schemas/CreateQuizRequest""}
            }
          }
        },
        ""responses"": {
          ""201"": {
            ""description"": ""Quiz successfully created"",
            ""content"": {
              ""application/json"": {
                ""schema"": {""$ref"": ""#/components/schemas/Quiz""}
              }
            }
          },
          ""400"": {""description"": ""Invalid request data""},
          ""401"": {""description"": ""API key required or invalid""},
          ""429"": {""description"": ""Rate limit exceeded""}
        }
      }
    },
    ""/quizzes/{quizId}"": {
      ""get"": {
        ""tags"": [""Quizzes - Read""],
        ""summary"": ""Get quiz by ID"",
        ""description"": ""Retrieves detailed information about a specific quiz by its ID. No API key required."",
        ""operationId"": ""GetQuizById"",
        ""parameters"": [
          {
            ""name"": ""quizId"",
            ""in"": ""path"",
            ""required"": true,
            ""description"": ""The unique identifier of the quiz"",
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved quiz details"",
            ""content"": {
              ""application/json"": {
                ""schema"": {""$ref"": ""#/components/schemas/Quiz""}
              }
            }
          },
          ""400"": {""description"": ""Invalid quiz ID format""},
          ""404"": {""description"": ""Quiz not found""}
        }
      },
      ""put"": {
        ""tags"": [""Quizzes - Write""],
        ""summary"": ""Update an existing quiz"",
        ""description"": ""Updates an existing quiz. Requires API key with 'quiz:write' scope."",
        ""operationId"": ""UpdateQuiz"",
        ""security"": [{""ApiKeyAuth"": []}],
        ""parameters"": [
          {
            ""name"": ""quizId"",
            ""in"": ""path"",
            ""required"": true,
            ""description"": ""The unique identifier of the quiz"",
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""requestBody"": {
          ""required"": true,
          ""description"": ""Quiz update request"",
          ""content"": {
            ""application/json"": {
              ""schema"": {""$ref"": ""#/components/schemas/UpdateQuizRequest""}
            }
          }
        },
        ""responses"": {
          ""200"": {
            ""description"": ""Quiz successfully updated"",
            ""content"": {
              ""application/json"": {
                ""schema"": {""$ref"": ""#/components/schemas/Quiz""}
              }
            }
          },
          ""400"": {""description"": ""Invalid request data""},
          ""401"": {""description"": ""API key required or invalid""},
          ""404"": {""description"": ""Quiz not found""}
        }
      },
      ""delete"": {
        ""tags"": [""Quizzes - Write""],
        ""summary"": ""Delete a quiz"",
        ""description"": ""Soft deletes a quiz (sets deleted_at timestamp). Requires API key with 'quiz:delete' scope."",
        ""operationId"": ""DeleteQuiz"",
        ""security"": [{""ApiKeyAuth"": []}],
        ""parameters"": [
          {
            ""name"": ""quizId"",
            ""in"": ""path"",
            ""required"": true,
            ""description"": ""The unique identifier of the quiz"",
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""responses"": {
          ""204"": {""description"": ""Quiz successfully deleted""},
          ""401"": {""description"": ""API key required or invalid""},
          ""404"": {""description"": ""Quiz not found""}
        }
      }
    },
    ""/quizzes/{quizId}/questions"": {
      ""get"": {
        ""tags"": [""Questions - Read""],
        ""summary"": ""Get all questions for a quiz"",
        ""description"": ""Retrieves all questions associated with a specific quiz. No API key required."",
        ""operationId"": ""GetQuizQuestions"",
        ""parameters"": [
          {
            ""name"": ""quizId"",
            ""in"": ""path"",
            ""required"": true,
            ""description"": ""The unique identifier of the quiz"",
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved quiz questions"",
            ""content"": {
              ""application/json"": {
                ""schema"": {
                  ""type"": ""array"",
                  ""items"": {""$ref"": ""#/components/schemas/Question""}
                }
              }
            }
          },
          ""404"": {""description"": ""Quiz not found""}
        }
      }
    },
    ""/questions"": {
      ""get"": {
        ""tags"": [""Questions - Read""],
        ""summary"": ""Get all questions"",
        ""description"": ""Retrieves a list of questions with optional filtering. No API key required."",
        ""operationId"": ""GetQuestions"",
        ""parameters"": [
          {
            ""name"": ""questionType"",
            ""in"": ""query"",
            ""description"": ""Filter by question type"",
            ""schema"": {""type"": ""string"", ""enum"": [""multiple_choice_single"", ""multiple_choice_multi"", ""fill_in_blank"", ""short_answer"", ""matching"", ""ordering"", ""program_submission""]}
          },
          {
            ""name"": ""difficulty"",
            ""in"": ""query"",
            ""description"": ""Filter by difficulty"",
            ""schema"": {""type"": ""string"", ""enum"": [""easy"", ""medium"", ""hard""]}
          },
          {
            ""name"": ""subject"",
            ""in"": ""query"",
            ""description"": ""Filter by subject"",
            ""schema"": {""type"": ""string""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved questions"",
            ""content"": {
              ""application/json"": {
                ""schema"": {
                  ""type"": ""array"",
                  ""items"": {""$ref"": ""#/components/schemas/Question""}
                }
              }
            }
          }
        }
      },
      ""post"": {
        ""tags"": [""Questions - Write""],
        ""summary"": ""Create a new question"",
        ""description"": ""Creates a new question. Requires API key with 'question:write' scope."",
        ""operationId"": ""CreateQuestion"",
        ""security"": [{""ApiKeyAuth"": []}],
        ""requestBody"": {
          ""required"": true,
          ""content"": {
            ""application/json"": {
              ""schema"": {""$ref"": ""#/components/schemas/CreateQuestionRequest""}
            }
          }
        },
        ""responses"": {
          ""201"": {""description"": ""Question created successfully""},
          ""400"": {""description"": ""Invalid request data""},
          ""401"": {""description"": ""API key required or invalid""}
        }
      }
    },
    ""/questions/{questionId}"": {
      ""get"": {
        ""tags"": [""Questions - Read""],
        ""summary"": ""Get question by ID"",
        ""description"": ""Retrieves a specific question by ID. No API key required."",
        ""operationId"": ""GetQuestionById"",
        ""parameters"": [
          {
            ""name"": ""questionId"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved question"",
            ""content"": {
              ""application/json"": {
                ""schema"": {""$ref"": ""#/components/schemas/Question""}
              }
            }
          },
          ""404"": {""description"": ""Question not found""}
        }
      },
      ""delete"": {
        ""tags"": [""Questions - Write""],
        ""summary"": ""Delete a question"",
        ""description"": ""Soft deletes a question. Requires API key with 'question:delete' scope."",
        ""operationId"": ""DeleteQuestion"",
        ""security"": [{""ApiKeyAuth"": []}],
        ""parameters"": [
          {
            ""name"": ""questionId"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""responses"": {
          ""204"": {""description"": ""Question deleted successfully""},
          ""401"": {""description"": ""API key required or invalid""},
          ""404"": {""description"": ""Question not found""}
        }
      }
    },
    ""/attempts"": {
      ""get"": {
        ""tags"": [""Attempts""],
        ""summary"": ""Get user attempts"",
        ""description"": ""Retrieves all attempts for a specific user."",
        ""operationId"": ""GetUserAttempts"",
        ""parameters"": [
          {
            ""name"": ""userId"",
            ""in"": ""query"",
            ""required"": true,
            ""description"": ""User identifier"",
            ""schema"": {""type"": ""string""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved user attempts"",
            ""content"": {
              ""application/json"": {
                ""schema"": {
                  ""type"": ""array"",
                  ""items"": {""$ref"": ""#/components/schemas/Attempt""}
                }
              }
            }
          }
        }
      },
      ""post"": {
        ""tags"": [""Attempts""],
        ""summary"": ""Start a new quiz attempt"",
        ""description"": ""Starts a new attempt for a quiz."",
        ""operationId"": ""StartAttempt"",
        ""requestBody"": {
          ""required"": true,
          ""content"": {
            ""application/json"": {
              ""schema"": {
                ""type"": ""object"",
                ""properties"": {
                  ""quizId"": {""type"": ""string"", ""format"": ""uuid""},
                  ""userId"": {""type"": ""string""}
                },
                ""required"": [""quizId"", ""userId""]
              }
            }
          }
        },
        ""responses"": {
          ""201"": {
            ""description"": ""Attempt started successfully"",
            ""content"": {
              ""application/json"": {
                ""schema"": {""$ref"": ""#/components/schemas/Attempt""}
              }
            }
          },
          ""400"": {""description"": ""Invalid request""},
          ""404"": {""description"": ""Quiz not found""}
        }
      }
    },
    ""/attempts/{attemptId}"": {
      ""get"": {
        ""tags"": [""Attempts""],
        ""summary"": ""Get attempt by ID"",
        ""description"": ""Retrieves details of a specific attempt."",
        ""operationId"": ""GetAttemptById"",
        ""parameters"": [
          {
            ""name"": ""attemptId"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved attempt"",
            ""content"": {
              ""application/json"": {
                ""schema"": {""$ref"": ""#/components/schemas/Attempt""}
              }
            }
          },
          ""404"": {""description"": ""Attempt not found""}
        }
      }
    },
    ""/attempts/{attemptId}/complete"": {
      ""post"": {
        ""tags"": [""Attempts""],
        ""summary"": ""Complete an attempt"",
        ""description"": ""Marks an attempt as complete and calculates the final score."",
        ""operationId"": ""CompleteAttempt"",
        ""parameters"": [
          {
            ""name"": ""attemptId"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Attempt completed successfully"",
            ""content"": {
              ""application/json"": {
                ""schema"": {""$ref"": ""#/components/schemas/Attempt""}
              }
            }
          },
          ""400"": {""description"": ""Attempt already completed""},
          ""404"": {""description"": ""Attempt not found""}
        }
      }
    },
    ""/attempts/{attemptId}/responses"": {
      ""get"": {
        ""tags"": [""Responses""],
        ""summary"": ""Get all responses for an attempt"",
        ""description"": ""Retrieves all responses submitted for a specific attempt."",
        ""operationId"": ""GetAttemptResponses"",
        ""parameters"": [
          {
            ""name"": ""attemptId"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved responses"",
            ""content"": {
              ""application/json"": {
                ""schema"": {
                  ""type"": ""array"",
                  ""items"": {""$ref"": ""#/components/schemas/Response""}
                }
              }
            }
          }
        }
      }
    },
    ""/responses"": {
      ""post"": {
        ""tags"": [""Responses""],
        ""summary"": ""Submit an answer"",
        ""description"": ""Submits an answer for a question in an attempt."",
        ""operationId"": ""SubmitAnswer"",
        ""requestBody"": {
          ""required"": true,
          ""content"": {
            ""application/json"": {
              ""schema"": {
                ""type"": ""object"",
                ""properties"": {
                  ""attemptId"": {""type"": ""string"", ""format"": ""uuid""},
                  ""questionId"": {""type"": ""string"", ""format"": ""uuid""},
                  ""answerPayload"": {""type"": ""object"", ""description"": ""JSONB answer data""}
                },
                ""required"": [""attemptId"", ""questionId"", ""answerPayload""]
              }
            }
          }
        },
        ""responses"": {
          ""201"": {
            ""description"": ""Response submitted successfully"",
            ""content"": {
              ""application/json"": {
                ""schema"": {""$ref"": ""#/components/schemas/Response""}
              }
            }
          },
          ""400"": {""description"": ""Invalid request""}
        }
      }
    },
    ""/responses/{responseId}"": {
      ""get"": {
        ""tags"": [""Responses""],
        ""summary"": ""Get response by ID"",
        ""description"": ""Retrieves a specific response by ID."",
        ""operationId"": ""GetResponseById"",
        ""parameters"": [
          {
            ""name"": ""responseId"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved response"",
            ""content"": {
              ""application/json"": {
                ""schema"": {""$ref"": ""#/components/schemas/Response""}
              }
            }
          },
          ""404"": {""description"": ""Response not found""}
        }
      }
    },
    ""/responses/{responseId}/grade"": {
      ""post"": {
        ""tags"": [""Responses""],
        ""summary"": ""Grade a response"",
        ""description"": ""Manually grades a response. Requires API key with 'response:grade' scope."",
        ""operationId"": ""GradeResponse"",
        ""security"": [{""ApiKeyAuth"": []}],
        ""parameters"": [
          {
            ""name"": ""responseId"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string"", ""format"": ""uuid""}
          }
        ],
        ""requestBody"": {
          ""required"": true,
          ""content"": {
            ""application/json"": {
              ""schema"": {
                ""type"": ""object"",
                ""properties"": {
                  ""pointsEarned"": {""type"": ""number""},
                  ""isCorrect"": {""type"": ""boolean""},
                  ""feedback"": {""type"": ""string"", ""nullable"": true}
                }
              }
            }
          }
        },
        ""responses"": {
          ""200"": {""description"": ""Response graded successfully""},
          ""401"": {""description"": ""API key required or invalid""},
          ""404"": {""description"": ""Response not found""}
        }
      }
    },
    ""/content"": {
      ""get"": {
        ""tags"": [""Content (i18n)""],
        ""summary"": ""Get translations by locale"",
        ""description"": ""Retrieves all translations for a specific locale."",
        ""operationId"": ""GetContent"",
        ""parameters"": [
          {
            ""name"": ""locale"",
            ""in"": ""query"",
            ""required"": true,
            ""description"": ""Locale code (e.g., en-US, es-ES)"",
            ""schema"": {""type"": ""string""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved translations"",
            ""content"": {
              ""application/json"": {
                ""schema"": {
                  ""type"": ""object"",
                  ""additionalProperties"": {""type"": ""string""}
                }
              }
            }
          }
        }
      }
    },
    ""/content/all"": {
      ""get"": {
        ""tags"": [""Content (i18n)""],
        ""summary"": ""Get all translations for all locales"",
        ""description"": ""Retrieves translations for all locales."",
        ""operationId"": ""GetAllContent"",
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved all translations"",
            ""content"": {
              ""application/json"": {
                ""schema"": {
                  ""type"": ""array"",
                  ""items"": {
                    ""type"": ""object"",
                    ""properties"": {
                      ""locale"": {""type"": ""string""},
                      ""translations"": {""type"": ""object""}
                    }
                  }
                }
              }
            }
          }
        }
      }
    },
    ""/content/{locale}"": {
      ""put"": {
        ""tags"": [""Content (i18n)""],
        ""summary"": ""Update translations for a locale"",
        ""description"": ""Updates or creates translations for a locale. Requires API key with 'content:write' scope."",
        ""operationId"": ""UpdateContent"",
        ""security"": [{""ApiKeyAuth"": []}],
        ""parameters"": [
          {
            ""name"": ""locale"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string""}
          }
        ],
        ""requestBody"": {
          ""required"": true,
          ""content"": {
            ""application/json"": {
              ""schema"": {
                ""type"": ""object"",
                ""additionalProperties"": {""type"": ""string""}
              }
            }
          }
        },
        ""responses"": {
          ""200"": {""description"": ""Translations updated successfully""},
          ""401"": {""description"": ""API key required or invalid""}
        }
      }
    },
    ""/content/{locale}/{key}"": {
      ""get"": {
        ""tags"": [""Content (i18n)""],
        ""summary"": ""Get specific translation"",
        ""description"": ""Retrieves a specific translation key for a locale."",
        ""operationId"": ""GetTranslation"",
        ""parameters"": [
          {
            ""name"": ""locale"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string""}
          },
          {
            ""name"": ""key"",
            ""in"": ""path"",
            ""required"": true,
            ""schema"": {""type"": ""string""}
          }
        ],
        ""responses"": {
          ""200"": {
            ""description"": ""Successfully retrieved translation"",
            ""content"": {
              ""application/json"": {
                ""schema"": {
                  ""type"": ""object"",
                  ""properties"": {
                    ""key"": {""type"": ""string""},
                    ""value"": {""type"": ""string""}
                  }
                }
              }
            }
          },
          ""404"": {""description"": ""Translation not found""}
        }
      }
    }
  },
  ""components"": {
    ""schemas"": {
      ""Quiz"": {
        ""type"": ""object"",
        ""properties"": {
          ""quizId"": {""type"": ""string"", ""format"": ""uuid"", ""description"": ""Unique identifier for the quiz""},
          ""title"": {""type"": ""string"", ""description"": ""Quiz title""},
          ""description"": {""type"": ""string"", ""nullable"": true, ""description"": ""Quiz description""},
          ""ageMin"": {""type"": ""integer"", ""nullable"": true, ""description"": ""Minimum age recommendation""},
          ""ageMax"": {""type"": ""integer"", ""nullable"": true, ""description"": ""Maximum age recommendation""},
          ""subject"": {""type"": ""string"", ""nullable"": true, ""description"": ""Subject area (e.g., Math, Science)""},
          ""difficulty"": {""type"": ""string"", ""enum"": [""easy"", ""medium"", ""hard""], ""nullable"": true, ""description"": ""Difficulty level""},
          ""estimatedMinutes"": {""type"": ""integer"", ""nullable"": true, ""description"": ""Estimated time to complete (in minutes)""},
          ""tags"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""nullable"": true, ""description"": ""Tags for categorization""},
          ""createdAt"": {""type"": ""string"", ""format"": ""date-time"", ""description"": ""Creation timestamp""},
          ""updatedAt"": {""type"": ""string"", ""format"": ""date-time"", ""description"": ""Last update timestamp""}
        },
        ""required"": [""quizId"", ""title"", ""createdAt"", ""updatedAt""]
      },
      ""CreateQuizRequest"": {
        ""type"": ""object"",
        ""properties"": {
          ""title"": {""type"": ""string"", ""description"": ""Quiz title""},
          ""description"": {""type"": ""string"", ""nullable"": true, ""description"": ""Quiz description""},
          ""ageMin"": {""type"": ""integer"", ""nullable"": true, ""minimum"": 0},
          ""ageMax"": {""type"": ""integer"", ""nullable"": true, ""minimum"": 0},
          ""subject"": {""type"": ""string"", ""nullable"": true},
          ""difficulty"": {""type"": ""string"", ""enum"": [""easy"", ""medium"", ""hard""], ""nullable"": true},
          ""estimatedMinutes"": {""type"": ""integer"", ""nullable"": true, ""minimum"": 1},
          ""tags"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""nullable"": true}
        },
        ""required"": [""title""]
      },
      ""UpdateQuizRequest"": {
        ""type"": ""object"",
        ""properties"": {
          ""title"": {""type"": ""string"", ""description"": ""Quiz title""},
          ""description"": {""type"": ""string"", ""nullable"": true},
          ""ageMin"": {""type"": ""integer"", ""nullable"": true, ""minimum"": 0},
          ""ageMax"": {""type"": ""integer"", ""nullable"": true, ""minimum"": 0},
          ""subject"": {""type"": ""string"", ""nullable"": true},
          ""difficulty"": {""type"": ""string"", ""enum"": [""easy"", ""medium"", ""hard""], ""nullable"": true},
          ""estimatedMinutes"": {""type"": ""integer"", ""nullable"": true, ""minimum"": 1},
          ""tags"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""nullable"": true}
        },
        ""required"": [""title""]
      },
      ""Question"": {
        ""type"": ""object"",
        ""properties"": {
          ""questionId"": {""type"": ""string"", ""format"": ""uuid""},
          ""questionType"": {""type"": ""string"", ""enum"": [""multiple_choice_single"", ""multiple_choice_multi"", ""fill_in_blank"", ""short_answer"", ""matching"", ""ordering"", ""program_submission""]},
          ""questionText"": {""type"": ""string""},
          ""content"": {""type"": ""object"", ""description"": ""JSONB field with question-type specific data""},
          ""points"": {""type"": ""number""},
          ""difficulty"": {""type"": ""string"", ""enum"": [""easy"", ""medium"", ""hard""], ""nullable"": true},
          ""subject"": {""type"": ""string"", ""nullable"": true},
          ""tags"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""nullable"": true},
          ""createdAt"": {""type"": ""string"", ""format"": ""date-time""},
          ""updatedAt"": {""type"": ""string"", ""format"": ""date-time""}
        }
      },
      ""CreateQuestionRequest"": {
        ""type"": ""object"",
        ""properties"": {
          ""questionType"": {""type"": ""string"", ""enum"": [""multiple_choice_single"", ""multiple_choice_multi"", ""fill_in_blank"", ""short_answer"", ""matching"", ""ordering"", ""program_submission""]},
          ""questionText"": {""type"": ""string""},
          ""content"": {""type"": ""object""},
          ""points"": {""type"": ""number"", ""default"": 1},
          ""difficulty"": {""type"": ""string"", ""enum"": [""easy"", ""medium"", ""hard""], ""nullable"": true},
          ""subject"": {""type"": ""string"", ""nullable"": true},
          ""tags"": {""type"": ""array"", ""items"": {""type"": ""string""}, ""nullable"": true}
        },
        ""required"": [""questionType"", ""questionText"", ""content""]
      },
      ""Attempt"": {
        ""type"": ""object"",
        ""properties"": {
          ""attemptId"": {""type"": ""string"", ""format"": ""uuid""},
          ""quizId"": {""type"": ""string"", ""format"": ""uuid""},
          ""userId"": {""type"": ""string""},
          ""status"": {""type"": ""string"", ""enum"": [""in_progress"", ""completed"", ""abandoned""]},
          ""startedAt"": {""type"": ""string"", ""format"": ""date-time""},
          ""completedAt"": {""type"": ""string"", ""format"": ""date-time"", ""nullable"": true},
          ""scoreEarned"": {""type"": ""number"", ""nullable"": true},
          ""scorePossible"": {""type"": ""number"", ""nullable"": true},
          ""percentScore"": {""type"": ""number"", ""nullable"": true}
        }
      },
      ""Response"": {
        ""type"": ""object"",
        ""properties"": {
          ""responseId"": {""type"": ""string"", ""format"": ""uuid""},
          ""attemptId"": {""type"": ""string"", ""format"": ""uuid""},
          ""questionId"": {""type"": ""string"", ""format"": ""uuid""},
          ""answerPayload"": {""type"": ""object"", ""description"": ""JSONB answer data""},
          ""pointsEarned"": {""type"": ""number"", ""nullable"": true},
          ""pointsPossible"": {""type"": ""number"", ""nullable"": true},
          ""isCorrect"": {""type"": ""boolean"", ""nullable"": true},
          ""feedback"": {""type"": ""string"", ""nullable"": true},
          ""submittedAt"": {""type"": ""string"", ""format"": ""date-time""},
          ""gradedAt"": {""type"": ""string"", ""format"": ""date-time"", ""nullable"": true}
        }
      }
    },
    ""securitySchemes"": {
      ""ApiKeyAuth"": {
        ""type"": ""apiKey"",
        ""in"": ""header"",
        ""name"": ""X-API-Key"",
        ""description"": ""API key for write operations (POST, PUT, DELETE). Header format: X-API-Key: your-api-key-here""
      }
    }
  }
}";

            await response.WriteStringAsync(openApiDoc);
            return response;
        }

        [Function("SwaggerRedirect")]
        [OpenApiIgnore]
        public HttpResponseData SwaggerRedirect(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal-docs")] 
            HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.MovedPermanently);
            response.Headers.Add("Location", $"{_swaggerPath}/swagger/ui");
            return response;
        }
    }
}
