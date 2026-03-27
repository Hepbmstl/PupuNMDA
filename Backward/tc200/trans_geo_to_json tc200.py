import json
import uuid
import math
import re

# ==========================================
# 1. 基础数学与几何工具 (与 tcD 版本相同)
# ==========================================
def rescale_diam(d):
    """
    应用 NEURON geo 文件中的树突直径缩放公式 (仅用于 dend 节)
    对应 .geo 底部 rescale_diameters()：
      newdiam = diam / (1 + exp(-(diam - diam_hlf) / diam_stp))
      diam_min = 0.8, diam_hlf = 1.5, diam_stp = 1.5
    """
    d_new = d / (1.0 + math.exp(-(d - 1.5) / 1.5))
    return max(0.8, d_new)

def distance(p1, p2):
    return math.sqrt(sum((a - b)**2 for a, b in zip(p1, p2)))

def normalize(v):
    norm = math.sqrt(sum(x**2 for x in v))
    if norm == 0: return [1, 0, 0]
    return [x / norm for x in v]

def cross_product(v1, v2):
    return [
        v1[1]*v2[2] - v1[2]*v2[1],
        v1[2]*v2[0] - v1[0]*v2[2],
        v1[0]*v2[1] - v1[1]*v2[0]
    ]

def get_transform_matrix(p1, p2):
    """
    生成 4x4 齐次变换矩阵，匹配 NeuronCAD AxonVisual.AlignTo() 的行为：
      1) 将局部 Z 轴 (0,0,1) 旋转对齐到 p1→p2 方向
      2) 平移至 p1
    WPF Matrix3D row-major 布局：
      [M11 M12 M13 M14]   ← Row1 = 变换后的 X 轴
      [M21 M22 M23 M24]   ← Row2 = 变换后的 Y 轴
      [M31 M32 M33 M34]   ← Row3 = 变换后的 Z 轴 (圆台轴向)
      [OffX OffY OffZ M44] ← 平移 + 齐次
    """
    v = [p2[0]-p1[0], p2[1]-p1[1], p2[2]-p1[2]]
    length = math.sqrt(sum(x**2 for x in v))
    if length == 0:
        return [1,0,0,0, 0,1,0,0, 0,0,1,0, p1[0],p1[1],p1[2],1]

    fwd = [x / length for x in v]       # new Z axis

    local_z = [0, 0, 1]
    dot = fwd[0]*local_z[0] + fwd[1]*local_z[1] + fwd[2]*local_z[2]

    if abs(dot) > 0.9999:
        if dot < 0:
            return [
                1, 0, 0, 0,
                0, -1, 0, 0,
                0, 0, -1, 0,
                p1[0], p1[1], p1[2], 1
            ]
        else:
            return [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                p1[0], p1[1], p1[2], 1
            ]

    axis = cross_product(local_z, fwd)
    axis = normalize(axis)
    angle = math.acos(max(-1, min(1, dot)))

    c = math.cos(angle)
    s = math.sin(angle)
    t = 1.0 - c
    kx, ky, kz = axis

    r00 = c + t*kx*kx;    r01 = t*kx*ky - s*kz;  r02 = t*kx*kz + s*ky
    r10 = t*ky*kx + s*kz; r11 = c + t*ky*ky;      r12 = t*ky*kz - s*kx
    r20 = t*kz*kx - s*ky; r21 = t*kz*ky + s*kx;   r22 = c + t*kz*kz

    return [
        r00, r10, r20, 0.0,
        r01, r11, r21, 0.0,
        r02, r12, r22, 0.0,
        p1[0], p1[1], p1[2], 1.0
    ]

# ==========================================
# 2. 生成 NeuronCAD 对象
# ==========================================
def create_entity(p1, p2, type_name, color, channels, Ra, Cm):
    length = distance(p1[:3], p2[:3])
    r1, r2 = p1[3] / 2.0, p2[3] / 2.0
    transform = get_transform_matrix(p1[:3], p2[:3])

    return {
        "Id": str(uuid.uuid4()),
        "Type": type_name,
        "BaseRadius": r1,
        "TopRadius": r2,
        "Length": length,
        "Ra": Ra,
        "Cm": Cm,
        "Color": color,
        "Transform": transform,
        "Channels": channels
    }

def create_connection(ent_a_id, ent_b_id, axial_a, axial_b=0.0):
    """NSeg=1 模式下 AxialT 只取 0 或 1"""
    mode_a = "AxonCapEnd" if axial_a == 1.0 else "AxonCapStart"
    mode_b = "AxonCapEnd" if axial_b == 1.0 else "AxonCapStart"

    return {
        "Id": str(uuid.uuid4()),
        "EntityA_Id": ent_a_id,
        "EntityB_Id": ent_b_id,
        "AnchorA": { "Mode": mode_a, "AxialT": axial_a, "Angle": 0.0 },
        "AnchorB": { "Mode": mode_b, "AxialT": axial_b, "Angle": 0.0 },
        "Weight": 1.0
    }

# ==========================================
# 3. 主解析逻辑
# ==========================================
def load_biophy(biophy_filepath):
    """加载 Biophy.json，返回解析后的字典。"""
    with open(biophy_filepath, 'r', encoding='utf-8') as f:
        return json.load(f)

def build_channels_from_biophy(region_params):
    """
    从 Biophy.json 的 biophysical_assignments 区域参数构建 NeuronCAD channels 字典。
    单位转换：g_pas / gnabar / gkbar: S/cm² → mS/cm² (×1000)
              pcabar: cm/s → cm/s (不变, IsPermeability=True)
    """
    channels = {}
    passive = region_params.get("passive", {})
    hh2 = region_params.get("hh2", {})
    it = region_params.get("itGHK", {})

    # Na (hh2 gnabar S/cm² → mS/cm²)
    if "gnabar" in hh2:
        channels["Na"] = {"G": hh2["gnabar"] * 1000.0, "IsPermeability": False}
    # K (hh2 gkbar S/cm² → mS/cm²)
    if "gkbar" in hh2:
        channels["K"] = {"G": hh2["gkbar"] * 1000.0, "IsPermeability": False}
    # L (passive g_pas S/cm² → mS/cm²)
    if "g_pas" in passive:
        channels["L"] = {"G": passive["g_pas"] * 1000.0, "IsPermeability": False}
    # CaT (itGHK pcabar cm/s, 保持原值)
    if "pcabar" in it:
        channels["CaT"] = {"G": it["pcabar"], "IsPermeability": True}

    return channels


def build_region_lookup(biophy):
    """
    从 Biophy.json 的 spatial_regions 构建 segment_name → region_name 的查找表。
    如果某个 region 的 segments 为 "_remainder"，则标记为默认区域。
    返回 (lookup_dict, default_region_name)。
    """
    lookup = {}
    default_region = None
    for region_name, region_info in biophy["spatial_regions"].items():
        segs = region_info.get("segments", [])
        if segs == "_remainder":
            default_region = region_name
        else:
            for seg_name in segs:
                lookup[seg_name] = region_name
    return lookup, default_region


def parse_geo_to_json(geo_filepath, out_filepath, biophy_filepath="Biophy_tc200.json"):
    # ---- 加载 Biophy.json ----
    biophy = load_biophy(biophy_filepath)
    bio_env = biophy["global_environment"]
    bio_assign = biophy["biophysical_assignments"]

    # 构建 segment_name → region_name 查找表
    region_lookup, default_region = build_region_lookup(biophy)

    # 预构建每个区域的 channels / Ra / Cm
    region_channels = {}
    region_Ra = {}
    region_Cm = {}
    region_depth = {}
    for region_name, region_params in bio_assign.items():
        if region_name.startswith("_"):
            continue
        region_channels[region_name] = build_channels_from_biophy(region_params)
        region_Ra[region_name] = region_params["passive"]["Ra"]
        region_Cm[region_name] = region_params["passive"]["cm"]
        region_depth[region_name] = region_params.get("cadecay", {}).get("depth", 0.1)

    with open(geo_filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()

    # ---- 3.1 从 create 语句自动识别树突结构 ----
    # 例: "create soma,\  dend1[1],\  dend2[3], ..."  (可能跨多行)
    # 同时记录 dend 编号以便后续构建 segment name
    dend_group_sizes = []
    dend_group_numbers = []  # 实际的编号 (1, 2, 3, ...)

    # 先将多行 create 语句拼接为一行
    create_stmt = ""
    capturing = False
    for line in lines:
        ls = line.strip()
        if not capturing and ls.startswith("create "):
            capturing = True
        if capturing:
            # 去掉行尾续行符 '\'
            stripped = ls.rstrip("\\").strip()
            create_stmt += " " + stripped
            if not ls.endswith("\\"):
                break  # 最后一行（无续行符）

    for part in create_stmt.split(","):
        part = part.strip()
        m = re.match(r'dend(\d+)\[(\d+)\]', part)
        if m:
            dend_group_numbers.append(int(m.group(1)))
            dend_group_sizes.append(int(m.group(2)))

    # ---- 3.2 逐段解析数据 ----
    soma_points = []
    dend_blocks = []
    topo_connections = []

    state = "SEEK"
    idx = 0
    while idx < len(lines):
        line = lines[idx].strip()
        idx += 1
        if not line:
            continue

        # -- Soma 数据 --
        if "SOMA COORDINATES AND DIAMETERS:" in line:
            while idx < len(lines) and not lines[idx].strip():
                idx += 1
            num_pts = int(lines[idx].strip())
            idx += 1
            for _ in range(num_pts):
                parts = list(map(float, lines[idx].strip().split()))
                soma_points.append(parts)
                idx += 1

        # -- 树突数据 --
        elif "NEURITE COORDINATES AND DIAMETERS:" in line:
            state = "READ_DEND"

        elif line.startswith("CONNECTIONS"):
            state = "READ_CONN"

        elif state == "READ_DEND":
            tokens = line.split()
            if len(tokens) == 2:
                try:
                    _nseg = int(tokens[0])
                    num_pts = int(tokens[1])
                except ValueError:
                    continue
                block = []
                for _ in range(num_pts):
                    pt = list(map(float, lines[idx].strip().split()))
                    pt[3] = rescale_diam(pt[3])
                    block.append(pt)
                    idx += 1
                dend_blocks.append(block)

        # -- 拓扑连接 --
        elif state == "READ_CONN":
            if "/*" in line or "proc " in line or "//" in line:
                state = "DONE"
                continue
            tokens = line.split()
            if len(tokens) >= 2:
                try:
                    topo_connections.append((int(tokens[0]), float(tokens[1])))
                except ValueError:
                    pass

    # ---- 3.3 按 create 声明分组树突 blocks ----
    dend_groups = []
    blk_idx = 0
    for size in dend_group_sizes:
        group = []
        for _ in range(size):
            if blk_idx < len(dend_blocks):
                group.append(dend_blocks[blk_idx])
                blk_idx += 1
        dend_groups.append(group)

    entities = []
    connections_list = []

    # ---- 3.4 根据 segment name 查找区域 ----
    def get_region_for_segment(seg_name):
        """查找 segment_name 对应的区域，未命中则返回 default_region。"""
        return region_lookup.get(seg_name, default_region)

    # ---- 3.5 Soma 实体 ----
    soma_region = get_region_for_segment("soma")
    soma_channels = region_channels[soma_region]
    soma_Ra = region_Ra[soma_region]
    soma_Cm = region_Cm[soma_region]

    soma_entities = []
    soma_lengths = []
    for i in range(len(soma_points) - 1):
        ent = create_entity(soma_points[i], soma_points[i+1],
                            "Soma", "#FF1E90FF", soma_channels,
                            soma_Ra, soma_Cm)
        entities.append(ent)
        soma_entities.append(ent)
        soma_lengths.append(ent["Length"])
        if i > 0:
            connections_list.append(
                create_connection(soma_entities[i-1]["Id"], ent["Id"], 1.0, 0.0))

    total_soma_length = sum(soma_lengths)

    # ---- 3.6 树突实体 ----
    # dend_entities_map[g_local_idx][sec_idx] = [entity_list]
    # 通过 segment name "dendN[sec_idx]" 查找 Biophy 区域
    dend_entities_map = {}
    for g_local_idx, (dend_num, group) in enumerate(zip(dend_group_numbers, dend_groups)):
        dend_entities_map[g_local_idx] = []
        for sec_idx, pts in enumerate(group):
            # 构建 NEURON 风格的 segment name
            seg_name = f"dend{dend_num}[{sec_idx}]"
            region = get_region_for_segment(seg_name)

            ch = region_channels[region]
            ra = region_Ra[region]
            cm = region_Cm[region]

            section_ents = []
            for i in range(len(pts) - 1):
                ent = create_entity(pts[i], pts[i+1],
                                    "Dend", "#FF9370DB", ch, ra, cm)
                entities.append(ent)
                section_ents.append(ent)
                if i > 0:
                    connections_list.append(
                        create_connection(section_ents[i-1]["Id"], ent["Id"], 1.0, 0.0))
            dend_entities_map[g_local_idx].append(section_ents)

    # ---- 3.7 Soma ↔ Dendrite 主干连接 ----
    # .geo: "soma connect dendN[0] (0), 0.5"
    soma_mid_ent_idx = 0
    accumulated = 0.0
    half_len = total_soma_length * 0.5
    for i, l in enumerate(soma_lengths):
        if accumulated + l >= half_len or i == len(soma_lengths) - 1:
            soma_mid_ent_idx = i
            break
        accumulated += l

    for g_local_idx in range(len(dend_groups)):
        if dend_entities_map[g_local_idx] and dend_entities_map[g_local_idx][0]:
            connections_list.append(
                create_connection(soma_entities[soma_mid_ent_idx]["Id"],
                                  dend_entities_map[g_local_idx][0][0]["Id"],
                                  1.0, 0.0))

    # ---- 3.8 组内 section 间拓扑 ----
    conn_idx = 0
    for g_local_idx, size in enumerate(dend_group_sizes):
        for child_sec_idx in range(1, size):
            if conn_idx >= len(topo_connections):
                break
            parent_sec_idx, connect_t = topo_connections[conn_idx]
            conn_idx += 1

            parent_ents = dend_entities_map[g_local_idx][parent_sec_idx]
            child_ents  = dend_entities_map[g_local_idx][child_sec_idx]

            if not parent_ents or not child_ents:
                continue

            if connect_t == 1.0:
                connections_list.append(
                    create_connection(parent_ents[-1]["Id"],
                                      child_ents[0]["Id"], 1.0, 0.0))
            else:
                connections_list.append(
                    create_connection(parent_ents[0]["Id"],
                                      child_ents[0]["Id"], 0.0, 0.0))

    # ---- 3.9 构建 JSON (从 Biophy.json 填充全局参数) ----
    e_tbl = biophy["e_table"]
    hh2_kin = biophy["hh2_kinetics"]
    ca_kin = biophy["ca_kinetics"]

    neuron_cad_data = {
        "GlobalEnvironment": {
            "V_init": bio_env["v_init_mV"],
            "dt": bio_env["dt_ms"],
            "STEPS": bio_env["STEPS"],
            "celsius": bio_env["celsius"],
            "CA_OUT": bio_env["ca_out_mM"],
            "CA_INF": bio_env["ca_inf_mM"],
            "TAU_CA": bio_env["tau_ca_ms"]
        },
        "E_TABLE": {
            "Na": {"E": e_tbl["Na"]},
            "K":  {"E": e_tbl["K"]},
            "L":  {"E": e_tbl["L"]}
        },
        "HH_PARAMS": {
            "vtraub":    hh2_kin["vtraub"],
            "alpha_m_A": hh2_kin["alpha_m_A"], "alpha_m_V": hh2_kin["alpha_m_V"], "alpha_m_k": hh2_kin["alpha_m_k"],
            "beta_m_A":  hh2_kin["beta_m_A"],  "beta_m_V":  hh2_kin["beta_m_V"],  "beta_m_k":  hh2_kin["beta_m_k"],
            "alpha_h_A": hh2_kin["alpha_h_A"], "alpha_h_V": hh2_kin["alpha_h_V"], "alpha_h_k": hh2_kin["alpha_h_k"],
            "beta_h_A":  hh2_kin["beta_h_A"],  "beta_h_V":  hh2_kin["beta_h_V"],  "beta_h_k":  hh2_kin["beta_h_k"],
            "alpha_n_A": hh2_kin["alpha_n_A"], "alpha_n_V": hh2_kin["alpha_n_V"], "alpha_n_k": hh2_kin["alpha_n_k"],
            "beta_n_A":  hh2_kin["beta_n_A"],  "beta_n_V":  hh2_kin["beta_n_V"],  "beta_n_k":  hh2_kin["beta_n_k"]
        },
        "CA_PARAMS": {
            "shift":    ca_kin["shift"],
            "actshift": ca_kin["actshift"],
            "inf_mT_Vh": ca_kin["inf_mT_Vh"], "inf_mT_k": ca_kin["inf_mT_k"],
            "inf_hT_Vh": ca_kin["inf_hT_Vh"], "inf_hT_k": ca_kin["inf_hT_k"],
            "tau_mT_base": ca_kin["tau_mT_base"],
            "tau_mT_V1": ca_kin["tau_mT_V1"], "tau_mT_k1": ca_kin["tau_mT_k1"],
            "tau_mT_V2": ca_kin["tau_mT_V2"], "tau_mT_k2": ca_kin["tau_mT_k2"],
            "tau_mT_Q10": ca_kin["tau_mT_Q10"], "tau_mT_Tref": ca_kin["tau_mT_Tref"],
            "tau_hT_Vthresh": ca_kin["tau_hT_Vthresh"],
            "tau_hT_V1": ca_kin["tau_hT_V1"], "tau_hT_k1": ca_kin["tau_hT_k1"],
            "tau_hT_base": ca_kin["tau_hT_base"],
            "tau_hT_V2": ca_kin["tau_hT_V2"], "tau_hT_k2": ca_kin["tau_hT_k2"],
            "tau_hT_Q10": ca_kin["tau_hT_Q10"], "tau_hT_Tref": ca_kin["tau_hT_Tref"]
        },
        "Segmentation": {
            "Mode": "NSeg",
            "NSeg": 1,
            "LSeg": 20
        },
        "Entities": entities,
        "Connections": connections_list,
        "Devices": []
    }

    with open(out_filepath, 'w', encoding='utf-8') as f:
        json.dump(neuron_cad_data, f, indent=2)

    print(f"解析完成！生成了 {len(entities)} 个 Entities 和 {len(connections_list)} 条 Connections。")
    print(f"文件已保存至: {out_filepath}")

if __name__ == "__main__":
    parse_geo_to_json("tc200.geo", "tc200_NeuronCAD.json", "Biophy_tc200.json")
