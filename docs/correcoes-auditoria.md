# Correções da auditoria do piloto

## Atualização local

Atualize a branch integracao/megahairpro-preco no simulador. No MegaHairPro, a branch integracao/simulador-megahair passou a incorporar feature/postgresql-migration. Uma base PostgreSQL nova do MegaHairPro ainda precisa da migration inicial descrita no README da migração. Use dois bancos isolados e o roteiro de homologação. A API do simulador aplica a migration AddGenerationReservations ao iniciar.

## Mudanças a validar

| Fluxo | Resultado esperado |
|---|---|
| Fotos | Upload devolve um comprovante temporário. As fotos usam URLs assinadas com prazo de duas horas. Histórico e ajuste de volume exigem login da equipe. Fotos antigas já compartilhadas por links estáticos deixam de abrir por esses links. |
| Cache e concorrência | Requisições idênticas simultâneas são bloqueadas no PostgreSQL. O pedido duplicado recebe 409 antes de reservar crédito ou chamar IA. Um resultado entregue pode ser reutilizado sem crédito, exceto ao pedir nova versão. |
| Cota | Público tem teto diário de tentativas mesmo com Pix desligado. Sem Pix, uma carteira sem vaga recebe 429. Com Pix habilitado, recebe 402 e pode comprar créditos. Equipe continua independente da cota pública. |
| Cancelamento | O token de cancelamento atravessa Web, API e o pipeline. O provedor externo ainda pode processar ou cobrar uma operação que já recebeu. |
| Reservas | O débito tem uma reserva persistente. A conclusão do resultado e a confirmação da reserva são atômicas. Uma falha estorna a reserva. Uma interrupção do processo deixa a reserva recuperável após 20 minutos; a verificação roda a cada minuto e ao consultar ou usar uma carteira. |
| Pix | Na compra, salve o código privado da carteira. Em outro navegador, use Já comprei créditos e informe esse código. O servidor consulta periodicamente os pedidos com pagamento vinculado e credita os aprovados uma vez. A tela também permite verificar manualmente. Se o pedido ficou sem ID de pagamento após uma resposta interrompida, retome a geração do Pix com o mesmo pedido para reutilizar a idempotência. |
| Preço | Se o orçamento mudar entre prévia e confirmação, volte à escolha antes de gerar. Se uma oferta Pix mudar antes da criação, a cliente precisa confirmar os novos dados antes de criar o pagamento. |
| Limites | Até oito requisições à rota de geração por instância e até trinta uploads por minuto por instância. O piloto precisa de teste de carga com o hardware real. |

Teste também duas sessões diferentes, interrupção do processo durante uma geração, foto expirada, retorno do Pix após fechar a aba e Parear TV durante uma geração. Use fotos sintéticas, credenciais de teste e bases próprias. Nenhuma mudança foi aplicada aos dados ou ao servidor em produção.
