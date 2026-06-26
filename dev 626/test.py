from dataclasses import dataclass
from pathlib import Path

import vtk


SWC_FILE = Path("Sst-IRES-Cre_Ai14-179870.04.01.01_491120344_m.swc")

TYPE_COLORS = {
    1: (1.0, 0.05, 0.05),  # soma
    2: (0.1, 0.35, 1.0),  # axon
    3: (0.1, 0.85, 0.2),  # dendrite
}
DEFAULT_COLOR = (0.8, 0.8, 0.8)
AXIS_COLOR = (0.0, 0.0, 0.0)


@dataclass(frozen=True)
class SwcNode:
    node_id: int
    node_type: int
    x: float
    y: float
    z: float
    radius: float
    parent_id: int


def read_swc_nodes(path: Path) -> dict[int, SwcNode]:
    nodes = {}

    with path.open("r", encoding="utf-8") as file:
        for line in file:
            line = line.strip()
            if not line or line.startswith("#"):
                continue

            columns = line.split()
            if len(columns) < 7:
                continue

            node = SwcNode(
                node_id=int(columns[0]),
                node_type=int(columns[1]),
                x=float(columns[2]),
                y=float(columns[3]),
                z=float(columns[4]),
                radius=float(columns[5]),
                parent_id=int(columns[6]),
            )
            nodes[node.node_id] = node

    return nodes


def translate_soma_to_origin(nodes: dict[int, SwcNode], soma_id: int = 1) -> dict[int, SwcNode]:
    soma = nodes[soma_id]

    return {
        node_id: SwcNode(
            node_id=node.node_id,
            node_type=node.node_type,
            x=node.x - soma.x,
            y=node.y - soma.y,
            z=node.z - soma.z,
            radius=node.radius,
            parent_id=node.parent_id,
        )
        for node_id, node in nodes.items()
    }


def get_segment_type(parent: SwcNode, child: SwcNode) -> int:
    smaller_id_node = parent if parent.node_id < child.node_id else child
    return smaller_id_node.node_type


def build_tube_polydata_by_type(nodes: dict[int, SwcNode]) -> dict[int, vtk.vtkPolyData]:
    points_by_type = {}
    lines_by_type = {}
    radii_by_type = {}

    for node_id in sorted(nodes):
        child = nodes[node_id]
        if child.parent_id == -1 or child.parent_id not in nodes:
            continue

        parent = nodes[child.parent_id]
        segment_type = get_segment_type(parent, child)

        if segment_type not in points_by_type:
            points_by_type[segment_type] = vtk.vtkPoints()
            lines_by_type[segment_type] = vtk.vtkCellArray()

            radii = vtk.vtkDoubleArray()
            radii.SetName("Radius")
            radii.SetNumberOfComponents(1)
            radii_by_type[segment_type] = radii

        points = points_by_type[segment_type]
        lines = lines_by_type[segment_type]
        radii = radii_by_type[segment_type]

        parent_point_id = points.InsertNextPoint(parent.x, parent.y, parent.z)
        child_point_id = points.InsertNextPoint(child.x, child.y, child.z)
        radii.InsertNextValue(parent.radius)
        radii.InsertNextValue(child.radius)

        line = vtk.vtkLine()
        line.GetPointIds().SetId(0, parent_point_id)
        line.GetPointIds().SetId(1, child_point_id)
        lines.InsertNextCell(line)

    polydata_by_type = {}

    for segment_type, points in points_by_type.items():
        polydata = vtk.vtkPolyData()
        polydata.SetPoints(points)
        polydata.SetLines(lines_by_type[segment_type])
        polydata.GetPointData().SetScalars(radii_by_type[segment_type])
        polydata_by_type[segment_type] = polydata

    return polydata_by_type


def make_tube_actor(polydata: vtk.vtkPolyData, color: tuple[float, float, float]) -> vtk.vtkActor:
    tube_filter = vtk.vtkTubeFilter()
    tube_filter.SetInputData(polydata)
    tube_filter.SetNumberOfSides(16)
    tube_filter.SetVaryRadiusToVaryRadiusByAbsoluteScalar()
    tube_filter.CappingOff()
    tube_filter.Update()

    mapper = vtk.vtkPolyDataMapper()
    mapper.SetInputConnection(tube_filter.GetOutputPort())
    mapper.ScalarVisibilityOff()

    actor = vtk.vtkActor()
    actor.SetMapper(mapper)
    actor.GetProperty().SetColor(*color)

    return actor


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


def show_swc(nodes: dict[int, SwcNode]) -> None:
    renderer = vtk.vtkRenderer()
    renderer.SetBackground(1.0, 1.0, 1.0)

    combined_bounds = [float("inf"), float("-inf"), float("inf"), float("-inf"), float("inf"), float("-inf")]

    for segment_type, polydata in build_tube_polydata_by_type(nodes).items():
        color = TYPE_COLORS.get(segment_type, DEFAULT_COLOR)
        renderer.AddActor(make_tube_actor(polydata, color))
        bounds = polydata.GetBounds()
        combined_bounds[0] = min(combined_bounds[0], bounds[0])
        combined_bounds[1] = max(combined_bounds[1], bounds[1])
        combined_bounds[2] = min(combined_bounds[2], bounds[2])
        combined_bounds[3] = max(combined_bounds[3], bounds[3])
        combined_bounds[4] = min(combined_bounds[4], bounds[4])
        combined_bounds[5] = max(combined_bounds[5], bounds[5])

    window = vtk.vtkRenderWindow()
    window.AddRenderer(renderer)
    window.SetSize(1000, 800)
    window.SetWindowName("SWC Frustum Viewer")

    interactor = vtk.vtkRenderWindowInteractor()
    interactor.SetRenderWindow(window)
    interactor.SetInteractorStyle(vtk.vtkInteractorStyleTrackballCamera())

    renderer.AddActor(make_axes_actor(tuple(combined_bounds), renderer.GetActiveCamera()))
    renderer.ResetCamera()
    window.Render()
    interactor.Start()


if __name__ == "__main__":
    swc_path = Path(__file__).with_name(SWC_FILE.name)
    swc_nodes = read_swc_nodes(swc_path)
    swc_nodes = translate_soma_to_origin(swc_nodes)
    show_swc(swc_nodes)
