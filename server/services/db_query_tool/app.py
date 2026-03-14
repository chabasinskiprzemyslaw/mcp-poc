import sqlite3
from pathlib import Path

from flask import Flask, jsonify, request

app = Flask(__name__)

DATA_DIR = Path(__file__).with_name("data")
DB_PATH = DATA_DIR / "sample.db"
INIT_SQL_PATH = DATA_DIR / "init.sql"
MAX_ROWS = 100

DISALLOWED_KEYWORDS = {
    "insert",
    "update",
    "delete",
    "drop",
    "alter",
    "create",
    "replace",
    "attach",
    "detach",
    "pragma",
    "vacuum",
}


def initialize_db():
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    if DB_PATH.exists():
        return

    script = INIT_SQL_PATH.read_text(encoding="utf-8")
    with sqlite3.connect(DB_PATH) as connection:
        connection.executescript(script)
        connection.commit()


def validate_select_query(sql: str):
    normalized = " ".join(sql.strip().lower().split())
    if not normalized.startswith("select "):
        raise ValueError("Only SELECT queries are allowed.")

    if ";" in normalized:
        raise ValueError("Semicolons are not allowed.")

    for keyword in DISALLOWED_KEYWORDS:
        if f" {keyword} " in f" {normalized} ":
            raise ValueError(f"Keyword '{keyword}' is not allowed.")


initialize_db()


@app.get("/health")
def health():
    return jsonify({"status": "ok", "service": "db_query_tool"})


@app.post("/query")
def query_db():
    payload = request.get_json(silent=True) or {}
    sql = payload.get("sql")
    params = payload.get("params", [])

    if not isinstance(sql, str) or not sql.strip():
        return jsonify({"error": "Field 'sql' must be a non-empty string."}), 400
    if not isinstance(params, list):
        return jsonify({"error": "Field 'params' must be a list."}), 400

    try:
        validate_select_query(sql)
    except ValueError as ex:
        return jsonify({"error": str(ex)}), 400

    try:
        with sqlite3.connect(DB_PATH) as connection:
            cursor = connection.execute(sql, params)
            rows = cursor.fetchmany(MAX_ROWS + 1)
            column_names = [column[0] for column in cursor.description or []]
    except sqlite3.Error as ex:
        return jsonify({"error": f"SQLite error: {ex}"}), 400

    truncated = len(rows) > MAX_ROWS
    if truncated:
        rows = rows[:MAX_ROWS]

    results = [dict(zip(column_names, row)) for row in rows]
    return jsonify(
        {
            "sql": sql,
            "params": params,
            "count": len(results),
            "truncated": truncated,
            "results": results,
        }
    )


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8080)
