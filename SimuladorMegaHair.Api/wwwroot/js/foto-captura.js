// foto-captura.js — controlador de distância e enquadramento da foto (ES module, sem dependências).
// A detecção do rosto é feita pelo servidor (POST /api/foto/avaliar), com o MESMO detector do pipeline.
// As constantes abaixo espelham as regras do servidor (SimulacaoPipelineService): cabeça adulta ≈ 23 cm,
// altura da cabeça ≈ 1,45 × altura da caixa do rosto, rosto mínimo = 11% da altura da foto.

export const K = {
  CABECA_CM: 23,        // altura da cabeça (topo do cabelo → queixo)
  FATOR_CABECA: 1.45,   // altura da cabeça = altura da caixa do rosto × fator
  MAO_CM: 12,           // margem: a foto deve mostrar ~1 palmo abaixo do fim do cabelo
  MARGEM_MIN_CM: 4,     // abaixo disso a foto corta o cabelo
  ACIMA_MIN: 0.12,      // espaço mínimo acima da cabeça (× altura da cabeça)
  ACIMA_MAX: 0.50,
  ACIMA_ALVO: 0.20,
  CENTRO_TOL: 0.08,     // tolerância de centralização (fração da largura)
  FACE_MIN: 0.11,       // rosto mínimo (fração da altura da foto) — igual ao servidor
  LUZ_MIN: 55           // luminosidade média mínima (0..255)
};

/** Rosto ideal (fração da altura do quadro) para o comprimento pedido. */
export function rostoAlvo(cm) {
  const total = cm + K.MAO_CM + K.ACIMA_ALVO * K.CABECA_CM;   // cm de altura do quadro
  return K.CABECA_CM / (K.FATOR_CABECA * total);
}

/**
 * face: {x,y,w,h} normalizados (0..1) ou null. cm: comprimento escolhido. luz: média 0..255 (opcional).
 * Retorna { ok, problemas:[{id,msg}], spanCm, precisaCm, acima, centro, faceH }.
 */
export function avaliarEnquadramento({ face, cm, luz = null }) {
  const precisaCm = cm + K.MAO_CM;
  const r = { ok: false, problemas: [], spanCm: null, precisaCm, acima: null, centro: null, faceH: null };

  if (!face) {
    r.problemas.push({ id: 'rosto', msg: 'Não encontrei o rosto. Fique de frente para a câmera.' });
    return r;
  }

  const headH = face.h * K.FATOR_CABECA;          // fração da altura do quadro
  const queixo = face.y + face.h;
  const topo = queixo - headH;                    // pode ser negativo (cabeça cortada)
  const fracPorCm = headH / K.CABECA_CM;
  r.faceH = face.h;
  r.spanCm = (1 - topo) / fracPorCm;              // cm do topo da cabeça até a borda inferior da foto
  r.acima = topo / headH;
  r.centro = face.x + face.w / 2;

  const longeDemais = face.h < Math.max(K.FACE_MIN * 1.2, 0.5 * rostoAlvo(cm));

  if (r.spanCm < cm + K.MARGEM_MIN_CM) {
    r.problemas.push({ id: 'distancia',
      msg: `Afaste-se: a foto mostra só ~${Math.round(r.spanCm)} cm abaixo da cabeça e o cabelo de ${cm} cm precisa de ~${precisaCm} cm.` });
  } else if (longeDemais) {
    r.problemas.push({ id: 'distancia', msg: 'Aproxime-se um pouco: o rosto está pequeno na foto.' });
  }

  if (r.acima < K.ACIMA_MIN) {
    r.problemas.push({ id: 'topo', msg: 'Falta espaço acima da cabeça: incline o celular para cima.' });
  } else if (r.acima > K.ACIMA_MAX) {
    r.problemas.push({ id: 'topo', msg: 'Espaço demais acima da cabeça: incline o celular para baixo.' });
  }

  if (Math.abs(r.centro - 0.5) > K.CENTRO_TOL) {
    r.problemas.push({ id: 'centro', msg: 'Centralize o rosto na tela.' });
  }

  if (luz !== null && luz < K.LUZ_MIN) {
    r.problemas.push({ id: 'luz', msg: 'Pouca luz: procure um local mais iluminado, com a luz de frente.' });
  }

  r.ok = r.problemas.length === 0;
  return r;
}

/** Retângulo ideal do rosto (frações do quadro), para desenhar a guia. aspect = largura/altura do vídeo. */
export function retanguloIdeal(cm, aspect, razaoRosto = 0.72) {
  const h = rostoAlvo(cm);
  const w = h * razaoRosto / aspect;
  const queixo = (1 + K.ACIMA_ALVO) * K.FATOR_CABECA * h;
  return { x: 0.5 - w / 2, y: queixo - h, w, h };
}

// ─────────────────────────── câmera / interface ───────────────────────────
/**
 * opts: {
 *   video, overlay, botaoLigar, botaoVirar, botaoFoto, botaoForcar,  // elementos
 *   msg, detalhe, barra, contagem,                                   // elementos de texto/barra
 *   endpoint: '/api/foto/avaliar', getCm: () => 65,
 *   getHeaders?: () => ({Authorization:'Bearer ...'}),
 *   onCapture: (file) => {},  input?: HTMLInputElement               // entrega a foto
 * }
 */
export function iniciarCaptura(opts) {
  const o = opts;
  let stream = null, ativo = false, timer = null, facing = 'user', okSeguidos = 0, ultima = null;
  const cv = document.createElement('canvas');
  const ctx = cv.getContext('2d', { willReadFrequently: true });

  async function ligar() {
    if (!navigator.mediaDevices?.getUserMedia) {
      o.msg.textContent = 'Este navegador não permite usar a câmera aqui (é preciso HTTPS). Envie uma foto da galeria.';
      return;
    }
    parar();
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: facing, width: { ideal: 1280 }, height: { ideal: 1280 } }, audio: false });
    } catch {
      o.msg.textContent = 'Não consegui acessar a câmera. Permita o acesso ou envie uma foto da galeria.';
      return;
    }
    o.video.srcObject = stream;
    await o.video.play();
    o.video.parentElement.style.aspectRatio = `${o.video.videoWidth} / ${o.video.videoHeight}`;
    o.video.parentElement.classList.toggle('espelho', facing === 'user');
    ativo = true; okSeguidos = 0;
    ciclo();
  }

  function parar() {
    ativo = false; clearTimeout(timer);
    stream?.getTracks().forEach(t => t.stop()); stream = null;
  }

  async function ciclo() {
    if (!ativo) return;
    try {
      const fw = 360, fh = Math.max(1, Math.round(o.video.videoHeight * fw / o.video.videoWidth));
      cv.width = fw; cv.height = fh;
      ctx.drawImage(o.video, 0, 0, fw, fh);
      const luz = luzMedia(ctx.getImageData(0, 0, fw, fh).data);
      const blob = await new Promise(res => cv.toBlob(res, 'image/jpeg', 0.6));
      const fd = new FormData(); fd.append('frame', blob, 'f.jpg');
      const resp = await fetch(o.endpoint, { method: 'POST', body: fd, headers: o.getHeaders ? o.getHeaders() : undefined });
      if (!resp.ok) throw new Error(String(resp.status));
      const j = await resp.json();
      const face = j.encontrado ? { x: j.x, y: j.y, w: j.w, h: j.h } : null;
      const av = avaliarEnquadramento({ face, cm: o.getCm(), luz });
      okSeguidos = av.ok ? okSeguidos + 1 : 0;
      ultima = av;
      desenhar(av, face);
    } catch {
      o.msg.textContent = 'Sem resposta do servidor. Verifique a conexão.';
      o.botaoFoto.disabled = true;
    } finally {
      if (ativo) timer = setTimeout(ciclo, 500);
    }
  }

  function luzMedia(d) {
    let s = 0, n = 0;
    for (let i = 0; i < d.length; i += 16) { s += 0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2]; n++; }
    return n ? s / n : 0;
  }

  function desenhar(av, face) {
    const pronto = okSeguidos >= 2;
    o.msg.textContent = pronto ? 'Perfeito! Pode tirar a foto.' : (av.problemas[0]?.msg ?? '');
    o.msg.dataset.estado = pronto ? 'ok' : (av.problemas.length ? 'erro' : 'aguarde');
    o.botaoFoto.disabled = !pronto;

    if (av.spanCm != null) {
      const pct = Math.max(0, Math.min(1, av.spanCm / av.precisaCm));
      o.barra.style.width = `${Math.round(pct * 100)}%`;
      o.barra.dataset.estado = av.spanCm >= av.precisaCm ? 'ok' : (av.spanCm >= av.precisaCm - 8 ? 'quase' : 'erro');
      o.detalhe.textContent = `A foto mostra até ~${Math.round(av.spanCm)} cm abaixo do topo da cabeça · precisa de ~${av.precisaCm} cm para o cabelo de ${o.getCm()} cm.`;
    } else {
      o.barra.style.width = '0%'; o.detalhe.textContent = '';
    }

    const c = o.overlay, W = c.clientWidth, H = c.clientHeight;
    c.width = W; c.height = H;
    const g = c.getContext('2d'); g.clearRect(0, 0, W, H);
    const ideal = retanguloIdeal(o.getCm(), o.video.videoWidth / o.video.videoHeight);
    g.setLineDash([8, 6]); g.lineWidth = 2; g.strokeStyle = 'rgba(212,167,44,.9)';
    rr(g, ideal.x * W, ideal.y * H, ideal.w * W, ideal.h * H, 18); g.stroke();
    if (face) {
      g.setLineDash([]); g.lineWidth = 3;
      g.strokeStyle = pronto ? '#58d68d' : (av.problemas.length ? '#e57373' : '#d4a72c');
      rr(g, face.x * W, face.y * H, face.w * W, face.h * H, 14); g.stroke();
    }
  }

  function rr(g, x, y, w, h, r) {
    g.beginPath(); g.moveTo(x + r, y);
    g.arcTo(x + w, y, x + w, y + h, r); g.arcTo(x + w, y + h, x, y + h, r);
    g.arcTo(x, y + h, x, y, r); g.arcTo(x, y, x + w, y, r); g.closePath();
  }

  function capturar() {
    const c = document.createElement('canvas');
    c.width = o.video.videoWidth; c.height = o.video.videoHeight;
    c.getContext('2d').drawImage(o.video, 0, 0);
    c.toBlob(blob => {
      const file = new File([blob], 'foto-cliente.jpg', { type: 'image/jpeg' });
      if (o.input) {
        const dt = new DataTransfer(); dt.items.add(file);
        o.input.files = dt.files;
        o.input.dispatchEvent(new Event('change', { bubbles: true }));
      }
      o.onCapture?.(file);
      parar();
    }, 'image/jpeg', 0.92);
  }

  async function comContagem(forcar) {
    if (!forcar && !(ultima && okSeguidos >= 2)) return;
    o.botaoFoto.disabled = true;
    for (let i = 3; i > 0; i--) {
      o.contagem.hidden = false; o.contagem.textContent = String(i);
      await new Promise(r => setTimeout(r, 1000));
    }
    o.contagem.hidden = true;
    capturar();
  }

  o.botaoLigar.addEventListener('click', ligar);
  o.botaoVirar?.addEventListener('click', () => { facing = facing === 'user' ? 'environment' : 'user'; ligar(); });
  o.botaoFoto.addEventListener('click', () => comContagem(false));
  o.botaoForcar?.addEventListener('click', () => comContagem(true));
  return { ligar, parar };
}
