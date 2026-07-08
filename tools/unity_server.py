import websockets
import anyio

uri = "ws://localhost:8766"

async def _run_editor(name):
    print(f"connect {uri}")
    async with websockets.connect(uri) as ws:
        print(f"send {name}")
        await ws.send(name)
        recv = await ws.recv()
        print(f"recv {recv}")
        if recv != "success":
            raise Exception()

def run_editor(name):
    anyio.run(_run_editor, name)
