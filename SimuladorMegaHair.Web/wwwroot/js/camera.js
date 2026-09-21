// Captura de foto via câmera do navegador (getUserMedia).
//
// Funciona em: Safari iPad/iPhone, Chrome Android, Chrome/Edge desktop,
// navegador de Smart TV com câmera USB — QUALQUER navegador moderno.
//
// REQUISITO OBRIGATÓRIO DO NAVEGADOR (não é escolha nossa): só funciona
// em "contexto seguro" — https:// ou http://localhost. Em HTTP puro na
// rede local, getUserMedia retorna undefined e o navegador nem pergunta
// permissão de câmera. Por isso o Program.cs força HTTPS.
window.megaHairCamera = {
    _stream: null,

    /**
     * Inicia a câmera e conecta ao elemento <video>.
     * @param {string} videoElementId
     * @param {boolean} usarCameraFrontal true = selfie (frontal), false = traseira
     * @returns {Promise<boolean>} true se conseguiu abrir a câmera
     */
    iniciar: async function (videoElementId, usarCameraFrontal) {
        try {
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                console.error("[Câmera] Navegador não suporta getUserMedia (falta HTTPS ou é muito antigo).");
                return false;
            }

            const video = document.getElementById(videoElementId);
            if (!video) return false;

            this._stream = await navigator.mediaDevices.getUserMedia({
                video: {
                    facingMode: usarCameraFrontal ? "user" : "environment",
                    width: { ideal: 1280 },
                    height: { ideal: 1280 }
                },
                audio: false
            });

            video.srcObject = this._stream;
            // playsInline + muted são necessários no Safari/iPad para
            // autoplay funcionar sem gesto extra do usuário.
            video.setAttribute("playsinline", "true");
            video.muted = true;
            await video.play();

            return true;
        } catch (err) {
            console.error("[Câmera] Erro ao iniciar:", err);
            return false;
        }
    },

    /**
     * Captura o frame atual do <video> e retorna como PNG base64
     * (sem o prefixo "data:image/png;base64,").
     *
     * Espera até 1,5s pelas dimensões do vídeo ficarem disponíveis antes
     * de desistir — em alguns navegadores/aparelhos, videoWidth/videoHeight
     * só ficam populados alguns instantes depois do play() resolver
     * (condição de corrida real do getUserMedia, não erro nosso).
     *
     * @param {string} videoElementId
     * @returns {Promise<string|null>}
     */
    capturar: async function (videoElementId) {
        const video = document.getElementById(videoElementId);
        if (!video) {
            console.error("[Câmera] Elemento de vídeo não encontrado:", videoElementId);
            return null;
        }

        const inicio = Date.now();
        while (video.videoWidth === 0 && (Date.now() - inicio) < 1500) {
            await new Promise(r => setTimeout(r, 100));
        }

        if (video.videoWidth === 0) {
            console.error("[Câmera] videoWidth continua 0 após esperar — o stream não está realmente conectado a este elemento.");
            return null;
        }

        const canvas = document.createElement("canvas");
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;

        const ctx = canvas.getContext("2d");
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

        const dataUrl = canvas.toDataURL("image/png");
        return dataUrl.split(",")[1];
    },

    /**
     * Encerra a câmera (libera o hardware). Sempre chamar ao sair da
     * tela de captura, senão a luz da câmera fica acesa.
     */
    parar: function () {
        if (this._stream) {
            this._stream.getTracks().forEach(track => track.stop());
            this._stream = null;
        }
    }
};