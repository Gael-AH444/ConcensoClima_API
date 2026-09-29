using ConsensoClima.App.Domain;
using ConsensoClima.App.Presentation;
using ConsensoClima.App.Providers;
using ConsensoClima.App.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

//Servicios y configuración
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<OpcionesConsenso>(
    builder.Configuration.GetSection(OpcionesConsenso.Seccion));

builder.Services.AddHttpClient<OpenMeteoProveedor>();
builder.Services.AddHttpClient<MetNoProveedor>(c =>
    c.DefaultRequestHeaders.TryAddWithoutValidation(
        "User-Agent", "ConsensoClima/0.1 (aprendizaje; micorreo@gmail.com)"));
builder.Services.AddHttpClient<GeocodificadorOpenMeteo>();

builder.Services.AddTransient<IProveedorClima>(sp => sp.GetRequiredService<OpenMeteoProveedor>());
builder.Services.AddTransient<IProveedorClima>(sp => sp.GetRequiredService<MetNoProveedor>());
builder.Services.AddTransient<IGeocodificador>(sp => sp.GetRequiredService<GeocodificadorOpenMeteo>()); // Registramos las implementaciones concretas de los proveedores y geocodificador

builder.Services.AddTransient<AgregadorClima>(); // AgregadorClima depende de IProveedorClima y IGeocodificador, que ya están registrados

using IHost host = builder.Build();

// Ejecutamos la aplicación
AgregadorClima agregador = host.Services.GetRequiredService<AgregadorClima>(); // Obtenemos el agregador de clima del contenedor de servicios
OpcionesConsenso opciones = host.Services.GetRequiredService<IOptions<OpcionesConsenso>>().Value; // Obtenemos las opciones de configuración

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