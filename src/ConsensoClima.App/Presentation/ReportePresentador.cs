using ConsensoClima.App.Domain;

namespace ConsensoClima.App.Presentation;

static class ReportePresentador
{
    public static void Imprimir(ReporteCiudad r)
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
}