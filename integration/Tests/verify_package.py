from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).parents[1]
MANIFEST = ROOT / "Data" / "SHOW-V20-MINIMAL-V4-PORTABLE.json"
SENTINELS = ROOT / "Data" / "ORIENTATION-SENTINELS.json"


def main() -> None:
    doc = json.loads(MANIFEST.read_text(encoding="utf-8"))
    assert doc["format"] == "lightman-v20-show-manifest"
    assert doc["schema_version"] == 4
    assert doc["configuration"]["mapping_revision"] == "V20-MINIMAL-4-PORTABLE-VISUAL-BRIDGE"
    elements = doc["elements"]
    routes = {route["route_id"]: route for route in doc["configuration"]["bridge_routes"]}
    assert len(elements) == 52 and len(routes) == 15
    assert sum(element["pixel_count"] for element in elements) == 20_223
    assert not any("previous_target_ip" in element or "previous_tree_name" in element for element in elements)
    expected_inputs = {
        "flag1": (0, 1, 2520), "flag2": (15, 1, 2520), "flag3": (30, 1, 2520),
        "flag4": (45, 1, 2520), "flag5": (60, 1, 2520), "flag6": (75, 1, 2520),
        "tree1": (90, 1, 600), "tree2": (94, 1, 600),
        "tree3": (98, 1, 600), "tree4": (102, 1, 600),
        "hangers-left": (106, 1, 800), "hangers-right": (111, 1, 800),
        "waves-left": (116, 1, 400), "waves-right": (119, 1, 400),
        "custom-casa-400": (122, 1, 400),
    }
    assert {
        route_id: (route["bridge_input"]["universe"], route["bridge_input"]["channel"], route["pixels"])
        for route_id, route in routes.items()
    } == expected_inputs

    expected_physical = {
        "flag1": ("LMP3", "192.168.1.201", 7777, 1),
        "flag2": ("LMP3", "192.168.1.202", 7777, 2),
        "flag3": ("LMP3", "192.168.1.203", 7777, 3),
        "flag4": ("LMP3", "192.168.1.204", 7777, 4),
        "flag5": ("LMP3", "192.168.1.205", 7777, 5),
        "flag6": ("LMP3", "192.168.1.207", 7777, 7),
        "tree1": ("Art-Net", "192.168.1.91", 6454, 0, 1),
        "tree2": ("Art-Net", "192.168.1.91", 6454, 3, 271),
        "tree3": ("Art-Net", "192.168.1.91", 6454, 7, 31),
        "tree4": ("Art-Net", "192.168.1.91", 6454, 10, 301),
        "hangers-left": ("Art-Net", "192.168.1.93", 6454, 2, 181),
        "hangers-right": ("Art-Net", "192.168.1.92", 6454, 2, 181),
        "waves-left": ("Art-Net", "192.168.1.93", 6454, 0, 1),
        "waves-right": ("Art-Net", "192.168.1.92", 6454, 0, 1),
        "custom-casa-400": ("Art-Net", "192.168.1.93", 6454, 7, 31),
    }
    actual_physical = {}
    for route_id, route in routes.items():
        output = route["physical_output"]
        actual_physical[route_id] = (
            output["protocol"], output["ip"], output["port"],
            output["ident"] if output["protocol"] == "LMP3" else output["universe"],
            *(() if output["protocol"] == "LMP3" else (output["channel"],)),
        )
    assert actual_physical == expected_physical
    for element in elements:
        lookup = element["visual"]["pixel_mapping"]["route_pixel_indices"]
        assert len(lookup) == element["pixel_count"]
        assert len(set(lookup)) == len(lookup)
        assert min(lookup) >= 0 and max(lookup) < routes[element["route_id"]]["pixels"]

    flags = [element for element in elements if element["kind"] == "flag"]
    assert [element["visual"]["slot"] for element in flags] == [
        "upper-left-outer", "upper-left-inner", "upper-right-inner",
        "upper-right-outer", "lower-left", "lower-right"
    ]
    assert [element["physical_output"]["ip"] for element in flags] == [
        "192.168.1.201", "192.168.1.202", "192.168.1.203",
        "192.168.1.204", "192.168.1.205", "192.168.1.207"
    ]
    ceiling = doc["configuration"]["visual_layout"]["ceiling"]["groups"]
    assert ceiling["CEILING-LEFT"]["transform"]["rotation_deg_xyz"] == [0, 180, 0]
    assert ceiling["CEILING-RIGHT"]["transform"]["rotation_deg_xyz"] == [0, 180, 0]
    wave_right = next(element for element in elements if element["element_id"] == "WAVE-RIGHT")
    assert wave_right["visual"]["geometry_transform"]["mirror_axis"] == "X"
    assert wave_right["visual"]["pixel_mapping"]["visual_pixel_transform"][0]["operation"] == "flip-x"

    sentinel_doc = json.loads(SENTINELS.read_text(encoding="utf-8"))
    by_id = {element["element_id"]: element for element in elements}
    for element_id, checks in sentinel_doc["sentinels"].items():
        lookup = by_id[element_id]["visual"]["pixel_mapping"]["route_pixel_indices"]
        assert all(lookup[int(visual_index)] == route_pixel for visual_index, route_pixel in checks.items())

    digest = hashlib.sha256(MANIFEST.read_bytes()).hexdigest().upper()
    print("PASS - paquete LIGHTMAN V20 R1")
    print(f"SHA256 - {digest}")


if __name__ == "__main__":
    main()
