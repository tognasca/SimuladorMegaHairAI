# Guia de Deploy — Mega Hair AI no Salão

Este guia parte do zero: rede, servidor, e cada dispositivo (TV, tablets, iPad, celulares). Siga na ordem — cada etapa depende da anterior.

---

## 0. Visão geral da arquitetura

```
                        ┌─────────────────────────┐
                        │  SimuladorMegaHair.Api   │  ← o "cérebro": clientes,
                        │  (roda em 1 computador)  │    catálogo, IA, banco de dados
                        └────────────┬─────────────┘
                                     │ mesma rede Wi-Fi/cabo do salão
              ┌──────────────────────┼──────────────────────┐
              │                      │                      │
    ┌─────────▼────────┐   ┌─────────▼────────┐   ┌─────────▼────────┐
    │ App MAUI (Windows │   │ App MAUI (Android│   │ Navegador (Web)  │
    │ ou Android nativo)│   │ tablet/TV touch) │   │ iPad, celular,   │
    │                   │   │                  │   │ qualquer TV      │
    └───────────────────┘   └──────────────────┘   └──────────────────┘
```

**Regra de ouro:** só existe **UM** servidor (`SimuladorMegaHair.Api`) rodando em **UMA** máquina do salão. Todos os outros aparelhos são clientes dele. Se dois aparelhos apontarem para servidores diferentes, cada um vê uma base de clientes diferente — o problema que já identificamos e corrigimos na configuração, mas que só funciona se você seguir os passos abaixo.

---

## 1. Preparar o computador-servidor

Escolha **um** computador Windows que fique sempre ligado no salão (pode ser o mesmo que roda o app na TV, ou um separado).

### 1.1 Descobrir e fixar o IP local

```powershell
ipconfig
```

Anote o **IPv4** (ex: `192.168.1.100`). Depois, no roteador do salão, reserve esse IP para o computador (procure por "DHCP Reservation" ou "IP fixo" nas configurações do roteador) — assim ele nunca muda, mesmo depois de reiniciar o roteador.

### 1.2 Configurar a chave de API (obrigatória a partir da Fase 1)

A API agora exige uma chave secreta em todo pedido (cabeçalho `X-Api-Key`) — sem ela, ninguém acessa nada. Escolha uma chave longa e aleatória (ex.: gere uma com `openssl rand -base64 32` ou qualquer gerador de senhas) e **não a escreva em nenhum arquivo versionado**. Defina como variável de ambiente **no computador-servidor**, antes de rodar a API:

```powershell
$env:MEGAHAIR_API_KEY = "cole-aqui-uma-chave-longa-e-aleatoria"
```

Você vai usar essa **mesma chave** em três lugares: aqui (API), no site Web (passo 2.1) e em cada instalação do app MAUI (passo 3.1). Anote-a num cofre de senhas do salão — se for perdida, é só gerar outra e reconfigurar os três lugares.

### 1.3 Rodar a API em modo de produção

```powershell
cd SimuladorMegaHair.Api
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run --launch-profile https
```

`ASPNETCORE_ENVIRONMENT=Production` é importante: sem isso, o Swagger (painel que lista e permite chamar todos os endpoints) fica exposto para qualquer aparelho na rede. Isso agora escuta em `0.0.0.0` (todas as placas de rede), não só `localhost` — correção necessária para os tablets/TV/iPad conseguirem alcançar. Confirme testando de **outro** aparelho na mesma Wi-Fi, no navegador:

```
http://192.168.1.100:5185/api/catalogo
```

Se aparecer uma lista (ainda que vazia `[]`), a API está acessível pela rede. Se der "não é possível acessar este site", o Firewall do Windows está bloqueando — libere a porta 5185 (e 7064) no Firewall do Windows Defender para redes privadas.

### 1.4 🔴 Revogar os tokens do Replicate expostos anteriormente (pendência crítica)

Duas chaves de API do Replicate já foram commitadas neste repositório em algum momento e ficaram registradas no **histórico do Git**, mesmo tendo sido removidas depois do `appsettings.json` atual. Enquanto isso não for resolvido, qualquer pessoa com acesso ao repositório (ou a uma cópia dele) pode gastar o crédito da conta:

1. Acesse [replicate.com/account/api-tokens](https://replicate.com/account/api-tokens)
2. Revogue **todos** os tokens que já apareceram neste projeto (se você não tem certeza de quais são, revogue todos e gere um novo — é mais seguro que tentar adivinhar)
3. Gere um novo token
4. Configure-o como variável de ambiente `Replicate__ApiToken` (ou em `dotnet user-secrets`), **nunca** direto em `SimuladorMegaHair.Api/appsettings.json`:

```powershell
$env:Replicate__ApiToken = "cole-aqui-o-novo-token"
```

5. Veja `docs/SEGURANCA.md` para o passo de reescrever o histórico do Git e remover as chaves e as fotos de clientes que também foram commitadas por engano.

---

## 2. Rodar o Web (Blazor) — para iPad, celular, Smart TV comum

### 2.1 Configurar o endereço da API e a chave

Edite `SimuladorMegaHair.Web/appsettings.json`:

```json
"Api": { "BaseUrl": "http://192.168.1.100:5185/" }
```

(troque pelo IP fixo que você reservou no passo 1.1)

E defina a **mesma chave** do passo 1.2 como variável de ambiente, antes de rodar o Web (passo 2.3):

```powershell
$env:MEGAHAIR_API_KEY = "a-mesma-chave-configurada-na-api"
```

Essa chave fica só no servidor que roda o site (Blazor Server) — o navegador da cliente nunca a recebe.

### 2.2 Gerar e confiar o certificado HTTPS (obrigatório para a câmera funcionar)

No computador-servidor:

```powershell
dotnet dev-certs https --trust
```

Isso confia o certificado **nesse computador**. Só resolve o navegador local — os outros aparelhos (iPad, tablets) ainda vão ver aviso de segurança na primeira vez, porque eles não conhecem esse certificado. Duas opções:

- **Rápida (aceitável para uso interno):** no iPad/tablet, ao abrir o site pela primeira vez, toque em "Avançado" → "Continuar mesmo assim" (Safari) ou "Avançado" → "Prosseguir" (Chrome). Faz isso **uma vez por aparelho**; o navegador lembra depois.
- **Mais correta (sem aviso nenhum):** exportar o certificado gerado e instalá-lo como "confiável" no perfil de cada iPad (Ajustes → Geral → VPN e Gerenciamento de Dispositivo → instalar perfil `.cer`, depois Ajustes → Geral → Sobre → Configurações de Confiança de Certificado → ativar). Chame se quiser esse passo a passo mais detalhado depois.

### 2.3 Rodar o Web

```powershell
cd SimuladorMegaHair.Web
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run --launch-profile https
```

Acesse de qualquer aparelho na mesma rede:

```
https://192.168.1.100:7180
```

### 2.4 No iPad: virar "app" de verdade (sem App Store)

No Safari, abra o endereço acima → toque no ícone de compartilhar → **"Adicionar à Tela de Início"**. Isso cria um ícone com a logo do salão, abre em tela cheia (sem barra de endereço), como se fosse instalado — porque configuramos isso no `App.razor`.

---

## 3. Rodar o App MAUI (Windows ou Android)

### 3.1 Configurar o endereço do servidor e a chave em cada aparelho

Dentro do app → **Configurações**:

- **Endereço do servidor**: `http://192.168.1.100:5185/`
- **Chave de API**: a mesma chave configurada no passo 1.2

Precisa **reabrir o app** depois de mudar (o app avisa isso na própria tela). Sem a chave certa, todas as telas voltam erro 401.

### 3.2 Publicar o Android (gerar o `.apk`/`.aab`)

```powershell
cd SimuladorMegaHair.App
dotnet workload install android maui
dotnet publish -f net8.0-android -c Release
```

O instalador fica em `bin/Release/net8.0-android/publish/*.apk` — copie para o tablet/TV touch Android e instale diretamente (não precisa de Play Store para uso interno; ative "Instalar de fontes desconhecidas" no aparelho).

### 3.3 Publicar o Windows

```powershell
cd SimuladorMegaHair.App
dotnet publish -f net8.0-windows10.0.19041.0 -c Release
```

---

## 4. Checklist do dia da instalação no salão

- [ ] Computador-servidor com IP fixo reservado no roteador
- [ ] Chave de API gerada (`MEGAHAIR_API_KEY`) e configurada na API, no Web e em cada aparelho MAUI
- [ ] Tokens antigos do Replicate revogados em replicate.com/account/api-tokens e substituído por um novo
- [ ] `ASPNETCORE_ENVIRONMENT=Production` definido antes de rodar a API e o Web (senão o Swagger fica exposto)
- [ ] `dotnet run` da API testado de **outro** aparelho na mesma rede (não só localhost), incluindo um teste sem a chave certa (deve dar 401)
- [ ] Certificado HTTPS confiado em cada iPad/tablet que vai acessar via navegador
- [ ] Endereço do servidor e chave de API configurados em cada instalação do app MAUI
- [ ] TV testada em pé (retrato) e deitada (paisagem) — tela de simulação deve alternar sozinha
- [ ] Uma simulação completa de ponta a ponta testada em **cada** tipo de aparelho antes de abrir para clientes
- [ ] Ver `docs/SEGURANCA.md` — repositório tornado privado e histórico do Git tratado (fotos de clientes e tokens antigos)

---

## 5. Se algo não funcionar

| Sintoma | Causa mais provável |
|---|---|
| App/site abre mas "não carrega catálogo/clientes" | Endereço do servidor errado, API não está rodando, ou chave de API errada/ausente (veja o código de erro: 401 = chave errada) |
| Erro 401 em qualquer tela | `MEGAHAIR_API_KEY` não configurada, ou diferente entre API/Web/app |
| Erro 429 ao gerar simulação | Limite de gerações simultâneas ou cota diária atingidos (`Simulacao:MaxGeracoesSimultaneas` / `LimiteDiarioGlobal` em appsettings) |
| Câmera não abre no iPad/navegador | Site não está em HTTPS, ou certificado não foi aceito ainda |
| Cada aparelho mostra clientes diferentes | Endereços de servidor diferentes entre os aparelhos — revisar passo 3.1/2.1 |
| Simulação demora muito ou dá erro de rede | Verificar se o token do Replicate é válido e tem créditos |
