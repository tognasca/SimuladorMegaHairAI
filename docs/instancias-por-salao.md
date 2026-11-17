# Instância isolada por salão

O Compose principal continua atendendo a Tati. O arquivo `compose.salao.yml` sobe outro salão com URL, administrador, banco, fotos, resultados, créditos e sessões de TV próprios. A tabela de orçamento é cadastrada no Simulador.

## Ativar um salão

1. Copie `deploy/salao.env.example` para um arquivo privado fora do repositório.
2. Defina um identificador exclusivo com letras minúsculas, números e hífens, duas portas locais livres, dois endereços HTTPS, nome e WhatsApp do salão. Gere valores exclusivos para `POSTGRES_PASSWORD`, `JWT_KEY` e `SALAO_SETUP_KEY`; prefira valores aleatórios em hexadecimal. Configure as credenciais do provedor de imagens.
3. Na raiz do repositório, rode `docker compose --env-file CAMINHO_DO_ENV -f compose.salao.yml up -d --build`.
4. No Windows com PowerShell 5.1, rode o bloco abaixo para criar o administrador antes de abrir o túnel. Use os valores configurados no arquivo privado. O bloco não imprime o token de login.

```powershell
$porta = Read-Host "Porta local da API do salão"
$email = Read-Host "E-mail do administrador"
$chave = [Net.NetworkCredential]::new('', (Read-Host "Chave inicial do salão" -AsSecureString)).Password
$senha = [Net.NetworkCredential]::new('', (Read-Host "Senha do administrador" -AsSecureString)).Password
try {
    $corpo = @{ email = $email; senha = $senha } | ConvertTo-Json -Compress
    Invoke-RestMethod "http://127.0.0.1:$porta/api/auth/setup-inicial" -Method Post `
        -Headers @{ 'X-Cadastro-Salao' = $chave } -ContentType 'application/json' -Body $corpo | Out-Null
} finally {
    Remove-Variable senha, chave, corpo -ErrorAction SilentlyContinue
}
```

5. No Cloudflare Tunnel, aponte a URL do Simulador para a porta Web local e a da API para a porta da API local.
6. Entre pelo link do salão, abra **Configurar orçamento** e cadastre a tabela. Se houver cobrança, use apenas a credencial Mercado Pago da operação central e habilite os créditos na instância.

## Homologação

Suba dois salões com diferentes identificadores, portas, e-mails e segredos. Configure preços diferentes para a mesma origem e confira os valores em cada link. Valide os logins em ambas as URLs, gere uma foto em cada salão e confirme que o link de mídia e o código da TV são aceitos somente na instância que os gerou. Confira a configuração e os dados da Tati no endereço atual antes e depois do teste.

Este procedimento provisiona instâncias independentes. O cadastro centralizado e o provisionamento pela interface administrativa exigem implementação própria.
