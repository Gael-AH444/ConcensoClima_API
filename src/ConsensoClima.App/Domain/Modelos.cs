namespace ConsensoClima.App.Domain;

record LecturaClima(string Fuente, double TemperaturaC);

record Ubicacion(string Nombre, string? Pais, double Latitud, double Longitud);

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