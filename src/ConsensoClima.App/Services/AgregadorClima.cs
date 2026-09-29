using ConsensoClima.App.Domain;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ConsensoClima.App.Services;

class AgregadorClima
{
    private readonly IEnumerable<IProveedorClima> _proveedores;
    private readonly IGeocodificador _geocodificador;
    private readonly OpcionesConsenso _opciones;
    private readonly ILogger<AgregadorClima> _log;

    public AgregadorClima(
        IEnumerable<IProveedorClima> proveedores,
        IGeocodificador geocodificador,
        IOptions<OpcionesConsenso> opciones,
        ILogger<AgregadorClima> log)
    {
        _proveedores = proveedores;
        _geocodificador = geocodificador;
        _opciones = opciones.Value;
        _log = log;
    }

    //Procesa la ciudad, obteniendo su ubicación y consultando los proveedores de clima
    public async Task<ReporteCiudad> ProcesarAsync(string ciudad, CancellationToken ct = default)
    {
        //Obtiene la ubicación de la ciudad usando el geocodificador (Latitud, Longitud)
        Ubicacion? lugar = await _geocodificador.ResolverAsync(ciudad, ct);
        if (lugar is null)
        {
            _log.LogWarning("No se encontró la ciudad {Ciudad}.", ciudad);
            return new ReporteCiudad(ciudad, Array.Empty<ResultadoFuente>(), null);
        }

        TimeSpan timeout = TimeSpan.FromSeconds(_opciones.TimeoutSegundos);
        _log.LogInformation("Consultando {N} fuentes para {Ciudad}.", _proveedores.Count(), lugar.Nombre);

        ResultadoFuente[] resultados = await Task.WhenAll(
            _proveedores.Select(p => IntentarAsync(p, lugar, timeout)));

        List<LecturaClima> exitosas = resultados.Where(r => r.Ok).Select(r => r.Lectura!).ToList(); // Extrae las lecturas exitosas

        // Consolida los resultados exitosos para obtener un consenso (Promedio de las lecturas)
        ResultadoConsolidado? consenso =
            exitosas.Count == 0 ? null : Consolidador.Consolidar(lugar.Nombre, exitosas); //Consolidador.Consolidar es static por lo cual se puede llamar directamente.

        return new ReporteCiudad(lugar.Nombre, resultados, consenso);
    }

    //Intenta obtener la lectura de clima de un proveedor con un timeout
    private async Task<ResultadoFuente> IntentarAsync(
        IProveedorClima proveedor, Ubicacion lugar, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            LecturaClima lectura = await proveedor.ObtenerAsync(lugar.Latitud, lugar.Longitud, cts.Token);
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
}