# MegaHair AI OneClick 2027

A instalação por salão continua usando uma instância da API, da Web e do PostgreSQL por salão. Cada instância mantém suas próprias mídias, chaves e credenciais. Consulte [instâncias por salão](instancias-por-salao.md) para a infraestrutura dessa base.

## Verificar uma instância de homologação

1. Verifique se o endereço Web público responde com HTTP 204 em /health/live. A API tem /health/ready, que responde com 204 após a inicialização e enquanto consegue se conectar ao banco.
2. Confirme que API e Web apontam para os containers e volumes do mesmo salão. Mantenha o banco indisponível pela Internet.
3. Gere uma simulação com uma foto sintética. Use as URLs assinadas retornadas pela API em MEGAHAIR_TEST_ORIGINAL e MEGAHAIR_TEST_RESULTADO, e a origem pública HTTPS em MEGAHAIR_TEST_WEB. Execute python scripts/testar-midia-web.py. Trate as URLs assinadas como credenciais temporárias.
4. Em outra rede, abra o catálogo de simulações e confira os resultados. Verifique também o comparador no atendimento e o envio para /tv. O teste automatizado confere a entrega pelo proxy. A conferência visual e de rede também é necessária.
5. Para testar a PWA, abra a origem HTTPS. No iPhone e iPad, use Compartilhar → Adicionar à Tela de Início no Safari. No Android, use Instalar aplicativo ou Adicionar à tela inicial no menu do navegador. Termine o atendimento antes de confirmar uma atualização na interface. A página de emergência offline é o único item no Cache Storage do service worker.

## Imagens Docker

Atribua MEGAHAIR_API_IMAGE e MEGAHAIR_WEB_IMAGE no arquivo de configuração do salão com as referências das imagens já publicadas em um registry autorizado. Preserve os valores atuais e faça um backup verificado do banco e das mídias antes da atualização. Em homologação, execute:

    docker compose --env-file salao.env -f compose.salao.yml pull api web
    docker compose --env-file salao.env -f compose.salao.yml up -d --no-build

Verifique as rotas de saúde e a entrega de mídia antes de aprovar a mudança. A API aplica migrações na inicialização. Reutilize imagens anteriores somente após confirmar a compatibilidade com o esquema aplicado. O fluxo de rollback e restauração precisa de homologação própria.

## Homologação física pendente

| Ambiente | Verificação |
| --- | --- |
| iPad e iPhone com Safari | PWA, câmera, orientação, arquivos HEIC/HEIF, comparador, reconexão |
| Tablet Android | PWA, câmera, galeria, pareamento, APK assinado quando disponível |
| Samsung Smart TV e LG Smart TV | Abrir /tv, controle remoto, pareamento e mídia |
| Android TV, Google TV e TV Box | Modo navegador, APK TV quando disponível, controle remoto e reconexão |
| Windows 10 e Windows 11 | Instalação, reinício, diagnóstico, backup, restauração e rollback |
| Wi-Fi e 4G/5G em redes distintas | Domínio HTTPS, sessão, TV e mídia assinada pelo endereço público |

O teste de CI usa arquivos sintéticos e HTTP no loopback da execução. Ele não substitui a prova contra uma instância HTTPS pública nem uma geração OpenAI ou Replicate com credenciais reais.
