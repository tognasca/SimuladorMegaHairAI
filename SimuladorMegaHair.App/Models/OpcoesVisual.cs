namespace SimuladorMegaHair.App.Models;

public static class OpcoesVisual
{
    public static List<string> Comprimentos { get; } = new()
    {
        "Curto",
        "Médio",
        "Longo",
        "Extra Longo"
    };

    public static List<string> Cores { get; } = new()
    {
        "Preto",
        "Castanho Escuro",
        "Castanho Claro",
        "Loiro Escuro",
        "Loiro Médio",
        "Loiro Claro",
        "Loiro Platinado",
        "Ruivo",
        "Grisalho"
    };

    public static List<string> TiposCabelo { get; } = new()
    {
        "Liso",
        "Ondulado",
        "Cacheado",
        "Crespo"
    };

    public static List<string> Metodos { get; } = new()
    {
        "Fita Adesiva",
        "Queratina",
        "Micro Link",
        "Costurado"
    };
}