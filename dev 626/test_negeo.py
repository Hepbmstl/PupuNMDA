from __future__ import annotations

import argparse
import json
import math
from dataclasses import dataclass
from pathlib import Path

import vtk


DEFAULT_JSON_FILE = Path("tc200") / "tc200_NeuronCAD.json"

#DEFAULT_JSON_FILE = Path("tcD") / "tcD_geo.json"

TYPE_COLORS = {
    "Soma": (1.0, 0.05, 0.05),
    "Axon": (0.1, 0.35, 1.0),
    "Dend": (0.1, 0.85, 0.2),
}
DEFAULT_COLOR = (0.8, 0.8, 0.8)
MISSING_CHANNEL_COLOR = (0.82, 0.82, 0.82)
CONNECTION_COLOR = (0.02, 0.02, 0.02)
STIMULATION_COLOR = (1.0, 0.82, 0.0)
PROBE_COLOR = (0.0, 0.78, 0.72)
AXIS_COLOR = (0.0, 0.0, 0.0)
DEFAULT_SHADOW_STRENGTH = 0.35


@dataclass(frozen=True)
class NeuronCadEntity:
    entity_id: str
    entity_type: str
    base_radius: float
    top_radius: float
    length: float
    transform: tuple[float, ...]
    color: tuple[float, float, float]
    channels: dict[str, float]


@dataclass(frozen=True)
class ChannelStats:
    name: str
    count: int
    missing: int
    min_g: float
    max_g: float
    unique_count: int


def parse_argb_color(value: str | None) -> tuple[float, float, float] | None:
    if not value or not value.startswith("#"):
        return None

    raw = value[1:]
    if len(raw) == 8:
        # NeuronCAD/WPF stores colors as #AARRGGBB.
        raw = raw[2:]
    if len(raw) != 6:
        return None

    try:
        r = int(raw[0:2], 16) / 255.0
        g = int(raw[2:4], 16) / 255.0
        b = int(raw[4:6], 16) / 255.0
    except ValueError:
        return None

    return (r, g, b)


def resolve_input_path(raw_path: str) -> Path:
    path = Path(raw_path)
    if path.is_absolute():
        return path

    if path.exists():
        return path

    return Path(__file__).resolve().parent / path


def parse_window_size(raw_size: str) -> tuple[int, int]:
    normalized = raw_size.lower().replace(",", "x")
    parts = normalized.split("x")
    if len(parts) != 2:
        raise argparse.ArgumentTypeError("Window size must use WIDTHxHEIGHT, for example 1100x850.")

    try:
        width = int(parts[0])
        height = int(parts[1])
    except ValueError as exc:
        raise argparse.ArgumentTypeError("Window size must use integer WIDTHxHEIGHT.") from exc

    return (max(1, width), max(1, height))


def get_parent_client_size(parent_hwnd: int | None) -> tuple[int, int] | None:
    if parent_hwnd is None:
        return None

    try:
        import ctypes
        from ctypes import wintypes
    except ImportError:
        return None

    rect = wintypes.RECT()
    if not ctypes.windll.user32.GetClientRect(wintypes.HWND(parent_hwnd), ctypes.byref(rect)):
        return None

    width = max(1, int(rect.right - rect.left))
    height = max(1, int(rect.bottom - rect.top))
    return (width, height)


def attach_parent_resize_sync(
    interactor: vtk.vtkRenderWindowInteractor,
    window: vtk.vtkRenderWindow,
    parent_hwnd: int | None,
) -> None:
    if parent_hwnd is None:
        return

    last_size = {"value": tuple(window.GetSize())}

    def sync_size(_obj, _event) -> None:
        current_size = get_parent_client_size(parent_hwnd)
        if current_size is None or current_size == last_size["value"]:
            return

        last_size["value"] = current_size
        window.SetSize(*current_size)
        window.Render()

    interactor.AddObserver("TimerEvent", sync_size)
    interactor.CreateRepeatingTimer(150)


def read_neuroncad_json(path: Path) -> tuple[list[NeuronCadEntity], list[dict], list[dict]]:
    with path.open("r", encoding="utf-8") as file:
        data = json.load(file)

    entities = []
    for item in data.get("Entities", []):
        entity_type = item.get("Type", "Unknown")
        color = parse_argb_color(item.get("Color")) or TYPE_COLORS.get(entity_type, DEFAULT_COLOR)
        transform = tuple(float(v) for v in item.get("Transform", []))
        if len(transform) != 16:
            continue

        channels = {}
        for channel_name, channel_data in (item.get("Channels") or {}).items():
            if not isinstance(channel_data, dict) or "G" not in channel_data:
                continue
            try:
                channels[str(channel_name)] = float(channel_data["G"])
            except (TypeError, ValueError):
                continue

        entities.append(
            NeuronCadEntity(
                entity_id=str(item.get("Id", "")),
                entity_type=entity_type,
                base_radius=float(item.get("BaseRadius", 0.0)),
                top_radius=float(item.get("TopRadius", 0.0)),
                length=float(item.get("Length", 0.0)),
                transform=transform,
                color=color,
                channels=channels,
            )
        )

    return entities, data.get("Connections", []), data.get("Devices", [])


def collect_channel_stats(entities: list[NeuronCadEntity]) -> list[ChannelStats]:
    channel_names = sorted({name for entity in entities for name in entity.channels})
    stats = []

    for name in channel_names:
        values = [entity.channels[name] for entity in entities if name in entity.channels]
        if not values:
            continue

        stats.append(
            ChannelStats(
                name=name,
                count=len(values),
                missing=len(entities) - len(values),
                min_g=min(values),
                max_g=max(values),
                unique_count=len(set(values)),
            )
        )

    return stats


def print_channel_menu(stats: list[ChannelStats]) -> None:
    print("\nAvailable ion channels:")
    print("  [Enter] Render by JSON Color")
    for index, stat in enumerate(stats, start=1):
        print(
            f"  [{index}] {stat.name:<8} "
            f"count={stat.count:<5} missing={stat.missing:<5} "
            f"min={stat.min_g:.8g} max={stat.max_g:.8g} unique={stat.unique_count}"
        )


def select_channel_from_menu(stats: list[ChannelStats]) -> str | None:
    if not stats:
        print("No numeric channel G values were found in this JSON. Press Enter to render by JSON colors.")
        return None

    names_by_lower = {stat.name.lower(): stat.name for stat in stats}

    while True:
        print_channel_menu(stats)
        choice = input("\nSelect a channel by number or name (Enter = JSON colors, q = quit): ").strip()

        if choice == "":
            return None

        if choice.lower() in {"q", "quit"}:
            raise SystemExit(0)

        if choice.isdigit():
            index = int(choice)
            if 1 <= index <= len(stats):
                return stats[index - 1].name

        selected = names_by_lower.get(choice.lower())
        if selected is not None:
            return selected

        print(f"Invalid channel selection: {choice!r}")


def transform_point(matrix: tuple[float, ...], point: tuple[float, float, float]) -> tuple[float, float, float]:
    """Apply a WPF Matrix3D stored in row-major order to a local point."""
    x, y, z = point
    return (
        x * matrix[0] + y * matrix[4] + z * matrix[8] + matrix[12],
        x * matrix[1] + y * matrix[5] + z * matrix[9] + matrix[13],
        x * matrix[2] + y * matrix[6] + z * matrix[10] + matrix[14],
    )


def insert_triangle(
    cells: vtk.vtkCellArray,
    a: int,
    b: int,
    c: int,
    cell_scalars: vtk.vtkDoubleArray | None = None,
    scalar_value: float | None = None,
) -> None:
    triangle = vtk.vtkTriangle()
    triangle.GetPointIds().SetId(0, a)
    triangle.GetPointIds().SetId(1, b)
    triangle.GetPointIds().SetId(2, c)
    cells.InsertNextCell(triangle)

    if cell_scalars is not None and scalar_value is not None:
        cell_scalars.InsertNextValue(scalar_value)


def append_frustum(
    entity: NeuronCadEntity,
    points: vtk.vtkPoints,
    polys: vtk.vtkCellArray,
    sides: int,
    cell_scalars: vtk.vtkDoubleArray | None = None,
    scalar_value: float | None = None,
) -> None:
    base_ids = []
    top_ids = []

    for i in range(sides):
        angle = 2.0 * math.pi * i / sides
        cos_a = math.cos(angle)
        sin_a = math.sin(angle)

        base_local = (entity.base_radius * cos_a, entity.base_radius * sin_a, 0.0)
        top_local = (entity.top_radius * cos_a, entity.top_radius * sin_a, entity.length)

        base_ids.append(points.InsertNextPoint(*transform_point(entity.transform, base_local)))
        top_ids.append(points.InsertNextPoint(*transform_point(entity.transform, top_local)))

    for i in range(sides):
        next_i = (i + 1) % sides
        b0 = base_ids[i]
        b1 = base_ids[next_i]
        t0 = top_ids[i]
        t1 = top_ids[next_i]

        insert_triangle(polys, b0, b1, t0, cell_scalars, scalar_value)
        insert_triangle(polys, t0, b1, t1, cell_scalars, scalar_value)

    if entity.base_radius > 1e-9:
        center_id = points.InsertNextPoint(*transform_point(entity.transform, (0.0, 0.0, 0.0)))
        for i in range(sides):
            next_i = (i + 1) % sides
            insert_triangle(polys, center_id, base_ids[next_i], base_ids[i], cell_scalars, scalar_value)

    if entity.top_radius > 1e-9:
        center_id = points.InsertNextPoint(*transform_point(entity.transform, (0.0, 0.0, entity.length)))
        for i in range(sides):
            next_i = (i + 1) % sides
            insert_triangle(polys, center_id, top_ids[i], top_ids[next_i], cell_scalars, scalar_value)


def add_normals(polydata: vtk.vtkPolyData) -> vtk.vtkPolyData:
    normals = vtk.vtkPolyDataNormals()
    normals.SetInputData(polydata)
    normals.ConsistencyOn()
    normals.AutoOrientNormalsOn()
    normals.SplittingOff()
    normals.Update()

    return normals.GetOutput()


def build_channel_polydata(
    entities: list[NeuronCadEntity],
    sides: int,
    channel_name: str,
) -> tuple[vtk.vtkPolyData | None, vtk.vtkPolyData | None, tuple[float, float] | None]:
    channel_points = vtk.vtkPoints()
    channel_polys = vtk.vtkCellArray()
    channel_scalars = vtk.vtkDoubleArray()
    channel_scalars.SetName(f"{channel_name} G")

    missing_points = vtk.vtkPoints()
    missing_polys = vtk.vtkCellArray()

    values = []

    for entity in entities:
        if channel_name in entity.channels:
            value = entity.channels[channel_name]
            values.append(value)
            append_frustum(entity, channel_points, channel_polys, sides, channel_scalars, value)
        else:
            append_frustum(entity, missing_points, missing_polys, sides)

    channel_polydata = None
    if channel_points.GetNumberOfPoints() > 0:
        channel_polydata = vtk.vtkPolyData()
        channel_polydata.SetPoints(channel_points)
        channel_polydata.SetPolys(channel_polys)
        channel_polydata.GetCellData().SetScalars(channel_scalars)
        channel_polydata = add_normals(channel_polydata)

    missing_polydata = None
    if missing_points.GetNumberOfPoints() > 0:
        missing_polydata = vtk.vtkPolyData()
        missing_polydata.SetPoints(missing_points)
        missing_polydata.SetPolys(missing_polys)
        missing_polydata = add_normals(missing_polydata)

    scalar_range = (min(values), max(values)) if values else None
    return channel_polydata, missing_polydata, scalar_range


def build_color_polydata_by_color(
    entities: list[NeuronCadEntity],
    sides: int,
) -> dict[tuple[float, float, float], vtk.vtkPolyData]:
    points_by_color: dict[tuple[float, float, float], vtk.vtkPoints] = {}
    polys_by_color: dict[tuple[float, float, float], vtk.vtkCellArray] = {}

    for entity in entities:
        if entity.color not in points_by_color:
            points_by_color[entity.color] = vtk.vtkPoints()
            polys_by_color[entity.color] = vtk.vtkCellArray()

        append_frustum(entity, points_by_color[entity.color], polys_by_color[entity.color], sides)

    polydata_by_color = {}
    for color, points in points_by_color.items():
        polydata = vtk.vtkPolyData()
        polydata.SetPoints(points)
        polydata.SetPolys(polys_by_color[color])
        polydata_by_color[color] = add_normals(polydata)

    return polydata_by_color


def make_heat_lut() -> vtk.vtkLookupTable:
    lut = vtk.vtkLookupTable()
    lut.SetNumberOfTableValues(256)
    lut.Build()

    transfer = vtk.vtkColorTransferFunction()
    transfer.SetColorSpaceToRGB()
    transfer.AddRGBPoint(0.00, 0.00, 0.00, 1.00)
    transfer.AddRGBPoint(0.33, 0.00, 1.00, 1.00)
    transfer.AddRGBPoint(0.66, 1.00, 1.00, 0.00)
    transfer.AddRGBPoint(1.00, 1.00, 0.00, 0.00)

    for i in range(256):
        t = i / 255.0
        r, g, b = transfer.GetColor(t)
        lut.SetTableValue(i, r, g, b, 1.0)

    return lut


def apply_lighting(actor: vtk.vtkActor, shadow_strength: float) -> None:
    strength = min(1.0, max(0.0, shadow_strength))
    prop = actor.GetProperty()

    if strength == 0.0:
        prop.LightingOff()
        return

    prop.LightingOn()
    prop.SetAmbient(0.75 - 0.45 * strength)
    prop.SetDiffuse(0.25 + 0.45 * strength)
    prop.SetSpecular(0.02 + 0.08 * strength)
    prop.SetSpecularPower(6.0 + 24.0 * strength)


def make_mesh_actor(
    polydata: vtk.vtkPolyData,
    color: tuple[float, float, float],
    shadow_strength: float,
) -> vtk.vtkActor:
    mapper = vtk.vtkPolyDataMapper()
    mapper.SetInputData(polydata)
    mapper.ScalarVisibilityOff()

    actor = vtk.vtkActor()
    actor.SetMapper(mapper)
    actor.GetProperty().SetColor(*color)
    apply_lighting(actor, shadow_strength)

    return actor


def make_channel_actor(
    polydata: vtk.vtkPolyData,
    scalar_range: tuple[float, float],
    lut: vtk.vtkLookupTable,
    shadow_strength: float,
) -> vtk.vtkActor:
    mapper = vtk.vtkPolyDataMapper()
    mapper.SetInputData(polydata)
    mapper.SetScalarModeToUseCellData()
    mapper.ScalarVisibilityOn()
    mapper.SetLookupTable(lut)

    min_g, max_g = scalar_range
    if min_g == max_g:
        epsilon = max(abs(min_g) * 0.01, 1e-12)
        display_range = (min_g - epsilon, max_g + epsilon)
    else:
        display_range = (min_g, max_g)

    lut.SetRange(*display_range)
    mapper.SetScalarRange(*display_range)

    actor = vtk.vtkActor()
    actor.SetMapper(mapper)
    apply_lighting(actor, shadow_strength)

    return actor


def make_scalar_bar(
    lut: vtk.vtkLookupTable,
    channel_name: str,
    scalar_range: tuple[float, float],
) -> vtk.vtkScalarBarActor:
    scalar_bar = vtk.vtkScalarBarActor()
    scalar_bar.SetLookupTable(lut)
    scalar_bar.SetTitle(f"{channel_name} G\n{scalar_range[0]:.4g} - {scalar_range[1]:.4g}")
    scalar_bar.SetNumberOfLabels(5)
    scalar_bar.SetMaximumWidthInPixels(90)
    scalar_bar.SetMaximumHeightInPixels(320)
    scalar_bar.GetTitleTextProperty().SetColor(0.0, 0.0, 0.0)
    scalar_bar.GetLabelTextProperty().SetColor(0.0, 0.0, 0.0)

    return scalar_bar


def anchor_to_world_point(entity: NeuronCadEntity, anchor: dict) -> tuple[float, float, float]:
    mode = anchor.get("Mode", "AxonCylinder")

    if mode == "AxonCapStart":
        local = (0.0, 0.0, 0.0)
    elif mode == "AxonCapEnd":
        local = (0.0, 0.0, entity.length)
    else:
        t = min(1.0, max(0.0, float(anchor.get("AxialT", 0.5))))
        angle = float(anchor.get("Angle", 0.0))
        radius = entity.base_radius + (entity.top_radius - entity.base_radius) * t
        local = (radius * math.cos(angle), radius * math.sin(angle), entity.length * t)

    return transform_point(entity.transform, local)


def device_axial_t(anchor: dict) -> float:
    try:
        return min(1.0, max(0.0, float(anchor.get("AxialT", 0.5))))
    except (TypeError, ValueError):
        return 0.5


def entity_radius_at(entity: NeuronCadEntity, axial_t: float) -> float:
    return entity.base_radius + (entity.top_radius - entity.base_radius) * axial_t


def make_device_ring_actor(
    entity: NeuronCadEntity,
    device: dict,
    shadow_strength: float,
    ring_gap: float = 0.45,
    tube_radius: float = 0.16,
    ring_segments: int = 96,
    tube_segments: int = 12,
) -> vtk.vtkActor:
    anchor = device.get("Anchor", {})
    axial_t = device_axial_t(anchor)
    z = axial_t * entity.length
    entity_radius = max(0.0, entity_radius_at(entity, axial_t))
    major_radius = entity_radius + ring_gap + tube_radius

    points = vtk.vtkPoints()
    polys = vtk.vtkCellArray()

    point_ids = []
    for i in range(ring_segments):
        theta = 2.0 * math.pi * i / ring_segments
        cos_theta = math.cos(theta)
        sin_theta = math.sin(theta)

        row = []
        for j in range(tube_segments):
            phi = 2.0 * math.pi * j / tube_segments
            radial = major_radius + tube_radius * math.cos(phi)
            local = (
                radial * cos_theta,
                radial * sin_theta,
                z + tube_radius * math.sin(phi),
            )
            row.append(points.InsertNextPoint(*transform_point(entity.transform, local)))
        point_ids.append(row)

    for i in range(ring_segments):
        next_i = (i + 1) % ring_segments
        for j in range(tube_segments):
            next_j = (j + 1) % tube_segments
            p00 = point_ids[i][j]
            p10 = point_ids[next_i][j]
            p01 = point_ids[i][next_j]
            p11 = point_ids[next_i][next_j]
            insert_triangle(polys, p00, p10, p01)
            insert_triangle(polys, p01, p10, p11)

    polydata = vtk.vtkPolyData()
    polydata.SetPoints(points)
    polydata.SetPolys(polys)
    polydata = add_normals(polydata)

    mapper = vtk.vtkPolyDataMapper()
    mapper.SetInputData(polydata)
    mapper.ScalarVisibilityOff()

    actor = vtk.vtkActor()
    actor.SetMapper(mapper)
    if device.get("Type") == "Probe":
        actor.GetProperty().SetColor(*PROBE_COLOR)
    else:
        actor.GetProperty().SetColor(*STIMULATION_COLOR)
    apply_lighting(actor, shadow_strength)

    return actor


def make_connection_actor(
    entities_by_id: dict[str, NeuronCadEntity],
    connections: list[dict],
) -> vtk.vtkActor | None:
    points = vtk.vtkPoints()
    lines = vtk.vtkCellArray()

    for connection in connections:
        entity_a = entities_by_id.get(str(connection.get("EntityA_Id", "")))
        entity_b = entities_by_id.get(str(connection.get("EntityB_Id", "")))
        if entity_a is None or entity_b is None:
            continue

        point_a = anchor_to_world_point(entity_a, connection.get("AnchorA", {}))
        point_b = anchor_to_world_point(entity_b, connection.get("AnchorB", {}))

        id_a = points.InsertNextPoint(*point_a)
        id_b = points.InsertNextPoint(*point_b)

        line = vtk.vtkLine()
        line.GetPointIds().SetId(0, id_a)
        line.GetPointIds().SetId(1, id_b)
        lines.InsertNextCell(line)

    if points.GetNumberOfPoints() == 0:
        return None

    polydata = vtk.vtkPolyData()
    polydata.SetPoints(points)
    polydata.SetLines(lines)

    mapper = vtk.vtkPolyDataMapper()
    mapper.SetInputData(polydata)

    actor = vtk.vtkActor()
    actor.SetMapper(mapper)
    actor.GetProperty().SetColor(*CONNECTION_COLOR)
    actor.GetProperty().SetLineWidth(1.0)

    return actor


def make_device_actors(
    entities_by_id: dict[str, NeuronCadEntity],
    devices: list[dict],
    shadow_strength: float,
) -> list[vtk.vtkActor]:
    actors = []

    for device in devices:
        entity = entities_by_id.get(str(device.get("TargetEntityId", "")))
        if entity is None:
            continue

        actors.append(make_device_ring_actor(entity, device, shadow_strength))

    return actors


def make_axes_actor(bounds: tuple[float, float, float, float, float, float], camera: vtk.vtkCamera) -> vtk.vtkCubeAxesActor:
    axes = vtk.vtkCubeAxesActor()
    axes.SetBounds(bounds)
    axes.SetCamera(camera)
    axes.SetXTitle("X")
    axes.SetYTitle("Y")
    axes.SetZTitle("Z")
    axes.SetFlyModeToOuterEdges()
    axes.SetTickLocationToOutside()
    axes.DrawXGridlinesOn()
    axes.DrawYGridlinesOn()
    axes.DrawZGridlinesOn()
    axes.SetGridLineLocation(axes.VTK_GRID_LINES_FURTHEST)

    for axis_index in range(3):
        axes.GetTitleTextProperty(axis_index).SetColor(*AXIS_COLOR)
        axes.GetLabelTextProperty(axis_index).SetColor(*AXIS_COLOR)

    axes.GetXAxesLinesProperty().SetColor(*AXIS_COLOR)
    axes.GetYAxesLinesProperty().SetColor(*AXIS_COLOR)
    axes.GetZAxesLinesProperty().SetColor(*AXIS_COLOR)
    axes.GetXAxesGridlinesProperty().SetColor(0.85, 0.85, 0.85)
    axes.GetYAxesGridlinesProperty().SetColor(0.85, 0.85, 0.85)
    axes.GetZAxesGridlinesProperty().SetColor(0.85, 0.85, 0.85)

    return axes


def expand_bounds(
    combined_bounds: list[float],
    bounds: tuple[float, float, float, float, float, float],
) -> None:
    combined_bounds[0] = min(combined_bounds[0], bounds[0])
    combined_bounds[1] = max(combined_bounds[1], bounds[1])
    combined_bounds[2] = min(combined_bounds[2], bounds[2])
    combined_bounds[3] = max(combined_bounds[3], bounds[3])
    combined_bounds[4] = min(combined_bounds[4], bounds[4])
    combined_bounds[5] = max(combined_bounds[5], bounds[5])


def show_neuroncad_json(
    json_path: Path,
    sides: int,
    show_connections: bool,
    show_devices: bool,
    shadow_strength: float,
    selected_channel: str | None = None,
    force_json_colors: bool = False,
    parent_hwnd: int | None = None,
    window_size: tuple[int, int] = (1100, 850),
) -> None:
    entities, connections, devices = read_neuroncad_json(json_path)
    if not entities:
        raise ValueError(f"No drawable entities found in {json_path}")

    if not force_json_colors and selected_channel is None:
        selected_channel = select_channel_from_menu(collect_channel_stats(entities))

    renderer = vtk.vtkRenderer()
    renderer.SetBackground(1.0, 1.0, 1.0)

    combined_bounds = [float("inf"), float("-inf"), float("inf"), float("-inf"), float("inf"), float("-inf")]

    if selected_channel is None:
        for color, polydata in build_color_polydata_by_color(entities, sides).items():
            renderer.AddActor(make_mesh_actor(polydata, color, shadow_strength))
            expand_bounds(combined_bounds, polydata.GetBounds())
    else:
        channel_polydata, missing_polydata, scalar_range = build_channel_polydata(entities, sides, selected_channel)
        if channel_polydata is not None and scalar_range is not None:
            heat_lut = make_heat_lut()
            renderer.AddActor(make_channel_actor(channel_polydata, scalar_range, heat_lut, shadow_strength))
            renderer.AddActor2D(make_scalar_bar(heat_lut, selected_channel, scalar_range))
            expand_bounds(combined_bounds, channel_polydata.GetBounds())

        if missing_polydata is not None:
            renderer.AddActor(make_mesh_actor(missing_polydata, MISSING_CHANNEL_COLOR, shadow_strength))
            expand_bounds(combined_bounds, missing_polydata.GetBounds())

    entities_by_id = {entity.entity_id: entity for entity in entities}

    if show_connections:
        connection_actor = make_connection_actor(entities_by_id, connections)
        if connection_actor is not None:
            renderer.AddActor(connection_actor)

    if show_devices:
        for actor in make_device_actors(entities_by_id, devices, shadow_strength):
            renderer.AddActor(actor)
            expand_bounds(combined_bounds, actor.GetBounds())

    window = vtk.vtkRenderWindow()
    if parent_hwnd is not None:
        print(f"Embedding VTK render window in HWND {parent_hwnd}.", flush=True)
        window.SetParentInfo(str(parent_hwnd))
    window.AddRenderer(renderer)
    parent_size = get_parent_client_size(parent_hwnd)
    window.SetSize(*(parent_size or window_size))
    window.SetWindowName(f"NeuronCAD JSON Viewer - {json_path.name}")

    interactor = vtk.vtkRenderWindowInteractor()
    interactor.SetRenderWindow(window)
    interactor.SetInteractorStyle(vtk.vtkInteractorStyleTrackballCamera())

    renderer.AddActor(make_axes_actor(tuple(combined_bounds), renderer.GetActiveCamera()))
    renderer.ResetCamera()
    window.Render()
    interactor.Initialize()
    attach_parent_resize_sync(interactor, window, parent_hwnd)
    print(f"VTK viewer ready: {json_path}", flush=True)
    interactor.Start()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Draw NeuronCAD JSON geometry with VTK.")
    parser.add_argument(
        "json_file",
        nargs="?",
        default=str(DEFAULT_JSON_FILE),
        help=f"Path to a NeuronCAD JSON file. Default: {DEFAULT_JSON_FILE}",
    )
    parser.add_argument(
        "--sides",
        type=int,
        default=18,
        help="Number of radial sides used for each frustum. Default: 18",
    )
    parser.add_argument(
        "--connections",
        action="store_true",
        help="Draw topology connections as thin black lines.",
    )
    parser.add_argument(
        "--hide-devices",
        action="store_true",
        help="Do not draw stimulation/probe device markers.",
    )
    parser.add_argument(
        "--shadow-strength",
        type=float,
        default=DEFAULT_SHADOW_STRENGTH,
        help="Surface lighting strength from 0 to 1. 0 disables shading, 1 is strongest. Default: 0.35",
    )
    parser.add_argument(
        "--channel",
        help="Render by this channel without showing the interactive channel menu.",
    )
    parser.add_argument(
        "--json-colors",
        action="store_true",
        help="Render directly by JSON entity colors and skip the channel menu.",
    )
    parser.add_argument(
        "--parent-hwnd",
        type=lambda value: int(value, 0),
        help="Embed the VTK render window in the provided Win32 parent HWND.",
    )
    parser.add_argument(
        "--window-size",
        type=parse_window_size,
        default=(1100, 850),
        help="Initial render window size as WIDTHxHEIGHT. Default: 1100x850",
    )
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    input_path = resolve_input_path(args.json_file)
    show_neuroncad_json(
        json_path=input_path,
        sides=max(6, args.sides),
        show_connections=args.connections,
        show_devices=not args.hide_devices,
        shadow_strength=args.shadow_strength,
        selected_channel=args.channel,
        force_json_colors=args.json_colors,
        parent_hwnd=args.parent_hwnd,
        window_size=args.window_size,
    )
