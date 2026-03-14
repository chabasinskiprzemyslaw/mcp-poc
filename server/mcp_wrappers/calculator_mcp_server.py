import os

from fastmcp import FastMCP

from http_client import post_json

mcp = FastMCP("calculator-tool-mcp")
SERVICE_BASE_URL = os.getenv("CALCULATOR_TOOL_BASE_URL", "http://localhost:8001")


@mcp.tool
def calculator_calculate(operation: str, a: float, b: float) -> dict:
    """Run a deterministic arithmetic operation: add, subtract, multiply, or divide."""
    return post_json(
        SERVICE_BASE_URL,
        "/calculate",
        {"operation": operation, "a": a, "b": b},
    )


@mcp.tool
def calculator_add_multiple(numbers: list[float]) -> dict:
    """Add multiple numbers in one request."""
    return post_json(SERVICE_BASE_URL, "/add-multiple", {"numbers": numbers})


if __name__ == "__main__":
    transport = os.getenv("MCP_TRANSPORT", "http")
    if transport == "http":
        mcp.run(
            transport="http",
            host=os.getenv("MCP_HOST", "0.0.0.0"),
            port=int(os.getenv("MCP_PORT", "9001")),
        )
    else:
        mcp.run(transport=transport)
