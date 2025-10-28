import os
from mcp.server.fastmcp import FastMCP

# Bind to Aspire’s assigned port (env PORT) and all interfaces
PORT = int(os.environ.get("PORT", "8000"))

# stateless_http is fine for simple servers; drop it if you want stateful sessions
mcp = FastMCP(
    name="PyMCP",
    host="0.0.0.0",
    port=PORT,
    stateless_http=True,          # optional
    # streamable_http_path="/"    # optional: expose at "/" instead of "/mcp"
)

# A simple tool
@mcp.tool()
def add(a: int, b: int) -> int:
    """Add two numbers."""
    return a + b

# A simple resource
@mcp.resource("status://ping")
def ping() -> str:
    return "ok"

if __name__ == "__main__":
    # Run over HTTP so Inspector / DevTunnel can reach it
    mcp.run(transport="streamable-http")


# tools -> funkcije za klicat
# resources -> dostop do vsebin
# prompts -> vnaprej pripravljeni pozivi