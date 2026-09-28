using ConsensoClima.App.Domain;
using ConsensoClima.App.Presentation;
using ConsensoClima.App.Providers;
using ConsensoClima.App.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<OpcionesConsenso>(
    builder.Configuration.GetSection(OpcionesConsenso.Seccion));

builder.Services.AddHttpClient<OpenMeteoProveedor>();
builder.Services.AddHttpClient<MetNoProveedor>(c =>
    c.DefaultRequestHeaders.TryAddWithoutValidation(
        "User-Agent", "ConsensoClima/0.1 (aprendizaje; tu-correo@ejemplo.com)"));
builder.Services.AddHttpClient<GeocodificadorOpenMeteo>();

builder.Services.AddTransient<IProveedorClima>(sp => sp.GetRequiredService<OpenMeteoProveedor>());
builder.Services.AddTransient<IProveedorClima>(sp => sp.GetRequiredService<MetNoProveedor>());
builder.Services.AddTransient<IGeocodificador>(sp => sp.GetRequiredService<GeocodificadorOpenMeteo>());

builder.Services.AddTransient<AgregadorClima>();

using IHost host = builder.Build();

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
    ReportePresentador.Imprimir(reporte);
    Console.WriteLine();
}