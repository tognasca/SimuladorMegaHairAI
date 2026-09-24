# Segurança — Fase 1 (correções críticas)

Este documento resume o que foi corrigido nesta branch (`fase1-correcoes`) e,
principalmente, **o que só você consegue fazer** — nenhuma ferramenta externa
tem acesso à sua conta do GitHub ou da Replicate.

## 1. Ações que só você pode fazer — faça antes de qualquer outra coisa

1. **Revogue os dois tokens do Replicate** em
   [replicate.com/account/api-tokens](https://replicate.com/account/api-tokens).
   Se não tiver certeza de quais tokens já apareceram no projeto, revogue
   **todos** e gere um novo — é mais seguro do que tentar adivinhar.
2. **Torne o repositório privado** no GitHub (Settings → General → Danger
   Zone → Change visibility), até o passo 3 abaixo ser concluído.
3. **Reescreva o histórico do Git** para remover os tokens e as fotos de
   clientes que foram commitados em versões anteriores. Remover os arquivos
   do HEAD (o que esta branch já fez) **não** os apaga do histórico —
   qualquer pessoa pode rodar `git log -p` ou clonar uma cópia antiga e
   recuperá-los.

   Com [`git filter-repo`](https://github.com/newren/git-filter-repo)
   (recomendado pelo próprio GitHub, mais rápido e seguro que `filter-branch`):

   ```bash
   pip install git-filter-repo
   git clone --mirror https://github.com/<seu-usuario>/SimuladorMegaHairAI.git
   cd SimuladorMegaHairAI.git

   git filter-repo \
     --path SimuladorMegaHair.Api/wwwroot/uploads --invert-paths \
     --path SimuladorMegaHair.Api/wwwroot/resultados --invert-paths \
     --path SimuladorMegaHair.Api/wwwroot/temp --invert-paths \
     --path SimuladorMegaHair.Api/wwwroot/masks --invert-paths \
     --path SimuladorMegaHair.App/resultados --invert-paths \
     --path SimuladorMegaHair.Api/erro_completo.txt --invert-paths \
     --path SimuladorMegaHair.Web/web_log.txt --invert-paths

   # Troque pelos dois prefixos reais que você revogou no passo 1
   git filter-repo --replace-text <(printf 'r8_PRIMEIRO_PREFIXO_REAL==>***REMOVIDO***\nr8_SEGUNDO_PREFIXO_REAL==>***REMOVIDO***\n')

   git push --force --mirror origin
   ```

   Depois disso, peça para todos que já clonaram o repositório clonarem de
   novo do zero (um `git pull` normal não limpa o histórico local deles).
4. **Confirme se as fotos usadas nos testes são de clientes reais ou só de
   teste.** Se forem de clientes reais, trate como um incidente de dados
   pessoais — avise as pessoas envolvidas, conforme a política do salão.
5. Depois de revogado o token antigo, gere um novo e siga o `DEPLOY.md`
   atualizado (seções 1.2 a 1.4) para configurá-lo como variável de
   ambiente — nunca direto em `appsettings.json`.

## 2. O que esta branch já corrige (código)

| Item da auditoria | O que mudou |
|---|---|
| P01 — tokens no Git | `Replicate:ApiToken` e `OpenAI:ApiToken` esvaziados em `appsettings.json`; documentado que precisam vir de variável de ambiente/user-secrets |
| P02 — fotos no repositório | Removidas do estado atual (`uploads`, `resultados`, `temp`, `masks`, `App/resultados`) e adicionadas ao `.gitignore` |
| P03 — API sem autenticação | `X-Api-Key` obrigatório em toda a API (`Seguranca/ApiKeyAuthentication.cs`), com política padrão que exige autenticação mesmo em endpoints futuros que esqueçam o `[Authorize]` |
| P04 — leitura arbitrária de arquivo | `Storage/CaminhosSeguros.cs`: único ponto que valida um caminho recebido de fora contra os nomes que o próprio servidor gera. Aplicado no controller **e** no serviço de IA (defesa em profundidade) |
| P05 — IA chamada duas vezes | Chamada duplicada a `PipelineKontextAsync` removida do `SimulacoesController` |
| P06 — provider padrão inválido | `Simulacao:DefaultProvider` corrigido para `Replicate` em `appsettings.json`; `HabilitarProviderLocal`/`OpenAI` deixados desligados por não estarem implementados |
| P07 — upload do catálogo sem validação | `CatalogoController` agora exige `X-Api-Key` para cadastrar (leitura continua pública) e valida o arquivo por conteúdo real (`Storage/ImagemInspetor.cs`), não pela extensão do nome |
| P08 — ajuste de volume com caminho inconsistente | Corrigido como efeito colateral da correção do P04: `CaminhosSeguros` resolve `uploads` e `resultados` da mesma forma, sempre a partir de `wwwroot/` |
| P13 — erros viram 500 genérico | `Seguranca/GlobalExceptionHandler.cs`: respostas padronizadas (`ProblemDetails`), sem stack trace, com mensagem apropriada para cada tipo de falha |
| P17 — exclusão não apaga arquivos | `Services/ExclusaoDadosService.cs`: excluir um cliente agora remove as simulações do banco **e** os arquivos de foto em disco |
| Exposição de fotos por link público | Fotos passam a ser servidas por `MediaController` com URL assinada (HMAC) e prazo de validade, em vez de arquivo estático público. Só o catálogo (sem dado pessoal) continua público |
| Swagger exposto em produção | Só carrega quando `ASPNETCORE_ENVIRONMENT=Development` |
| CORS aberto | Restrito às origens em `Cors:OrigensPermitidas` |
| Sem limite de uso | `Seguranca/GeracaoThrottle.cs` (gerações simultâneas) + cota diária global (`Simulacao:LimiteDiarioGlobal`) |
| Arquivos temporários acumulando fotos | `Servicos/LimpezaTempService.cs`: apaga periodicamente o que sobra em `wwwroot/temp` e `wwwroot/masks` |
| Modo "imagem de teste" ativável em produção (MAUI) | Agora só funciona em builds DEBUG |
| Versões de pacote divergentes (Npgsql) | Alinhadas em 8.0.4 |

## 3. O que esta branch **não** cobre (fica para a Fase 2, conforme o relatório de auditoria)

- Segmentação de cabelo real (a máscara continua heurística — P09)
- Prompts contraditórios e mapeamento errado de cor/comprimento/preço (P10/P11)
- Validação do resultado gerado pela IA (P12)
- Correção de orientação EXIF (P14)
- Testes automatizados (nenhum foi adicionado)
- Login por usuário/perfil (a chave de API de Fase 1 autentica "o salão", não pessoas)
- Migração para .NET 10 antes do fim do suporte do .NET 8 (10/11/2026)

## 4. Como testar localmente que a autenticação está funcionando

Sem chave — deve retornar 401:

```bash
curl -i http://localhost:5185/api/clientes
```

Com a chave certa — deve retornar 200:

```bash
curl -i http://localhost:5185/api/clientes -H "X-Api-Key: sua-chave-aqui"
```

Tentando ler um arquivo fora de `wwwroot/uploads` pelo endpoint de simulação
(deve retornar 400, não o conteúdo do arquivo):

```bash
curl -i -X POST http://localhost:5185/api/simulacoes \
  -H "X-Api-Key: sua-chave-aqui" -H "Content-Type: application/json" \
  -d '{"fotoOriginalPath":"../../appsettings.json","comprimento":"55 cm","cor":"Preto","tipoCabelo":"Liso","metodoMegaHair":"Fita Adesiva","provider":"Replicate"}'
```
