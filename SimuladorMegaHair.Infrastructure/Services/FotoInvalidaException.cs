using System;

namespace SimuladorMegaHair.Infrastructure.Services;

/// Erro causado pela foto enviada (rosto nao encontrado/pequeno, foto bloqueada).
/// A mensagem e segura para exibir ao usuario.
public class FotoInvalidaException : InvalidOperationException
{
    public FotoInvalidaException(string message) : base(message) { }
    public FotoInvalidaException(string message, Exception innerException) : base(message, innerException) { }
}