"""Ejemplo independiente para consumir el manifiesto LIGHTMAN V20 V4.

No transmite por red. Construye los buffers lógicos y los universos Art-Net que
un software fuente debe enviar a V20. Está diseñado como referencia, no como
dependencia del ejecutable LIGHTMAN.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Iterable


PIXELS_PER_UNIVERSE = 170
USABLE_CHANNELS = 510


class LightmanMap:
    def __init__(self, manifest_path: Path):
        self.document = json.loads(manifest_path.read_text(encoding="utf-8"))
        if self.document.get("format") != "lightman-v20-show-manifest":
            raise ValueError("Formato LIGHTMAN desconocido")
        if self.document.get("schema_version") != 4:
            raise ValueError("Este adaptador requiere schema_version 4")
        configuration = self.document["configuration"]
        self.routes = {route["route_id"]: route for route in configuration["bridge_routes"]}
        self.elements = {element["element_id"]: element for element in self.document["elements"]}
        self.route_rgb = {
            route_id: bytearray(route["pixels"] * 3)
            for route_id, route in self.routes.items()
        }

    def clear(self) -> None:
        for buffer in self.route_rgb.values():
            buffer[:] = bytes(len(buffer))

    def set_element_pixels(self, element_id: str, colors: Iterable[tuple[int, int, int]]) -> None:
        """Coloca colores en orden visual sin volver a aplicar offset/snake/flip."""
        element = self.elements[element_id]
        colors = list(colors)
        if len(colors) != element["pixel_count"]:
            raise ValueError(f"{element_id}: se esperaban {element['pixel_count']} colores")
        route_id = element["route_id"]
        lookup = element["visual"]["pixel_mapping"]["route_pixel_indices"]
        target = self.route_rgb[route_id]
        for visual_index, (red, green, blue) in enumerate(colors):
            route_pixel = lookup[visual_index]  # Ya incluye route_offset.
            at = route_pixel * 3
            target[at:at + 3] = bytes((red, green, blue))

    def build_input_universes(self) -> dict[int, bytearray]:
        """Devuelve universos de 512 slots; solo 1–510 contienen RGB."""
        universes: dict[int, bytearray] = {}
        for route_id, route in self.routes.items():
            source = self.route_rgb[route_id]
            input_address = route["bridge_input"]
            universe = input_address["universe"]
            offset = input_address["channel"] - 1
            cursor = 0
            while cursor < len(source):
                frame = universes.setdefault(universe, bytearray(512))
                size = min(USABLE_CHANNELS - offset, len(source) - cursor)
                frame[offset:offset + size] = source[cursor:cursor + size]
                cursor += size
                universe += 1
                offset = 0
        return universes


def artdmx_packet(universe: int, dmx: bytes, sequence: int = 1) -> bytes:
    """Construye ArtDMX v14. El envío UDP debe hacerlo la aplicación anfitriona."""
    if not 0 <= universe <= 32767 or not 2 <= len(dmx) <= 512:
        raise ValueError("Universo o longitud DMX inválidos")
    packet = bytearray(18 + len(dmx))
    packet[0:8] = b"Art-Net\0"
    packet[8:10] = (0x5000).to_bytes(2, "little")
    packet[10:12] = (14).to_bytes(2, "big")
    packet[12] = sequence % 256
    packet[14:16] = universe.to_bytes(2, "little")
    packet[16:18] = len(dmx).to_bytes(2, "big")
    packet[18:] = dmx
    return bytes(packet)


if __name__ == "__main__":
    manifest = Path(__file__).parents[1] / "Data" / "SHOW-V20-MINIMAL-V4-PORTABLE.json"
    mapping = LightmanMap(manifest)
    # Ejemplo: FLAG-1 en rojo, usando el orden visual correcto.
    count = mapping.elements["FLAG-1"]["pixel_count"]
    mapping.set_element_pixels("FLAG-1", [(255, 0, 0)] * count)
    frames = mapping.build_input_universes()
    assert set(frames) == set(range(125))
    assert all(len(frame) == 512 for frame in frames.values())
    print(f"OK: {len(mapping.elements)} elementos, {len(mapping.routes)} rutas, {len(frames)} universos")

