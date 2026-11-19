# Homologação e atualização do Simulador MegaHair

## Antes de atualizar

1. Identifique no servidor o arquivo Compose, o arquivo privado de ambiente, o nome do projeto e as portas que a instalação já usa. Para outro salão, consulte [Instância isolada por salão](instancias-por-salao.md). Confirme que cada salão usa seu próprio banco, volumes, JWT e credenciais. Mantenha a JWT atual durante a atualização para preservar sessões e assinaturas vigentes.
2. Com a mesma configuração usada pela instalação, consulte `docker compose ps` e `docker compose config --volumes`. Guarde em local privado os IDs das imagens em execução e aplique tags exclusivas de rollback com `docker image tag`. Registre a revisão atual do código e preserve os arquivos de configuração privados.
3. Faça um dump PostgreSQL em formato customizado com `pg_dump -Fc` e cópias dos volumes de uploads, resultados e chaves Web. Faça a cópia em janela sem novos uploads e restaure em ambiente descartável para conferir os dados e arquivos. No Windows, evite redirecionar o dump binário pelo PowerShell 5.1. Grave em um arquivo dentro do contêiner e extraia com `docker compose cp`.
4. Verifique o histórico de migrações aplicado no banco e compare ao artefato aprovado. A API roda migrações no início. Teste previamente qualquer migração pendente da instalação em cópia do banco. Esta correção de mídia não acrescenta migração.

## Atualização

Use os mesmos parâmetros de projeto, ambiente e Compose em todos os comandos. Execute a partir da revisão aprovada.

```powershell
docker compose config --quiet
docker compose build api web
docker compose up -d --no-deps api web
docker compose ps
docker compose logs --tail 100 api web
```

Confirme a inicialização da API e valide `/api/orcamento/opcoes` e a página do simulador. Confira se o Cloudflare Tunnel aponta para as portas locais corretas e se a origem Web continua acessando a API em `http://api:5185/`. No domínio público, confirme suporte ao WebSocket de `/_blazor` e cache desativado para `/midia-simulacao/*` e `/media/*`. Trate URLs assinadas como credenciais temporárias e restrinja sua coleta em logs.

## Critérios de aceite

- Faça upload de JPEG, PNG e WebP. Faça uma geração de homologação e confira rosto, tom de pele, roupa, fundo, topo da cabeça e estilo selecionado.
- Confira antes/depois no Safari do iPhone, Chrome do Android, tablet e navegadores desktop. Teste deslizamento, zoom, arraste, salvamento e compartilhamento. Repita em Wi-Fi e 4G/5G, incluindo perda e retorno da conexão com a tela aberta.
- Recarregue uma simulação recente após expirar o link, ainda dentro da janela de recuperação. Confira que a imagem reaparece sem nova cobrança e que o navegador carrega as fotos na origem Web. A renovação pública termina 26 horas após a criação da simulação. Para registros antigos, a equipe deve abrir o histórico autenticado para obter links novos.
- Simule acesso sem assinatura e acesso de outra instância. Confira também login, histórico e orçamento dos dois salões. Após reiniciar API e Web, abra o histórico e valide fotos já salvas.
- Revise acesso, retenção e exclusão de fotos e logs conforme o procedimento de proteção de dados do salão.

## Rollback

Em caso de falha, volte à revisão anterior e configure os serviços API e Web para usar as imagens tagueadas antes da atualização, por meio de um override Compose. Execute `docker compose up -d --no-deps --no-build api web` com os mesmos parâmetros de projeto e com o override. Preserve o PostgreSQL e os volumes atuais. Valide domínio, login e imagens existentes. Se houver suspeita de inconsistência de banco, restaure o backup primeiro em ambiente separado e planeje a recuperação dos registros criados após o backup.
