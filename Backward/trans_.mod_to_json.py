import json
import math
import numpy as np

def rescale_diameter(d):
    """依照论文逻辑修正树突直径"""
    diam_min = 0.8
    diam_hlf = 1.5
    diam_stp = 1.5
    new_d = d / (1.0 + math.exp(-(d - diam_hlf) / diam_stp))
    return max(diam_min, new_d)

def calculate_transform_matrix(pA, pB):
    """
    计算 WPF 格式的 4x4 仿射变换矩阵
    将圆柱体的底部置于 pA，中心轴指向 pB
    """
    dir_vec = pB - pA
    length = np.linalg.norm(dir_vec)
    
    if length == 0:
        return [1,0,0,0, 0,1,0,0, 0,0,1,0, pA[0],pA[1],pA[2],1]

    # Z 轴指向目标点
    Z = dir_vec / length
    
    # 找一个不与 Z 平行的参考向量来计算 X 和 Y
    up = np.array([0.0, 1.0, 0.0])
    if abs(np.dot(Z, up)) > 0.999:
        up = np.array([1.0, 0.0, 0.0])
        
    X = np.cross(up, Z)
    X = X / np.linalg.norm(X)
    Y = np.cross(Z, X)
    Y = Y / np.linalg.norm(Y)
    
    # WPF Matrix3D 内存布局 (row-major)
    # [ M11, M12, M13, M14 ]
    # [ M21, M22, M23, M24 ]
    # [ M31, M32, M33, M34 ]
    # [ OffsetX, OffsetY, OffsetZ, M44 ]
    return [
        X[0], X[1], X[2], 0.0,
        Y[0], Y[1], Y[2], 0.0,
        Z[0], Z[1], Z[2], 0.0,
        pA[0], pA[1], pA[2], 1.0
    ]

def main():
    # ---------------------------------------------------------
    # 1. 定义数据 (为了独立运行，这里将 tcD.geo 的核心数据硬编码提取)
    # 在实际应用中，你可以写正则或 readline 循环来动态读取文件
    # ---------------------------------------------------------
    soma_pts = [
        [-23.25, -7.35, -34.2, 0], [-21.9, -6.18, -31.05, 14.959],
        [-21, -5.8875, -30.4875, 21.439], [-20.1, -5.03862, -28.6974, 24.974],
        [-16.95, -4.48638, -27.5526, 25.363], [-16.05, -4.26426, -26.5824, 26.719],
        [-13.8, -3.05889, -24.0111, 28.865], [-9.75, -1.51125, -21.6675, 28.311],
        [-4.8, 1.0875, -17.5662, 25.297], [-3.45, 0.795, -15.435, 23.776],
        [-0.75, 1.875, -11.4192, 15.383], [0.15, 1.56, -10.53, 10.826],
        [1.5, 3, -9, 0]
    ]

    dend1_pts = [
        [[7, -3, -5, 4.8], [7.5, -4, 2.5, 3.9], [9, -6, 7.5, 2.5], [11, -8, 10.5, 2.5]], # dend1[0]
        [[11, -8, 10.5, 2.5], [14, -8, 11, 2], [17, -7.5, 12.5, 2], [20.5, -6.5, 14, 2], [23.5, -4.5, 13.5, 2], [27, -2.5, 13, 3]], # dend1[1]
        [[11, -8, 10.5, 2.5], [12, -10, 10.5, 2], [15, -11, 14, 2], [17.5, -12, 19.5, 2], [18.5, -14, 23.5, 2], [18, -16.5, 26.5, 2], [17, -19, 30.5, 2]] # dend1[2]
    ]

    dend2_pts = [
        [[-13.5, 0, -38.5, 2.2], [-22.5, 0.5, -33.5, 2.2]], # dend2[0]
        [[-22.5, 0.5, -33.5, 2.2], [-30, -1.5, -33.5, 1.2], [-36.5, -2, -33.5, 1.2], [-41, -2, -33.5, 1.2], [-47, -2, -28, 1.2], [-53, -1, -21, 1.2], [-60.5, 1.5, -14.5, 1.5], [-66.5, 2.5, -13, 1.5], [-72.5, 1, -13.5, 1.2]], # dend2[1]
        [[-22.5, 0.5, -33.5, 2.2], [-26, 2.5, -36, 1.6]], # dend2[2]
        [[-26, 2.5, -36, 1.6], [-31.5, 2, -35.5, 1.2], [-38.5, 2.5, -34.5, 1.2], [-45.5, 4, -29, 1.2], [-53.5, 5, -24.5, 1.2], [-61.5, 5.5, -20, 1.2]], # dend2[3]
        [[-26, 2.5, -36, 1.6], [-32, 8.5, -29, 1.2], [-38, 15, -24.5, 1.2], [-45.5, 22, -22.5, 1.2], [-52, 27.5, -20, 1.2], [-58.5, 33.5, -17, 1.2], [-63, 40, -15, 1.2], [-68.5, 47.5, -13.5, 1.2]] # dend2[4]
    ]

    soma_center_pt = np.array([soma_pts[0][0], soma_pts[0][1], soma_pts[0][2]])
    
    entities = []
    connections = []
    conn_id_counter = 0

    def add_section(name, pts, is_soma=False):
        nonlocal conn_id_counter
        section_entities = []
        for i in range(len(pts) - 1):
            pA = np.array(pts[i][:3])
            pB = np.array(pts[i+1][:3])
            
            # 直径处理 (注意 NEURON 里的 pt3d 存的是直径，我们需要半径)
            dA = pts[i][3] if is_soma else rescale_diameter(pts[i][3])
            dB = pts[i+1][3] if is_soma else rescale_diameter(pts[i+1][3])
            
            length = np.linalg.norm(pB - pA)
            if length == 0: continue
            
            # 计算到胞体的距离，分配不同密度的通道
            dist_to_soma = np.linalg.norm(pA - soma_center_pt)
            ca_g = 8.5e-5 if (not is_soma and dist_to_soma > 11.0) else 1.7e-5
            
            channels = {
                "L": {"G": 0.0379, "IsPermeability": False},
                "CaT": {"G": ca_g, "IsPermeability": True}
            }
            if is_soma:
                channels["Na"] = {"G": 100.0, "IsPermeability": False}
                channels["K"] = {"G": 100.0, "IsPermeability": False}

            ent_id = f"{name}_{i}"
            entity = {
                "Id": ent_id,
                "Type": "Soma" if is_soma else "Dend",
                "BaseRadius": dA / 2.0,
                "TopRadius": dB / 2.0,
                "Length": float(length),
                "Ra": 173.0,
                "Cm": 0.878,
                "Color": "#FF1E90FF" if is_soma else "#FF9370DB",
                "Transform": calculate_transform_matrix(pA, pB),
                "Channels": channels
            }
            entities.append(entity)
            section_entities.append(ent_id)

            # 内部区室的隐式相连
            if i > 0:
                connections.append({
                    "Id": f"conn_internal_{conn_id_counter}",
                    "EntityA_Id": section_entities[i-1],
                    "EntityB_Id": ent_id,
                    "AnchorA": {"Mode": "AxonCapEnd", "AxialT": 1.0, "Angle": 0.0},
                    "AnchorB": {"Mode": "AxonCapStart", "AxialT": 0.0, "Angle": 0.0},
                    "Weight": 1.0
                })
                conn_id_counter += 1
        return section_entities

    # ---------------------------------------------------------
    # 2. 生成所有实体 (Entities)
    # ---------------------------------------------------------
    soma_ents = add_section("soma", soma_pts, is_soma=True)
    
    dend1_ents = []
    for i, pts in enumerate(dend1_pts):
        dend1_ents.append(add_section(f"dend1_{i}", pts))
        
    dend2_ents = []
    for i, pts in enumerate(dend2_pts):
        dend2_ents.append(add_section(f"dend2_{i}", pts))

    # ---------------------------------------------------------
    # 3. 处理 Section 之间的外部显式相连 (Connections)
    # ---------------------------------------------------------
    def link_sections(parent_ents, parent_loc, child_ents):
        nonlocal conn_id_counter
        # 根据 parent_loc (0.0 到 1.0) 找到具体的子实体
        idx = int(parent_loc * (len(parent_ents) - 1))
        local_t = (parent_loc * len(parent_ents)) - idx
        if local_t > 1.0: 
            idx = len(parent_ents) - 1
            local_t = 1.0
            
        connections.append({
            "Id": f"conn_external_{conn_id_counter}",
            "EntityA_Id": parent_ents[idx],
            "EntityB_Id": child_ents[0],
            "AnchorA": {"Mode": "AxonCylinder" if local_t < 1.0 else "AxonCapEnd", "AxialT": local_t, "Angle": 0.0},
            "AnchorB": {"Mode": "AxonCapStart", "AxialT": 0.0, "Angle": 0.0},
            "Weight": 1.0
        })
        conn_id_counter += 1

    # 根据 tcD.geo 文件中的逻辑建立连接
    # soma connect dend1[0] (0), 0.5
    link_sections(soma_ents, 0.5, dend1_ents[0])
    # soma connect dend2[0] (0), 0.5
    link_sections(soma_ents, 0.5, dend2_ents[0])
    
    # 底部 CONNECTIONS 块映射
    link_sections(dend1_ents[0], 1.0, dend1_ents[1]) # 0 1
    link_sections(dend1_ents[0], 1.0, dend1_ents[2]) # 0 1
    link_sections(dend2_ents[0], 1.0, dend2_ents[1]) # 0 1
    link_sections(dend2_ents[0], 1.0, dend2_ents[2]) # 0 1
    link_sections(dend2_ents[2], 1.0, dend2_ents[3]) # 2 1
    link_sections(dend2_ents[2], 1.0, dend2_ents[4]) # 2 1

    # ---------------------------------------------------------
    # 4. 组装并导出 JSON
    # ---------------------------------------------------------
    final_json = {
        "GlobalEnvironment": {
            "V_init": -65, "dt": 0.025, "STEPS": 10000, "celsius": 34.0,
            "CA_OUT": 2.0, "CA_INF": 2.4e-4, "TAU_CA": 5.0
        },
        "E_TABLE": {"Na": {"E": 50.0}, "K": {"E": -100.0}, "L": {"E": -69.85}},
        "HH_PARAMS": {
            "alpha_m_A": 0.32, "alpha_m_Vs": 13.0, "alpha_m_k": 4.0, "beta_m_A": 0.28, "beta_m_Vs": 40.0, "beta_m_k": 5.0,
            "alpha_h_A": 0.128, "alpha_h_Vs": 17.0, "alpha_h_k": 18.0, "beta_h_A": 4.0, "beta_h_Vs": 40.0, "beta_h_k": 5.0,
            "alpha_n_A": 0.032, "alpha_n_Vs": 15.0, "alpha_n_k": 5.0, "beta_n_A": 0.5, "beta_n_Vs": 10.0, "beta_n_k": 40.0
        },
        "CA_PARAMS": {
            "inf_mT_Vh": 56.0, "inf_mT_k": 6.2, "inf_hT_Vh": 80.0, "inf_hT_k": 4.0,
            "tau_mT_base": 0.612, "tau_mT_V1": 132.0, "tau_mT_k1": 16.7, "tau_mT_V2": 16.8, "tau_mT_k2": 18.2, "tau_mT_Q10": 5.0, "tau_mT_Tref": 24.0,
            "tau_hT_Vthresh": -80.0, "tau_hT_V1": 467.0, "tau_hT_k1": 66.6, "tau_hT_base": 28.0, "tau_hT_V2": 22.0, "tau_hT_k2": 10.5, "tau_hT_Q10": 3.0, "tau_hT_Tref": 24.0
        },
        "Segmentation": {"Mode": "NSeg", "NSeg": 1, "LSeg": 20.0}, # 由于我们是离散实体，NSeg这里强制定为1
        "Entities": entities,
        "Connections": connections,
        "Devices": []
    }

    with open("NeuronModel.json", "w", encoding="utf-8") as f:
        json.dump(final_json, f, indent=4, ensure_ascii=False)
        
    print(f"转换成功！生成了 {len(entities)} 个独立的实体圆台与 {len(connections)} 条拓扑连接。")

if __name__ == "__main__":
    main()