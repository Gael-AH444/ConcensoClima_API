using ConsensoClima.App.Domain;
using ConsensoClima.App.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace ConsensoClima.Tests;

public class AgregadorClimaTests
{
    // --- Helpers que arman dobles sin tocar la red ---

    private static IGeocodificador GeoFijo()
    {
        var geo = new Mock<IGeocodificador>();
        geo.Setup(g => g.ResolverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new Ubicacion("Querétaro", "México", 20.59, -100.39));
        return geo.Object;
    }

    private static AgregadorClima Construir(IEnumerable<IProveedorClima> proveedores)
    {
        IOptions<OpcionesConsenso> opciones = Options.Create(new OpcionesConsenso { TimeoutSegundos = 5 });
        return new AgregadorClima(proveedores, GeoFijo(), opciones, NullLogger<AgregadorClima>.Instance);
    }

    private static IProveedorClima ProveedorOk(string nombre, double temp)
    {
        var p = new Mock<IProveedorClima>();
        p.SetupGet(x => x.Nombre).Returns(nombre);
        p.Setup(x => x.ObtenerAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(new LecturaClima(nombre, temp));
        return p.Object;
    }

    private static IProveedorClima ProveedorQueTruena(string nombre)
    {
        var p = new Mock<IProveedorClima>();
        p.SetupGet(x => x.Nombre).Returns(nombre);
        p.Setup(x => x.ObtenerAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
         .ThrowsAsync(new HttpRequestException("caída simulada"));
        return p.Object;
    }

    // --- Los tests ---

    [Fact]
    public async Task Una_fuente_caida_no_tumba_la_corrida()
    {
        IProveedorClima[] proveedores = { ProveedorOk("Buena", 21.0), ProveedorQueTruena("Mala") };

        ReporteCiudad reporte = await Construir(proveedores).ProcesarAsync("Querétaro");

        // Sobrevive: hay consenso, calculado con la fuente que sí respondió.
        Assert.NotNull(reporte.Consenso);
        Assert.Equal(21.0, reporte.Consenso!.PromedioC, 3);
        Assert.Equal(1, reporte.Consenso.FuentesConsultadas);

        // Y reporta ambas: la buena OK y la mala con su motivo.
        Assert.Equal(2, reporte.Fuentes.Count);
        Assert.Contains(reporte.Fuentes, f => f.Fuente == "Buena" && f.Ok);
        Assert.Contains(reporte.Fuentes, f => f.Fuente == "Mala" && !f.Ok && f.Error is not null);
    }

    [Fact]
    public async Task Si_todas_fallan_no_hay_consenso_pero_no_lanza()
    {
        IProveedorClima[] proveedores = { ProveedorQueTruena("Mala1"), ProveedorQueTruena("Mala2") };

        ReporteCiudad reporte = await Construir(proveedores).ProcesarAsync("Querétaro");

        Assert.Null(reporte.Consenso);              // sin consenso…
        Assert.Equal(2, reporte.Fuentes.Count);     // …pero reporta ambas
        Assert.All(reporte.Fuentes, f => Assert.False(f.Ok));
    }
}