from __future__ import annotations

import argparse
import io
import json
import math
import zipfile
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import vtk


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

HISTORY_ARRAY_NAMES = {
    "V": "HISTORY_V",
    "m": "HISTORY_M",
    "h": "HISTORY_H",
    "n": "HISTORY_N",
    "Ca": "HISTORY_CA",
    "mT": "HISTORY_MT",
    "hT": "HISTORY_HT",
}


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
class SceneCompartment:
    global_id: int
    parent_entity_id: str
    index: int
    axial_start: float
    axial_end: float


def parse_argb_color(value: str | None) -> tuple[float, float, float] | None:
    if not value or not value.startswith("#"):
        return None

    raw = value[1:]
    if len(raw) == 8:
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
    return Path.cwd() / path


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


def read_scene_payload(
    path: Path,
) -> tuple[list[NeuronCadEntity], list[SceneCompartment], list[dict], list[dict]]:
    if not path.is_file():
        raise FileNotFoundError(f"Scene payload was not found: {path}")

    with path.open("r", encoding="utf-8") as file:
        data = json.load(file)

    entities: list[NeuronCadEntity] = []
    for item in data.get("Entities", data.get("entities", [])):
        entity_type = str(item.get("Type", item.get("type", "Unknown")))
        color = parse_argb_color(item.get("Color", item.get("color"))) or TYPE_COLORS.get(entity_type, DEFAULT_COLOR)
        transform = tuple(float(v) for v in item.get("Transform", item.get("transform", [])))
        if len(transform) != 16:
            continue

        channels: dict[str, float] = {}
        for channel_name, channel_data in (item.get("Channels", item.get("channels", {})) or {}).items():
            raw_g = channel_data.get("G", channel_data.get("g")) if isinstance(channel_data, dict) else channel_data
            try:
                channels[str(channel_name)] = float(raw_g)
            except (TypeError, ValueError):
                continue

        try:
            entity = NeuronCadEntity(
                entity_id=str(item.get("Id", item.get("id", ""))),
                entity_type=entity_type,
                base_radius=float(item.get("BaseRadius", item.get("baseRadius", 0.0))),
                top_radius=float(item.get("TopRadius", item.get("topRadius", 0.0))),
                length=float(item.get("Length", item.get("length", 0.0))),
                transform=transform,
                color=color,
                channels=channels,
            )
        except (TypeError, ValueError):
            continue

        if entity.length > 0.0 and (entity.base_radius > 0.0 or entity.top_radius > 0.0):
            entities.append(entity)

    compartments: list[SceneCompartment] = []
    for item in data.get("Compartments", data.get("compartments", [])):
        try:
            compartments.append(
                SceneCompartment(
                    global_id=int(item.get("GlobalId", item.get("globalId"))),
                    parent_entity_id=str(item.get("ParentEntityId", item.get("parentEntityId", ""))),
                    index=int(item.get("Index", item.get("index", 0))),
                    axial_start=float(item.get("AxialStart", item.get("axialStart", 0.0))),
                    axial_end=float(item.get("AxialEnd", item.get("axialEnd", 1.0))),
                )
            )
        except (TypeError, ValueError):
            continue

    connections = data.get("Connections", data.get("connections", [])) or []
    devices = data.get("Devices", data.get("devices", [])) or []
    return entities, compartments, connections, devices


def transform_point(matrix: tuple[float, ...], point: tuple[float, float, float]) -> tuple[float, float, float]:
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


def entity_radius_at(entity: NeuronCadEntity, axial_t: float) -> float:
    return entity.base_radius + (entity.top_radius - entity.base_radius) * axial_t


def append_frustum(
    entity: NeuronCadEntity,
    points: vtk.vtkPoints,
    polys: vtk.vtkCellArray,
    sides: int,
    cell_scalars: vtk.vtkDoubleArray | None = None,
    scalar_value: float | None = None,
) -> None:
    append_frustum_range(
        entity,
        points,
        polys,
        sides,
        0.0,
        1.0,
        cell_scalars,
        scalar_value,
    )


def append_frustum_range(
    entity: NeuronCadEntity,
    points: vtk.vtkPoints,
    polys: vtk.vtkCellArray,
    sides: int,
    axial_start: float,
    axial_end: float,
    cell_scalars: vtk.vtkDoubleArray | None = None,
    scalar_value: float | None = None,
) -> tuple[int, int]:
    axial_start = min(1.0, max(0.0, axial_start))
    axial_end = min(1.0, max(0.0, axial_end))
    if axial_end < axial_start:
        axial_start, axial_end = axial_end, axial_start

    first_cell = polys.GetNumberOfCells()
    start_radius = entity_radius_at(entity, axial_start)
    end_radius = entity_radius_at(entity, axial_end)
    start_z = entity.length * axial_start
    end_z = entity.length * axial_end

    base_ids: list[int] = []
    top_ids: list[int] = []

    for i in range(sides):
        angle = 2.0 * math.pi * i / sides
        cos_a = math.cos(angle)
        sin_a = math.sin(angle)

        base_local = (start_radius * cos_a, start_radius * sin_a, start_z)
        top_local = (end_radius * cos_a, end_radius * sin_a, end_z)

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

    if axial_start <= 1e-9 and start_radius > 1e-9:
        center_id = points.InsertNextPoint(*transform_point(entity.transform, (0.0, 0.0, start_z)))
        for i in range(sides):
            next_i = (i + 1) % sides
            insert_triangle(polys, center_id, base_ids[next_i], base_ids[i], cell_scalars, scalar_value)

    if axial_end >= 1.0 - 1e-9 and end_radius > 1e-9:
        center_id = points.InsertNextPoint(*transform_point(entity.transform, (0.0, 0.0, end_z)))
        for i in range(sides):
            next_i = (i + 1) % sides
            insert_triangle(polys, center_id, top_ids[i], top_ids[next_i], cell_scalars, scalar_value)

    return first_cell, polys.GetNumberOfCells()


def add_normals(polydata: vtk.vtkPolyData) -> vtk.vtkPolyData:
    normals = vtk.vtkPolyDataNormals()
    normals.SetInputData(polydata)
    normals.ConsistencyOn()
    normals.AutoOrientNormalsOn()
    normals.SplittingOff()
    normals.Update()
    return normals.GetOutput()


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
    values: list[float] = []

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


def read_history_npz(path: Path, variable: str) -> tuple[np.ndarray, dict]:
    if not path.is_file():
        raise FileNotFoundError(f"History NPZ was not found: {path}")

    array_name = HISTORY_ARRAY_NAMES.get(variable)
    if array_name is None:
        raise ValueError(f"Unsupported history variable {variable!r}. Choose one of {', '.join(HISTORY_ARRAY_NAMES)}.")

    with zipfile.ZipFile(path, mode="r") as archive:
        entry_name = f"{array_name}.npy"
        if entry_name not in archive.namelist():
            raise ValueError(f"History archive does not contain {entry_name}.")

        manifest = {}
        if "manifest.json" in archive.namelist():
            manifest = json.loads(archive.read("manifest.json").decode("utf-8"))

        with archive.open(entry_name) as entry:
            matrix = np.load(io.BytesIO(entry.read()), allow_pickle=False)

    if matrix.ndim != 2:
        raise ValueError(f"{array_name} must be a 2D array, got shape {matrix.shape}.")
    if matrix.shape[0] == 0 or matrix.shape[1] == 0:
        raise ValueError(f"{array_name} must contain at least one frame and one node, got shape {matrix.shape}.")

    return np.asarray(matrix, dtype=float), manifest


def build_history_polydata(
    entities: list[NeuronCadEntity],
    compartments: list[SceneCompartment],
    sides: int,
    initial_values: np.ndarray,
) -> tuple[vtk.vtkPolyData, vtk.vtkDoubleArray, dict[int, tuple[int, int]]]:
    entities_by_id = {entity.entity_id: entity for entity in entities}
    points = vtk.vtkPoints()
    polys = vtk.vtkCellArray()
    scalars = vtk.vtkDoubleArray()
    scalars.SetName("History")
    cell_ranges_by_gid: dict[int, tuple[int, int]] = {}

    for compartment in sorted(compartments, key=lambda item: item.global_id):
        entity = entities_by_id.get(compartment.parent_entity_id)
        if entity is None:
            continue
        if compartment.global_id < 0 or compartment.global_id >= len(initial_values):
            continue

        value = float(initial_values[compartment.global_id])
        first_cell, next_cell = append_frustum_range(
            entity,
            points,
            polys,
            sides,
            compartment.axial_start,
            compartment.axial_end,
            scalars,
            value,
        )
        cell_ranges_by_gid[compartment.global_id] = (first_cell, next_cell)

    if points.GetNumberOfPoints() == 0:
        raise ValueError("No history compartments could be mapped to drawable entities.")

    polydata = vtk.vtkPolyData()
    polydata.SetPoints(points)
    polydata.SetPolys(polys)
    polydata.GetCellData().SetScalars(scalars)
    polydata = add_normals(polydata)

    output_scalars = polydata.GetCellData().GetScalars()
    if output_scalars is None:
        raise ValueError("History mesh did not preserve cell scalars.")

    return polydata, output_scalars, cell_ranges_by_gid


def update_history_scalars(
    scalars: vtk.vtkDoubleArray,
    cell_ranges_by_gid: dict[int, tuple[int, int]],
    frame_values: np.ndarray,
) -> None:
    for global_id, (first_cell, next_cell) in cell_ranges_by_gid.items():
        value = float(frame_values[global_id])
        for cell_id in range(first_cell, next_cell):
            scalars.SetValue(cell_id, value)
    scalars.Modified()


def scalar_display_range(values: np.ndarray | tuple[float, float]) -> tuple[float, float]:
    if isinstance(values, tuple):
        min_value, max_value = values
    else:
        finite = values[np.isfinite(values)]
        if finite.size == 0:
            return (0.0, 1.0)
        min_value = float(np.min(finite))
        max_value = float(np.max(finite))

    if min_value == max_value:
        epsilon = max(abs(min_value) * 0.01, 1e-12)
        return (min_value - epsilon, max_value + epsilon)

    return (float(min_value), float(max_value))


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


def make_scalar_actor(
    polydata: vtk.vtkPolyData,
    value_range: tuple[float, float],
    lut: vtk.vtkLookupTable,
    shadow_strength: float,
) -> vtk.vtkActor:
    mapper = vtk.vtkPolyDataMapper()
    mapper.SetInputData(polydata)
    mapper.SetScalarModeToUseCellData()
    mapper.ScalarVisibilityOn()
    mapper.SetLookupTable(lut)
    mapper.SetScalarRange(*value_range)

    lut.SetRange(*value_range)

    actor = vtk.vtkActor()
    actor.SetMapper(mapper)
    apply_lighting(actor, shadow_strength)
    return actor


def make_scalar_bar(
    lut: vtk.vtkLookupTable,
    title: str,
    value_range: tuple[float, float],
) -> vtk.vtkScalarBarActor:
    scalar_bar = vtk.vtkScalarBarActor()
    scalar_bar.SetLookupTable(lut)
    scalar_bar.SetTitle(f"{title}\n{value_range[0]:.4g} - {value_range[1]:.4g}")
    scalar_bar.SetNumberOfLabels(5)
    scalar_bar.SetMaximumWidthInPixels(90)
    scalar_bar.SetMaximumHeightInPixels(320)
    scalar_bar.GetTitleTextProperty().SetColor(0.0, 0.0, 0.0)
    scalar_bar.GetLabelTextProperty().SetColor(0.0, 0.0, 0.0)
    return scalar_bar


def make_playback_slider(
    interactor: vtk.vtkRenderWindowInteractor,
    title: str,
    value_range: tuple[float, float],
    initial_value: float,
    point1: tuple[float, float],
    point2: tuple[float, float],
    width: float = 0.018,
) -> tuple[vtk.vtkSliderWidget, vtk.vtkSliderRepresentation2D]:
    representation = vtk.vtkSliderRepresentation2D()
    representation.SetMinimumValue(value_range[0])
    representation.SetMaximumValue(value_range[1])
    representation.SetValue(initial_value)
    representation.SetTitleText(title)
    representation.SetLabelFormat("%.0f")
    representation.SetSliderWidth(width)
    representation.SetTubeWidth(width * 0.45)
    representation.SetEndCapWidth(width * 0.7)
    representation.SetEndCapLength(width * 0.7)
    representation.GetPoint1Coordinate().SetCoordinateSystemToNormalizedDisplay()
    representation.GetPoint1Coordinate().SetValue(point1[0], point1[1])
    representation.GetPoint2Coordinate().SetCoordinateSystemToNormalizedDisplay()
    representation.GetPoint2Coordinate().SetValue(point2[0], point2[1])
    representation.GetTitleProperty().SetColor(0.0, 0.0, 0.0)
    representation.GetLabelProperty().SetColor(0.0, 0.0, 0.0)
    representation.GetTubeProperty().SetColor(0.74, 0.74, 0.74)
    representation.GetSliderProperty().SetColor(0.08, 0.36, 0.9)
    representation.GetSelectedProperty().SetColor(0.0, 0.58, 1.0)

    widget = vtk.vtkSliderWidget()
    widget.SetInteractor(interactor)
    widget.SetRepresentation(representation)
    widget.SetAnimationModeToAnimate()
    widget.EnabledOn()
    return widget, representation


def make_speed_slider(
    interactor: vtk.vtkRenderWindowInteractor,
    initial_speed: float,
) -> tuple[vtk.vtkSliderWidget, vtk.vtkSliderRepresentation2D]:
    widget, representation = make_playback_slider(
        interactor=interactor,
        title="Speed",
        value_range=(0.25, 4.0),
        initial_value=initial_speed,
        point1=(0.76, 0.055),
        point2=(0.95, 0.055),
        width=0.016,
    )
    representation.SetLabelFormat("%.2fx")
    representation.GetSliderProperty().SetColor(0.0, 0.56, 0.42)
    representation.GetSelectedProperty().SetColor(0.0, 0.72, 0.54)
    return widget, representation


def make_play_pause_actor() -> vtk.vtkTextActor:
    actor = vtk.vtkTextActor()
    actor.GetPositionCoordinate().SetCoordinateSystemToNormalizedViewport()
    actor.SetPosition(0.035, 0.028)
    actor.GetTextProperty().SetFontSize(22)
    actor.GetTextProperty().SetBold(True)
    actor.GetTextProperty().SetColor(0.0, 0.0, 0.0)
    actor.GetTextProperty().SetBackgroundColor(0.9, 0.9, 0.9)
    actor.GetTextProperty().SetBackgroundOpacity(0.85)
    actor.GetTextProperty().SetFrame(True)
    actor.GetTextProperty().SetFrameColor(0.2, 0.2, 0.2)
    actor.SetInput("Pause")
    return actor


def anchor_to_world_point(entity: NeuronCadEntity, anchor: dict) -> tuple[float, float, float]:
    mode = anchor.get("Mode", anchor.get("mode", "AxonCylinder"))

    if mode == "AxonCapStart":
        local = (0.0, 0.0, 0.0)
    elif mode == "AxonCapEnd":
        local = (0.0, 0.0, entity.length)
    else:
        t = min(1.0, max(0.0, float(anchor.get("AxialT", anchor.get("axialT", 0.5)))))
        angle = float(anchor.get("Angle", anchor.get("angle", 0.0)))
        radius = entity_radius_at(entity, t)
        local = (radius * math.cos(angle), radius * math.sin(angle), entity.length * t)

    return transform_point(entity.transform, local)


def device_axial_t(anchor: dict) -> float:
    try:
        return min(1.0, max(0.0, float(anchor.get("AxialT", anchor.get("axialT", 0.5)))))
    except (TypeError, ValueError):
        return 0.5


def make_device_ring_actor(
    entity: NeuronCadEntity,
    device: dict,
    shadow_strength: float,
    ring_gap: float = 0.45,
    tube_radius: float = 0.16,
    ring_segments: int = 96,
    tube_segments: int = 12,
) -> vtk.vtkActor:
    anchor = device.get("Anchor", device.get("anchor", {}))
    axial_t = device_axial_t(anchor)
    z = axial_t * entity.length
    entity_radius = max(0.0, entity_radius_at(entity, axial_t))
    major_radius = entity_radius + ring_gap + tube_radius

    points = vtk.vtkPoints()
    polys = vtk.vtkCellArray()
    point_ids: list[list[int]] = []

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
    device_type = str(device.get("Type", device.get("type", "")))
    actor.GetProperty().SetColor(*(PROBE_COLOR if device_type == "Probe" else STIMULATION_COLOR))
    apply_lighting(actor, shadow_strength)
    return actor


def make_connection_actor(
    entities_by_id: dict[str, NeuronCadEntity],
    connections: list[dict],
) -> vtk.vtkActor | None:
    points = vtk.vtkPoints()
    lines = vtk.vtkCellArray()

    for connection in connections:
        entity_a = entities_by_id.get(str(connection.get("EntityAId", connection.get("entityAId", ""))))
        entity_b = entities_by_id.get(str(connection.get("EntityBId", connection.get("entityBId", ""))))
        if entity_a is None or entity_b is None:
            continue

        point_a = anchor_to_world_point(entity_a, connection.get("AnchorA", connection.get("anchorA", {})))
        point_b = anchor_to_world_point(entity_b, connection.get("AnchorB", connection.get("anchorB", {})))

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
        entity = entities_by_id.get(str(device.get("TargetEntityId", device.get("targetEntityId", ""))))
        if entity is not None:
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
    if not all(math.isfinite(value) for value in bounds):
        return

    combined_bounds[0] = min(combined_bounds[0], bounds[0])
    combined_bounds[1] = max(combined_bounds[1], bounds[1])
    combined_bounds[2] = min(combined_bounds[2], bounds[2])
    combined_bounds[3] = max(combined_bounds[3], bounds[3])
    combined_bounds[4] = min(combined_bounds[4], bounds[4])
    combined_bounds[5] = max(combined_bounds[5], bounds[5])


def finalize_camera(renderer: vtk.vtkRenderer, bounds: tuple[float, float, float, float, float, float]) -> None:
    renderer.ResetCamera(bounds)
    camera = renderer.GetActiveCamera()
    camera.SetViewUp(0.0, 1.0, 0.0)
    renderer.ResetCameraClippingRange(bounds)


class NeuronCadInteractorStyle(vtk.vtkInteractorStyleTrackballCamera):
    """Match the Helix viewport controls: left pan, right rotate, wheel zoom, middle roll."""

    def __init__(self) -> None:
        super().__init__()
        self._mode: str | None = None
        self._last_roll_pos: tuple[int, int] | None = None
        self.AddObserver("LeftButtonPressEvent", self._on_left_press)
        self.AddObserver("LeftButtonReleaseEvent", self._on_left_release)
        self.AddObserver("RightButtonPressEvent", self._on_right_press)
        self.AddObserver("RightButtonReleaseEvent", self._on_right_release)
        self.AddObserver("MiddleButtonPressEvent", self._on_middle_press)
        self.AddObserver("MiddleButtonReleaseEvent", self._on_middle_release)
        self.AddObserver("MouseMoveEvent", self._on_mouse_move)

    def _current_renderer(self) -> vtk.vtkRenderer | None:
        interactor = self.GetInteractor()
        if interactor is None:
            return None

        x, y = interactor.GetEventPosition()
        self.FindPokedRenderer(x, y)
        return self.GetCurrentRenderer()

    def _is_playback_control_area(self) -> bool:
        interactor = self.GetInteractor()
        if interactor is None:
            return False

        render_window = interactor.GetRenderWindow()
        if render_window is None:
            return False

        _width, height = render_window.GetSize()
        if height <= 0:
            return False

        _x, y = interactor.GetEventPosition()
        return y / height <= 0.12

    def _on_left_press(self, _obj, _event) -> None:
        if self._is_playback_control_area():
            return
        if self._current_renderer() is None:
            return

        self._mode = "pan"
        self.StartPan()

    def _on_left_release(self, _obj, _event) -> None:
        if self._mode == "pan":
            self.EndPan()
        self._mode = None

    def _on_right_press(self, _obj, _event) -> None:
        if self._current_renderer() is None:
            return

        self._mode = "rotate"
        self.StartRotate()

    def _on_right_release(self, _obj, _event) -> None:
        if self._mode == "rotate":
            self.EndRotate()
        self._mode = None

    def _on_middle_press(self, _obj, _event) -> None:
        if self._current_renderer() is None:
            return

        interactor = self.GetInteractor()
        self._mode = "roll"
        self._last_roll_pos = interactor.GetEventPosition()

    def _on_middle_release(self, _obj, _event) -> None:
        self._mode = None
        self._last_roll_pos = None

    def _on_mouse_move(self, _obj, _event) -> None:
        if self._mode == "pan":
            self.Pan()
        elif self._mode == "rotate":
            self.Rotate()
        elif self._mode == "roll":
            self._roll_camera()

    def _roll_camera(self) -> None:
        interactor = self.GetInteractor()
        renderer = self.GetCurrentRenderer()
        if interactor is None or renderer is None or self._last_roll_pos is None:
            return

        current = interactor.GetEventPosition()
        width, height = interactor.GetRenderWindow().GetSize()
        center = (width * 0.5, height * 0.5)

        previous_vector = (
            self._last_roll_pos[0] - center[0],
            self._last_roll_pos[1] - center[1],
        )
        current_vector = (
            current[0] - center[0],
            current[1] - center[1],
        )

        previous_length = math.hypot(*previous_vector)
        current_length = math.hypot(*current_vector)
        if previous_length > 8.0 and current_length > 8.0:
            previous_angle = math.atan2(previous_vector[1], previous_vector[0])
            current_angle = math.atan2(current_vector[1], current_vector[0])
            delta = current_angle - previous_angle
            if delta > math.pi:
                delta -= 2.0 * math.pi
            elif delta < -math.pi:
                delta += 2.0 * math.pi
            angle_degrees = math.degrees(delta)
        else:
            angle_degrees = (current[0] - self._last_roll_pos[0]) * 0.35

        angle_degrees = max(-30.0, min(30.0, angle_degrees))
        if abs(angle_degrees) > 1e-6:
            camera = renderer.GetActiveCamera()
            camera.Roll(angle_degrees)
            camera.OrthogonalizeViewUp()
            renderer.ResetCameraClippingRange()
            interactor.Render()

        self._last_roll_pos = current


def checked_bounds(combined_bounds: list[float]) -> tuple[float, float, float, float, float, float]:
    if not all(math.isfinite(value) for value in combined_bounds):
        raise ValueError("Scene did not produce valid render bounds.")
    return tuple(combined_bounds)  # type: ignore[return-value]


def add_scene_overlays(
    renderer: vtk.vtkRenderer,
    entities: list[NeuronCadEntity],
    connections: list[dict],
    devices: list[dict],
    show_connections: bool,
    show_devices: bool,
    shadow_strength: float,
    combined_bounds: list[float],
) -> None:
    entities_by_id = {entity.entity_id: entity for entity in entities}

    if show_connections:
        connection_actor = make_connection_actor(entities_by_id, connections)
        if connection_actor is not None:
            renderer.AddActor(connection_actor)

    if show_devices:
        for actor in make_device_actors(entities_by_id, devices, shadow_strength):
            renderer.AddActor(actor)
            expand_bounds(combined_bounds, actor.GetBounds())


def configure_window(
    renderer: vtk.vtkRenderer,
    window_name: str,
    window_size: tuple[int, int],
) -> tuple[vtk.vtkRenderWindow, vtk.vtkRenderWindowInteractor]:
    window = vtk.vtkRenderWindow()
    window.AddRenderer(renderer)
    window.SetSize(*window_size)
    window.SetWindowName(window_name)

    interactor = vtk.vtkRenderWindowInteractor()
    interactor.SetRenderWindow(window)
    style = NeuronCadInteractorStyle()
    style.SetDefaultRenderer(renderer)
    interactor.SetInteractorStyle(style)
    interactor._neuroncad_style = style
    return window, interactor


def show_scene(
    scene_payload_path: Path,
    sides: int,
    show_connections: bool,
    show_devices: bool,
    shadow_strength: float,
    selected_channel: str | None,
    window_size: tuple[int, int],
) -> None:
    entities, _compartments, connections, devices = read_scene_payload(scene_payload_path)
    if not entities:
        raise ValueError(f"No drawable entities found in scene payload: {scene_payload_path}")

    renderer = vtk.vtkRenderer()
    renderer.SetBackground(1.0, 1.0, 1.0)
    combined_bounds = [float("inf"), float("-inf"), float("inf"), float("-inf"), float("inf"), float("-inf")]

    if selected_channel:
        channel_polydata, missing_polydata, scalar_range = build_channel_polydata(entities, sides, selected_channel)
        if channel_polydata is not None and scalar_range is not None:
            display_range = scalar_display_range(scalar_range)
            heat_lut = make_heat_lut()
            renderer.AddActor(make_scalar_actor(channel_polydata, display_range, heat_lut, shadow_strength))
            renderer.AddActor2D(make_scalar_bar(heat_lut, f"{selected_channel} G", display_range))
            expand_bounds(combined_bounds, channel_polydata.GetBounds())
        else:
            print(f"VTK warning: channel {selected_channel!r} was not found in any drawable entity.", flush=True)

        if missing_polydata is not None:
            renderer.AddActor(make_mesh_actor(missing_polydata, MISSING_CHANNEL_COLOR, shadow_strength))
            expand_bounds(combined_bounds, missing_polydata.GetBounds())
    else:
        for color, polydata in build_color_polydata_by_color(entities, sides).items():
            renderer.AddActor(make_mesh_actor(polydata, color, shadow_strength))
            expand_bounds(combined_bounds, polydata.GetBounds())

    add_scene_overlays(
        renderer,
        entities,
        connections,
        devices,
        show_connections,
        show_devices,
        shadow_strength,
        combined_bounds,
    )

    bounds = checked_bounds(combined_bounds)
    renderer.AddActor(make_axes_actor(bounds, renderer.GetActiveCamera()))
    finalize_camera(renderer, bounds)

    window, interactor = configure_window(renderer, "NeuronCAD VTK - Live Scene", window_size)
    interactor.Initialize()
    window.Render()
    print(
        f"VTK viewer ready: payload={scene_payload_path}, entities={len(entities)}, "
        f"channel={selected_channel or 'entity-colors'}",
        flush=True,
    )
    interactor.Start()


def show_history_playback(
    scene_payload_path: Path,
    history_npz_path: Path,
    history_variable: str,
    sides: int,
    show_connections: bool,
    show_devices: bool,
    shadow_strength: float,
    window_size: tuple[int, int],
    fps: float,
) -> None:
    entities, compartments, connections, devices = read_scene_payload(scene_payload_path)
    if not entities:
        raise ValueError(f"No drawable entities found in scene payload: {scene_payload_path}")
    if not compartments:
        raise ValueError(f"No simulation compartments found in scene payload: {scene_payload_path}")

    history_matrix, manifest = read_history_npz(history_npz_path, history_variable)
    max_global_id = max(compartment.global_id for compartment in compartments)
    if history_matrix.shape[1] <= max_global_id:
        raise ValueError(
            f"History node count {history_matrix.shape[1]} does not cover max scene compartment id {max_global_id}."
        )

    renderer = vtk.vtkRenderer()
    renderer.SetBackground(1.0, 1.0, 1.0)
    combined_bounds = [float("inf"), float("-inf"), float("inf"), float("-inf"), float("inf"), float("-inf")]

    display_range = scalar_display_range(history_matrix)
    polydata, scalars, cell_ranges_by_gid = build_history_polydata(entities, compartments, sides, history_matrix[0])
    expand_bounds(combined_bounds, polydata.GetBounds())

    print(
        f"VTK history data loaded: var={history_variable}, frames={history_matrix.shape[0]}, "
        f"nodes={history_matrix.shape[1]}, mapped={len(cell_ranges_by_gid)}, "
        f"points={polydata.GetNumberOfPoints()}, cells={polydata.GetNumberOfCells()}, "
        f"range={display_range[0]:.4g}..{display_range[1]:.4g}.",
        flush=True,
    )

    heat_lut = make_heat_lut()
    renderer.AddActor(make_scalar_actor(polydata, display_range, heat_lut, shadow_strength))

    dt = float((manifest.get("metadata") or {}).get("DT", 0.0))
    scalar_bar = make_scalar_bar(heat_lut, history_variable, display_range)
    scalar_bar.SetTitle(
        f"{history_variable}\n"
        f"{display_range[0]:.4g} - {display_range[1]:.4g}\n"
        f"step 0  t={0.0:.4g} ms"
    )
    renderer.AddActor2D(scalar_bar)

    add_scene_overlays(
        renderer,
        entities,
        connections,
        devices,
        show_connections,
        show_devices,
        shadow_strength,
        combined_bounds,
    )

    bounds = checked_bounds(combined_bounds)
    renderer.AddActor(make_axes_actor(bounds, renderer.GetActiveCamera()))
    finalize_camera(renderer, bounds)

    window, interactor = configure_window(renderer, f"NeuronCAD VTK History - {history_variable}", window_size)
    interactor.Initialize()
    window.Render()

    frame_count = history_matrix.shape[0]
    frame_state = {
        "step": 0,
        "playing": True,
        "speed": 1.0,
        "timer_id": -1,
        "updating_slider": False,
    }

    play_pause_actor = make_play_pause_actor()
    renderer.AddActor2D(play_pause_actor)

    progress_widget, progress_representation = make_playback_slider(
        interactor=interactor,
        title="Frame",
        value_range=(0.0, float(max(0, frame_count - 1))),
        initial_value=0.0,
        point1=(0.15, 0.055),
        point2=(0.70, 0.055),
    )
    speed_widget, speed_representation = make_speed_slider(interactor, 1.0)

    def effective_interval_ms() -> int:
        base_fps = max(0.1, fps)
        speed = max(0.05, float(frame_state["speed"]))
        return max(1, int(1000.0 / (base_fps * speed)))

    def set_frame(step: int, update_progress: bool = True) -> None:
        step = int(max(0, min(frame_count - 1, step)))
        frame_state["step"] = step
        update_history_scalars(scalars, cell_ranges_by_gid, history_matrix[step])
        polydata.Modified()
        scalar_bar.SetTitle(
            f"{history_variable}\n"
            f"{display_range[0]:.4g} - {display_range[1]:.4g}\n"
            f"step {step}  t={step * dt:.4g} ms"
        )
        if update_progress:
            frame_state["updating_slider"] = True
            progress_representation.SetValue(float(step))
            frame_state["updating_slider"] = False
        window.Render()

    def set_playing(playing: bool) -> None:
        frame_state["playing"] = playing
        play_pause_actor.SetInput("Pause" if playing else "Play")
        window.Render()

    def reset_timer() -> None:
        timer_id = int(frame_state["timer_id"])
        if timer_id >= 0:
            interactor.DestroyTimer(timer_id)
        frame_state["timer_id"] = interactor.CreateRepeatingTimer(effective_interval_ms())

    def on_timer(_obj, _event) -> None:
        if not frame_state["playing"]:
            return
        next_step = (frame_state["step"] + 1) % frame_count
        set_frame(next_step)

    def on_progress_changed(_obj, _event) -> None:
        if frame_state["updating_slider"]:
            return
        set_frame(int(round(progress_representation.GetValue())), update_progress=False)

    def on_speed_changed(_obj, _event) -> None:
        frame_state["speed"] = float(speed_representation.GetValue())
        reset_timer()
        window.Render()

    def on_left_button_press(_obj, _event) -> None:
        click_x, click_y = interactor.GetEventPosition()
        width, height = window.GetSize()
        if width <= 0 or height <= 0:
            return

        normalized_x = click_x / width
        normalized_y = click_y / height
        if 0.025 <= normalized_x <= 0.105 and 0.02 <= normalized_y <= 0.085:
            set_playing(not frame_state["playing"])

    def on_key_press(_obj, _event) -> None:
        key = interactor.GetKeySym()
        if key == "space":
            set_playing(not frame_state["playing"])
        elif key in {"Left", "Down"}:
            set_playing(False)
            set_frame(frame_state["step"] - 1)
        elif key in {"Right", "Up"}:
            set_playing(False)
            set_frame(frame_state["step"] + 1)

    interactor.AddObserver("TimerEvent", on_timer)
    progress_widget.AddObserver("InteractionEvent", on_progress_changed)
    progress_widget.AddObserver("EndInteractionEvent", on_progress_changed)
    speed_widget.AddObserver("InteractionEvent", on_speed_changed)
    interactor.AddObserver("LeftButtonPressEvent", on_left_button_press, 2.0)
    interactor.AddObserver("KeyPressEvent", on_key_press)
    reset_timer()
    timer_id = int(frame_state["timer_id"])
    print(
        f"VTK history ready: {history_variable}, frames={frame_count}, compartments={len(cell_ranges_by_gid)}, "
        f"timer={timer_id}, fps={fps:.4g}, controls=slider/speed/pause.",
        flush=True,
    )
    interactor._neuroncad_history_controls = (
        progress_widget,
        progress_representation,
        speed_widget,
        speed_representation,
        play_pause_actor,
    )
    interactor.Start()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Render a NeuronCAD scene payload with VTK.")
    parser.add_argument(
        "--scene-payload",
        required=True,
        help="Path to the temporary scene payload exported by the C# host.",
    )
    parser.add_argument(
        "--channel",
        help="Render this ion channel using cell scalar colors. Omit to render entity colors.",
    )
    parser.add_argument(
        "--history-npz",
        help="Path to the temporary NeuronCAD simulation NPZ archive used for history playback.",
    )
    parser.add_argument(
        "--history-var",
        default="V",
        choices=sorted(HISTORY_ARRAY_NAMES.keys()),
        help="History variable to animate. Default: V",
    )
    parser.add_argument(
        "--history-fps",
        type=float,
        default=20.0,
        help="Playback frames per second for history animation. Default: 20",
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
        help="Surface lighting strength from 0 to 1. Default: 0.35",
    )
    parser.add_argument(
        "--window-size",
        type=parse_window_size,
        default=(1100, 850),
        help="Initial render window size as WIDTHxHEIGHT. Default: 1100x850",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    scene_payload_path = resolve_input_path(args.scene_payload)
    sides = max(6, args.sides)

    if args.history_npz:
        show_history_playback(
            scene_payload_path=scene_payload_path,
            history_npz_path=resolve_input_path(args.history_npz),
            history_variable=args.history_var,
            sides=sides,
            show_connections=args.connections,
            show_devices=not args.hide_devices,
            shadow_strength=args.shadow_strength,
            window_size=args.window_size,
            fps=args.history_fps,
        )
    else:
        show_scene(
            scene_payload_path=scene_payload_path,
            sides=sides,
            show_connections=args.connections,
            show_devices=not args.hide_devices,
            shadow_strength=args.shadow_strength,
            selected_channel=args.channel,
            window_size=args.window_size,
        )


if __name__ == "__main__":
    main()
