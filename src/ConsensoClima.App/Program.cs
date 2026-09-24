using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

// ================= Composición y orquestación =================

string ciudad = "Querétaro";

using HttpClient http = new HttpClient();
// met.no EXIGE que te identifiques o responde 403. Pon tu correo real.
http.DefaultRequestHeaders.TryAddWithoutValidation(
    "User-Agent", "ConsensoClima/0.1 (aprendizaje; gaelalejo.444@gmail.com)");

// 1) Geocodificar el nombre -> coordenadas (una sola vez; se comparten).
GeoResultado? lugar = await GeocodificarAsync(ciudad, http);
if (lugar is null)
{
    Console.WriteLine($"No encontré la ciudad \"{ciudad}\".");
    return;
}

// met.no rechaza (403) coordenadas con MÁS de 4 decimales. Redondeamos una vez.
double lat = Math.Round(lugar.Latitude, 4);
double lon = Math.Round(lugar.Longitude, 4);

// 2) La lista de proveedores. De aquí en adelante el código NO conoce
//    ninguna API concreta: solo habla con la interfaz IProveedorClima.
List<IProveedorClima> proveedores = new()
{
    new OpenMeteoProveedor(http),
    new MetNoProveedor(http),
    new ProveedorCaido(),   // ← quítalo cuando termines de probar
};

// 3) Llamarlos EN SECUENCIA y cronometrar el total.
Console.WriteLine($"Clima en {lugar.Name}, {lugar.Country} (lat {lat}, lon {lon}):\n");

TimeSpan timeout = TimeSpan.FromSeconds(8);
Stopwatch sw = Stopwatch.StartNew();

List<Task<ResultadoFuente>> tareas = proveedores
    .Select(p => IntentarAsync(p, lat, lon, timeout))
    .ToList();

ResultadoFuente[] resultados = await Task.WhenAll(tareas);
sw.Stop();

// --- Reporte por fuente: quién respondió y quién no ---
Console.WriteLine("Estado por fuente:");
foreach (ResultadoFuente r in resultados)
{
    if (r.Ok)
        Console.WriteLine($"  ✓ {r.Fuente,-12}: {r.Lectura!.TemperaturaC,5:F1} °C");
    else
        Console.WriteLine($"  ✗ {r.Fuente,-12}: {r.Error}");
}

// --- Consolidar SOLO las que respondieron ---
List<LecturaClima> exitosas = resultados
    .Where(r => r.Ok)
    .Select(r => r.Lectura!)
    .ToList();

if (exitosas.Count == 0)
{
    Console.WriteLine("\n⚠ Ninguna fuente respondió. Sin consenso.");
}
else
{
    ResultadoConsolidado consenso = Consolidar(lugar.Name, exitosas);
    Console.WriteLine($"\n── Consenso para {consenso.Ciudad} ──");
    Console.WriteLine($"  Promedio : {consenso.PromedioC:F1} °C");
    Console.WriteLine($"  Rango    : {consenso.MinC:F1} … {consenso.MaxC:F1} °C  (amplitud {consenso.AmplitudC:F1} °C)");
    Console.WriteLine($"  Respondieron: {consenso.FuentesConsultadas} de {resultados.Length}");
    Console.WriteLine($"  Acuerdo  : {consenso.Acuerdo}");
}

Console.WriteLine($"\nTotal: {sw.ElapsedMilliseconds} ms");


// ================= Función local: geocoding =================
async Task<GeoResultado?> GeocodificarAsync(string nombre, HttpClient cliente)
{
    string url =
        "https://geocoding-api.open-meteo.com/v1/search" +
        $"?name={Uri.EscapeDataString(nombre)}&count=1&language=es&format=json";

    GeoRespuesta? geo = await cliente.GetFromJsonAsync<GeoRespuesta>(url);
    return geo?.Results?.FirstOrDefault();
}

static ResultadoConsolidado Consolidar(string ciudad, IReadOnlyList<LecturaClima> lecturas)
{
    double promedio = lecturas.Average(l => l.TemperaturaC);
    double min = lecturas.Min(l => l.TemperaturaC);
    double max = lecturas.Max(l => l.TemperaturaC);
    double amplitud = max - min;

    // La amplitud (cuánto se separan las fuentes) define la confianza.
    // Los umbrales son una DECISIÓN de producto, no una ley física.
    NivelAcuerdo acuerdo = amplitud switch
    {
        <= 1.0 => NivelAcuerdo.Alto,   // casi idénticas
        <= 3.0 => NivelAcuerdo.Medio,  // discrepancia moderada
        _ => NivelAcuerdo.Bajo,   // desacuerdo notable
    };

    return new ResultadoConsolidado(
        Ciudad: ciudad,
        PromedioC: promedio,
        MinC: min,
        MaxC: max,
        FuentesConsultadas: lecturas.Count,
        Acuerdo: acuerdo);
}

static async Task<ResultadoFuente> IntentarAsync(
    IProveedorClima proveedor, double lat, double lon, TimeSpan timeout)
{
    // Este CTS se dispara solo a los 'timeout' segundos y cancela la petición.
    using var cts = new CancellationTokenSource(timeout);
    try
    {
        LecturaClima lectura = await proveedor.ObtenerAsync(lat, lon, cts.Token);
        return ResultadoFuente.Exito(lectura);
    }
    catch (OperationCanceledException)
    {
        return ResultadoFuente.Falla(proveedor.Nombre, $"timeout (> {timeout.TotalSeconds:F0}s)");
    }
    catch (Exception ex)
    {
        // Red caída, 4xx/5xx, JSON inesperado… cualquier cosa se vuelve DATO.
        return ResultadoFuente.Falla(proveedor.Nombre, ex.Message);
    }
}



// ================= Dominio: el contrato y el modelo =================

interface IProveedorClima
{
    string Nombre { get; }
    Task<LecturaClima> ObtenerAsync(double latitud, double longitud, CancellationToken ct = default);
}

//Record para representar la lectura de clima de un proveedor
record LecturaClima(string Fuente, double TemperaturaC);

enum NivelAcuerdo { Alto, Medio, Bajo }

record ResultadoConsolidado(
    string Ciudad,
    double PromedioC,
    double MinC,
    double MaxC,
    int FuentesConsultadas,
    NivelAcuerdo Acuerdo)
{
    // Propiedad calculada: un record no es solo una bolsa de datos.
    public double AmplitudC => MaxC - MinC;
}

// Resultado de INTENTAR una fuente: o trajo lectura, o falló con un motivo.
record ResultadoFuente(string Fuente, LecturaClima? Lectura, string? Error)
{
    public bool Ok => Lectura is not null;

    public static ResultadoFuente Exito(LecturaClima l) => new(l.Fuente, l, null);
    public static ResultadoFuente Falla(string fuente, string error) => new(fuente, null, error);
}




// ================= Proveedores (adaptadores) =================

class OpenMeteoProveedor : IProveedorClima
{
    private readonly HttpClient _http;
    //Constructor que recibe HttpClient para inyección de dependencias
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
    //Constructor que recibe HttpClient para inyección de dependencias
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

// Proveedor de PRUEBA: simula una fuente caída para ver la resiliencia.
class ProveedorCaido : IProveedorClima
{
    public string Nombre => "FuenteCaida";
    public Task<LecturaClima> ObtenerAsync(double lat, double lon, CancellationToken ct = default)
        => throw new HttpRequestException("host no encontrado (simulado)");
}


// ================= DTOs: mapeo del JSON de cada API =================

// -- Geocoding (Open-Meteo) --
record GeoRespuesta(
    [property: JsonPropertyName("results")] List<GeoResultado>? Results);
record GeoResultado(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("country")] string? Country);

// -- Open-Meteo forecast: { "current": { "temperature_2m": .. } } --
record OpenMeteoRespuesta(
    [property: JsonPropertyName("current")] OpenMeteoCurrent? Current);
record OpenMeteoCurrent(
    [property: JsonPropertyName("temperature_2m")] double Temperature2m);

// -- met.no: properties.timeseries[0].data.instant.details.air_temperature --
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