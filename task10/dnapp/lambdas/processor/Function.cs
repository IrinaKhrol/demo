using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using System;
using System.Collections.Generic;
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
        private readonly IAmazonDynamoDB _dynamoDB;
        private readonly string _tableName;
        private static readonly HttpClient _httpClient = new HttpClient();

        public Function()
        {
            this._dynamoDB = new AmazonDynamoDBClient();
            // Получаем имя таблицы из переменной окружения или используем префикс для имени
            this._tableName = Environment.GetEnvironmentVariable("DYNAMO_TABLE");
            AWSSDKHandler.RegisterXRayForAllServices();
        }

        [LambdaFunction]
        public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
        {
            context.Logger.LogLine("Starting weather forecast processing with X-Ray tracing...");
            context.Logger.LogLine($"Initialized with DynamoDB table: {this._tableName}");

            try
            {
                // Проверяем, существует ли таблица
                try
                {
                    var describeTableRequest = new DescribeTableRequest { TableName = this._tableName };
                    var tableDescription = await this._dynamoDB.DescribeTableAsync(describeTableRequest);
                    context.Logger.LogLine($"Found table: {tableDescription.Table.TableName}");
                }
                catch (ResourceNotFoundException ex)
                {
                    context.Logger.LogLine($"Table not found: {this._tableName}. Error: {ex.Message}");
                    throw new Exception($"DynamoDB table {this._tableName} not found", ex);
                }

                 // Получаем прогноз погоды для Киева напрямую из Open-Meteo API
                var weatherData = await FetchWeatherDataFromApi(50.4375, 30.5, context); // Координаты Киева
                if (weatherData == null)
                {
                    context.Logger.LogLine("Weather data is null before processing");
                    throw new Exception("Failed to fetch weather data from Open-Meteo API");
                }

                context.Logger.LogLine($"Weather data fetched: {JsonSerializer.Serialize(weatherData)}");

                // Генерируем уникальный ID в формате uuidv4
                string uuid = Guid.NewGuid().ToString(); // Генерирует строку в формате uuidv4, например, "550e8400-e29b-41d4-a716-446655440000"

                // Форматируем данные для DynamoDB согласно схеме
                var dynamoItem = new Dictionary<string, AttributeValue>
                {
                    { "id", new AttributeValue { S = uuid } }, // Уникальный ID вместо фиксированного
                    {
                       "forecast", new AttributeValue {
                            M = new Dictionary<string, AttributeValue>
                            {
                                { "elevation", new AttributeValue { N = weatherData?.elevation.ToString() ?? "0" } },
                                { "generationtime_ms", new AttributeValue { N = weatherData?.generationtime_ms.ToString() ?? "0" } },
                                {
                                    "hourly", new AttributeValue {
                                        M = new Dictionary<string, AttributeValue>
                                        {
                                            { "temperature_2m", new AttributeValue { L = weatherData?.hourly?.temperature_2m?.Select(t => new AttributeValue { N = t.ToString() }).ToList() ?? new List<AttributeValue>() } },
                                            { "time", new AttributeValue { L = weatherData?.hourly?.time?.Select(t => new AttributeValue { S = t }).ToList() ?? new List<AttributeValue>() } }
                                        }
                                    }
                                },
                                {
                                    "hourly_units", new AttributeValue {
                                        M = new Dictionary<string, AttributeValue>
                                        {
                                            { "temperature_2m", new AttributeValue { S = weatherData?.hourly_units?.temperature_2m ?? string.Empty } },
                                            { "time", new AttributeValue { S = weatherData?.hourly_units?.time ?? string.Empty } }
                                        }
                                    }
                                },
                                { "latitude", new AttributeValue { N = weatherData?.latitude.ToString() ?? "0" } },
                                { "longitude", new AttributeValue { N = weatherData?.longitude.ToString() ?? "0" } },
                                { "timezone", new AttributeValue { S = weatherData?.timezone ?? string.Empty } },
                                { "timezone_abbreviation", new AttributeValue { S = weatherData?.timezone_abbreviation ?? string.Empty } },
                                { "utc_offset_seconds", new AttributeValue { N = weatherData?.utc_offset_seconds.ToString() ?? "0" } }
                            }
                        }
                    }
                };

                // Логируем данные перед сохранением
                context.Logger.LogLine($"Attempting to save item with ID {dynamoItem["id"].S} to DynamoDB table {this._tableName}");

                var putRequest = new PutItemRequest
                {
                    TableName = this._tableName,
                    Item = dynamoItem
                };

                try
                {
                    await this._dynamoDB.PutItemAsync(putRequest);
                    context.Logger.LogLine("Weather forecast saved to DynamoDB successfully with X-Ray tracing.");
                }
                catch (Exception ex)
                {
                    context.Logger.LogLine($"DynamoDB error while saving: {ex.Message}\nStackTrace: {ex.StackTrace}");
                    throw;
                }

                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(new { message = "Weather forecast processed and saved to DynamoDB", id = dynamoItem["id"].S }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Detailed error processing weather forecast: {ex.Message}\nStackTrace: {ex.StackTrace}");
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
                "&hourly=temperature_2m" + // Запрашиваем только temperature_2m, как в ожидаемом JSON
                "&timezone=Europe/Kiev&forecast_days=7&timeformat=iso8601";

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

        // Модель данных о погоде, соответствующая структуре ответа Open-Meteo API и схеме DynamoDB для Киева
        public class WeatherData
        {
            public double? latitude { get; set; }  // Сделано nullable для безопасности
            public double? longitude { get; set; }
            public double? generationtime_ms { get; set; }
            public int? utc_offset_seconds { get; set; }
            public string? timezone { get; set; }
            public string? timezone_abbreviation { get; set; }
            public double? elevation { get; set; }
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
            public int? interval { get; set; }  // Сделано nullable
            public double? temperature_2m { get; set; }
            public double? wind_speed_10m { get; set; }
        }

        public class HourlyUnits
        {
            public string? time { get; set; }
            public string? temperature_2m { get; set; }
        }

        public class Hourly
        {
            public string[]? time { get; set; }
            public double[]? temperature_2m { get; set; }
        }
    }
}
