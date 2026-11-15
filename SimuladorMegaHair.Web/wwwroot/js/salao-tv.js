(function () {
    'use strict';

    var status = document.getElementById('status');
    var codigo = document.getElementById('codigo');
    var fotos = document.getElementById('fotos');
    var orcamento = document.getElementById('orcamento');
    var chave;
    var revisao = '';
    var timeout;

    function foto(quadro, imagem, url) {
        var wrapper = document.getElementById(quadro);
        var img = document.getElementById(imagem);
        wrapper.hidden = !url;
        if (url && img.getAttribute('src') !== url) img.setAttribute('src', url);
        if (!url) img.removeAttribute('src');
    }

    function exibir(tela) {
        status.textContent = tela.fase === 'aguardando' ? 'Digite o código no tablet do salão.' :
            tela.fase === 'gerando' ? 'Preparando seu novo visual…' :
            tela.estilo || 'Tudo pronto para a próxima transformação.';
        codigo.hidden = tela.fase !== 'aguardando';
        fotos.hidden = !(tela.antes || tela.depois);
        foto('original', 'antes', tela.antes);
        foto('simulacao', 'depois', tela.depois);
        orcamento.hidden = !tela.orcamento;
        orcamento.textContent = tela.orcamento || '';
    }

    function consultar() {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', '/salao-tv/tela');
        xhr.setRequestHeader('X-Salao-TV', chave);
        xhr.setRequestHeader('If-None-Match', revisao || '""');
        xhr.timeout = 6000;
        xhr.onloadend = function () {
            if (xhr.status === 404) { iniciar(); return; }
            if (xhr.status === 200) {
                revisao = xhr.getResponseHeader('ETag') || '';
                exibir(JSON.parse(xhr.responseText));
            } else if (xhr.status !== 304) {
                exibir({ fase: 'pronto' });
                status.textContent = 'Reconectando à sessão…';
                revisao = '';
            }
            timeout = setTimeout(consultar, 1200);
        };
        xhr.send();
    }

    function iniciar() {
        clearTimeout(timeout);
        revisao = '';
        exibir({ fase: 'pronto' });
        status.textContent = 'Preparando o pareamento…';
        var xhr = new XMLHttpRequest();
        xhr.open('POST', '/salao-tv/sessao');
        xhr.timeout = 6000;
        xhr.onloadend = function () {
            if (xhr.status !== 200) {
                status.textContent = 'Aguardando conexão…';
                timeout = setTimeout(iniciar, 5000);
                return;
            }
            var sessao = JSON.parse(xhr.responseText);
            chave = sessao.chave;
            codigo.textContent = sessao.codigo;
            exibir({ fase: 'aguardando' });
            consultar();
        };
        xhr.send();
    }

    iniciar();
}());
