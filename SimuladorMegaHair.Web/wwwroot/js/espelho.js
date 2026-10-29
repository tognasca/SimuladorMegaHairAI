// ═══════════════════════════════════════════════════════════════════
//  ESPELHO VIRTUAL — captura de foto com pré-visualização em tempo real.
//
//  Princípios (ver também Captura.razor):
//   1. A foto que vai para a IA é SEMPRE a foto natural, sem efeitos.
//      Os filtros existem só para a cliente se ver bonita no espelho e
//      na confirmação; eles nunca alteram a cor real do cabelo enviada.
//   2. O enquadramento é fixo em retrato 2:3. É o formato 1024x1536 que
//      a OpenAI devolve; enviando a foto já em 2:3, "antes" e "depois"
//      ficam com o mesmo enquadramento e o comparador alinha.
//   3. Filtros são calculados por pixel em <canvas>, de forma idêntica
//      na pré-visualização e na captura. Não usamos ctx.filter (o Safari
//      não suporta de forma confiável) nem WebGL (frágil em aparelhos
//      antigos).
//   4. Nada aqui altera forma de rosto, olhos, nariz ou boca. Os filtros
//      só mexem em luz, contraste, saturação, calor e um leve suavizado
//      aplicado apenas em tons de pele.
// ═══════════════════════════════════════════════════════════════════
(function () {
    'use strict';

    const ASPECTO = 2 / 3;          // largura / altura do enquadramento
    const SAIDA_MAX_ALTURA = 1536;  // = altura do formato 1024x1536 da OpenAI
    // Níveis de resolução da pré-visualização. Começa no melhor e desce sozinho
    // se o aparelho não conseguir manter a fluidez (aparelhos simples).
    const NIVEIS = [[480, 720], [400, 600], [320, 480], [240, 360]];
    const JANELA_QUADROS = 12;
    const LIMITE_MS_QUADRO = 38;
    const MINI_W = 56;
    const MINI_H = 84;
    const QUALIDADE_JPEG = 0.92;
    const FPS_MAX = 30;

    // Parâmetros de cada efeito, em intensidade 1.0. A intensidade escolhida
    // (0–1) multiplica todos eles.
    //   brilho/contraste/saturacao: -1..1   calor: -1..1 (+ = mais quente)
    //   desbotar: levanta os pretos (aspecto "suave")
    //   suavizar: mistura com versão desfocada, só em tons de pele
    const FILTROS = {
        natural:  { nome: 'Natural' },
        soft:     { nome: 'Soft Beauty',   brilho: 0.03, contraste: -0.04, saturacao: 0.00,  calor: 0.00,  desbotar: 0.00, suavizar: 0.60 },
        studio:   { nome: 'Studio Light',  brilho: 0.06, contraste: 0.14,  saturacao: 0.04,  calor: -0.01, desbotar: 0.00, suavizar: 0.10 },
        golden:   { nome: 'Golden Glow',   brilho: 0.03, contraste: 0.04,  saturacao: 0.08,  calor: 0.12,  desbotar: 0.02, suavizar: 0.10 },
        clean:    { nome: 'Clean Beauty',  brilho: 0.04, contraste: 0.10,  saturacao: -0.02, calor: 0.00,  desbotar: 0.00, suavizar: 0.20 },
        portrait: { nome: 'Soft Portrait', brilho: 0.02, contraste: -0.06, saturacao: -0.04, calor: 0.03,  desbotar: 0.05, suavizar: 0.35 }
    };

    // ─── Pipeline de pixels ──────────────────────────────────────────

    function criarLut(p, k) {
        const lut = new Uint8ClampedArray(256);
        const brilho = p.brilho * k * 255;
        const contraste = 1 + p.contraste * k;
        const f = (p.desbotar || 0) * k;
        for (let i = 0; i < 256; i++) {
            let v = (i - 128) * contraste + 128 + brilho;
            v = v * (1 - f) + 255 * 0.18 * f;
            lut[i] = v;
        }
        return lut;
    }

    // Regra clássica de tom de pele em RGB (Peer et al.). Só usada para
    // limitar o suavizado: olhos, cílios, cabelo e fundo ficam intactos.
    function ehPele(r, g, b) {
        return r > 95 && g > 40 && b > 20 &&
            (Math.max(r, g, b) - Math.min(r, g, b)) > 15 &&
            Math.abs(r - g) > 15 && r > g && r > b;
    }

    /**
     * Aplica o filtro em um ImageData (in place).
     * @param {ImageData} img
     * @param {object} p parâmetros do filtro
     * @param {number} k intensidade 0..1
     * @param {ImageData|null} desfocada versão desfocada do mesmo quadro (para suavizar)
     */
    function aplicarFiltro(img, p, k, desfocada) {
        const d = img.data;
        const lut = criarLut(p, k);
        const sat = 1 + p.saturacao * k;
        const calor = p.calor * k * 40;
        const forcaSuave = Math.min(0.5, (p.suavizar || 0) * k * 0.5);
        const usarSuave = desfocada && forcaSuave > 0.01;
        const bl = usarSuave ? desfocada.data : null;

        for (let i = 0; i < d.length; i += 4) {
            let r = d[i], g = d[i + 1], b = d[i + 2];

            // Mesma regra de ehPele(), inline: evita uma chamada de função por pixel.
            if (usarSuave && r > 95 && g > 40 && b > 20 && r > g + 15 && r > b && (r - (g < b ? g : b)) > 15) {
                r += (bl[i] - r) * forcaSuave;
                g += (bl[i + 1] - g) * forcaSuave;
                b += (bl[i + 2] - b) * forcaSuave;
            }

            r = lut[r | 0] + calor;
            g = lut[g | 0];
            b = lut[b | 0] - calor;

            if (sat !== 1) {
                const y = 0.299 * r + 0.587 * g + 0.114 * b;
                r = y + (r - y) * sat;
                g = y + (g - y) * sat;
                b = y + (b - y) * sat;
            }

            d[i] = r; d[i + 1] = g; d[i + 2] = b; // Uint8ClampedArray satura em 0..255
        }
    }

    // Cria uma cópia desfocada de um canvas usando redução + ampliação
    // (barato e funciona em qualquer navegador).
    function desfocarCanvas(origem, tmp, destino, fator) {
        const w = origem.width, h = origem.height;
        const sw = Math.max(8, Math.round(w / fator)), sh = Math.max(8, Math.round(h / fator));
        tmp.width = sw; tmp.height = sh;
        const t = tmp.getContext('2d');
        t.imageSmoothingQuality = 'high';
        t.drawImage(origem, 0, 0, sw, sh);
        destino.width = w; destino.height = h;
        const c = destino.getContext('2d', { willReadFrequently: true });
        c.imageSmoothingEnabled = true;
        c.imageSmoothingQuality = 'high';
        c.drawImage(tmp, 0, 0, w, h);
        return c.getImageData(0, 0, w, h);
    }

    // Retângulo de recorte 2:3 (cobrir) dentro de uma fonte de largura sw e altura sh.
    function recorte23(sw, sh) {
        const a = sw / sh;
        if (a > ASPECTO) {
            const w = sh * ASPECTO;
            return { x: (sw - w) / 2, y: 0, w: w, h: sh };
        }
        const h = sw / ASPECTO;
        return { x: 0, y: (sh - h) / 2, w: sw, h: h };
    }

    function paraBlob(canvas, qualidade) {
        return new Promise((resolve, reject) => {
            canvas.toBlob(b => b ? resolve(b) : reject(new Error('toBlob falhou')), 'image/jpeg', qualidade);
        });
    }

    function mapearErro(e) {
        const n = e && e.name;
        if (n === 'NotAllowedError' || n === 'PermissionDeniedError') return 'permissao';
        if (n === 'NotFoundError' || n === 'DevicesNotFoundError' || n === 'OverconstrainedError') return 'sem-camera';
        if (n === 'NotReadableError' || n === 'TrackStartError' || n === 'AbortError') return 'em-uso';
        if (n === 'SecurityError') return 'inseguro';
        return 'erro';
    }

    // ─── Módulo público ──────────────────────────────────────────────

    const Espelho = {
        _stream: null,
        _video: null,
        _canvas: null,
        _ctx: null,
        _tmp: document.createElement('canvas'),
        _blurCanvas: document.createElement('canvas'),
        _raf: 0,
        _ultimoQuadro: 0,
        _rodando: false,
        _nivel: 0,
        _tempos: [],
        _miniBase: document.createElement('canvas'),
        _filtro: 'natural',
        _k: 0.6,
        _espelhar: false,
        _timerMiniaturas: 0,
        _urlNatural: null,
        _urlFiltrada: null,
        _blobNatural: null,
        _args: null,
        _retomar: false,

        suportaCamera() {
            return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
        },

        listarFiltros() {
            return Object.keys(FILTROS).map(id => ({ id: id, nome: FILTROS[id].nome }));
        },

        /**
         * Abre a câmera e começa a desenhar a pré-visualização no canvas.
         * @returns {Promise<{ok:boolean, codigo?:string, multiplas?:boolean, frontal?:boolean}>}
         */
        async iniciar(canvasId, videoId, frontal) {
            this._args = [canvasId, videoId, frontal];
            this._retomar = false;
            this._pararStream();
            this._canvas = document.getElementById(canvasId);
            this._video = document.getElementById(videoId);
            if (!this._canvas || !this._video) return { ok: false, codigo: 'erro' };
            if (!this.suportaCamera()) {
                return { ok: false, codigo: window.isSecureContext === false ? 'inseguro' : 'sem-suporte' };
            }

            const facing = frontal ? 'user' : 'environment';
            const tentativas = [
                { video: { facingMode: { ideal: facing }, width: { ideal: 1080 }, height: { ideal: 1920 } }, audio: false },
                { video: { facingMode: { ideal: facing } }, audio: false },
                { video: true, audio: false }
            ];

            let stream = null, ultimoErro = null;
            for (const c of tentativas) {
                try { stream = await navigator.mediaDevices.getUserMedia(c); break; }
                catch (e) {
                    ultimoErro = e;
                    // Permissão negada não melhora tentando de novo com outras restrições.
                    if (mapearErro(e) === 'permissao' || mapearErro(e) === 'inseguro') break;
                }
            }
            if (!stream) return { ok: false, codigo: mapearErro(ultimoErro) };

            this._stream = stream;
            const v = this._video;
            v.srcObject = stream;
            v.muted = true;
            v.setAttribute('playsinline', 'true');
            try { await v.play(); } catch (e) { this._pararStream(); return { ok: false, codigo: 'erro' }; }

            const limite = Date.now() + 3000;
            while (v.videoWidth === 0 && Date.now() < limite) await new Promise(r => setTimeout(r, 50));
            if (v.videoWidth === 0) { this._pararStream(); return { ok: false, codigo: 'erro' }; }

            const trilha = stream.getVideoTracks()[0];
            const ajustes = trilha && trilha.getSettings ? trilha.getSettings() : {};
            // Selfie é exibida espelhada (como um espelho de verdade); a foto salva NÃO é espelhada.
            this._espelhar = frontal && ajustes.facingMode !== 'environment';
            this._nivel = 0;
            this._tempos = [];
            this._canvas.width = NIVEIS[0][0];
            this._canvas.height = NIVEIS[0][1];
            this._canvas.classList.toggle('mh-espelhado', this._espelhar);
            this._ctx = this._canvas.getContext('2d', { willReadFrequently: true });

            let multiplas = false;
            try {
                const dispositivos = await navigator.mediaDevices.enumerateDevices();
                multiplas = dispositivos.filter(d => d.kind === 'videoinput').length > 1;
            } catch (e) { /* sem lista: assume uma câmera */ }

            this._rodando = true;
            this._ultimoQuadro = 0;
            this._laco();
            clearInterval(this._timerMiniaturas);
            this._timerMiniaturas = setInterval(() => this._desenharMiniaturas(), 400);
            return { ok: true, multiplas: multiplas, frontal: this._espelhar };
        },

        /** Liga os controles (filtros, intensidade, remover efeitos). Idempotente. */
        ligarControles(raizId) {
            const raiz = document.getElementById(raizId);
            if (!raiz) return;
            if (!raiz.dataset.mhLigado) {
                raiz.dataset.mhLigado = '1';
                raiz.addEventListener('click', ev => {
                    const alvo = ev.target.closest('[data-filtro]');
                    if (alvo && raiz.contains(alvo)) this.definirFiltro(alvo.dataset.filtro);
                    if (ev.target.closest('[data-remover-efeitos]')) this.definirFiltro('natural');
                });
                const slider = raiz.querySelector('input[type=range][data-intensidade]');
                if (slider) {
                    slider.addEventListener('input', () => this.definirIntensidade(Number(slider.value) / 100));
                }
            }
            const slider = raiz.querySelector('input[type=range][data-intensidade]');
            if (slider) slider.value = String(Math.round(this._k * 100));
            this._atualizarUiFiltros(raiz);
        },

        definirFiltro(id) {
            if (!FILTROS[id]) return;
            this._filtro = id;
            this._atualizarUiFiltros(document);
            this._desenharMiniaturas();
        },

        definirIntensidade(k) {
            this._k = Math.max(0, Math.min(1, k));
            this._desenharMiniaturas();
        },

        filtroAtual() { return this._filtro; },

        _atualizarUiFiltros(raiz) {
            const escopo = raiz || document;
            escopo.querySelectorAll('[data-filtro]').forEach(el => {
                el.classList.toggle('mh-ativo', el.dataset.filtro === this._filtro);
                el.setAttribute('aria-pressed', el.dataset.filtro === this._filtro ? 'true' : 'false');
            });
            document.querySelectorAll('.mh-intensidade').forEach(el =>
                el.classList.toggle('mh-sem-efeito', this._filtro === 'natural'));
            document.querySelectorAll('[data-remover-efeitos]').forEach(el =>
                el.classList.toggle('mh-oculto', this._filtro === 'natural'));
        },

        _laco() {
            if (!this._rodando) return;
            this._raf = requestAnimationFrame(() => this._laco());
            const agora = performance.now();
            if (agora - this._ultimoQuadro < 1000 / FPS_MAX) return;
            this._ultimoQuadro = agora;
            const v = this._video;
            if (!v || v.readyState < 2 || v.videoWidth === 0) return;
            const t0 = performance.now();
            this._desenharQuadro(v, this._ctx, this._canvas, this._filtro, this._k);
            this._ajustarQualidade(performance.now() - t0);
        },

        // Se o custo médio por quadro passar do limite, reduz a resolução da pré-visualização
        // (o canvas continua ocupando o mesmo espaço na tela; só fica menos detalhado).
        _ajustarQualidade(ms) {
            this._tempos.push(ms);
            if (this._tempos.length < JANELA_QUADROS) return;
            const media = this._tempos.reduce((a, b) => a + b, 0) / this._tempos.length;
            this._tempos = [];
            if (media > LIMITE_MS_QUADRO && this._nivel < NIVEIS.length - 1) {
                this._nivel++;
                this._canvas.width = NIVEIS[this._nivel][0];
                this._canvas.height = NIVEIS[this._nivel][1];
                console.info('[Espelho] Qualidade da pré-visualização reduzida para ' + NIVEIS[this._nivel].join('x') + ' (' + Math.round(media) + ' ms/quadro).');
            }
        },

        // Desenha o quadro do vídeo, já recortado em 2:3, aplicando o filtro.
        _desenharQuadro(fonte, ctx, canvas, filtroId, k) {
            const sw = fonte.videoWidth || fonte.width;
            const sh = fonte.videoHeight || fonte.height;
            const r = recorte23(sw, sh);
            ctx.drawImage(fonte, r.x, r.y, r.w, r.h, 0, 0, canvas.width, canvas.height);
            this._filtrarCanvas(canvas, ctx, filtroId, k);
        },

        _filtrarCanvas(canvas, ctx, filtroId, k) {
            const p = FILTROS[filtroId];
            if (!p || filtroId === 'natural' || k <= 0.001) return;
            const img = ctx.getImageData(0, 0, canvas.width, canvas.height);
            let desfocada = null;
            if ((p.suavizar || 0) * k > 0.02) {
                desfocada = desfocarCanvas(canvas, this._tmp, this._blurCanvas, 5);
            }
            aplicarFiltro(img, p, k, desfocada);
            ctx.putImageData(img, 0, 0);
        },

        _desenharMiniaturas() {
            const v = this._video;
            if (!v || v.readyState < 2 || v.videoWidth === 0) return;
            const r = recorte23(v.videoWidth, v.videoHeight);
            const base = this._miniBase;
            if (base.width !== MINI_W * 2) { base.width = MINI_W * 2; base.height = MINI_H * 2; }
            base.getContext('2d').drawImage(v, r.x, r.y, r.w, r.h, 0, 0, base.width, base.height);
            document.querySelectorAll('canvas[data-miniatura]').forEach(c => {
                if (c.width !== MINI_W) { c.width = MINI_W; c.height = MINI_H; }
                const ctx = c.getContext('2d', { willReadFrequently: true });
                ctx.drawImage(base, 0, 0, base.width, base.height, 0, 0, MINI_W, MINI_H);
                // Miniaturas mostram o efeito na intensidade escolhida (o que se vê é o que se obtém).
                this._filtrarCanvas(c, ctx, c.dataset.miniatura, this._k);
            });
        },

        /**
         * Captura a foto. Guarda a versão NATURAL (vai para a IA) e,
         * se houver filtro, uma versão com efeito (só para exibir).
         */
        async capturar() {
            const v = this._video;
            if (!v || v.videoWidth === 0) return { ok: false, codigo: 'erro' };
            // Pausa o laço da pré-visualização: a captura em resolução total usa a thread inteira.
            const estavaRodando = this._rodando;
            this._rodando = false;
            cancelAnimationFrame(this._raf);
            try {
                const r = recorte23(v.videoWidth, v.videoHeight);
                const escala = Math.min(1, SAIDA_MAX_ALTURA / r.h);
                const w = Math.round(r.w * escala), h = Math.round(r.h * escala);

                const natural = document.createElement('canvas');
                natural.width = w; natural.height = h;
                const nctx = natural.getContext('2d', { willReadFrequently: true });
                nctx.drawImage(v, r.x, r.y, r.w, r.h, 0, 0, w, h);
                const blobNatural = await paraBlob(natural, QUALIDADE_JPEG);

                let blobFiltrada = null;
                const filtro = this._filtro;
                if (filtro !== 'natural' && this._k > 0.001) {
                    this._filtrarCanvas(natural, nctx, filtro, this._k); // altera o canvas; o blob natural já foi gerado
                    blobFiltrada = await paraBlob(natural, 0.9);
                }

                this._guardarFoto(blobNatural, blobFiltrada);
                return {
                    ok: true, largura: w, altura: h,
                    urlNatural: this._urlNatural, urlFiltrada: this._urlFiltrada,
                    filtro: filtro, filtroNome: FILTROS[filtro].nome, origem: 'camera', ajustada: false
                };
            } catch (e) {
                console.error('[Espelho] Falha ao capturar:', e);
                if (estavaRodando && this._stream) { this._rodando = true; this._laco(); } // volta a mostrar a câmera
                return { ok: false, codigo: 'erro' };
            }
        },

        /** Efeito visual + vibração ao tirar a foto. */
        feedbackCaptura(palcoId) {
            const palco = document.getElementById(palcoId);
            if (palco) {
                palco.classList.remove('mh-flash');
                void palco.offsetWidth; // reinicia a animação
                palco.classList.add('mh-flash');
                setTimeout(() => palco.classList.remove('mh-flash'), 450);
            }
            try { if (navigator.vibrate) navigator.vibrate(25); } catch (e) { /* sem vibração */ }
        },

        /** Liga o <input type=file> da galeria. Ao escolher, processa e chama o .NET. */
        registrarArquivo(inputId, dotnetRef) {
            const inp = document.getElementById(inputId);
            if (!inp) return;
            if (inp._mhRef !== undefined) { inp._mhRef = dotnetRef; return; }
            inp._mhRef = dotnetRef;
            inp.addEventListener('change', async () => {
                const arquivo = inp.files && inp.files[0];
                inp.value = ''; // permite escolher a mesma foto de novo
                if (!arquivo) return;
                const resultado = await this.processarArquivo(arquivo);
                try { await inp._mhRef.invokeMethodAsync('AoProcessarArquivo', resultado); }
                catch (e) { console.warn('[Espelho] Componente já foi descartado.', e); }
            });
        },

        /**
         * Normaliza uma foto da galeria: aplica a rotação do EXIF (o <img>
         * já a aplica), reduz e enquadra em 2:3, e salva como JPEG.
         */
        async processarArquivo(arquivo) {
            if (!arquivo.type || !/^image\/(jpeg|png|webp|heic|heif)$/i.test(arquivo.type)) {
                return { ok: false, codigo: 'formato' };
            }
            if (arquivo.size > 25 * 1024 * 1024) return { ok: false, codigo: 'grande' };

            const url = URL.createObjectURL(arquivo);
            try {
                const img = new Image();
                img.src = url;
                try { await img.decode(); }
                catch (e) { return { ok: false, codigo: 'formato' }; }

                const sw = img.naturalWidth, sh = img.naturalHeight;
                if (!sw || !sh) return { ok: false, codigo: 'formato' };
                const r = recorte23(sw, sh);
                const perda = 1 - (r.w * r.h) / (sw * sh);
                const escala = Math.min(1, SAIDA_MAX_ALTURA / r.h);
                const w = Math.round(r.w * escala), h = Math.round(r.h * escala);

                const c = document.createElement('canvas');
                c.width = w; c.height = h;
                const ctx = c.getContext('2d');
                ctx.imageSmoothingQuality = 'high';
                ctx.drawImage(img, r.x, r.y, r.w, r.h, 0, 0, w, h);
                const blob = await paraBlob(c, QUALIDADE_JPEG);

                this._guardarFoto(blob, null);
                return {
                    ok: true, largura: w, altura: h,
                    urlNatural: this._urlNatural, urlFiltrada: null,
                    filtro: 'natural', filtroNome: FILTROS.natural.nome,
                    origem: 'galeria', ajustada: perda > 0.03
                };
            } catch (e) {
                console.error('[Espelho] Falha ao processar arquivo:', e);
                return { ok: false, codigo: 'erro' };
            } finally {
                URL.revokeObjectURL(url);
            }
        },

        _guardarFoto(blobNatural, blobFiltrada) {
            this._liberarUrls();
            this._blobNatural = blobNatural;
            this._urlNatural = URL.createObjectURL(blobNatural);
            this._urlFiltrada = blobFiltrada ? URL.createObjectURL(blobFiltrada) : null;
        },

        /** Bytes da foto natural (JPEG) para enviar à API. */
        async obterBytes() {
            if (!this._blobNatural) return null;
            return new Uint8Array(await this._blobNatural.arrayBuffer());
        },

        temFoto() { return !!this._blobNatural; },

        _liberarUrls() {
            if (this._urlNatural) URL.revokeObjectURL(this._urlNatural);
            if (this._urlFiltrada) URL.revokeObjectURL(this._urlFiltrada);
            this._urlNatural = null; this._urlFiltrada = null; this._blobNatural = null;
        },

        _pararStream() {
            this._rodando = false;
            cancelAnimationFrame(this._raf);
            clearInterval(this._timerMiniaturas);
            if (this._stream) {
                this._stream.getTracks().forEach(t => t.stop());
                this._stream = null;
            }
            if (this._video) this._video.srcObject = null;
        },

        /** Fecha a câmera (apaga a luz do aparelho). Mantém a foto já capturada. */
        parar() { this._pararStream(); },

        /** Fecha a câmera e descarta a foto. */
        liberar() { this._pararStream(); this._liberarUrls(); },

        // Exposto para testes automatizados.
        _teste: { aplicarFiltro: aplicarFiltro, recorte23: recorte23, FILTROS: FILTROS, ehPele: ehPele }
    };

    // Se a aba ficar oculta, solta a câmera (economiza bateria e apaga a luz).
    // Ao voltar para a aba, reabre a câmera sozinha (evita a "tela preta").
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
            if (Espelho._rodando) { Espelho._retomar = true; Espelho._pararStream(); }
        } else if (Espelho._retomar && Espelho._args) {
            Espelho._retomar = false;
            Espelho.iniciar(...Espelho._args);
        }
    });

    window.megaHairEspelho = Espelho;
})();
