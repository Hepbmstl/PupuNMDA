import numpy as np
from scipy import linalg
import math
from dataclasses import dataclass, field
import json

E_TABLE = {
    "Na": {"E": 55.0},
    "K": {"E": -72.0},
    "L": {"E": -54.3}
}

SEGMENT = {}

V = -65.0
DT = 0.02
DEL = DT / 2
STEPS = 1000
N_NODE = 0

HISTORY_V = None
HISTORY_M = None
HISTORY_H = None
HISTORY_N = None

PROBE_LIST = []
# PROBE_LIST stores tuples for interval probes:
# (probe_id, segment_id, probe_start_ms, probe_duration_ms)

STIMULATION = []
#(stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration)

PROBE_SAVE_DATA = {}
#key:stimulation_id value:{all_params_name:params_value}

# -----------------------------------------------------------------------------------
# tools：

def save_data_HH(prob_id, data: dict):
    global PROBE_SAVE_DATA
    # Append probe data entries so each probe_id maps to a time-ordered list
    if prob_id not in PROBE_SAVE_DATA:
        PROBE_SAVE_DATA[prob_id] = []
    PROBE_SAVE_DATA[prob_id].append(data)

# -----------------------------------------------------------------------------------
# init：

# env set
def set_env(V_init: float= -65.0,
        dt: float = 0.02,
        steps: int = 1000,
        n_node:int = 0 ): 
    global V, DT, DEL, STEPS, N_NODE, HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N

    V = V_init # 初始化所有区室的电压
    DT = dt # 模拟步长
    DEL = dt / 2
    STEPS = steps # 模拟步数
    N_NODE = n_node # 区室总数
    HISTORY_V = np.zeros((steps + 1, n_node))
    HISTORY_M = np.zeros((steps + 1, n_node))
    HISTORY_H = np.zeros((steps + 1, n_node))
    HISTORY_N = np.zeros((steps + 1, n_node))

def set_E(table : dict): # 设置所有离子的电位
    global E_TABLE
    E_TABLE = table

def insert_probe(probe_id, segment_id, probe_start_ms, probe_duration_ms):
    """
    注册一个区间探针，参考 insert_stimulation 的参数顺序。
    - probe_id: 探针唯一标识
    - segment_id: 监听的区室 id
    - probe_start_ms: 监听开始时间 (ms)
    - probe_duration_ms: 监听持续时间 (ms)
    """
    global PROBE_LIST
    PROBE_LIST.append((probe_id, segment_id, probe_start_ms, probe_duration_ms))

def insert_stimulation(stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration):
    global STIMULATION
    STIMULATION.append((stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration))

@dataclass
class Segment:
    uid: str
    Ra: float          # 局部轴向电阻率 (ohm * cm)，标准值通常为 35.4 到 100
    D: float           # 直径 (um)
    L: float           # 长度 (um)
    Cm: float          # 比膜电容 (uF/cm^2)，标准值通常为 1.0
    id: int

    channels: dict = field(default_factory=dict)
    connected_segments: list = field(default_factory=list)
    @property
    def surface_area_cm2(self):
        # 将表面积单位从 um^2 转换为 cm^2 (乘 10^-8)
        return np.pi * self.D * self.L * 1e-8
    
    @property
    def cross_area_um2(self):
        # 截面积保持 um^2 用于轴向电阻计算
        return np.pi * (self.D / 2)**2
    
    @property
    def absolute_C(self):
        # 结果单位：uF
        return self.Cm * self.surface_area_cm2
        
    def get_absolute_g_max(self, ion_name):
        # 结果单位：mS
        return self.channels.get(ion_name, 0.0) * self.surface_area_cm2
    
    def add_channels(self, name:str, g_max:float):
        self.channels[name] = g_max

    def add_connection(self, target_id : int):
        self.connected_segments.append(target_id)

    def get_id(self):
        return self.id
    
    def get_uid(self):
        return self.uid
    
    def return_connection(self):
        return None


def init_segment(uid: str, # 初始化区室
        Ra: float,          # 局部轴向电阻率 (ohm * cm)
        D: float,           # 直径 (um)
        L: float,           # 长度 (um)
        Cm: float,
        id: int ):
    new_seg = Segment(uid=uid, Ra=Ra, D=D, L=L, Cm=Cm, id=id)
    SEGMENT[id] = new_seg


def add_connection(id, target_uid): # 连接区室
    SEGMENT[id].add_connection(target_uid)


# -----------------------------------------------------------------------------------
# simulation

def calculate_Kij(seg_i: Segment, seg_j: Segment):
    # 计算绝对电阻，单位转换为 kOhm
    # R (kOhm) = Ra (ohm*cm) * 10 * L (um) / Area (um^2)
    R_i = seg_i.Ra * 10.0 * (seg_i.L / 2) / seg_i.cross_area_um2
    R_j = seg_j.Ra * 10.0 * (seg_j.L / 2) / seg_j.cross_area_um2
    
    return 1.0 / (R_i + R_j)

def alpha_m(V): 
    # V -> -35 时的极限值为 0.1 / (1/10) = 1.0
    if abs(V + 35.0) < 1e-6:
        return 1.0
    return (0.1 * (V + 35))/(1 - np.exp(-((V+35)/10)))

def alpha_n(V): 
    # V -> -50 时的极限值为 0.01 / (1/10) = 0.1
    if abs(V + 50.0) < 1e-6:
        return 0.1
    return (0.01 * (V + 50))/(1 - np.exp(-((V+50)/10)))

def beta_m(V): return 4 * np.exp(-(V+60)/18)
def alpha_h(V): return 0.07 * np.exp(-(V+60)/20)
def beta_h(V): return 1/(1 + np.exp(-((V+30)/10)))
def beta_n(V): return 0.125 * np.exp(-(V+60)/80)

def init_gates(V):
    m0 = alpha_m(V) / (alpha_m(V) + beta_m(V))
    h0 = alpha_h(V) / (alpha_h(V) + beta_h(V))
    n0 = alpha_n(V) / (alpha_n(V) + beta_n(V))
    return m0, n0, h0

def gating_update(DEL, xi_old, alpha, beta):
    decay = (1 - DEL * (alpha + beta)) / (1 + DEL * (alpha + beta))
    drive = (2 * DEL * alpha) / (1 + DEL * (alpha + beta))
    return xi_old * decay + drive


# 状态容器：维度为 (steps + 1, N_NODE)
def start_simulation():
    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("请先调用 set_env")
    
    # 建立 segment_id 到 矩阵索引 i 的映射，保证 O(1) 寻址时间复杂度
    seg_list = list(SEGMENT.values())
    seg_id_to_idx = {seg.id: i for i, seg in enumerate(seg_list)}
    
    # 初始状态装载
    for i, seg in enumerate(seg_list):
        HISTORY_V[0, i] = V
        m0, n0, h0 = init_gates(V)
        HISTORY_M[0, i] = m0
        HISTORY_N[0, i] = n0
        HISTORY_H[0, i] = h0

    for step in range(STEPS):
        t_current = step * DT  # 当前绝对时间 (ms)
        
        V_t = HISTORY_V[step]
        m_t = HISTORY_M[step]
        h_t = HISTORY_H[step]
        n_t = HISTORY_N[step]
        
        m_half = np.zeros(N_NODE)
        h_half = np.zeros(N_NODE)
        n_half = np.zeros(N_NODE)
        g_Na_half = np.zeros(N_NODE)
        g_K_half = np.zeros(N_NODE)
        g_L_abs = np.zeros(N_NODE) 
        
        # 步骤 1: 门控变量与电导状态更新
        for i, seg in enumerate(seg_list):
            v_current = V_t[i]
            m_half[i] = gating_update(DEL, m_t[i], alpha_m(v_current), beta_m(v_current))
            h_half[i] = gating_update(DEL, h_t[i], alpha_h(v_current), beta_h(v_current))
            n_half[i] = gating_update(DEL, n_t[i], alpha_n(v_current), beta_n(v_current))
            
            g_Na_half[i] = (m_half[i]**3) * h_half[i] * seg.get_absolute_g_max("Na")
            g_K_half[i] = (n_half[i]**4) * seg.get_absolute_g_max("K")
            g_L_abs[i] = seg.get_absolute_g_max("L")
            
        A = np.zeros((N_NODE, N_NODE))
        b = np.zeros(N_NODE)
        
        # 步骤 2: 矩阵组装与刺激电流注入
        for i, seg in enumerate(seg_list):
            C_factor = seg.absolute_C / DEL
            sum_Kij = 0.0
            
            for connected_id in seg.connected_segments:
                target_seg = SEGMENT[connected_id]
                target_idx = seg_id_to_idx[connected_id]
                K_ij = calculate_Kij(seg, target_seg)
                
                A[i, target_idx] = -K_ij
                sum_Kij += K_ij
            
            A[i, i] = C_factor + g_Na_half[i] + g_K_half[i] + g_L_abs[i] + sum_Kij

            b[i] = (C_factor * V_t[i] + 
                    g_Na_half[i] * E_TABLE["Na"]["E"] + 
                    g_K_half[i] * E_TABLE["K"]["E"] + 
                    g_L_abs[i] * E_TABLE["L"]["E"])
            
            # --- 外部刺激数据流 ---
            # 遍历所有刺激配置，若命中当前空间位置与时间窗口，叠加电流至已知项向量 b
            for stim in STIMULATION:
                stim_id, s_id, stim_uA, stim_start, stim_duration = stim
                if seg.id == s_id:
                    if stim_start <= t_current <= (stim_start + stim_duration):
                        b[i] += stim_uA

        # 步骤 3: 隐式求解与时间步推进
        V_half = linalg.solve(A, b)
        HISTORY_V[step + 1] = 2 * V_half - V_t
        HISTORY_M[step + 1] = m_half
        HISTORY_H[step + 1] = h_half
        HISTORY_N[step + 1] = n_half

        # --- 探针数据登记 ---
        # 检查当前步长是否触发探针，若触发则打包整个状态空间的标量与向量参数
        for probe in PROBE_LIST:
            # probe tuple: (probe_id, segment_id, probe_start_ms, probe_duration_ms)
            probe_id, p_seg_id, probe_start_ms, probe_duration_ms = probe
            # 若当前时间在探针监听区间内，则记录该区室的状态
            if probe_start_ms <= t_current <= (probe_start_ms + probe_duration_ms):
                # 找到对应区室索引并采集数据
                idx = seg_id_to_idx.get(p_seg_id)
                if idx is None:
                    continue

                probe_data = {
                    "step": step,
                    "time_ms": t_current,
                    "segment_id": p_seg_id,
                    "V": float(V_t[idx]),
                    "m": float(m_t[idx]),
                    "h": float(h_t[idx]),
                    "n": float(n_t[idx]),
                    "g_Na_half": float(g_Na_half[idx]),
                    "g_K_half": float(g_K_half[idx]),
                    "g_L_abs": float(g_L_abs[idx]),
                    "A_matrix_row": A[idx, :].tolist(),
                    "b_vector_val": float(b[idx]),
                    "V_half_val": float(V_half[idx]),
                    "V_t_next": float(HISTORY_V[step + 1, idx])
                }
                save_data_HH(probe_id, probe_data)

    print("Simulation completed. Matrices assembled from Segment localized properties.")

# -----------------------------------------------------------------------------------
# 外部接口层

def export_history_matrices():
    """
    提供给外部运行时的时序状态流出口。
    保证 C-contiguous 连续内存状态。
    """
    global HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N
    return (
        np.ascontiguousarray(HISTORY_V, dtype=np.float64),
        np.ascontiguousarray(HISTORY_M, dtype=np.float64),
        np.ascontiguousarray(HISTORY_H, dtype=np.float64),
        np.ascontiguousarray(HISTORY_N, dtype=np.float64)
    )

def export_probe_data_json() -> str:
    """
    提供给外部运行时的探针数据出口。
    使用 JSON 字符串格式越过 C# 与 Python 之间的字典封送障碍。
    """
    return json.dumps(PROBE_SAVE_DATA)


# -----------------------------------------------------------------------------------
