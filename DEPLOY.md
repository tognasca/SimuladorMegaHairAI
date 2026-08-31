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

### 1.2 Rodar a API

```powershell
cd SimuladorMegaHair.Api
dotnet run --launch-profile https
```

Isso agora escuta em `0.0.0.0` (todas as placas de rede), não só `localhost` — correção necessária para os tablets/TV/iPad conseguirem alcançar. Confirme testando de **outro** aparelho na mesma Wi-Fi, no navegador:

```
http://192.168.1.100:5185/api/catalogo
```

Se aparecer uma lista (ainda que vazia `[]`), a API está acessível pela rede. Se der "não é possível acessar este site", o Firewall do Windows está bloqueando — libere a porta 5185 (e 7064) no Firewall do Windows Defender para redes privadas.

### 1.3 🔴 Revogar o token do Replicate (pendência de segurança já identificada)

O `appsettings.json` da API tem uma chave de API real. Antes de colocar em produção:
1. Acesse [replicate.com/account/api-tokens](https://replicate.com/account/api-tokens)
2. Revogue o token atual
3. Gere um novo e substitua em `SimuladorMegaHair.Api/appsettings.json` → `Replicate:ApiToken`

---

## 2. Rodar o Web (Blazor) — para iPad, celular, Smart TV comum

### 2.1 Configurar o endereço da API

Edite `SimuladorMegaHair.Web/appsettings.json`:

```json
"Api": { "BaseUrl": "http://192.168.1.100:5185/" }
```

(troque pelo IP fixo que você reservou no passo 1.1)

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

### 3.1 Configurar o endereço do servidor em cada aparelho

Dentro do app → **Configurações** → campo **"Endereço do servidor"**:

```
http://192.168.1.100:5185/
```

Precisa **reabrir o app** depois de mudar (o app avisa isso na própria tela).

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
- [ ] Token do Replicate revogado e substituído
- [ ] `dotnet run` da API testado de **outro** aparelho na mesma rede (não só localhost)
- [ ] Certificado HTTPS confiado em cada iPad/tablet que vai acessar via navegador
- [ ] Endereço do servidor configurado em cada instalação do app MAUI
- [ ] TV testada em pé (retrato) e deitada (paisagem) — tela de simulação deve alternar sozinha
- [ ] Uma simulação completa de ponta a ponta testada em **cada** tipo de aparelho antes de abrir para clientes

---

## 5. Se algo não funcionar

| Sintoma | Causa mais provável |
|---|---|
| App/site abre mas "não carrega catálogo/clientes" | Endereço do servidor errado, ou API não está rodando |
| Câmera não abre no iPad/navegador | Site não está em HTTPS, ou certificado não foi aceito ainda |
| Cada aparelho mostra clientes diferentes | Endereços de servidor diferentes entre os aparelhos — revisar passo 3.1/2.1 |
| Simulação demora muito ou dá erro de rede | Verificar se o token do Replicate é válido e tem créditos |
