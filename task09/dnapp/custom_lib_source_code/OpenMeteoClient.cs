using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System;

public static class OpenMeteoClient
{
    private static readonly HttpClient _httpClient = new HttpClient();

    // Модель данных о погоде, соответствующая структуре ответа API
    public class WeatherData
    {
        public double latitude { get; set; }
        public double longitude { get; set; }
        public double elevation { get; set; }
        public string timezone { get; set; }
        public string timezone_abbreviation { get; set; }
        public CurrentUnits current_units { get; set; }
        public Current current { get; set; }
        public HourlyUnits hourly_units { get; set; }
        public Hourly hourly { get; set; }
    }

    public class CurrentUnits
    {
        public string time { get; set; }
        public string interval { get; set; }
        public string temperature_2m { get; set; }
        public string wind_speed_10m { get; set; }
    }

    public class Current
    {
        public string time { get; set; }
        public int interval { get; set; }
        public double temperature_2m { get; set; }
        public double wind_speed_10m { get; set; }
    }

    public class HourlyUnits
    {
        public string time { get; set; }
        public string temperature_2m { get; set; }
        public string relative_humidity_2m { get; set; }
        public string wind_speed_10m { get; set; }
    }

    public class Hourly
    {
        public string[] time { get; set; }
        public double[] temperature_2m { get; set; }
        public double[] relative_humidity_2m { get; set; }
        public double[] wind_speed_10m { get; set; }
    }

    public static async Task<WeatherData?> GetWeatherData(double latitude, double longitude)
    {
        string openMeteoUri =
            $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}" +
            "&current=temperature_2m,wind_speed_10m" +
            "&hourly=temperature_2m,relative_humidity_2m,wind_speed_10m";

        var response = await _httpClient.GetAsync(openMeteoUri);
        response.EnsureSuccessStatusCode();

        WeatherData? weatherData;
        try
        {
            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            weatherData = JsonSerializer.Deserialize<WeatherData>(content, options);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Ошибка десериализации: {e.Message}");
            throw;
        }

        return weatherData;
    }
}