using ConsensoClima.App.Domain;
using ConsensoClima.App.Services;

using Xunit;

namespace ConsensoClima.Tests;

public class ConsolidadorTests
{
    private static LecturaClima L(string fuente, double temp) => new(fuente, temp);

    [Fact]
    public void Consolidar_calcula_promedio_min_y_max()
    {
        LecturaClima[] lecturas = { L("A", 20.0), L("B", 21.0), L("C", 24.0) };

        ResultadoConsolidado r = Consolidador.Consolidar("Ciudad", lecturas);

        Assert.Equal(21.667, r.PromedioC, 3);   // (20+21+24)/3, con tolerancia de 3 decimales
        Assert.Equal(20.0, r.MinC);
        Assert.Equal(24.0, r.MaxC);
        Assert.Equal(4.0, r.AmplitudC);
        Assert.Equal(3, r.FuentesConsultadas);
    }

    [Theory]
    [InlineData(20.0, 20.5, nameof(NivelAcuerdo.Alto))]    // amplitud 0.5
    [InlineData(20.0, 22.5, nameof(NivelAcuerdo.Medio))]   // amplitud 2.5
    [InlineData(20.0, 26.0, nameof(NivelAcuerdo.Bajo))]    // amplitud 6.0
    public void Consolidar_clasifica_el_acuerdo_por_amplitud(double t1, double t2, string esperado)
    {
        LecturaClima[] lecturas = { L("A", t1), L("B", t2) };

        ResultadoConsolidado r = Consolidador.Consolidar("Ciudad", lecturas);

        Assert.Equal(esperado, r.Acuerdo.ToString());
    }

    [Theory]
    [InlineData(1.0, nameof(NivelAcuerdo.Alto))]    // frontera exacta: <= 1.0 sigue siendo Alto
    [InlineData(3.0, nameof(NivelAcuerdo.Medio))]   // frontera exacta: <= 3.0 sigue siendo Medio
    public void Consolidar_respeta_las_fronteras_de_los_umbrales(double amplitud, string esperado)
    {
        LecturaClima[] lecturas = { L("A", 20.0), L("B", 20.0 + amplitud) };

        ResultadoConsolidado r = Consolidador.Consolidar("Ciudad", lecturas);

        Assert.Equal(esperado, r.Acuerdo.ToString());
    }
}