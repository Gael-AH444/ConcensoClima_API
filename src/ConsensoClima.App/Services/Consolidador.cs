using ConsensoClima.App.Domain;

namespace ConsensoClima.App.Services;

static class Consolidador
{
    public static ResultadoConsolidado Consolidar(string ciudad, IReadOnlyList<LecturaClima> lecturas)
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