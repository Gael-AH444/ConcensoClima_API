using System.Globalization;
using System.Net.Http.Json;
using ConsensoClima.App.Domain;

namespace ConsensoClima.App.Providers;

class MetNoProveedor : IProveedorClima
{
    private readonly HttpClient _http;
    public MetNoProveedor(HttpClient http) => _http = http;
    public string Nombre => "MET Norway";

    public async Task<LecturaClima> ObtenerAsync(double lat, double lon, CancellationToken ct = default)
    {
        string url =
            "https://api.met.no/weatherapi/locationforecast/2.0/compact" +
            $"?lat={lat.ToString(CultureInfo.InvariantCulture)}" +
            $"&lon={lon.ToString(CultureInfo.InvariantCulture)}";
        MetNoRespuesta? r = await _http.GetFromJsonAsync<MetNoRespuesta>(url, ct);
        double temp = r?.Properties?.Timeseries?.FirstOrDefault()
                        ?.Data?.Instant?.Details?.AirTemperature
            ?? throw new InvalidOperationException("MET Norway no devolvió temperatura.");
        return new LecturaClima(Nombre, temp);
    }
}