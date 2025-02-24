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
               ?? throw new InvalidOperationException(" TARGET_TABLE is not set");
       }

       public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
       {
           context.Logger.LogLine($"AwsRequestId: {context.AwsRequestId}");

           using var cts = new CancellationTokenSource();
           cts.CancelAfter(context.RemainingTime.Add(TimeSpan.FromSeconds(-1)));

           try
           {
               var requestBody = JsonSerializer.Deserialize<RequestBody>(request.Body);
               if (requestBody?.PrincipalId <= 0 || requestBody.Content == null)
               {
                   return new APIGatewayProxyResponse
                   {
                       StatusCode = (int)HttpStatusCode.BadRequest,
                       Body = JsonSerializer.Serialize(new { error = " " })
                   };
               }

               var id = Guid.NewGuid().ToString();
               var createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

               var newEvent = new Event
               {
                   Id = id,
                   PrincipalId = requestBody.PrincipalId,
                   CreatedAt = createdAt,
                   Body = requestBody.Content
               };

               var table = Table.LoadTable(_dynamoDb, _tableName);
               var document = new Document
               {
                   ["id"] = newEvent.Id,
                   ["principalId"] = newEvent.PrincipalId,
                   ["createdAt"] = newEvent.CreatedAt,
                   ["body"] = Document.FromJson(JsonSerializer.Serialize(newEvent.Body))
               };

               await table.PutItemAsync(document, cts.Token);

               return new APIGatewayProxyResponse
               {
                   StatusCode = (int)HttpStatusCode.Created,
                   Body = JsonSerializer.Serialize(new
                   {
                       statusCode = 201,
                       @event = newEvent
                   })
               };
           }
           catch (Exception ex)
           {
               context.Logger.LogLine($"Ошибка: {ex.Message}");
               return new APIGatewayProxyResponse
               {
                   StatusCode = (int)HttpStatusCode.InternalServerError,
                   Body = JsonSerializer.Serialize(new { error = ex.Message })
               };
           }
           finally
           {
               cts.Cancel();
           }
       }

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


