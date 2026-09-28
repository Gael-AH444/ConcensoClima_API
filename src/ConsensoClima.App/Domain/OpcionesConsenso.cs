namespace ConsensoClima.App.Domain;

class OpcionesConsenso
{
    public const string Seccion = "ConsensoClima";
    public List<string> Ciudades { get; set; } = new();
    public int TimeoutSegundos { get; set; } = 5;
}