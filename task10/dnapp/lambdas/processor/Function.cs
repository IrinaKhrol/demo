using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using System.Collections.Generic;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Annotations;
using System.Net.Http;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using System.Linq;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SimpleLambdaFunction
{
    public class Function
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly IAmazonDynamoDB _dynamoDB = new AmazonDynamoDBClient();
        private const string DYNAMO_TABLE = "Weather"; // Используем алиас через переменную окружения

        static Function()
        {
            // Инициализация AWS X-Ray для трассировки
            AWSSDKHandler.RegisterXRayForAllServices();
        }

        [LambdaFunction]
        public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
        {
            context.Logger.LogLine("Starting weather forecast processing with X-Ray tracing...");

            try
            {
                // Получаем прогноз погоды для Берлина напрямую из Open-Meteo API
                var weatherData = await FetchWeatherDataFromApi(52.52, 13.419998, context); // Координаты Берлина
                if (weatherData == null)
                {
                    throw new Exception("Failed to fetch weather data from Open-Meteo API");
                }

                // Форматируем данные для DynamoDB согласно схеме
                var dynamoItem = new Dictionary<string, AttributeValue>
                {
                    { "id", new AttributeValue { S = Guid.NewGuid().ToString() } },
                    {
                        "forecast", new AttributeValue {
                            M = new Dictionary<string, AttributeValue>
                            {
                                { "elevation", new AttributeValue { N = weatherData.elevation.ToString() } },
                                { "generationtime_ms", new AttributeValue { N = weatherData.generationtime_ms.ToString() } },
                                {
                                    "hourly", new AttributeValue {
                                        M = new Dictionary<string, AttributeValue>
                                        {
                                            { "temperature_2m", new AttributeValue { L = weatherData.hourly.temperature_2m.Select(t => new AttributeValue { N = t.ToString() }).ToList() } },
                                            { "time", new AttributeValue { L = weatherData.hourly.time.Select(t => new AttributeValue { S = t }).ToList() } }
                                        }
                                    }
                                },
                                {
                                    "hourly_units", new AttributeValue {
                                        M = new Dictionary<string, AttributeValue>
                                        {
                                            { "temperature_2m", new AttributeValue { S = weatherData.hourly_units.temperature_2m } },
                                            { "time", new AttributeValue { S = weatherData.hourly_units.time } }
                                        }
                                    }
                                },
                                { "latitude", new AttributeValue { N = weatherData.latitude.ToString() } },
                                { "longitude", new AttributeValue { N = weatherData.longitude.ToString() } },
                                { "timezone", new AttributeValue { S = weatherData.timezone } },
                                { "timezone_abbreviation", new AttributeValue { S = weatherData.timezone_abbreviation } },
                                { "utc_offset_seconds", new AttributeValue { N = weatherData.utc_offset_seconds.ToString() } }
                            }
                        }
                    }
                };

                // Сохраняем в DynamoDB с трассировкой X-Ray
                var putRequest = new PutItemRequest
                {
                    TableName = DYNAMO_TABLE,
                    Item = dynamoItem
                };

                await _dynamoDB.PutItemAsync(putRequest);
                context.Logger.LogLine("Weather forecast saved to DynamoDB successfully with X-Ray tracing.");

                // Возвращаем ответ
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(new { message = "Weather forecast processed and saved to DynamoDB" }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error processing weather forecast: {ex.Message}");
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 500,
                    Body = JsonSerializer.Serialize(new { error = "Internal server error", message = ex.Message }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
        }

        private static async Task<WeatherData?> FetchWeatherDataFromApi(double latitude, double longitude, ILambdaContext? context = null)
        {
            string openMeteoUri =
                $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}" +
                "&current=temperature_2m,wind_speed_10m" + // Исправлено с '¤t=' на 'current='
                "&hourly=temperature_2m,relative_humidity_2m,wind_speed_10m" +
                "&timezone=GMT&forecast_days=7&timeformat=iso8601";

            var response = await _httpClient.GetAsync(openMeteoUri);
            response.EnsureSuccessStatusCode();

            try
            {
                var content = await response.Content.ReadAsStringAsync();
                if (context != null) context.Logger.LogLine($"Raw API response: {content}");
                else Console.WriteLine($"Raw API response: {content}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                };
                return JsonSerializer.Deserialize<WeatherData>(content, options);
            }
            catch (Exception e)
            {
                if (context != null) context.Logger.LogLine($"Error deserializing weather data: {e.Message}");
                else Console.WriteLine($"Error deserializing weather data: {e.Message}");
                throw;
            }
        }

        // Модель данных о погоде, соответствующая структуре ответа Open-Meteo API и схеме DynamoDB
        public class WeatherData
        {
            public double latitude { get; set; }
            public double longitude { get; set; }
            public double generationtime_ms { get; set; }
            public int utc_offset_seconds { get; set; }
            public string? timezone { get; set; }
            public string? timezone_abbreviation { get; set; }
            public double elevation { get; set; }
            public CurrentUnits? current_units { get; set; }
            public Current? current { get; set; }
            public HourlyUnits? hourly_units { get; set; }
            public Hourly? hourly { get; set; }
        }

        public class CurrentUnits
        {
            public string? time { get; set; }
            public string? interval { get; set; }
            public string? temperature_2m { get; set; }
            public string? wind_speed_10m { get; set; }
        }

        public class Current
        {
            public string? time { get; set; }
            public int interval { get; set; }  // Изменено с string? на int
            public double temperature_2m { get; set; }
            public double wind_speed_10m { get; set; }
        }

        public class HourlyUnits
        {
            public string? time { get; set; }
            public string? temperature_2m { get; set; }
            public string? relative_humidity_2m { get; set; }
            public string? wind_speed_10m { get; set; }
        }

        public class Hourly
        {
            public string[]? time { get; set; }
            public double[]? temperature_2m { get; set; }
            public int[]? relative_humidity_2m { get; set; } // Изменено на int[] для соответствия целым числам (91, 90)
            public double[]? wind_speed_10m { get; set; }
        }
    }
}
