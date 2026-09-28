namespace ConsensoClima.App.Domain;

interface IProveedorClima
{
    string Nombre { get; }
    Task<LecturaClima> ObtenerAsync(double latitud, double longitud, CancellationToken ct = default);
}

interface IGeocodificador
{
    Task<Ubicacion?> ResolverAsync(string ciudad, CancellationToken ct = default);
}


