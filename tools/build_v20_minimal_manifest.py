from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src" / "LightmanV20"
BASE = PROJECT / "Patch" / "SHOW-V20-MINIMAL.json"
PORTABLE = ROOT / "integration" / "Data" / "SHOW-V20-MINIMAL-V4-PORTABLE.json"
TREE = ROOT / "tools" / "data" / "TREE-V33-A09-B10-99PX.json"

# Numbering requested from the frontal view.  Keep FLAG-N bound to flagN;
# only its visual slot changes, so the physical IP/ID contract is untouched.
FLAG_VISUALS = {
    1: ("Bandera 1 · superior izquierda exterior", "upper-left-outer"),
    2: ("Bandera 2 · superior izquierda interior", "upper-left-inner"),
    3: ("Bandera 3 · superior derecha interior", "upper-right-inner"),
    4: ("Bandera 4 · superior derecha exterior", "upper-right-outer"),
    5: ("Bandera 5 · inferior izquierda", "lower-left"),
    6: ("Bandera 6 · inferior derecha", "lower-right"),
}

# Everything below is descriptive metadata for portable visualizers.  It mirrors
# the current Web scene, but is never consumed by LiveEngine and therefore cannot
# change a controller destination or the bytes sent by the bridge.
FLAG_SLOTS = {
    "upper-left-outer": {
        "base_placement": {"position_m": [-2.75, 2.75, 0.13], "yaw_deg": -45, "tier": "B2", "portrait": True},
        "assembly_transform": {"position_m": [-2.582, 2.398, 0.3904], "rotation_deg_xyz": [45, 45, 0]},
    },
    "upper-left-inner": {
        "base_placement": {"position_m": [-2.05, 3.45, 0.43], "yaw_deg": -45, "tier": "B1", "portrait": True},
        "assembly_transform": {"position_m": [-1.314, 3.114, 0.334], "rotation_deg_xyz": [45, 45, 0]},
    },
    "upper-right-inner": {
        "base_placement": {"position_m": [2.05, 3.45, 0.43], "yaw_deg": 45, "tier": "B1", "portrait": True},
        "assembly_transform": {"position_m": [1.314, 3.114, 0.334], "rotation_deg_xyz": [45, -45, 0]},
    },
    "upper-right-outer": {
        "base_placement": {"position_m": [2.75, 2.75, 0.13], "yaw_deg": 45, "tier": "B2", "portrait": True},
        "assembly_transform": {"position_m": [2.582, 2.398, 0.3904], "rotation_deg_xyz": [45, -45, 0]},
    },
    "lower-left": {
        "base_placement": {"position_m": [-2.75, 0.85, -0.17], "yaw_deg": 45, "tier": "B3", "portrait": True},
        "assembly_transform": {"position_m": [-2.9824, 0.922, 0.6584], "rotation_deg_xyz": [0, 0, 0]},
    },
    "lower-right": {
        "base_placement": {"position_m": [2.75, 0.85, -0.17], "yaw_deg": -45, "tier": "B3", "portrait": True},
        "assembly_transform": {"position_m": [2.9824, 0.922, 0.6584], "rotation_deg_xyz": [0, 0, 0]},
    },
}

CAMERA_PRESETS = {
    "front": {"position_m": [0, 1.75, 8.6], "target_m": [0, 1.48, -0.10]},
    "rear": {"position_m": [0, 2.6, -8.6], "target_m": [0, 2.4, -0.208]},
    "perspective": {"position_m": [6.1, 3.6, 6.4], "target_m": [0, 1.45, -0.08]},
    "side": {"position_m": [7.4, 2.35, 0.7], "target_m": [0, 1.45, -0.15]},
    "top": {"position_m": [0.2, 8.8, 0.4], "target_m": [0, 1.25, 0]},
}

CEILING_GROUPS = {
    "CEILING-LEFT": {
        "side": "left",
        "active_layout_indices": list(range(0, 8)),
        "pivot_m": [-2.1, 2.7023936175515635, 0],
        "transform": {"rotation_deg_xyz": [0, 180, 0]},
        "affects_signal_mapping": False,
    },
    "CEILING-RIGHT": {
        "side": "right",
        "active_layout_indices": list(range(10, 18)),
        "pivot_m": [2.1, 2.7023936175515635, 0],
        "transform": {"rotation_deg_xyz": [0, 180, 0]},
        "affects_signal_mapping": False,
    },
}

WAVE_GROUPS = {
    "WAVE-LEFT": {
        "side": "left",
        "assembly_transform": {"position_m": [-1.65, 0, -0.965], "rotation_deg_xyz": [0, 0, 0], "scale_xyz": [1, 1, 1]},
        "geometry_transform": {"mirror_axis": None},
        "mirror_of": None,
    },
    "WAVE-RIGHT": {
        "side": "right",
        # The Web geometry is already projected as a mirror.  Keeping the
        # assembly scale at +1 prevents consumers from mirroring it twice.
        "assembly_transform": {"position_m": [1.65, 0, -0.965], "rotation_deg_xyz": [0, 0, 0], "scale_xyz": [1, 1, 1]},
        "geometry_transform": {"mirror_axis": "X"},
        "mirror_of": "WAVE-LEFT",
    },
}

HOUSE_SEGMENTS = {
    "HOUSE-LEFT-POST": ([-3.5, 0, 1.085], [-3.5, 4.0, 1.085]),
    "HOUSE-LEFT-ROOF": ([-3.5, 4.0, 1.085], [0, 5.0, 1.085]),
    "HOUSE-RIGHT-ROOF": ([0, 5.0, 1.085], [3.5, 4.0, 1.085]),
    "HOUSE-RIGHT-POST": ([3.5, 4.0, 1.085], [3.5, 0, 1.085]),
}


def build_visual_layout() -> dict:
    """Portable scene contract; all references resolve inside this manifest."""
    ceiling_spacing = (3.30 - 0.90) / 7
    return {
        "schema_version": 4,
        "coordinate_system": {
            "units": "meters",
            "handedness": "right-handed",
            "axes": {"x": "right", "y": "up", "z": "front-toward-audience"},
            "origin": "floor-center of the house outline",
            "angles": "degrees",
            "euler_order": "XYZ",
        },
        "camera": {
            "projection": "perspective",
            "fov_y_deg": 41,
            "near_m": 0.04,
            "far_m": 80,
            "initial": {"position_m": [0, 2.65, 9.4], "target_m": [0, 2.0, -0.08]},
            "orbit": {"damping_factor": 0.07, "min_distance_m": 3, "max_distance_m": 20},
            "presets": CAMERA_PRESETS,
        },
        "pixel_mapping": {
            "visual_index_base": 0,
            "artnet_universe_base": 0,
            "artnet_channel_base": 1,
            "channels_per_pixel": 3,
            "color_order": "RGB",
            "operation_order": [
                "visual_pixel_transform",
                "physical_wiring_transform",
                "route_offset",
            ],
            "algorithms": {
                "forward": "source = visual",
                "reverse": "source = pixel_count - 1 - visual",
                "matrix-snake-14": {
                    "width": 60,
                    "height": 42,
                    "segments": 3,
                    "rows_per_segment": 14,
                    "segment_pixels": 840,
                    "rule": "each 14-row segment starts at its own output; even local rows left-to-right, odd rows right-to-left",
                },
                "row-snake-100": {
                    "rows": 4,
                    "row_pixels": 100,
                    "rule": "even rows forward; odd rows reverse",
                },
                "row-snake-100-right": {
                    "rows": 4,
                    "row_pixels": 100,
                    "rule": "even rows reverse; odd rows forward",
                },
            },
        },
        "flags": {
            "geometry": {
                "primitive": "pixel-grid",
                "coordinate_space": "local, origin at grid center",
                "columns": 60,
                "rows": 42,
                "pixels": 2520,
                "pitch_m": 0.025,
                "native_size_m": [1.5, 1.05],
                "mounted_orientation": "portrait",
                "mounted_size_m": [1.05, 1.5],
                "point_order": "row-major from top-left before visual_pixel_transform",
            },
            "slots": FLAG_SLOTS,
            "placement_rule": "assembly_transform is authoritative; base_placement documents the generated fallback",
        },
        "ceiling": {
            "geometry": {
                "primitive": "piecewise-catenary-polyline",
                "coordinate_space": "world before the group pivot transform",
                "strip_length_m": 5.0,
                "pixels_per_strip": 100,
                "spans": 3,
                "span_length_m": 5.0 / 3,
                "samples_per_span": 601,
                "outer_x_m": 3.30,
                "inner_x_m": 0.90,
                "spacing_x_m": ceiling_spacing,
                "roof_rise_per_strip_m": ceiling_spacing / 3.5,
                "anchor_y_m": [5 - 3.30 / 3.5, 10 / 3, 8 / 3, 2],
                "anchor_z_m": [-1, -1 / 3, 1 / 3, 1],
                "support_ids": ["SC1", "SC2", "SC3", "SC4"],
                "pixel_sampling": "uniform arc length, inclusive endpoints",
            },
            "layout_index": {
                "period": 20,
                "left_slots": list(range(0, 10)),
                "right_slots": list(range(10, 20)),
                "active_indices": list(range(0, 8)) + list(range(10, 18)),
                "reserved_indices": [8, 9, 18, 19],
            },
            "groups": CEILING_GROUPS,
        },
        "waves": {
            "geometry": {
                "primitive": "four-row rounded-zigzag",
                "coordinate_space": "local banner space, origin at bottom center",
                "banner_size_m": [2.48, 2.05],
                "path_width_m": 2.4146,
                "neon_length_m": 5.0,
                "mounted_path_length_m": 4.9,
                "end_overhang_m": 0.05,
                "rows": 4,
                "pixels_per_row": 100,
                "fillet_radius_m": 0.055,
                "post_count": 5,
                "post_spacing_m": 2.4146 / 4,
                "amplitude_m": 1.1419082723750988,
                "amplitude_constraint": "rounded mounted path length equals 4.9 m",
                "row_formulas": ["y + 0.06", "1.49 - y", "y + 0.58", "2.01 - y"],
                "pixel_sampling": "100 points per row, uniform arc length, inclusive endpoints",
            },
            "groups": WAVE_GROUPS,
            "mirror_rule": "WAVE-RIGHT geometry is mirrored in X once; assembly scale remains positive",
        },
        "tree": {
            "geometry": {
                "primitive": "line-per-element",
                "endpoints": "visual.geometry.start_m/end_m on every tree element",
                "interpolation": "linear, inclusive endpoints",
            },
            "group": {"id": "CENTRAL-TREE", "pivot_m": [0, 2.402056664150803, 0]},
        },
        "house": {
            "geometry": {
                "primitive": "line-per-visible-segment",
                "endpoints": "visual.geometry.start_m/end_m on every house element",
                "physical_pixels": 400,
                "visible_pixels": 305,
                "hidden_physical_ranges_1_based": [[1, 47], [353, 400]],
            },
            "group": {"id": "HOUSE", "transform": {"position_m": [0, 0, 0], "rotation_deg_xyz": [0, 0, 0]}},
        },
    }


def build_bridge_routes() -> list[dict]:
    """Exact physical outputs mirrored from LiveEngine.DefaultRoutes/Normalize."""
    routes: list[dict] = []
    flag_ips = [201, 202, 203, 204, 205, 207]
    flag_ids = [1, 2, 3, 4, 5, 7]
    for i, (last_octet, ident) in enumerate(zip(flag_ips, flag_ids)):
        routes.append({
            "route_id": f"flag{i + 1}",
            "name": f"Bandera {i + 1}",
            "pixels": 2520,
            "bridge_input": {"protocol": "Art-Net", "universe": i * 15, "channel": 1},
            "physical_output": {
                "protocol": "LMP3",
                "ip": f"192.168.1.{last_octet}",
                "port": 7777,
                "ident": ident,
            },
        })
    for i in range(4):
        pixel_offset = i * 600
        routes.append({
            "route_id": f"tree{i + 1}",
            "name": f"Árbol {'A' if i < 2 else 'B'} · salida {i + 1}",
            "pixels": 600,
            "bridge_input": {"protocol": "Art-Net", "universe": 90 + i * 4, "channel": 1},
            "physical_output": {
                "protocol": "Art-Net",
                "ip": "192.168.1.91",
                "port": 6454,
                "universe": pixel_offset // 170,
                "channel": (pixel_offset % 170) * 3 + 1,
            },
        })
    routes.extend([
        {
            "route_id": "hangers-left", "name": "Izquierda · colgantes", "pixels": 800,
            "bridge_input": {"protocol": "Art-Net", "universe": 106, "channel": 1},
            "physical_output": {"protocol": "Art-Net", "ip": "192.168.1.93", "port": 6454, "universe": 2, "channel": 181},
        },
        {
            "route_id": "hangers-right", "name": "Derecha · colgantes", "pixels": 800,
            "bridge_input": {"protocol": "Art-Net", "universe": 111, "channel": 1},
            "physical_output": {"protocol": "Art-Net", "ip": "192.168.1.92", "port": 6454, "universe": 2, "channel": 181},
        },
        {
            "route_id": "waves-left", "name": "Izquierda · ondas M/W", "pixels": 400,
            "bridge_input": {"protocol": "Art-Net", "universe": 116, "channel": 1},
            "physical_output": {"protocol": "Art-Net", "ip": "192.168.1.93", "port": 6454, "universe": 0, "channel": 1},
        },
        {
            "route_id": "waves-right", "name": "Derecha · ondas M/W", "pixels": 400,
            "bridge_input": {"protocol": "Art-Net", "universe": 119, "channel": 1},
            "physical_output": {"protocol": "Art-Net", "ip": "192.168.1.92", "port": 6454, "universe": 0, "channel": 1},
        },
        {
            "route_id": "custom-casa-400", "name": "Casa · salida 3 · 400 LED", "pixels": 400,
            "bridge_input": {"protocol": "Art-Net", "universe": 122, "channel": 1},
            "physical_output": {"protocol": "Art-Net", "ip": "192.168.1.93", "port": 6454, "universe": 7, "channel": 31},
        },
    ])
    return routes


def source_pixel_for(element: dict, visual_pixel: int) -> int:
    """Python equivalent of Web/pixel-map.js, stored expanded in schema V4."""
    mapped_pixel = visual_pixel
    if element.get("visual_flip_x"):
        row = mapped_pixel // 100
        mapped_pixel = row * 100 + (99 - mapped_pixel % 100)
    if element.get("visual_rotate_180"):
        x = mapped_pixel % 60
        y = mapped_pixel // 60
        mapped_pixel = (41 - y) * 60 + (59 - x)

    direction = element["direction"]
    if direction == "reverse":
        return element["pixel_count"] - 1 - mapped_pixel
    if direction == "row-snake-100":
        row = mapped_pixel // 100
        return row * 100 + (99 - mapped_pixel % 100 if row % 2 else mapped_pixel % 100)
    if direction == "row-snake-100-right":
        row = mapped_pixel // 100
        return row * 100 + (mapped_pixel % 100 if row % 2 else 99 - mapped_pixel % 100)
    if direction == "matrix-snake-14":
        x = mapped_pixel % 60
        y = mapped_pixel // 60
        output = y // 14
        local_y = y % 14
        physical_x = x if local_y % 2 == 0 else 59 - x
        return output * 840 + local_y * 60 + physical_x
    return mapped_pixel


def element_physical_output(element: dict, route: dict) -> dict:
    """Resolve an element offset into the route's actual physical destination."""
    output = dict(route["physical_output"])
    offset = element["route_offset"]
    output["route_pixel_offset"] = offset
    output["element_pixels"] = element["pixel_count"]
    if output["protocol"] == "LMP3":
        output["pixel_start_0_based"] = offset
        output["pixel_end_0_based"] = offset + element["pixel_count"] - 1
        return output

    base_pixel = output.pop("universe") * 170 + (output.pop("channel") - 1) // 3
    physical_start = base_pixel + offset
    start_universe, start_channel = address(physical_start)
    end_universe, end_channel = finish(physical_start, element["pixel_count"])
    output.update({
        "universe_start": start_universe,
        "channel_start": start_channel,
        "universe_end": end_universe,
        "channel_end": end_channel,
    })
    return output


def build_element_visual(element: dict) -> dict:
    """Attach an explicit render and pixel-mapping contract to one element."""
    kind = element["kind"]
    transforms: list[dict] = []
    if element.get("visual_flip_x"):
        transforms.append({"operation": "flip-x", "row_width": 100, "rows": 4})
    if element.get("visual_rotate_180"):
        transforms.append({"operation": "rotate-180", "width": 60, "height": 42})
    mapping = {
        "visual_pixel_transform": transforms,
        "physical_wiring_transform": element["direction"],
        "route_id": element["route_id"],
        "route_offset_pixels": element["route_offset"],
        "operation_order_ref": "#/configuration/visual_layout/pixel_mapping/operation_order",
        "route_pixel_indices": [
            element["route_offset"] + source_pixel_for(element, visual_pixel)
            for visual_pixel in range(element["pixel_count"])
        ],
    }

    if kind == "flag":
        slot = element["visual_slot"]
        return {
            "element_type": "flag",
            "geometry_ref": "#/configuration/visual_layout/flags/geometry",
            "slot": slot,
            "slot_ref": f"#/configuration/visual_layout/flags/slots/{slot}",
            "placement": FLAG_SLOTS[slot],
            "pixel_mapping": mapping,
        }
    if kind == "central":
        return {
            "element_type": "tree",
            "group": "CENTRAL-TREE",
            "geometry": {"primitive": "line", "start_m": element["tree_bottom"], "end_m": element["tree_top"]},
            "pixel_mapping": mapping,
        }
    if kind == "ceiling":
        layout_index = element["layout_index"]
        side_slot = layout_index % 10
        group = "CEILING-LEFT" if layout_index < 10 else "CEILING-RIGHT"
        side_sign = -1 if layout_index < 10 else 1
        spacing = (3.30 - 0.90) / 7
        x = side_sign * (3.30 - side_slot * spacing)
        return {
            "element_type": "ceiling",
            "geometry_ref": "#/configuration/visual_layout/ceiling/geometry",
            "group": group,
            "group_ref": f"#/configuration/visual_layout/ceiling/groups/{group}",
            "layout_index": layout_index,
            "side_slot": side_slot,
            "path_x_m": x,
            "roof_rise_m": (abs(x) - 0.90) / 3.5,
            "pixel_mapping": mapping,
        }
    if kind == "wave":
        group = element["element_id"]
        return {
            "element_type": "wave",
            "geometry_ref": "#/configuration/visual_layout/waves/geometry",
            "group": group,
            "group_ref": f"#/configuration/visual_layout/waves/groups/{group}",
            "assembly_transform": WAVE_GROUPS[group]["assembly_transform"],
            "geometry_transform": WAVE_GROUPS[group]["geometry_transform"],
            "pixel_mapping": mapping,
        }
    if kind == "outline":
        start, end = HOUSE_SEGMENTS[element["element_id"]]
        return {
            "element_type": "house",
            "group": "HOUSE",
            "geometry": {"primitive": "line", "start_m": start, "end_m": end},
            "physical_led_range_1_based": [element["route_offset"] + 1, element["route_offset"] + element["pixel_count"]],
            "pixel_mapping": mapping,
        }
    raise ValueError(f"Tipo visual no soportado: {kind}")


def address(pixel_start: int) -> tuple[int, int]:
    return pixel_start // 170, (pixel_start % 170) * 3 + 1


def finish(pixel_start: int, count: int) -> tuple[int, int]:
    return address(pixel_start + count - 1)[0], address(pixel_start + count - 1)[1] + 2


def main() -> None:
    base = json.loads(BASE.read_text(encoding="utf-8-sig"))
    tree = {x["name"]: x for x in json.loads(TREE.read_text(encoding="utf-8"))["rods"]}
    elements: list[dict] = []

    for e in base["elements"]:
        kind = e["kind"]
        if kind == "flag":
            e = dict(e)
            number = int(e["element_id"].split("-")[1])
            label, visual_slot = FLAG_VISUALS[number]
            e["label"] = label
            e["visual_slot"] = visual_slot
            e["visual_rotate_180"] = number in (5, 6)
            e["route_id"] = f"flag{number}"
            e["route_offset"] = 0
            elements.append(e)
        elif kind == "central":
            e = dict(e)
            rod = tree[e["tree_name"]]
            e["pixel_count"] = rod["count"]
            e["pixel_start"] = rod["pixel_start"]
            e["universe_start"] = rod["universe"]
            e["channel_start"] = rod["channel"]
            e["universe_end"], e["channel_end"] = finish(rod["pixel_start"], rod["count"])
            number = int(e["tree_name"][1:])
            family_offset = 0 if e["tree_name"][0] == "A" else 2
            route_number = family_offset + (0 if number <= 6 else 1) + 1
            route_base = (90, 94, 98, 102)[route_number - 1] * 170
            e["route_id"] = f"tree{route_number}"
            e["route_offset"] = rod["pixel_start"] - route_base
            elements.append(e)
        elif kind == "ceiling" and e.get("active"):
            e = dict(e)
            old_index = int(e["element_id"].split("-")[1]) - 1
            e["layout_index"] = old_index
            if old_index < 10:
                e["route_id"] = "hangers-left"
                route_base = 106 * 170
            else:
                e["route_id"] = "hangers-right"
                route_base = 111 * 170
            e["route_offset"] = e["pixel_start"] - route_base
            elements.append(e)
        elif kind == "wave":
            e = dict(e)
            e["route_id"] = "waves-left" if e["element_id"] == "WAVE-LEFT" else "waves-right"
            e["route_offset"] = 0
            e["visual_flip_x"] = e["element_id"] == "WAVE-RIGHT"
            elements.append(e)

    house = [
        ("HOUSE-LEFT-POST", "Parante izquierdo · LED 48–129", 48, 82),
        ("HOUSE-LEFT-ROOF", "Techo izquierdo · LED 130–200", 130, 71),
        ("HOUSE-RIGHT-ROOF", "Techo derecho · LED 201–271", 201, 71),
        ("HOUSE-RIGHT-POST", "Parante derecho · LED 272–352", 272, 81),
    ]
    for element_id, label, first, count in house:
        pixel_start = 122 * 170 + first - 1
        u, c = address(pixel_start)
        eu, ec = finish(pixel_start, count)
        elements.append({
            "element_id": element_id,
            "controller": 107,
            "target_ip": "127.0.0.2",
            "output": 1,
            "pixel_start": pixel_start,
            "pixel_count": count,
            "direction": "forward",
            "kind": "outline",
            "label": label,
            "base_universe": 0,
            "universe_start": u,
            "channel_start": c,
            "universe_end": eu,
            "channel_end": ec,
            "active": True,
            "controller_label": "CASA",
            "route_id": "custom-casa-400",
            "route_offset": first - 1,
        })

    bridge_routes = build_bridge_routes()
    bridge_route_by_id = {route["route_id"]: route for route in bridge_routes}
    for element in elements:
        # Historical migration hints are deliberately excluded from the
        # portable contract; they can be mistaken for current destinations or
        # current tree identities by a generic consumer.
        element.pop("previous_target_ip", None)
        element.pop("previous_tree_name", None)
        route = bridge_route_by_id[element["route_id"]]
        # target_ip is retained for legacy consumers.  V4 removes its old
        # ambiguity by naming its role and publishing the real output beside it.
        element["target_ip_role"] = "bridge-input-destination"
        element["bridge_input"] = {
            "role": "bridge-input-destination",
            "protocol": "Art-Net",
            "ip": element["target_ip"],
            "port": 6454,
            "universe_start": element["universe_start"],
            "channel_start": element["channel_start"],
            "universe_end": element["universe_end"],
            "channel_end": element["channel_end"],
        }
        element["physical_output"] = element_physical_output(element, route)
        element["visual"] = build_element_visual(element)

    config = dict(base["configuration"])
    config.pop("central_pixels_per_strand", None)
    config.update({
        "project": "LIGHTMAN V20 MINIMAL · show exacto",
        "visible_pixels": sum(e["pixel_count"] for e in elements),
        "active_pixels": sum(e["pixel_count"] for e in elements),
        "central_pixels": sum(e["pixel_count"] for e in elements if e["kind"] == "central"),
        "central_standard_pixels_per_strand": 100,
        "central_pixel_exceptions": {"A09": 99, "B10": 99},
        "ceiling_arches": 16,
        "ceiling_active_arches": 16,
        "outline_pixels": 305,
        "house_physical_pixels": 400,
        "house_visible_pixels": 305,
        "house_hidden_ranges": [[1, 47], [353, 400]],
        "mapping_revision": "V20-MINIMAL-4-PORTABLE-VISUAL-BRIDGE",
        "manifest_schema_version": 4,
        "flag_visual_order": [
            {"element_id": f"FLAG-{number}", "route_id": f"flag{number}", "slot": visual_slot}
            for number, (_, visual_slot) in FLAG_VISUALS.items()
        ],
        "visual_contract": "Sólo píxeles físicamente visibles; colores tomados de buffers de salida por ruta",
        "visual_orientation": "WAVE-RIGHT espejo X; FLAG-5/FLAG-6 giro visual 180°; posiciones y patch físico intactos",
        "source_endpoints": {
            "resolume": "127.0.0.2:6454",
            "xlights": "127.0.0.3:6454",
            "tracking_local_test": "127.0.0.4:6454",
            "tracking_network": "IP LAN de esta PC:6454",
        },
        "physical_routes": {
            "flags": "192.168.1.201–205 y 192.168.1.207 · LMP3 UDP 7777 · ID 1–5/7",
            "tree": "192.168.1.91 · Art-Net U0–14",
            "right": "192.168.1.92 · Art-Net U0–7",
            "left_and_house": "192.168.1.93 · Art-Net U0–9 · casa inicia U7 C31",
        },
        "bridge_routes": bridge_routes,
        "bridge_routes_authority": "LiveEngine.DefaultRoutes plus custom-casa-400 normalization",
        "visual_layout": build_visual_layout(),
    })
    config["controller_labels"]["107"] = "CASA · 400 LED físicos / 305 visibles"
    config["wave_pixels_each"] = 400

    outputs = [dict(output) for output in base.get("outputs", [])]
    for output in outputs:
        if output["controller"] in (101, 102) and output["output"] == 2:
            output.update({
                "used_pixels": 599,
                "visible_pixels": 599,
                "reserved_pixels": 600,
                "used_channel_end": 267,
                "reserved_channel_end": 270,
                "pixel_last": output["pixel_first"] + 598,
                "reserved_pixel_last": output["pixel_first"] + 599,
            })
        if output["controller"] == 107:
            output.update({
                "name": "CASA",
                "used_pixels": 400,
                "physical_pixels": 400,
                "visible_pixels": 305,
                "hidden_ranges": [[1, 47], [353, 400]],
                "pixels": 400,
                "pixel_last": output["pixel_first"] + 399,
                "universe_end": 124,
                "channel_end": 180,
                "visible_universe_start": 122,
                "visible_channel_start": 142,
                "visible_universe_end": 124,
                "visible_channel_end": 36,
            })
    result = {
        "format": "lightman-v20-show-manifest",
        "schema_version": 4,
        "configuration": config,
        "outputs": outputs,
        "elements": elements,
    }

    assert len(elements) == 52
    assert config["visible_pixels"] == 20223
    assert sum(e["pixel_count"] for e in elements if e["kind"] == "central") == 2398
    assert sum(e["pixel_count"] for e in elements if e["kind"] == "outline") == 305
    assert {e["tree_name"]: e["pixel_count"] for e in elements if e["kind"] == "central"}["A09"] == 99
    assert {e["tree_name"]: e["pixel_count"] for e in elements if e["kind"] == "central"}["B10"] == 99
    assert all(0 <= e["route_offset"] for e in elements)
    assert next(o for o in outputs if o["controller"] == 107)["pixels"] == 400
    assert all(o["used_pixels"] == 599 for o in outputs if o["controller"] in (101, 102) and o["output"] == 2)
    assert len(bridge_routes) == 15
    assert len(bridge_route_by_id) == len(bridge_routes)
    assert {element["route_id"] for element in elements} <= set(bridge_route_by_id)
    assert all("previous_target_ip" not in element and "previous_tree_name" not in element for element in elements)
    assert all(element["target_ip_role"] == "bridge-input-destination" for element in elements)
    assert all(len(element["visual"]["pixel_mapping"]["route_pixel_indices"]) == element["pixel_count"] for element in elements)
    assert all(
        max(element["visual"]["pixel_mapping"]["route_pixel_indices"]) < bridge_route_by_id[element["route_id"]]["pixels"]
        for element in elements
    )
    flags = [e for e in elements if e["kind"] == "flag"]
    assert [(e["element_id"], e["route_id"], e["visual_slot"]) for e in flags] == [
        (f"FLAG-{number}", f"flag{number}", visual_slot)
        for number, (_, visual_slot) in FLAG_VISUALS.items()
    ]
    assert [e["element_id"] for e in flags if e["visual_rotate_180"]] == ["FLAG-5", "FLAG-6"]
    assert [e["physical_output"]["ip"] for e in flags] == [
        "192.168.1.201", "192.168.1.202", "192.168.1.203",
        "192.168.1.204", "192.168.1.205", "192.168.1.207",
    ]
    assert [e["physical_output"]["ident"] for e in flags] == [1, 2, 3, 4, 5, 7]
    waves = [e for e in elements if e["kind"] == "wave"]
    assert [(e["element_id"], e["direction"], e["visual_flip_x"]) for e in waves] == [
        ("WAVE-LEFT", "row-snake-100", False),
        ("WAVE-RIGHT", "row-snake-100-right", True),
    ]

    text = json.dumps(result, ensure_ascii=False, indent=2)
    BASE.write_text(text + "\n", encoding="utf-8")
    PORTABLE.write_text(text + "\n", encoding="utf-8")
    compact = json.dumps(result, ensure_ascii=False, separators=(",", ":"))
    (PROJECT / "Web" / "patch-map.js").write_text(
        "export const ETH01_PATCH = Object.freeze(" + compact + ");\n", encoding="utf-8"
    )
    print(json.dumps({"elements": len(elements), "visible_pixels": config["visible_pixels"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
