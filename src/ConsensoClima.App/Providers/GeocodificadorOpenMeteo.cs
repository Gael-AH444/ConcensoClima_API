using System.Net.Http.Json;

using ConsensoClima.App.Domain;

namespace ConsensoClima.App.Providers;

class GeocodificadorOpenMeteo : IGeocodificador
{
    private readonly HttpClient _http;
    public GeocodificadorOpenMeteo(HttpClient http) => _http = http;

    //Metodo para resolver la ubicación de una ciudad usando la API de geocodificación de Open-Meteo
    public async Task<Ubicacion?> ResolverAsync(string ciudad, CancellationToken ct = default)
    {
        string url =
            "https://geocoding-api.open-meteo.com/v1/search" +
            $"?name={Uri.EscapeDataString(ciudad)}&count=1&language=es&format=json";
        GeoRespuesta? geo = await _http.GetFromJsonAsync<GeoRespuesta>(url, ct);
        GeoResultado? r = geo?.Results?.FirstOrDefault();
        if (r is null) return null;
        return new Ubicacion(r.Name, r.Country, Math.Round(r.Latitude, 4), Math.Round(r.Longitude, 4));
    }
}