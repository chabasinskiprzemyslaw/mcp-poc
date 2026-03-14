import os

from fastmcp import FastMCP

from http_client import post_json

mcp = FastMCP("risk-check-tool-mcp")
SERVICE_BASE_URL = os.getenv("RISK_CHECK_TOOL_BASE_URL", "http://localhost:8004")


@mcp.tool
def risk_check(amount: float, country: str = "", merchant_category: str = "") -> dict:
    """Run a simple finance-style risk check with static rule scoring."""
    return post_json(
        SERVICE_BASE_URL,
        "/risk-check",
        {
            "amount": amount,
            "country": country,
            "merchant_category": merchant_category,
        },
    )


if __name__ == "__main__":
    transport = os.getenv("MCP_TRANSPORT", "http")
    if transport == "http":
        mcp.run(
            transport="http",
            host=os.getenv("MCP_HOST", "0.0.0.0"),
            port=int(os.getenv("MCP_PORT", "9004")),
        )
    else:
        mcp.run(transport=transport)
