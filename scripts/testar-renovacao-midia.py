"""Exercita upload, rotas assinadas e proxy Web contra PostgreSQL descartável da CI."""
import hashlib
import hmac
import json
import os
from base64 import urlsafe_b64encode
from io import BytesIO
from pathlib import Path
from subprocess import check_output
from time import time
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import Request, urlopen
from uuid import uuid4

from PIL import Image

API = "http://127.0.0.1:5185"
WEB = "http://127.0.0.1:5180"
ROOT = Path("SimuladorMegaHair.Api/wwwroot")
KEY = os.environ["Jwt__Key"].encode()
KEY = hmac.digest(KEY, b"media-url-signing-v1", hashlib.sha256)


def request(url, status=200, data=None, content_type="application/json"):
    req = Request(url, data=data, headers={"Content-Type": content_type})
    try:
        response = urlopen(req, timeout=15)
    except HTTPError as error:
        response = error
    with response:
        assert response.status == status, (urlsplit(url).path, response.status, status)
        return response.read(), response.headers


def upload(fmt, extension):
    output = BytesIO()
    Image.new("RGB", (32, 48), (44, 91, 114)).save(output, format=fmt)
    image = output.getvalue()
    body = (
        b'--megahair-ci\r\nContent-Disposition: form-data; name="file"; filename="foto.jpg"\r\n'
        b'Content-Type: application/octet-stream\r\n\r\n' + image + b'\r\n--megahair-ci--\r\n'
    )
    raw, _ = request(f"{API}/api/simulacoes/upload", data=body,
                     content_type="multipart/form-data; boundary=megahair-ci")
    path = json.loads(raw) if raw.startswith(b'"') else raw.decode()
    path = path.split("?")[0]
    assert path.endswith(extension), path
    assert (ROOT.parent / path).read_bytes() == image
    return path, image


jpeg, _ = upload("JPEG", ".jpg")
_, png = upload("PNG", ".png")
upload("WEBP", ".webp")

result_id = uuid4()
name = f"{result_id.hex}.png"
result = f"wwwroot/resultados/{name}"
(ROOT / "resultados").mkdir(exist_ok=True)
(ROOT / "resultados" / name).write_bytes(png)
check_output([
    "psql", "-h", "localhost", "-U", "postgres", "-d", "saloes_ci", "-v", "ON_ERROR_STOP=1", "-c",
    f"""INSERT INTO "Simulacoes" ("Id", "FotoOriginalPath", "FotoResultadoPath", "Comprimento", "Cor",
            "TipoCabelo", "MetodoMegaHair", "PromptUsado", "ValorEstimado", "CriadoEm")
        VALUES ('{result_id}', '{jpeg}', '{result}', '', '', '', '', '', 0, now())""",
])


def proof(file, exp):
    signed = hmac.digest(KEY, f"resultados/{file}|{exp}".encode(), hashlib.sha256)
    return {"exp": exp, "sig": urlsafe_b64encode(signed).rstrip(b"=").decode()}


def renew(proof_value, status=200, simulation=result_id):
    return request(f"{API}/api/simulacoes/{simulation}/renovar-midia", status,
                   data=json.dumps(proof_value).encode())


expired = proof(name, int(time()) - 60)
request(f"{API}/media/resultados/{name}?exp={expired['exp']}&sig={expired['sig']}", 403)
request(f"{API}/resultados/{name}", 404)
request(f"{API}/api/simulacoes/historico", 401)
renew({}, 404)
renew(proof("outro.png", expired["exp"]), 404)
renew(proof(name, int(time()) - 25 * 3600), 404)
renew(expired, 404, simulation=uuid4())
payload, headers = renew(expired)
assert "no-store" in headers["Cache-Control"]
links = json.loads(payload)
assert set(links) == {"fotoOriginalUrl", "fotoResultadoUrl"}
for field, mime, expected in (
    ("fotoOriginalUrl", "image/jpeg", (ROOT.parent / jpeg).read_bytes()),
    ("fotoResultadoUrl", "image/png", png),
):
    signed_url = urlsplit(links[field])
    path = signed_url.path.replace("/media/", "/midia-simulacao/", 1)
    content, headers = request(f"{WEB}{path}?{signed_url.query}")
    assert headers["Content-Type"] == mime
    assert "no-store" in headers["Cache-Control"]
    assert content == expected

print("Uploads JPEG, PNG e WebP, autorização, renovação e proxy aprovados.")
