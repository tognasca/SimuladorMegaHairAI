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
     * @param {string} videoElementId
     * @returns {string|null}
     */
    capturar: function (videoElementId) {
        const video = document.getElementById(videoElementId);
        if (!video || video.videoWidth === 0) return null;

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
