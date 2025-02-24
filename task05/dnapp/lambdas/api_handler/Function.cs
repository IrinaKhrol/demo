using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SimpleLambdaFunction
{
    public class Function
    {
        private readonly IAmazonDynamoDB _dynamoDb;
        private readonly string _tableName;

        public Function()
        {
            this._dynamoDb = new AmazonDynamoDBClient();
            this._tableName = Environment.GetEnvironmentVariable("TARGET_TABLE") ?? "Events";
        }

        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            context.Logger.LogLine($"Received event: {JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true })}");

            try
            {
                // Извлекаем principalId и content из запроса
                var requestBody = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body);
                
                if (!requestBody.TryGetValue("principalId", out JsonElement principalIdElement) ||
                    principalIdElement.ValueKind == JsonValueKind.Undefined ||
                    principalIdElement.ValueKind == JsonValueKind.Null ||
                    !principalIdElement.TryGetInt32(out int principalId))
                {
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = 400,
                        Body = JsonSerializer.Serialize(new
                        {
                            error = "principalId must be a valid number"
                        }),
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                    };
                }

                if (!requestBody.TryGetValue("content", out JsonElement contentElement) ||
                    contentElement.ValueKind == JsonValueKind.Undefined ||
                    contentElement.ValueKind == JsonValueKind.Null)
                {
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = 400,
                        Body = JsonSerializer.Serialize(new
                        {
                            error = "content is required"
                        }),
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                    };
                }

                // Преобразуем content в Dictionary<string, string>
                Dictionary<string, string> content;
                try 
                {
                    content = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        contentElement.GetRawText(), 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );
                }
                catch (JsonException)
                {
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = 400,
                        Body = JsonSerializer.Serialize(new
                        {
                            error = "content must be a valid object"
                        }),
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                    };
                }

                // Создаем запись для DynamoDB
                var id = Guid.NewGuid().ToString();
                var createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                var table = Table.LoadTable(_dynamoDb, _tableName);
                var document = new Document
                {
                    ["id"] = id,
                    ["principalId"] = principalId,
                    ["createdAt"] = createdAt,
                    ["body"] = JsonSerializer.Serialize(content)
                };

                context.Logger.LogLine($"Saving document: {JsonSerializer.Serialize(document)}");
                await table.PutItemAsync(document);
                
                return new APIGatewayProxyResponse
                {
                    StatusCode = 201,
                    Body = JsonSerializer.Serialize(new
                    {
                        statusCode = 201,
                        message = "Event created successfully",
                        @event = new
                        {
                            id,
                            principalId,
                            createdAt,
                            body = content
                        }
                    }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error: {ex.Message}");
                context.Logger.LogLine($"Stack trace: {ex.StackTrace}");
                
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = JsonSerializer.Serialize(new
                    {
                        error = "Internal server error",
                        message = ex.Message
                    }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
        }
    }
}

