using System.Collections.Generic;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SimpleLambdaFunction;

public class HelloResponse
{
    public int statusCode { get; set; }
    public string message { get; set; }
}

public class Function
{
    public APIGatewayHttpApiV2ProxyResponse FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        context.Logger.LogLine($"Request path: {request.RequestContext.Http.Path}");
        context.Logger.LogLine($"HTTP method: {request.RequestContext.Http.Method}");

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
            Headers = new Dictionary<string, string> 
            { 
                { "Content-Type", "application/json" } 
            },
            Body = JsonSerializer.Serialize(response),
            IsBase64Encoded = false
        };
    }
}
