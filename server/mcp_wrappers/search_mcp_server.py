import os

from fastmcp import FastMCP

from http_client import get_json

mcp = FastMCP("search-tool-mcp")
SERVICE_BASE_URL = os.getenv("SEARCH_TOOL_BASE_URL", "http://localhost:8002")


@mcp.tool
def search_list_movies() -> dict:
    """List all available movies from the local search dataset."""
    return get_json(SERVICE_BASE_URL, "/movies")


@mcp.tool
def search_movies(query: str) -> dict:
    """Search movie title/description/genres using a keyword query."""
    return get_json(SERVICE_BASE_URL, "/search", {"q": query})


if __name__ == "__main__":
    transport = os.getenv("MCP_TRANSPORT", "http")
    if transport == "http":
        mcp.run(
            transport="http",
            host=os.getenv("MCP_HOST", "0.0.0.0"),
            port=int(os.getenv("MCP_PORT", "9002")),
        )
    else:
        mcp.run(transport=transport)
