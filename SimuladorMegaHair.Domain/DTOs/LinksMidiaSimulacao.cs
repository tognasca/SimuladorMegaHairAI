namespace SimuladorMegaHair.Domain.DTOs;

public sealed record ProvaMidiaSimulacao(long Exp, string? Sig);

public sealed record LinksMidiaSimulacao(string FotoOriginalUrl, string FotoResultadoUrl);
