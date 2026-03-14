import json
import urllib.error
import urllib.parse
import urllib.request


def get_json(base_url: str, path: str, query: dict[str, str] | None = None) -> dict:
    url = _build_url(base_url, path, query)
    request = urllib.request.Request(url=url, method="GET")
    return _send(request)


def post_json(base_url: str, path: str, payload: dict) -> dict:
    url = _build_url(base_url, path)
    data = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(
        url=url,
        method="POST",
        data=data,
        headers={"Content-Type": "application/json"},
    )
    return _send(request)


def _build_url(base_url: str, path: str, query: dict[str, str] | None = None) -> str:
    cleaned_base = base_url.rstrip("/")
    cleaned_path = path if path.startswith("/") else f"/{path}"
    url = f"{cleaned_base}{cleaned_path}"
    if query:
        url = f"{url}?{urllib.parse.urlencode(query)}"
    return url


def _send(request: urllib.request.Request) -> dict:
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            body = response.read().decode("utf-8")
    except urllib.error.HTTPError as ex:
        error_body = ex.read().decode("utf-8")
        raise RuntimeError(f"Upstream HTTP error {ex.code}: {error_body}") from ex
    except urllib.error.URLError as ex:
        raise RuntimeError(f"Upstream connection error: {ex.reason}") from ex

    try:
        return json.loads(body)
    except json.JSONDecodeError as ex:
        raise RuntimeError(f"Upstream returned invalid JSON: {body}") from ex
