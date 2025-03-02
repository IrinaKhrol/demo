using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using System.Collections.Generic;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Annotations;
using System.Net.Http;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SimpleLambdaFunction
{
    public class Function
    {
        [LambdaFunction]
        public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(APIGatewayHttpApiV2ProxyRequest  request, ILambdaContext context)
        {
            string path = request.RawPath ?? string.Empty;
            string method = request.RequestContext.Http.Method;

            if (path.EndsWith("/weather") && method == HttpMethod.Get.Method)
            {
                var weatherData = await OpenMeteoClient.GetWeatherData(50.4375, 30.5); // Coordinates of Kyiv
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(weatherData)
                };                
            }

            try
            {
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 400,
                    Body = JsonSerializer.Serialize(new
                    {
                        statusCode = 400,
                        message = $"Bad request syntax or unsupported method. Request path: {path}. HTTP method: {method}"
                    }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };                
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error fetching weather data: {ex.Message}");
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 500,
                    Body = JsonSerializer.Serialize(new { error = "Internal server error" }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
        }
    }
}