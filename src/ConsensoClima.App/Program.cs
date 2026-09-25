using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ==================== Composición (composition root) ====================

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args); //Lee appsettings.json

// Configuración -> Options (ciudades y timeout salen de appsettings.json). Mapea datos a la clase OpcionesConsenso.
builder.Services.Configure<OpcionesConsenso>(
    builder.Configuration.GetSection(OpcionesConsenso.Seccion));

// IHttpClientFactory + un "typed client" por proveedor: cada AddHttpClient<T>
// inyecta un HttpClient ya configurado en ese tipo concreto.
builder.Services.AddHttpClient<OpenMeteoProveedor>();
builder.Services.AddHttpClient<MetNoProveedor>(cliente =>
    cliente.DefaultRequestHeaders.TryAddWithoutValidation(
        "User-Agent", "ConsensoClima/0.1 (aprendizaje; gaelalejo.444@gmail.com)"));
builder.Services.AddHttpClient(); // cliente por defecto, para el geocoding.

// Exponer cada proveedor concreto TAMBIÉN como IProveedorClima, para que el
// agregador reciba el IEnumerable<IProveedorClima> completo.
builder.Services.AddTransient<IProveedorClima>(sp => sp.GetRequiredService<OpenMeteoProveedor>());
builder.Services.AddTransient<IProveedorClima>(sp => sp.GetRequiredService<MetNoProveedor>());
builder.Services.AddTransient<AgregadorClima>();

using IHost host = builder.Build();

// ==================== Ejecución ====================
AgregadorClima agregador = host.Services.GetRequiredService<AgregadorClima>();
OpcionesConsenso opciones = host.Services.GetRequiredService<IOptions<OpcionesConsenso>>().Value;

if (opciones.Ciudades.Count == 0)
{
    Console.WriteLine("No hay ciudades configuradas en appsettings.json.");
    return;
}

foreach (string ciudad in opciones.Ciudades)
{
    ReporteCiudad reporte = await agregador.ProcesarAsync(ciudad);
    Imprimir(reporte);
    Console.WriteLine();
}

// Presentación (se separa en su propia capa en el paso 7).
static void Imprimir(ReporteCiudad r)
{
    Console.WriteLine($"══ {r.Ciudad} ══");

    foreach (ResultadoFuente f in r.Fuentes)
    {
        if (f.Ok)
            Console.WriteLine($"  ✓ {f.Fuente,-12}: {f.Lectura!.TemperaturaC,5:F1} °C");
        else
            Console.WriteLine($"  ✗ {f.Fuente,-12}: {f.Error}");
    }

    if (r.Consenso is null)
    {
        Console.WriteLine("  ⚠ Ninguna fuente respondió: sin consenso.");
        return;
    }

    ResultadoConsolidado c = r.Consenso;
    Console.WriteLine($"  Promedio : {c.PromedioC:F1} °C  (rango {c.MinC:F1}…{c.MaxC:F1}, amplitud {c.AmplitudC:F1})");
    Console.WriteLine($"  Acuerdo  : {c.Acuerdo}  ({c.FuentesConsultadas}/{r.Fuentes.Count} fuentes)");
}


// ==================== Configuración (Options) ====================
class OpcionesConsenso
{
    public const string Seccion = "ConsensoClimaConfig";
    public List<string> Ciudades { get; set; } = new();
    public int TimeoutSegundos { get; set; } = 6;
}


// ==================== Dominio: contrato y modelos ====================
interface IProveedorClima
{
    string Nombre { get; }
    Task<LecturaClima> ObtenerAsync(double latitud, double longitud, CancellationToken ct = default);
}

record LecturaClima(string Fuente, double TemperaturaC);

enum NivelAcuerdo { Alto, Medio, Bajo }

record ResultadoConsolidado(
    string Ciudad, double PromedioC, double MinC, double MaxC,
    int FuentesConsultadas, NivelAcuerdo Acuerdo)
{
    public double AmplitudC => MaxC - MinC;
}

record ResultadoFuente(string Fuente, LecturaClima? Lectura, string? Error)
{
    public bool Ok => Lectura is not null;
    public static ResultadoFuente Exito(LecturaClima l) => new(l.Fuente, l, null);
    public static ResultadoFuente Falla(string fuente, string error) => new(fuente, null, error);
}

record ReporteCiudad(string Ciudad, IReadOnlyList<ResultadoFuente> Fuentes, ResultadoConsolidado? Consenso);


// ==================== Servicio de aplicación ====================
class AgregadorClima
{
    private readonly IEnumerable<IProveedorClima> _proveedores;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OpcionesConsenso _opciones;
    private readonly ILogger<AgregadorClima> _log;

    public AgregadorClima(
        IEnumerable<IProveedorClima> proveedores,
        IHttpClientFactory httpFactory,
        IOptions<OpcionesConsenso> opciones,
        ILogger<AgregadorClima> log)
    {
        _proveedores = proveedores;
        _httpFactory = httpFactory;
        _opciones = opciones.Value;
        _log = log;
    }

    public async Task<ReporteCiudad> ProcesarAsync(string ciudad, CancellationToken ct = default)
    {
        GeoResultado? lugar = await GeocodificarAsync(ciudad, ct);
        if (lugar is null)
        {
            _log.LogWarning("No se encontró la ciudad {Ciudad}.", ciudad);
            return new ReporteCiudad(ciudad, Array.Empty<ResultadoFuente>(), null);
        }

        double lat = Math.Round(lugar.Latitude, 4);
        double lon = Math.Round(lugar.Longitude, 4);
        TimeSpan timeout = TimeSpan.FromSeconds(_opciones.TimeoutSegundos);

        _log.LogInformation("Consultando {N} fuentes para {Ciudad}.", _proveedores.Count(), lugar.Name);

        ResultadoFuente[] resultados = await Task.WhenAll(
            _proveedores.Select(p => IntentarAsync(p, lat, lon, timeout)));

        List<LecturaClima> exitosas = resultados.Where(r => r.Ok).Select(r => r.Lectura!).ToList();
        ResultadoConsolidado? consenso =
            exitosas.Count == 0 ? null : Consolidar(lugar.Name, exitosas);

        return new ReporteCiudad(lugar.Name, resultados, consenso);
    }

    private async Task<ResultadoFuente> IntentarAsync(
        IProveedorClima proveedor, double lat, double lon, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            LecturaClima lectura = await proveedor.ObtenerAsync(lat, lon, cts.Token);
            return ResultadoFuente.Exito(lectura);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("{Fuente} excedió el timeout de {Seg}s.", proveedor.Nombre, timeout.TotalSeconds);
            return ResultadoFuente.Falla(proveedor.Nombre, $"timeout (> {timeout.TotalSeconds:F0}s)");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{Fuente} falló.", proveedor.Nombre);
            return ResultadoFuente.Falla(proveedor.Nombre, ex.Message);
        }
    }

    private async Task<GeoResultado?> GeocodificarAsync(string nombre, CancellationToken ct)
    {
        HttpClient http = _httpFactory.CreateClient();
        string url =
            "https://geocoding-api.open-meteo.com/v1/search" +
            $"?name={Uri.EscapeDataString(nombre)}&count=1&language=es&format=json";
        GeoRespuesta? geo = await http.GetFromJsonAsync<GeoRespuesta>(url, ct);
        return geo?.Results?.FirstOrDefault();
    }

    private static ResultadoConsolidado Consolidar(string ciudad, IReadOnlyList<LecturaClima> lecturas)
    {
        double min = lecturas.Min(l => l.TemperaturaC);
        double max = lecturas.Max(l => l.TemperaturaC);
        NivelAcuerdo acuerdo = (max - min) switch
        {
            <= 1.0 => NivelAcuerdo.Alto,
            <= 3.0 => NivelAcuerdo.Medio,
            _ => NivelAcuerdo.Bajo,
        };
        return new ResultadoConsolidado(
            ciudad, lecturas.Average(l => l.TemperaturaC), min, max, lecturas.Count, acuerdo);
    }
}


// ==================== Proveedores (adaptadores) ====================
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


// ==================== DTOs: mapeo del JSON ====================
record GeoRespuesta(
    [property: JsonPropertyName("results")] List<GeoResultado>? Results);
record GeoResultado(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("country")] string? Country);

record OpenMeteoRespuesta(
    [property: JsonPropertyName("current")] OpenMeteoCurrent? Current);
record OpenMeteoCurrent(
    [property: JsonPropertyName("temperature_2m")] double Temperature2m);

record MetNoRespuesta(
    [property: JsonPropertyName("properties")] MetNoProps? Properties);
record MetNoProps(
    [property: JsonPropertyName("timeseries")] List<MetNoSerie>? Timeseries);
record MetNoSerie(
    [property: JsonPropertyName("data")] MetNoData? Data);
record MetNoData(
    [property: JsonPropertyName("instant")] MetNoInstant? Instant);
record MetNoInstant(
    [property: JsonPropertyName("details")] MetNoDetails? Details);
record MetNoDetails(
    [property: JsonPropertyName("air_temperature")] double AirTemperature);