# Orçamento local do simulador

A API do simulador lê a tabela no seu PostgreSQL. Escolher valores, consultar uma prévia, gerar uma imagem ou abrir um resultado em cache usa o cálculo local. A tabela começa vazia. O servidor aplica a migration AddLocalQuote no fluxo de migrations da API.

## Tabela própria do salão

Defina `Orcamento__AdministradorEmail` na API com o e-mail de uma conta existente do simulador. Se ele estiver ausente, usa-se `Piloto__OperadorEmail`. A tela está em `/configurar-orcamento`.

Cadastre ao menos uma origem do cabelo e um método de aplicação. Os itens incluídos são somados automaticamente. Itens opcionais aparecem na etapa de escolhas. Para cobrança por unidade, a cliente informa a quantidade, ou o administrador configura a quantidade incluída quando o item é obrigatório. Itens sob consulta aparecem como observação e ficam fora do total. A opção de mostrar a prévia também é salva nesta tabela.

## Tati com MegaHairPro

Configure uma chave aleatória compartilhada apenas entre os servidores.

| Servidor | Variável | Valor |
| --- | --- | --- |
| API do simulador | `Orcamento__ChaveSincronizacao` | Chave privada compartilhada |
| MegaHairPro Web | `OrcamentoSimulador__Chave` | A mesma chave |
| MegaHairPro Web | `OrcamentoSimulador__ApiUrl` | Endereço HTTPS da API do simulador |

Publique a API do simulador antes de ativar o envio no MegaHairPro. O MegaHairPro envia a tabela no início da execução e novamente a cada dois minutos. O envio usa o custo médio de compra das origens cadastradas para o simulador, a margem atual de 70%, os métodos ativos, as despesas rateadas e a configuração de prévia do MegaHairPro. Coloração segue sob consulta. Uma origem sem compra com custo válido não entra na tabela. O estoque não é alterado.

A última tabela aceita fica disponível mesmo com o MegaHairPro desligado. A data em `/configurar-orcamento` indica quando os valores foram alterados, e não quando ocorreu a última tentativa de sincronização. O orçamento manual e o envio de orçamento comercial no MegaHairPro seguem a operação existente.

## Conferência

Para a Tati, compare uma origem, método e gramagem no cálculo de simulação correspondente do MegaHairPro e na prévia do simulador. Atualize uma compra, espere o envio e repita. Em seguida, verifique a mesma cotação com a sincronização temporariamente indisponível.

Para outro salão, deixe a chave de sincronização ausente e salve uma tabela própria. Confira um item fixo incluído, um item por grama, um opcional com mais de uma unidade e um serviço sob consulta. Compare a prévia e o resultado usando as mesmas escolhas. Cada instalação tem uma tabela. O cadastro de várias empresas compartilhando a mesma instância ainda exige isolamento por empresa.
