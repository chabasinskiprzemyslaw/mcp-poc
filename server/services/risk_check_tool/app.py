from flask import Flask, jsonify, request

app = Flask(__name__)

HIGH_RISK_COUNTRIES = {"ir", "kp", "sy", "ru"}
HIGH_RISK_MERCHANT_CATEGORIES = {"crypto", "gambling", "cash_advance"}


@app.get("/health")
def health():
    return jsonify({"status": "ok", "service": "risk_check_tool"})


@app.post("/risk-check")
def risk_check():
    payload = request.get_json(silent=True) or {}

    amount = payload.get("amount")
    country = str(payload.get("country", "")).strip().lower()
    merchant_category = str(payload.get("merchant_category", "")).strip().lower()

    if amount is None:
        return jsonify({"error": "Field 'amount' is required."}), 400

    try:
        amount_value = float(amount)
    except (TypeError, ValueError):
        return jsonify({"error": "Field 'amount' must be numeric."}), 400

    if amount_value < 0:
        return jsonify({"error": "Field 'amount' must be non-negative."}), 400

    score = 0
    reasons = []

    if amount_value >= 10000:
        score += 3
        reasons.append("High transaction amount.")
    elif amount_value >= 3000:
        score += 2
        reasons.append("Medium transaction amount.")

    if country in HIGH_RISK_COUNTRIES:
        score += 3
        reasons.append("Origin country is in high-risk list.")

    if merchant_category in HIGH_RISK_MERCHANT_CATEGORIES:
        score += 2
        reasons.append("Merchant category is high risk.")

    if score >= 5:
        label = "high"
    elif score >= 3:
        label = "medium"
    else:
        label = "low"

    if not reasons:
        reasons.append("No high-risk indicators triggered.")

    return jsonify(
        {
            "amount": amount_value,
            "country": country or None,
            "merchant_category": merchant_category or None,
            "risk_score": score,
            "risk_label": label,
            "reasons": reasons,
        }
    )


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8080)
