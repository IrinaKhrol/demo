using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public static class OpenMeteoClient
{
    private static readonly HttpClient _httpClient = new HttpClient();

    public static async Task<object> GetWeatherForecast(double latitude, double longitude)
    {
        string url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&hourly=temperature_2m,relative_humidity_2m,wind_speed_10m¤t_weather=true";
        var response =	await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<object>(json);
    }
}