import os

from fastmcp import FastMCP

from http_client import post_json

mcp = FastMCP("db-query-tool-mcp")
SERVICE_BASE_URL = os.getenv("DB_QUERY_TOOL_BASE_URL", "http://localhost:8003")


@mcp.tool
def db_query_select(sql: str, params: list | None = None) -> dict:
    """Execute a read-only SELECT query against the local SQLite dataset."""
    return post_json(
        SERVICE_BASE_URL,
        "/query",
        {
            "sql": sql,
            "params": params or [],
        },
    )


if __name__ == "__main__":
    transport = os.getenv("MCP_TRANSPORT", "http")
    if transport == "http":
        mcp.run(
            transport="http",
            host=os.getenv("MCP_HOST", "0.0.0.0"),
            port=int(os.getenv("MCP_PORT", "9003")),
        )
    else:
        mcp.run(transport=transport)
