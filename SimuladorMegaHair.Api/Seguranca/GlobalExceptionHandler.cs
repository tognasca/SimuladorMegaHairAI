using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SimuladorMegaHair.Infrastructure.Storage;

namespace SimuladorMegaHair.Api.Seguranca;

/// <summary>
/// Converte exceções não tratadas em respostas ProblemDetails com mensagem
/// amigável, sem stack trace e sem texto vindo de fornecedores externos.
///
/// Antes, só FileNotFoundException e InvalidOperationException eram tratadas
/// no controller; qualquer outra falha (Replicate fora do ar, token vazio,
/// timeout, JSON inesperado) virava HTTP 500 com página de exceção do
/// desenvolvedor. O detalhe técnico fica APENAS no log do servidor.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext contexto, Exception excecao, CancellationToken ct)
    {
        // O cliente desistiu: não há a quem responder.
        if (excecao is OperationCanceledException && contexto.RequestAborted.IsCancellationRequested)
        {
            contexto.Response.StatusCode = 499;
            return true;
        }

        var (status, titulo, detalhe) = Classificar(excecao);

        if (status >= 500)
            _logger.LogError(excecao, "Falha ao processar {Metodo} {Caminho}", contexto.Request.Method, contexto.Request.Path);
        else
            _logger.LogWarning("Requisição rejeitada ({Status}) em {Caminho}: {Tipo}", status, contexto.Request.Path, excecao.GetType().Name);

        contexto.Response.StatusCode = status;
        await contexto.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = titulo, Detail = detalhe },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: ct);

        return true;
    }

    private static (int status, string titulo, string detalhe) Classificar(Exception excecao) => excecao switch
    {
        CaminhoInvalidoException =>
            (StatusCodes.Status400BadRequest, "Imagem inválida",
             "A foto informada não é válida. Envie a foto novamente."),

        FileNotFoundException =>
            (StatusCodes.Status404NotFound, "Imagem não encontrada",
             "A foto não foi encontrada no servidor. Envie a foto novamente."),

        TimeoutException =>
            (StatusCodes.Status504GatewayTimeout, "Tempo esgotado",
             "O serviço de geração de imagens demorou demais para responder. Tente novamente."),

        HttpRequestException =>
            (StatusCodes.Status502BadGateway, "Serviço de IA indisponível",
             "Não foi possível falar com o serviço de geração de imagens agora. Tente novamente em instantes."),

        InvalidOperationException =>
            (StatusCodes.Status422UnprocessableEntity, "Não foi possível gerar a simulação",
             "Não conseguimos gerar esta simulação com a foto e as opções escolhidas. Tente outra foto ou outras opções."),

        _ =>
            (StatusCodes.Status500InternalServerError, "Erro interno",
             "Ocorreu um erro inesperado. Tente novamente; se persistir, avise o suporte.")
    };
}
