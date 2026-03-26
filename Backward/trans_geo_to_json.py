import json
import uuid
import math
import re

# ==========================================
# 1. 基础数学与几何工具
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

    # 目标方向 = 局部 Z 轴映射到的世界方向
    fwd = [x / length for x in v]       # new Z axis

    # 构建正交基：选一个不与 fwd 平行的辅助向量
    local_z = [0, 0, 1]
    dot = fwd[0]*local_z[0] + fwd[1]*local_z[1] + fwd[2]*local_z[2]

    if abs(dot) > 0.9999:
        # fwd 与 (0,0,1) 几乎平行或反平行
        if dot < 0:
            # 反向：绕 X 轴 180° → X 不变, Y 取反, Z 取反
            return [
                1, 0, 0, 0,
                0, -1, 0, 0,
                0, 0, -1, 0,
                p1[0], p1[1], p1[2], 1
            ]
        else:
            # 同向：单位旋转
            return [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                p1[0], p1[1], p1[2], 1
            ]

    # 一般情况：通过 Rodrigues 旋转公式 (column-vector convention)
    # 旋转轴 = cross(localZ, fwd)，旋转角 = acos(dot)
    axis = cross_product(local_z, fwd)
    axis = normalize(axis)
    angle = math.acos(max(-1, min(1, dot)))

    # Rodrigues' rotation matrix R (column-vector: v' = R*v)
    c = math.cos(angle)
    s = math.sin(angle)
    t = 1.0 - c
    kx, ky, kz = axis

    r00 = c + t*kx*kx;    r01 = t*kx*ky - s*kz;  r02 = t*kx*kz + s*ky
    r10 = t*ky*kx + s*kz; r11 = c + t*ky*ky;      r12 = t*ky*kz - s*kx
    r20 = t*kz*kx - s*ky; r21 = t*kz*ky + s*kx;   r22 = c + t*kz*kz

    # WPF 使用 row-vector 约定 (v' = v * M)，因此输出 R 的转置
    # M 的 Row3 = R 的 Column3 = local Z 映射到的世界方向
    return [
        r00, r10, r20, 0.0,
        r01, r11, r21, 0.0,
        r02, r12, r22, 0.0,
        p1[0], p1[1], p1[2], 1.0
    ]

# ==========================================
# 2. 生成 NeuronCAD 对象
# ==========================================
def create_entity(p1, p2, type_name, color, channels):
    length = distance(p1[:3], p2[:3])
    r1, r2 = p1[3] / 2.0, p2[3] / 2.0
    transform = get_transform_matrix(p1[:3], p2[:3])

    return {
        "Id": str(uuid.uuid4()),
        "Type": type_name,
        "BaseRadius": r1,
        "TopRadius": r2,
        "Length": length,
        "Ra": 100.0,
        "Cm": 1.0,
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
def parse_geo_to_json(geo_filepath, out_filepath):
    with open(geo_filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()

    # ---- 3.1 从 create 语句自动识别树突结构 ----
    # 例: "create soma, dend1[3], dend2[5]"  → dend_group_sizes = [3, 5]
    dend_group_sizes = []
    for line in lines:
        ls = line.strip()
        if ls.startswith("create "):
            for part in ls[len("create "):].split(","):
                m = re.match(r'dend\d+\[(\d+)\]', part.strip())
                if m:
                    dend_group_sizes.append(int(m.group(1)))
            break

    # ---- 3.2 逐段解析数据 ----
    soma_points = []
    dend_blocks = []          # 全部树突 section 的点列表 (平铺)
    topo_connections = []     # (parent_section_idx, connect_t) 拓扑对

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
                soma_points.append(parts)       # Soma 不做 rescale
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
                    pt[3] = rescale_diam(pt[3])   # 树突直径缩放
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
    dend_groups = []     # dend_groups[g][sec] = [pt_list]
    blk_idx = 0
    for size in dend_group_sizes:
        group = []
        for _ in range(size):
            if blk_idx < len(dend_blocks):
                group.append(dend_blocks[blk_idx])
                blk_idx += 1
        dend_groups.append(group)

    # ---- 3.4 通道模板 (对齐 .mod 默认值) ----
    # hh2.mod:   gnabar = 0.003 S/cm² = 3 mS/cm²,  gkbar = 0.005 S/cm² = 5 mS/cm²
    # ITGHK.mod: pcabar = 0.2e-3 cm/s = 2e-4 cm/s
    soma_channels = {
        "Na":  {"G": 3,      "IsPermeability": False},
        "K":   {"G": 5,      "IsPermeability": False},
        "L":   {"G": 0.3,    "IsPermeability": False},
        "CaT": {"G": 0.0002, "IsPermeability": True}
    }
    dend_channels = {
        "Na":  {"G": 3,      "IsPermeability": False},
        "K":   {"G": 5,      "IsPermeability": False},
        "L":   {"G": 0.3,    "IsPermeability": False},
        "CaT": {"G": 0.0002, "IsPermeability": True}
    }

    entities = []
    connections_list = []

    # ---- 3.5 Soma 实体 ----
    soma_entities = []
    soma_lengths = []
    for i in range(len(soma_points) - 1):
        ent = create_entity(soma_points[i], soma_points[i+1],
                            "Soma", "#FF1E90FF", soma_channels)
        entities.append(ent)
        soma_entities.append(ent)
        soma_lengths.append(ent["Length"])
        if i > 0:
            connections_list.append(
                create_connection(soma_entities[i-1]["Id"], ent["Id"], 1.0, 0.0))

    total_soma_length = sum(soma_lengths)

    # ---- 3.6 树突实体 ----
    # dend_entities_map[g_id][sec_idx] = [entity_list]
    dend_entities_map = {}
    for g_id, group in enumerate(dend_groups):
        dend_entities_map[g_id] = []
        for sec_idx, pts in enumerate(group):
            section_ents = []
            for i in range(len(pts) - 1):
                ent = create_entity(pts[i], pts[i+1],
                                    "Dend", "#FF9370DB", dend_channels)
                entities.append(ent)
                section_ents.append(ent)
                if i > 0:
                    connections_list.append(
                        create_connection(section_ents[i-1]["Id"], ent["Id"], 1.0, 0.0))
            dend_entities_map[g_id].append(section_ents)

    # ---- 3.7 Soma ↔ Dendrite 主干连接 ----
    # .geo: "soma connect dendN[0] (0), 0.5"  —— 各主干 section[0] 的 0-端连到 soma 50% 处
    # 找到 soma 中点所在的 entity，连接该 entity 的 AxialT=1.0 端
    soma_mid_ent_idx = 0
    accumulated = 0.0
    half_len = total_soma_length * 0.5
    for i, l in enumerate(soma_lengths):
        if accumulated + l >= half_len or i == len(soma_lengths) - 1:
            soma_mid_ent_idx = i
            break
        accumulated += l

    for g_id in range(len(dend_groups)):
        if dend_entities_map[g_id] and dend_entities_map[g_id][0]:
            connections_list.append(
                create_connection(soma_entities[soma_mid_ent_idx]["Id"],
                                  dend_entities_map[g_id][0][0]["Id"],
                                  1.0, 0.0))

    # ---- 3.8 组内 section 间拓扑 ----
    # .geo: for i = 1,N-1 { dendK[fscan()] connect dendK[i] (0), fscan() }
    conn_idx = 0
    for g_id, size in enumerate(dend_group_sizes):
        for child_sec_idx in range(1, size):
            if conn_idx >= len(topo_connections):
                break
            parent_sec_idx, connect_t = topo_connections[conn_idx]
            conn_idx += 1

            parent_ents = dend_entities_map[g_id][parent_sec_idx]
            child_ents  = dend_entities_map[g_id][child_sec_idx]

            if not parent_ents or not child_ents:
                continue

            # connect_t == 1.0 → 子 section 接在父 section 的末端
            if connect_t == 1.0:
                connections_list.append(
                    create_connection(parent_ents[-1]["Id"],
                                      child_ents[0]["Id"], 1.0, 0.0))
            else:
                # connect_t == 0.0 → 子 section 接在父 section 的起始端
                connections_list.append(
                    create_connection(parent_ents[0]["Id"],
                                      child_ents[0]["Id"], 0.0, 0.0))

    # ---- 3.9 构建 JSON ----
    neuron_cad_data = {
        "GlobalEnvironment": {
            "V_init": -65,
            "dt": 0.02,
            "STEPS": 10000,
            "celsius": 36,                # hh2.mod / ITGHK.mod: celsius=36
            "CA_OUT": 2,                  # ITGHK.mod: cao=2
            "CA_INF": 0.0002,             # cadecay.mod: cainf=2e-4
            "TAU_CA": 5                   # cadecay.mod: taur=5
        },
        "E_TABLE": {
            "Na": {"E": 50},              # hh2.mod: ena=50
            "K":  {"E": -90},             # hh2.mod: ek=-90
            "L":  {"E": -54.3}
        },
        "HH_PARAMS": {
            "alpha_m_A": 0.1,  "alpha_m_Vs": 35, "alpha_m_k": 10,
            "beta_m_A": 4,     "beta_m_Vs": 60,  "beta_m_k": 18,
            "alpha_h_A": 0.07, "alpha_h_Vs": 60, "alpha_h_k": 20,
            "beta_h_A": 1,     "beta_h_Vs": 30,  "beta_h_k": 10,
            "alpha_n_A": 0.01, "alpha_n_Vs": 50, "alpha_n_k": 10,
            "beta_n_A": 0.125, "beta_n_Vs": 60,  "beta_n_k": 80
        },
        "CA_PARAMS": {
            "shift": 2.0,                 # ITGHK.mod: shift=2
            "actshift": 0.0,              # ITGHK.mod: actshift=0
            "inf_mT_Vh": 57,              # ITGHK.mod: 57
            "inf_mT_k": 6.2,              # ITGHK.mod: 6.2
            "inf_hT_Vh": 81,              # ITGHK.mod: 81
            "inf_hT_k": 4,                # ITGHK.mod: 4.0
            "tau_mT_base": 0.612,
            "tau_mT_V1": 132,
            "tau_mT_k1": 16.7,
            "tau_mT_V2": 16.8,
            "tau_mT_k2": 18.2,
            "tau_mT_Q10": 5,              # ITGHK.mod: qm=5
            "tau_mT_Tref": 24,
            "tau_hT_Vthresh": -80,
            "tau_hT_V1": 467,
            "tau_hT_k1": 66.6,
            "tau_hT_base": 28,
            "tau_hT_V2": 22,
            "tau_hT_k2": 10.5,
            "tau_hT_Q10": 3,              # ITGHK.mod: qh=3
            "tau_hT_Tref": 24
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
    parse_geo_to_json("tcD.geo", "tcD_NeuronCAD.json")