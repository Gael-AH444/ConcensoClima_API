using System.Globalization;
using System.Net.Http.Json;

using ConsensoClima.App.Domain;

namespace ConsensoClima.App.Providers;

class OpenMeteoProveedor : IProveedorClima
{
    private readonly HttpClient _http;
    public OpenMeteoProveedor(HttpClient http) => _http = http;
    public string Nombre => "Open-Meteo";

    public async Task<LecturaClima> ObtenerAsync(double lat, double lon, CancellationToken ct = default)
    {
        string url =
            "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
            $"&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m";
        OpenMeteoRespuesta? r = await _http.GetFromJsonAsync<OpenMeteoRespuesta>(url, ct);
        double temp = r?.Current?.Temperature2m
            ?? throw new InvalidOperationException("Open-Meteo no devolvió temperatura.");
        return new LecturaClima(Nombre, temp);
    }
}