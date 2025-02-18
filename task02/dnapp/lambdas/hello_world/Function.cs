using System.Collections.Generic;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using System;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SimpleLambdaFunction
{
   public class HelloResponse
   {
       public int statusCode { get; set; }
       public string message { get; set; } = string.Empty;
   }

   public class Function
   {
       public APIGatewayHttpApiV2ProxyResponse FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
       {
            context.Logger.LogLine($"Request path: {request.RequestContext.Http.Path}");
            context.Logger.LogLine($"HTTP method: {request.RequestContext.Http.Method}");

           if (request.RequestContext.Http.Path.EndsWith("/hello"))
           {
               var response = new HelloResponse
               {
                   statusCode = 200,
                   message = "Hello from Lambda"
               };

               var options = new JsonSerializerOptions
               {
                   PropertyNamingPolicy = JsonNamingPolicy.CamelCase
               };

               return new APIGatewayHttpApiV2ProxyResponse
               {
                   StatusCode = 200,
                   Body = JsonSerializer.Serialize(response, options),
                   Headers = new Dictionary<string, string> 
                   { 
                       { "Content-Type", "application/json" } 
                   }
               };
           }

            var errorResponse = new HelloResponse
            {
                statusCode = 400,
                message = $"Bad request syntax or unsupported method. Request path: {request.RequestContext.Http.Path}. HTTP method: {request.RequestContext.Http.Method}"
            };

           return new APIGatewayHttpApiV2ProxyResponse
           {
                StatusCode = 400,
                Body = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" }
                }
           };
       }
   }
}
