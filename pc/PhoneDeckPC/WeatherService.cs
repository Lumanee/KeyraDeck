using System.Globalization;
using System.Text.Json;

namespace PhoneDeckPC;

public record WeatherData(int Temp, int Code, int Wind, int Humidity, string City);

/// <summary>Météo actuelle via Open-Meteo (gratuit, sans clé). Ne bloque jamais : renvoie la dernière valeur connue et rafraîchit en arrière-plan.</summary>
public class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string _city = "";
    private double _lat, _lon;
    private string _label = "";
    private DateTime _nextTry = DateTime.MinValue;
    private bool _busy;
    private WeatherData? _data;

    public WeatherData? Current(string? city)
    {
        city = (city ?? "").Trim();
        if (city.Length == 0) return null;
        if (!string.Equals(city, _city, StringComparison.OrdinalIgnoreCase)) { _city = city; _data = null; _label = ""; _nextTry = DateTime.MinValue; }
        if (!_busy && DateTime.UtcNow >= _nextTry)
        {
            _busy = true;
            _ = Task.Run(RefreshAsync);
        }
        return _data;
    }

    private async Task RefreshAsync()
    {
        try
        {
            if (_label.Length == 0)
            {
                var geo = await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(_city)}&count=1&language=fr");
                using var g = JsonDocument.Parse(geo);
                if (!g.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) { _nextTry = DateTime.UtcNow.AddMinutes(2); return; }
                var r = res[0];
                _lat = r.GetProperty("latitude").GetDouble(); _lon = r.GetProperty("longitude").GetDouble();
                _label = r.TryGetProperty("name", out var n) ? n.GetString() ?? _city : _city;
            }
            var url = string.Create(CultureInfo.InvariantCulture, $"https://api.open-meteo.com/v1/forecast?latitude={_lat}&longitude={_lon}&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m");
            using var d = JsonDocument.Parse(await Http.GetStringAsync(url));
            var c = d.RootElement.GetProperty("current");
            _data = new WeatherData(
                (int)Math.Round(c.GetProperty("temperature_2m").GetDouble()),
                c.GetProperty("weather_code").GetInt32(),
                (int)Math.Round(c.GetProperty("wind_speed_10m").GetDouble()),
                (int)Math.Round(c.GetProperty("relative_humidity_2m").GetDouble()),
                _label);
            _nextTry = DateTime.UtcNow.AddMinutes(10);
        }
        catch { _nextTry = DateTime.UtcNow.AddMinutes(1); }
        finally { _busy = false; }
    }
}
