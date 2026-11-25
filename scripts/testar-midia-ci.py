"""Prepara imagens descartáveis somente para a instância de teste do CI."""
from base64 import b64decode, urlsafe_b64encode
import hmac
import os
from pathlib import Path
import subprocess
import sys
import time


def main():
    api = Path("SimuladorMegaHair.Api/wwwroot")
    png = b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aSioAAAAASUVORK5CYII=")
    nome = "6f441894-7740-4cc8-bb62-6f7514b16c28.png"
    exp = int(time.time()) + 300
    chave = hmac.digest(os.environ["Jwt__Key"].encode(), b"media-url-signing-v1", "sha256")
    env = dict(os.environ, MEGAHAIR_TEST_WEB="http://127.0.0.1:5180")
    arquivos = []
    try:
        for pasta, variavel in [("uploads", "MEGAHAIR_TEST_ORIGINAL"), ("resultados", "MEGAHAIR_TEST_RESULTADO")]:
            caminho = f"{pasta}/{nome}"
            arquivo = api / caminho
            arquivo.parent.mkdir(parents=True, exist_ok=True)
            with arquivo.open("xb") as saida:
                arquivos.append(arquivo)
                saida.write(png)
            assinatura = urlsafe_b64encode(hmac.digest(chave, f"{caminho}|{exp}".encode(),
                                                     "sha256")).decode().rstrip("=")
            env[variavel] = f"http://api:5185/media/{caminho}?exp={exp}&sig={assinatura}"
        subprocess.run([sys.executable, "scripts/testar-midia-web.py"], env=env, check=True)
    finally:
        for arquivo in arquivos:
            arquivo.unlink()


if __name__ == "__main__":
    main()
