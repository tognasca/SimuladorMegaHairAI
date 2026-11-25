"""Verifica original e resultado pelo domínio Web com arquivos sintéticos."""
import os
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, build_opener


class SemRedirecionamento(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def verificar(origem, url, pasta, tipos):
    partes = urlsplit(url)
    caminho = partes.path.split("/")
    assert len(caminho) == 4 and caminho[:3] == ["", "media", pasta], pasta
    proxy = f"{origem}/midia-simulacao/{pasta}/{caminho[-1]}"
    opener = build_opener(SemRedirecionamento())
    try:
        with opener.open(f"{proxy}?{partes.query}", timeout=25) as resposta:
            assert resposta.status == 200, pasta
            assert resposta.headers.get_content_type() in tipos, pasta
            assert "no-store" in resposta.headers.get("Cache-Control", ""), pasta
            assert resposta.read(16), pasta
    except HTTPError as erro:
        raise RuntimeError(f"{pasta}: HTTP {erro.code}") from None

    query = parse_qs(partes.query)
    assinatura = query["sig"][0]
    query["sig"] = [("B" if assinatura.startswith("A") else "A") + assinatura[1:]]
    try:
        with opener.open(f"{proxy}?{urlencode(query, doseq=True)}", timeout=25):
            raise AssertionError(f"{pasta}: assinatura inválida aceita")
    except HTTPError as erro:
        assert erro.code == 403, f"{pasta}: HTTP {erro.code}, esperado 403"


def main():
    origem = os.environ["MEGAHAIR_TEST_WEB"].rstrip("/")
    web = urlsplit(origem)
    assert web.hostname and not (web.path or web.query or web.fragment or web.username)
    assert web.scheme == "https" or (web.scheme == "http" and web.hostname == "127.0.0.1")
    verificar(origem, os.environ["MEGAHAIR_TEST_ORIGINAL"], "uploads",
              {"image/jpeg", "image/png", "image/webp"})
    verificar(origem, os.environ["MEGAHAIR_TEST_RESULTADO"], "resultados", {"image/png"})
    print("Original, resultado e rejeição de assinaturas inválidas verificados.")


if __name__ == "__main__":
    main()
