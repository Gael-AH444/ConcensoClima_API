using System.Text.Json.Serialization;

namespace ConsensoClima.App.Providers;

record GeoRespuesta([property: JsonPropertyName("results")] List<GeoResultado>? Results);
record GeoResultado(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("country")] string? Country);

record OpenMeteoRespuesta([property: JsonPropertyName("current")] OpenMeteoCurrent? Current);
record OpenMeteoCurrent([property: JsonPropertyName("temperature_2m")] double Temperature2m);

record MetNoRespuesta([property: JsonPropertyName("properties")] MetNoProps? Properties);
record MetNoProps([property: JsonPropertyName("timeseries")] List<MetNoSerie>? Timeseries);
record MetNoSerie([property: JsonPropertyName("data")] MetNoData? Data);
record MetNoData([property: JsonPropertyName("instant")] MetNoInstant? Instant);
record MetNoInstant([property: JsonPropertyName("details")] MetNoDetails? Details);
record MetNoDetails([property: JsonPropertyName("air_temperature")] double AirTemperature);