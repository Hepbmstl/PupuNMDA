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

def parse_geo_to_json(geo_filepath, out_filepath, biophy_filepath="Biophy.json"):
    # ---- 加载 Biophy.json ----
    biophy = load_biophy(biophy_filepath)
    bio_env = biophy["global_environment"]
    bio_assign = biophy["biophysical_assignments"]

    # 构建每个空间区域的 channels / Ra / Cm
    soma_bio = bio_assign["soma"]
    prox_bio = bio_assign["proximal_dendrites"]
    dist_bio = bio_assign["distal_dendrites"]

    soma_channels = build_channels_from_biophy(soma_bio)
    prox_channels = build_channels_from_biophy(prox_bio)
    dist_channels = build_channels_from_biophy(dist_bio)

    soma_Ra = soma_bio["passive"]["Ra"]
    soma_Cm = soma_bio["passive"]["cm"]
    prox_Ra = prox_bio["passive"]["Ra"]
    prox_Cm = prox_bio["passive"]["cm"]
    dist_Ra = dist_bio["passive"]["Ra"]
    dist_Cm = dist_bio["passive"]["cm"]

    soma_depth = soma_bio.get("cadecay", {}).get("depth", 0.1)

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

    # ---- 3.4 通道及被动参数从 Biophy.json 获取 (见上方加载) ----

    entities = []
    connections_list = []

    # ---- 3.5 Soma 实体 ----
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
    # dend_entities_map[g_id][sec_idx] = [entity_list]
    # sec_idx == 0 → proximal (dend1[0], dend2[0]), 其余 → distal
    dend_entities_map = {}
    for g_id, group in enumerate(dend_groups):
        dend_entities_map[g_id] = []
        for sec_idx, pts in enumerate(group):
            # 选择 proximal 或 distal 参数
            if sec_idx == 0:
                ch = prox_channels
                ra = prox_Ra
                cm = prox_Cm
            else:
                ch = dist_channels
                ra = dist_Ra
                cm = dist_Cm
            section_ents = []
            for i in range(len(pts) - 1):
                ent = create_entity(pts[i], pts[i+1],
                                    "Dend", "#FF9370DB", ch, ra, cm)
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
    parse_geo_to_json("tcD.geo", "tcD_NeuronCAD.json", "Biophy.json")