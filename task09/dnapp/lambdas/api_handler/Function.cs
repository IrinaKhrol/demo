using Amazon.Lambda.Core;
using System.Collections.Generic;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Reflection;
using Amazon.Lambda.Annotations;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SimpleLambdaFunction
{
    public class Function
    {
        [LambdaFunction]
        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            string path = request.Path ?? "/";
            string method = request.HttpMethod ?? "GET";

            if (path != "/weather" || method != "GET")
            {
                return new APIGatewayProxyResponse
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

            try
            {
                var assembly = Assembly.LoadFrom("/opt/lib/net8.0/OpenMeteoClient.dll");
                var type = assembly.GetType("OpenMeteoClient");
                var methodInfo = type.GetMethod("GetWeatherForecast", new[] { typeof(double), typeof(double) });
                var weatherData = await (Task<object>)methodInfo.Invoke(null, new object[] { 50.4375, 30.5 }); // Координаты Киева
                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(weatherData),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error fetching weather data: {ex.Message}");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = JsonSerializer.Serialize(new { error = "Internal server error" }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
        }
    }

    public class APIGatewayProxyRequest
    {
        public string Path { get; set; }
        public string HttpMethod { get; set; }
    }

    public class APIGatewayProxyResponse
    {
        public int StatusCode { get; set; }
        public string Body { get; set; }
        public Dictionary<string, string> Headers { get; set; }
    }
}
