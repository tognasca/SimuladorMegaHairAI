# Piloto de créditos do simulador

Configure no servidor da API. O Access Token pertence à conta do Mercado Pago do operador da IA, que recebe os pagamentos. Mantenha-o no gerenciador de segredos ou nas variáveis privadas da API.

| Variável da API | Padrão | Função |
| --- | --- | --- |
| `Creditos__Ativo` | `false` | Ativa o controle de gerações e o fluxo Pix após a homologação |
| `Creditos__PublicoAtivo` | `true` | Permite novas gerações públicas quando o controle está ativo |
| `Creditos__LimiteDiarioGratis` | `10` | Teto de tentativas gratuitas por dia, no horário de São Paulo |
| `Creditos__Preco` | `10` | Valor do pacote em reais |
| `Creditos__Quantidade` | `2` | Gerações por pacote |
| `Creditos__NomeNoPix` | `Simulador Mega Hair` | Descrição da compra |
| `MercadoPago__AccessToken` | Vazio | Credencial privada do operador que vai receber o Pix |

A API aplica a migration PostgreSQL das carteiras, cotas e pedidos no fluxo existente de migrations. A cota gratuita é por navegador e o teto diário é compartilhado por instalação. O teto conta tentativas para limitar gasto com chamadas que falham. O login existente da equipe fica fora da cota pública. Configure a conta da equipe antes de abrir a instalação na internet.

Na etapa de compra, o provedor recebe e-mail e CPF do pagador. O simulador guarda o pedido e a carteira, sem gravar esses dois campos no banco de créditos. Após pagar, a cliente toca em **Verificar pagamento**. A API confirma o Pix no Mercado Pago e libera o pacote uma única vez. O código da carteira fica no navegador, de modo que o saldo deve ser usado no mesmo navegador. Webhooks, recuperação de saldo em outro dispositivo e confirmação de identidade das clientes ficam fora deste piloto.

Para homologar, use uma credencial de teste e um banco separado. Teste uma primeira geração gratuita, o bloqueio pelo teto, um Pix pendente, um Pix aprovado, duas verificações do mesmo pagamento, o uso dos dois créditos, uma falha na geração e o Modo Salão. Valide câmera, QR Code e Pix copia e cola no mesmo celular usado pela cliente.

No piloto, esta configuração pertence à instalação da Tati. Para replicar com isolamento, cada instalação usa seu banco e seus limites. A credencial do operador continua centralmente administrada. Uma instância SaaS compartilhada entre salões exige identificação da empresa por sessão, isolamento de dados e cotas por empresa.
