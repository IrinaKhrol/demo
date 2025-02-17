using System;
using System.Collections.Generic;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

namespace SimpleLambdaFunction
{
   public class HelloResponse
   {
       public int StatusCode { get; set; }
       public string Message { get; set; }
   }

   public class Function
   {
       public APIGatewayHttpApiV2ProxyResponse FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
       {
           if (request.RequestContext.Http.Path.EndsWith("/hello"))
           {
               var response = new HelloResponse
               {
                   StatusCode = 200,
                   Message = "Hello from Lambda"
               };

               return new APIGatewayHttpApiV2ProxyResponse
               {
                   StatusCode = 200,
                   Body = JsonSerializer.Serialize(response),
                   Headers = new Dictionary<string, string> 
                   { 
                       { "Content-Type", "application/json" } 
                   }
               };
           }

           return new APIGatewayHttpApiV2ProxyResponse
           {
               StatusCode = 404,
               Body = "Not Found"
           };
       }
   }
}