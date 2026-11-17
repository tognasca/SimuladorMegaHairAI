"""Homologa o cadastro contra uma API e PostgreSQL descartáveis do CI."""
import json
import urllib.request
import urllib.error

base = "http://127.0.0.1:5185"

def request(path, method="GET", body=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + path,
        data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        return error.code, None

def setup():
    status, login = request("/api/auth/setup-inicial", "POST",
        {"email": "admin@example.com", "senha": "TesteCI-12345678!"})
    assert status == 200, status
    return login["token"]

token = setup()
path = "/api/admin/saloes"
assert request(path)[0] == 401
status, lista = request(path, token=token)
assert status == 200, status
assert len(lista["itens"]) == 1
assert lista["itens"][0]["megaHairPro"] is True

cadastro = {"nome": "Outro Salão", "identificador": "outro-salao", "responsavel": "Operador",
    "email": "outro@example.com", "whatsApp": "5511999999999",
    "urlPublica": "https://outro.example.com", "megaHairPro": True}
status, criado = request(path, "POST", cadastro, token)
assert status == 200, status
assert request(path, "POST", cadastro, token)[0] == 409
status, lista = request(path, token=token)
outro = next(s for s in lista["itens"] if s["id"] == criado["id"])
assert outro["megaHairPro"] is False
outro["nome"] = "Outro Salão Atualizado"
assert request(path + "/" + outro["id"], "PUT", outro, token)[0] == 204
assert request(path + "/" + outro["id"], "PUT", outro, token)[0] == 409
invalido = dict(cadastro, identificador="terceiro", urlPublica="javascript:alert(1)")
assert request(path, "POST", invalido, token)[0] == 400
duplicado = dict(cadastro, identificador="terceiro")
assert request(path, "POST", duplicado, token)[0] == 409
assert request("/api/auth/usuarios", "POST",
    {"email": "equipe@example.com", "senha": "TesteCI-12345678!"}, token)[0] == 204
status, equipe = request("/api/auth/login", "POST",
    {"email": "equipe@example.com", "senha": "TesteCI-12345678!"})
assert status == 200
assert request(path, token=equipe["token"])[0] == 403
print("Cadastro, duplicidade, link inválido, edição concorrente e autorização aprovados.")
