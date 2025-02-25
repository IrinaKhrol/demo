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
            context.Logger.LogLine($"AwsRequestId: {context.AwsRequestId}");
            context.Logger.LogLine($"Table name: {_tableName}");
            context.Logger.LogLine($"Request body: {request.Body}");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(context.RemainingTime.Add(TimeSpan.FromSeconds(-1)));

            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var responseJsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var requestBody = JsonSerializer.Deserialize<RequestBody>(request.Body, jsonOptions);
                context.Logger.LogLine($"Deserialized request: PrincipalId={requestBody?.PrincipalId}, Content={requestBody?.Content != null}");

                if (requestBody?.PrincipalId <= 0 || requestBody.Content == null)
                {
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest,
                        Body = JsonSerializer.Serialize(new
                        {
                            statusCode = 400,
                            error = "Invalid request body"
                        }, responseJsonOptions),
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                    };
                }

                var id = Guid.NewGuid().ToString();
                var createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                var newEvent = new
                {
                    id = id,
                    principalId = requestBody.PrincipalId,
                    createdAt = createdAt,
                    body = requestBody.Content
                };

                context.Logger.LogLine($"Created event: id={id}, principalId={requestBody.PrincipalId}, createdAt={createdAt}");

                var table = Table.LoadTable(_dynamoDb, _tableName);
                var document = new Document();
                document["id"] = id;
                document["principalId"] = requestBody.PrincipalId;
                document["createdAt"] = createdAt;

                var bodyJson = JsonSerializer.Serialize(requestBody.Content, responseJsonOptions);
                context.Logger.LogLine($"Body JSON: {bodyJson}");
                document["body"] = Document.FromJson(bodyJson);

                context.Logger.LogLine("Putting item into DynamoDB...");
                await table.PutItemAsync(document, cts.Token);
                context.Logger.LogLine("Item successfully put into DynamoDB");

                var response = new
                {
                    statusCode = 201,
                    @event = newEvent
                };

                var responseJson = JsonSerializer.Serialize(response, responseJsonOptions);
                context.Logger.LogLine($"Response JSON: {responseJson}");

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.Created,
                    Body = responseJson,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error: {ex.Message}");
                context.Logger.LogLine($"Stack Trace: {ex.StackTrace}");
                
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Body = JsonSerializer.Serialize(new
                    {
                        statusCode = 500,
                        error = ex.Message
                    }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
            finally
            {
                cts.Dispose();
            }
        }

        public class RequestBody
        {
            public int PrincipalId { get; set; }
            public Dictionary<string, string> Content { get; set; } = new Dictionary<string, string>();
        }
    }
}

