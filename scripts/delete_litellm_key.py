import json
import os
import urllib.request


token_id = os.environ["TOKEN_ID"]
master_key = os.environ["LITELLM_MASTER_KEY"]
request = urllib.request.Request(
    "http://127.0.0.1:4000/key/delete",
    data=json.dumps({"keys": [token_id]}).encode("utf-8"),
    headers={
        "Authorization": f"Bearer {master_key}",
        "Content-Type": "application/json",
    },
    method="POST",
)

with urllib.request.urlopen(request, timeout=15) as response:
    if response.status != 200:
        raise RuntimeError(f"LiteLLM key deletion returned HTTP {response.status}.")
    print(response.status)
