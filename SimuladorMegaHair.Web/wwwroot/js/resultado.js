// ═══════════════════════════════════════════════════════════════════
//  RESULTADO — comparador antes/depois, zoom, tela cheia e salvamento.
//
//  O "antes" e o "depois" vivem no MESMO palco (.mh-comp-stage). O zoom e
//  o arraste transformam o palco inteiro, então as duas imagens sempre
//  usam o mesmo sistema de coordenadas e nunca se deslocam entre si.
//  O "depois" é recortado com clip-path (não é esticado nem movido).
// ═══════════════════════════════════════════════════════════════════
(function () {
    'use strict';

    const ESCALA_MAX = 5;
    const TOQUE_DUPLO_MS = 300;
    const RAIO_ALCA = 32; // px em volta da alça em que o toque "pega" o slider

    function limitar(v, min, max) { return Math.max(min, Math.min(max, v)); }

    function esperarImagem(img) {
        return new Promise(resolve => {
            if (img.complete && img.naturalWidth > 0) return resolve(true);
            img.addEventListener('load', () => resolve(true), { once: true });
            img.addEventListener('error', () => resolve(false), { once: true });
        });
    }

    const Resultado = {
        _ativo: null,

        /**
         * Liga o comparador dentro de #wrapId.
         * @returns {Promise<{ok:boolean, desalinhado?:boolean}>}
         */
        async iniciarComparador(wrapId) {
            this.destruirComparador();
            const wrap = document.getElementById(wrapId);
            if (!wrap) return { ok: false };
            const raiz = wrap.querySelector('.mh-comp');
            const palco = wrap.querySelector('.mh-comp-stage');
            const antes = wrap.querySelector('.mh-comp-antes');
            const depois = wrap.querySelector('.mh-comp-depois');
            const alca = wrap.querySelector('.mh-comp-handle');
            if (!raiz || !palco || !antes || !depois || !alca) return { ok: false };

            const [okA, okD] = await Promise.all([esperarImagem(antes), esperarImagem(depois)]);
            if (!okA || !okD) return { ok: false };

            // O quadro tem a proporção do "antes". Se o "depois" tiver outra proporção
            // (não deveria, a foto é enviada em 2:3), as duas imagens usam object-fit:contain
            // e NADA é esticado — apenas avisamos no console.
            const arA = antes.naturalWidth / antes.naturalHeight;
            const arD = depois.naturalWidth / depois.naturalHeight;
            wrap.style.setProperty('--ar', String(arA));
            const desalinhado = Math.abs(arA - arD) / arA > 0.02;
            wrap.classList.toggle('mh-comp-desalinhado', desalinhado);
            if (desalinhado) console.warn('[Resultado] Proporções diferentes: antes=' + arA.toFixed(3) + ' depois=' + arD.toFixed(3));

            const S = { pos: 0.5, escala: 1, tx: 0, ty: 0, W: 1, H: 1 };
            const ponteiros = new Map();
            let modo = null;            // 'slider' | 'pan' | 'pinch' | null
            let pinch = null;
            let ultimoToque = { t: 0, x: 0, y: 0 };
            const ouvintes = [];

            const medir = () => {
                const r = raiz.getBoundingClientRect();
                S.W = Math.max(1, r.width);
                S.H = Math.max(1, r.height);
            };

            const limitarTranslacao = () => {
                S.tx = limitar(S.tx, S.W - S.W * S.escala, 0);
                S.ty = limitar(S.ty, S.H - S.H * S.escala, 0);
            };

            const aplicar = () => {
                limitarTranslacao();
                palco.style.transform = 'translate(' + S.tx + 'px,' + S.ty + 'px) scale(' + S.escala + ')';
                depois.style.clipPath = 'inset(0 0 0 ' + (S.pos * 100) + '%)';
                const x = S.tx + S.pos * S.W * S.escala;
                alca.style.transform = 'translateX(' + x + 'px)';
                alca.style.visibility = (x < -2 || x > S.W + 2) ? 'hidden' : 'visible';
                alca.setAttribute('aria-valuenow', String(Math.round(S.pos * 100)));
                raiz.style.touchAction = S.escala > 1.001 ? 'none' : 'pan-y';
                raiz.classList.toggle('mh-zoom', S.escala > 1.001);
            };

            const zoomEm = (cx, cy, novaEscala) => {
                novaEscala = limitar(novaEscala, 1, ESCALA_MAX);
                const f = novaEscala / S.escala;
                S.tx = cx - (cx - S.tx) * f;
                S.ty = cy - (cy - S.ty) * f;
                S.escala = novaEscala;
                if (S.escala <= 1.001) { S.escala = 1; S.tx = 0; S.ty = 0; }
                aplicar();
            };

            const posDoX = x => limitar((x - S.tx) / (S.W * S.escala), 0, 1);
            const local = e => {
                const r = raiz.getBoundingClientRect();
                return { x: e.clientX - r.left, y: e.clientY - r.top };
            };
            const xAlca = () => S.tx + S.pos * S.W * S.escala;
            const ouvir = (alvo, tipo, fn, opts) => { alvo.addEventListener(tipo, fn, opts); ouvintes.push([alvo, tipo, fn, opts]); };

            ouvir(raiz, 'pointerdown', e => {
                if (e.target.closest('.mh-comp-tools')) return;
                try { raiz.setPointerCapture(e.pointerId); } catch (x) { /* ok */ }
                const p = local(e);
                ponteiros.set(e.pointerId, p);

                if (ponteiros.size === 2) {
                    const [a, b] = [...ponteiros.values()];
                    pinch = {
                        dist: Math.hypot(a.x - b.x, a.y - b.y) || 1,
                        escala: S.escala, tx: S.tx, ty: S.ty,
                        cx: (a.x + b.x) / 2, cy: (a.y + b.y) / 2
                    };
                    modo = 'pinch';
                    return;
                }

                // Toque duplo: alterna zoom 1× ↔ 2,5× no ponto tocado.
                const agora = performance.now();
                if (agora - ultimoToque.t < TOQUE_DUPLO_MS && Math.hypot(p.x - ultimoToque.x, p.y - ultimoToque.y) < 30) {
                    zoomEm(p.x, p.y, S.escala > 1.001 ? 1 : 2.5);
                    ultimoToque = { t: 0, x: 0, y: 0 };
                    modo = null;
                    return;
                }
                ultimoToque = { t: agora, x: p.x, y: p.y };

                if (Math.abs(p.x - xAlca()) < RAIO_ALCA) {
                    modo = 'slider';
                } else if (S.escala > 1.001) {
                    modo = 'pan';
                } else {
                    modo = 'slider';
                    S.pos = posDoX(p.x);
                    aplicar();
                }
            });

            ouvir(raiz, 'pointermove', e => {
                if (!ponteiros.has(e.pointerId)) return;
                const p = local(e);
                const anterior = ponteiros.get(e.pointerId);
                ponteiros.set(e.pointerId, p);

                if (modo === 'pinch' && ponteiros.size >= 2 && pinch) {
                    const [a, b] = [...ponteiros.values()];
                    const dist = Math.hypot(a.x - b.x, a.y - b.y) || 1;
                    const cx = (a.x + b.x) / 2, cy = (a.y + b.y) / 2;
                    const nova = limitar(pinch.escala * (dist / pinch.dist), 1, ESCALA_MAX);
                    const f = nova / pinch.escala;
                    S.escala = nova;
                    S.tx = pinch.cx - (pinch.cx - pinch.tx) * f + (cx - pinch.cx);
                    S.ty = pinch.cy - (pinch.cy - pinch.ty) * f + (cy - pinch.cy);
                    if (S.escala <= 1.001) { S.escala = 1; S.tx = 0; S.ty = 0; }
                    aplicar();
                } else if (modo === 'slider') {
                    S.pos = posDoX(p.x);
                    aplicar();
                } else if (modo === 'pan') {
                    S.tx += p.x - anterior.x;
                    S.ty += p.y - anterior.y;
                    aplicar();
                }
            });

            const soltar = e => {
                ponteiros.delete(e.pointerId);
                try { raiz.releasePointerCapture(e.pointerId); } catch (x) { /* ok */ }
                if (ponteiros.size < 2) pinch = null;
                if (ponteiros.size === 0) modo = null;
                else if (ponteiros.size === 1) modo = S.escala > 1.001 ? 'pan' : 'slider';
            };
            ouvir(raiz, 'pointerup', soltar);
            ouvir(raiz, 'pointercancel', soltar);

            // Roda do mouse / gesto de pinça do trackpad. A roda só captura o gesto com Ctrl,
            // em tela cheia ou já ampliado: fora isso a página continua rolando normalmente.
            ouvir(raiz, 'wheel', e => {
                const capturar = e.ctrlKey || S.escala > 1.001 || wrap.classList.contains('mh-comp-full');
                if (!capturar) return;
                e.preventDefault();
                const dy = e.deltaMode === 1 ? e.deltaY * 16 : e.deltaY;
                const p = local(e);
                zoomEm(p.x, p.y, S.escala * Math.exp(-dy * (e.ctrlKey ? 0.005 : 0.0015)));
            }, { passive: false });

            // Teclado: setas movem o slider.
            ouvir(alca, 'keydown', e => {
                const passo = e.shiftKey ? 0.1 : 0.02;
                if (e.key === 'ArrowLeft') { S.pos = limitar(S.pos - passo, 0, 1); aplicar(); e.preventDefault(); }
                else if (e.key === 'ArrowRight') { S.pos = limitar(S.pos + passo, 0, 1); aplicar(); e.preventDefault(); }
                else if (e.key === 'Home') { S.pos = 0; aplicar(); e.preventDefault(); }
                else if (e.key === 'End') { S.pos = 1; aplicar(); e.preventDefault(); }
            });

            // Botões de zoom / restaurar / tela cheia (data-acao).
            ouvir(wrap, 'click', e => {
                const b = e.target.closest('[data-acao]');
                if (!b || !wrap.contains(b)) return;
                const cx = S.W / 2, cy = S.H / 2;
                switch (b.dataset.acao) {
                    case 'zoom-mais': zoomEm(cx, cy, S.escala * 1.5); break;
                    case 'zoom-menos': zoomEm(cx, cy, S.escala / 1.5); break;
                    case 'restaurar': zoomEm(cx, cy, 1); S.pos = 0.5; aplicar(); break;
                    case 'tela-cheia': this.alternarTelaCheia(wrapId); break;
                }
            });

            ouvir(document, 'keydown', e => {
                if (e.key === 'Escape' && wrap.classList.contains('mh-comp-full')) this.alternarTelaCheia(wrapId, false);
            });

            const obs = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(() => { medir(); aplicar(); }) : null;
            if (obs) obs.observe(raiz);

            medir();
            aplicar();
            this._ativo = {
                wrapId: wrapId,
                estado: S,
                destruir: () => {
                    ouvintes.forEach(([alvo, tipo, fn, opts]) => alvo.removeEventListener(tipo, fn, opts));
                    if (obs) obs.disconnect();
                    wrap.classList.remove('mh-comp-full');
                    document.documentElement.classList.remove('mh-sem-rolagem');
                }
            };
            return { ok: true, desalinhado: desalinhado };
        },

        /** Estado atual (usado em testes e para o botão Restaurar). */
        estado() { return this._ativo ? Object.assign({}, this._ativo.estado) : null; },

        alternarTelaCheia(wrapId, forcar) {
            const wrap = document.getElementById(wrapId);
            if (!wrap) return;
            const ligar = typeof forcar === 'boolean' ? forcar : !wrap.classList.contains('mh-comp-full');
            wrap.classList.toggle('mh-comp-full', ligar);
            document.documentElement.classList.toggle('mh-sem-rolagem', ligar);
            // O ResizeObserver recalcula o tamanho; volta o zoom ao normal para não "perder" a imagem.
            const btn = wrap.querySelector('[data-acao="restaurar"]');
            if (btn) btn.click();
        },

        destruirComparador() {
            if (this._ativo) { this._ativo.destruir(); this._ativo = null; }
        },

        /**
         * Salva a imagem. No celular abre a folha de compartilhar (que tem
         * "Salvar imagem"); no computador baixa o arquivo.
         * @returns {Promise<{ok:boolean, metodo?:string, motivo?:string}>}
         */
        async salvar(url, nome) {
            let blob;
            try {
                const resp = await fetch(url, { mode: 'cors' });
                if (!resp.ok) return { ok: false, motivo: 'http' };
                blob = await resp.blob();
            } catch (e) {
                // Normalmente: servidor da imagem não liberou CORS para este site.
                return { ok: false, motivo: 'cors' };
            }

            const arquivo = new File([blob], nome, { type: blob.type || 'image/png' });
            const ehCelular = /Android|iPhone|iPad|iPod/i.test(navigator.userAgent);
            if (ehCelular && navigator.canShare && navigator.canShare({ files: [arquivo] })) {
                try {
                    await navigator.share({ files: [arquivo], title: 'Minha transformação — Tati Mega Hair' });
                    return { ok: true, metodo: 'compartilhar' };
                } catch (e) {
                    if (e && e.name === 'AbortError') return { ok: false, motivo: 'cancelado' };
                    // outros erros: cai para o download
                }
            }

            const a = document.createElement('a');
            const objUrl = URL.createObjectURL(blob);
            a.href = objUrl;
            a.download = nome;
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(() => URL.revokeObjectURL(objUrl), 5000);
            return { ok: true, metodo: 'download' };
        }
    };

    window.megaHairResultado = Resultado;
})();
