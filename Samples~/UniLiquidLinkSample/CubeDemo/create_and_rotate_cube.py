"""
Cube Demo — create a red cube and rotate it once.

Prerequisites:
  1. In Unity, run menu: UniLiquidLink/Samples/Cube Demo Server Start
  2. Run this script:
"""
import sys
import os

# sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Python~'))

from lliquidlink.client import Client, TcpJsonRpcTransport
from lliquidlink.client.models import type_, enum

def on_execute(client):
    # Create a new Cube primitive in the scene
    cube = client.GameObject.CreatePrimitive(enum("Cube"))
    print(f"Created cube: {cube}")

    # Get the Renderer component by type, then set the material color to red
    renderer = cube.GetComponent(type_("Renderer"))
    renderer.material.color = {"r": 1, "g": 0, "b": 0, "a": 1}
    print("Set material color to red")

    # Rotate the cube 30 degrees on X, 45 degrees on Y
    cube.transform.Rotate(30, 45, 0)
    print("Rotated cube (30 on X, 45 on Y)")

if __name__ == "__main__":
    client = Client(TcpJsonRpcTransport("localhost", 8700))
    client.on_execute += on_execute
    print("Connecting to Unity server...")
    client.mainloop()
    print("Done.")
