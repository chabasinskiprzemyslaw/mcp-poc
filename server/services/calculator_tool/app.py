from flask import Flask, jsonify, request

app = Flask(__name__)


@app.get("/health")
def health():
    return jsonify({"status": "ok", "service": "calculator_tool"})


@app.post("/add-multiple")
def add_multiple():
    payload = request.get_json(silent=True) or {}
    numbers = payload.get("numbers")

    if not isinstance(numbers, list) or len(numbers) == 0:
        return jsonify({"error": "Field 'numbers' must be a non-empty list."}), 400

    try:
        parsed_numbers = [float(value) for value in numbers]
    except (TypeError, ValueError):
        return jsonify({"error": "All items in 'numbers' must be numeric."}), 400

    total = sum(parsed_numbers)
    return jsonify({"numbers": parsed_numbers, "sum": total})


@app.post("/calculate")
def calculate():
    payload = request.get_json(silent=True) or {}
    operation = str(payload.get("operation", "")).strip().lower()
    a = payload.get("a")
    b = payload.get("b")

    if operation not in {"add", "subtract", "multiply", "divide"}:
        return jsonify({"error": "Operation must be one of: add, subtract, multiply, divide."}), 400

    try:
        left = float(a)
        right = float(b)
    except (TypeError, ValueError):
        return jsonify({"error": "Fields 'a' and 'b' must be numeric."}), 400

    if operation == "add":
        result = left + right
    elif operation == "subtract":
        result = left - right
    elif operation == "multiply":
        result = left * right
    else:
        if right == 0:
            return jsonify({"error": "Division by zero is not allowed."}), 400
        result = left / right

    return jsonify({"operation": operation, "a": left, "b": right, "result": result})


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8080)
