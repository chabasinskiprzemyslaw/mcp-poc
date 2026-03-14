import json
from pathlib import Path

from flask import Flask, jsonify, request

app = Flask(__name__)
MOVIES_FILE = Path(__file__).with_name("movies.json")


def load_movies():
    with MOVIES_FILE.open("r", encoding="utf-8") as file:
        data = json.load(file)
    if not isinstance(data, list):
        raise ValueError("movies.json must contain a list of movie objects.")
    return data


MOVIES = load_movies()


@app.get("/health")
def health():
    return jsonify({"status": "ok", "service": "search_tool"})


@app.get("/movies")
def list_movies():
    return jsonify({"count": len(MOVIES), "results": MOVIES})


@app.get("/search")
def search_movies():
    query = request.args.get("q", "").strip().lower()
    if not query:
        return jsonify({"error": "Query parameter 'q' is required."}), 400

    results = []
    for movie in MOVIES:
        title = str(movie.get("title", ""))
        description = str(movie.get("description", ""))
        genres = " ".join(movie.get("genres", []))
        searchable = f"{title} {description} {genres}".lower()
        if query in searchable:
            results.append(movie)

    return jsonify({"query": query, "count": len(results), "results": results})


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8080)
