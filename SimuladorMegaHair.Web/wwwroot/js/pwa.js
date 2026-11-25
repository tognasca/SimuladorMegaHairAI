(async () => {
    if (!("serviceWorker" in navigator)) return;
    let registration;
    try {
        registration = await navigator.serviceWorker.register("/service-worker.js", { updateViaCache: "none" });
    } catch {
        return;
    }

    const aviso = document.createElement("aside");
    aviso.setAttribute("role", "status");
    aviso.style.cssText = "position:fixed;bottom:1rem;right:1rem;z-index:1200;max-width:calc(100vw - 2rem);padding:0.8rem;background:#241919;color:#fff;border-radius:0.5rem";
    const texto = document.createElement("span");
    const atualizar = document.createElement("button");
    atualizar.type = "button";
    atualizar.textContent = "Atualizar agora";
    aviso.append(texto, atualizar);
    document.body.append(aviso);

    function mostrar() {
        const offline = !navigator.onLine;
        atualizar.hidden = offline || !registration.waiting;
        aviso.hidden = !offline && atualizar.hidden;
        texto.textContent = offline
            ? "Sem internet. Mantenha esta tela aberta e aguarde a reconexão."
            : "Nova versão disponível. Termine o atendimento antes de atualizar. ";
    }

    atualizar.addEventListener("click", () => {
        const worker = registration.waiting;
        if (!worker) return;
        worker.addEventListener("statechange", () => {
            if (worker.state === "activated") location.reload();
        });
        worker.postMessage("ativar-versao");
    });
    registration.addEventListener("updatefound", () => {
        registration.installing?.addEventListener("statechange", mostrar);
    });
    window.addEventListener("offline", mostrar);
    window.addEventListener("online", mostrar);
    mostrar();
})();
