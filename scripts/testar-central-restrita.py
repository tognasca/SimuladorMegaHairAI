"""Confirma que a conta local não administra a central pela instância de outro salão."""
import json
import urllib.request
import urllib.error

base = "http://127.0.0.1:5186"
body = json.dumps({"email": "admin@example.com", "senha": "TesteCI-12345678!"}).encode()
req = urllib.request.Request(base + "/api/auth/login", data=body,
    headers={"Content-Type": "application/json"}, method="POST")
with urllib.request.urlopen(req) as response:
    token = json.load(response)["token"]
req = urllib.request.Request(base + "/api/admin/saloes",
    headers={"Authorization": "Bearer " + token})
try:
    urllib.request.urlopen(req)
except urllib.error.HTTPError as error:
    assert error.code == 403, error.code
else:
    raise AssertionError("A instância do salão liberou o cadastro central.")
print("A instância de outro salão bloqueia a central, inclusive para a conta administradora.")
