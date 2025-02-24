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
           this._tableName = Environment.GetEnvironmentVariable("TARGET_TABLE") 
               ?? throw new InvalidOperationException("TARGET_TABLE is not set");
       }

       public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
       {
           context.Logger.LogLine($"AwsRequestId: {context.AwsRequestId}");
           context.Logger.LogLine($"Full Request Body: {request.Body}");
           context.Logger.LogLine($"Request Headers: {JsonSerializer.Serialize(request.Headers)}");

           using var cts = new CancellationTokenSource();
           cts.CancelAfter(context.RemainingTime.Add(TimeSpan.FromSeconds(-1)));

           try
           {
               // Детальная десериализация с логированием
               RequestBody requestBody;
               try 
               {
                   requestBody = JsonSerializer.Deserialize<RequestBody>(request.Body, new JsonSerializerOptions 
                   { 
                       PropertyNameCaseInsensitive = true 
                   });
                   
                   context.Logger.LogLine($"Deserialized PrincipalId: {requestBody?.PrincipalId}");
                   context.Logger.LogLine($"Deserialized Content: {JsonSerializer.Serialize(requestBody?.Content)}");
               }
               catch (JsonException ex)
               {
                   context.Logger.LogLine($"JSON Deserialization Error: {ex.Message}");
                   context.Logger.LogLine($"Original Body: {request.Body}");
                   
                   return new APIGatewayProxyResponse
                   {
                       StatusCode = (int)HttpStatusCode.BadRequest,
                       Body = JsonSerializer.Serialize(new 
                       { 
                           statusCode = 400,
                           error = "Failed to parse request body",
                           details = new 
                           {
                               errorMessage = ex.Message,
                               originalBody = request.Body
                           }
                       }),
                       Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                   };
               }

               // Валидация входных данных с подробным логированием
               if (requestBody == null || requestBody.PrincipalId <= 0 || requestBody.Content == null)
               {
                   context.Logger.LogLine("Validation failed: Invalid request body");
                   return new APIGatewayProxyResponse
                   {
                       StatusCode = (int)HttpStatusCode.BadRequest,
                       Body = JsonSerializer.Serialize(new 
                       { 
                           statusCode = 400,
                           error = "Invalid request body",
                           details = new 
                           {
                               PrincipalId = requestBody?.PrincipalId,
                               ContentNull = requestBody?.Content == null
                           }
                       }),
                       Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                   };
               }

               // Создание события
               var id = Guid.NewGuid().ToString();
               var createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

               var newEvent = new Event
               {
                   Id = id,
                   PrincipalId = requestBody.PrincipalId,
                   CreatedAt = createdAt,
                   Body = requestBody.Content
               };

               // Подготовка документа для DynamoDB с явным маппингом
               var table = Table.LoadTable(_dynamoDb, _tableName);
               var document = new Document
               {
                   ["id"] = newEvent.Id,
                   ["principalId"] = newEvent.PrincipalId,
                   ["createdAt"] = newEvent.CreatedAt,
                   ["body"] = JsonSerializer.Serialize(newEvent.Body)
               };

               // Логирование перед сохранением
               context.Logger.LogLine($"Preparing to save document: {JsonSerializer.Serialize(document)}");

               // Сохранение в DynamoDB
               await table.PutItemAsync(document, cts.Token);
               context.Logger.LogLine($"Successfully saved event with ID: {id}");

               // Возврат DetailedSuccess
               return new APIGatewayProxyResponse
               {
                   StatusCode = (int)HttpStatusCode.Created,
                   Body = JsonSerializer.Serialize(new 
                   { 
                       statusCode = 201,
                       message = "Event created successfully",
                       @event = new {
                           id = newEvent.Id,
                           principalId = newEvent.PrincipalId,
                           createdAt = newEvent.CreatedAt,
                           body = newEvent.Body
                       }
                   }),
                   Headers = new Dictionary<string, string> { 
                       { "Content-Type", "application/json" } 
                   }
               };
           }
           catch (Exception ex)
           {
               // Расширенная обработка исключений
               context.Logger.LogLine($"Unhandled Error: {ex.Message}");
               context.Logger.LogLine($"Stack Trace: {ex.StackTrace}");

               return new APIGatewayProxyResponse
               {
                   StatusCode = (int)HttpStatusCode.InternalServerError,
                   Body = JsonSerializer.Serialize(new 
                   { 
                       statusCode = 500, 
                       error = "Unexpected error occurred",
                       details = new 
                       {
                           message = ex.Message,
                           type = ex.GetType().Name
                       }
                   }),
                   Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
               };
           }
           finally
           {
               cts.Cancel();
           }
       }

       // Классы RequestBody и Event без изменений
       public class RequestBody
       {
           public int PrincipalId { get; set; }
           public Dictionary<string, string>? Content { get; set; }
       }

       public class Event
       {
           public string Id { get; set; } = string.Empty;
           public int PrincipalId { get; set; }
           public string CreatedAt { get; set; } = string.Empty;
           public Dictionary<string, string>? Body { get; set; }
       }
   }
}


