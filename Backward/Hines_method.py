import numpy as np
from scipy import linalg
import math
from dataclasses import dataclass, field
import json
import matplotlib
try:
    matplotlib.use('TkAgg')
except Exception:
    pass
import matplotlib.pyplot as plt
import matplotlib.animation as animation

FARADAY = 96485.3       # 库仑/摩尔 (C/mol)
R_GAS = 8.314           # 焦耳/(摩尔·开尔文) (J/(mol*K))
CELSIUS = 36.0          # 模拟温度 (degC)
TEMP_K = CELSIUS + 273.15
Z_CA = 2.0              # 钙离子化合价

CA_OUT = 2.0            # 胞外钙浓度 (mM)
CA_INF = 2.4e-4         # 胞内稳态游离钙浓度 (mM)
TAU_CA = 5.0            # 钙离子衰减时间常数 (ms)

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

HISTORY_CA = None
HISTORY_MT = None
HISTORY_HT = None

PROBE_LIST = []
# PROBE_LIST stores tuples for interval probes:
# (probe_id, segment_id, probe_start_ms, probe_duration_ms)

STIMULATION = []
#(stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration)

PROBE_SAVE_DATA = {}
#key:stimulation_id value:{all_params_name:params_value}

CURRENT_STEP = -1
SIMULATION_RUNNING = False

# HH gating parameters (modifiable via C# Ion Channel Setting)
HH_PARAMS = {
    "alpha_m_A": 0.1, "alpha_m_Vs": 35.0, "alpha_m_k": 10.0,
    "beta_m_A": 4.0, "beta_m_Vs": 60.0, "beta_m_k": 18.0,
    "alpha_h_A": 0.07, "alpha_h_Vs": 60.0, "alpha_h_k": 20.0,
    "beta_h_A": 1.0, "beta_h_Vs": 30.0, "beta_h_k": 10.0,
    "alpha_n_A": 0.01, "alpha_n_Vs": 50.0, "alpha_n_k": 10.0,
    "beta_n_A": 0.125, "beta_n_Vs": 60.0, "beta_n_k": 80.0,
}

# Ca T-type channel parameters (modifiable via C# Ion Channel Setting)
# Raw Vh values from ITGHK.mod; shift/actshift applied in kinetic functions
CA_PARAMS = {
    "shift": 2.0, "actshift": 0.0,
    "inf_mT_Vh": 57.0, "inf_mT_k": 6.2,
    "inf_hT_Vh": 81.0, "inf_hT_k": 4.0,
    "tau_mT_base": 0.612, "tau_mT_V1": 132.0, "tau_mT_k1": 16.7,
    "tau_mT_V2": 16.8, "tau_mT_k2": 18.2, "tau_mT_Q10": 5.0, "tau_mT_Tref": 24.0,
    "tau_hT_Vthresh": -80.0,
    "tau_hT_V1": 467.0, "tau_hT_k1": 66.6,
    "tau_hT_base": 28.0, "tau_hT_V2": 22.0, "tau_hT_k2": 10.5,
    "tau_hT_Q10": 3.0, "tau_hT_Tref": 24.0,
}

_HH_PARAMS_DEFAULT = dict(HH_PARAMS)
_CA_PARAMS_DEFAULT = dict(CA_PARAMS)

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
        n_node:int = 0,
        celsius: float = 36.0,
        ca_out: float = 2.0,
        ca_inf: float = 2.4e-4,
        tau_ca: float = 5.0):
    global V, DT, DEL, STEPS, N_NODE, HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N
    global HISTORY_CA, HISTORY_MT, HISTORY_HT
    global CELSIUS, TEMP_K, CA_OUT, CA_INF, TAU_CA

    V = V_init # 初始化所有区室的电压
    DT = dt # 模拟步长
    DEL = dt / 2
    STEPS = steps # 模拟步数
    N_NODE = n_node # 区室总数
    HISTORY_V = np.zeros((steps + 1, n_node))
    HISTORY_M = np.zeros((steps + 1, n_node))
    HISTORY_H = np.zeros((steps + 1, n_node))
    HISTORY_N = np.zeros((steps + 1, n_node))
    HISTORY_CA = np.zeros((steps + 1, n_node))
    HISTORY_MT = np.zeros((steps + 1, n_node))
    HISTORY_HT = np.zeros((steps + 1, n_node))

    CELSIUS = celsius
    TEMP_K = celsius + 273.15
    CA_OUT = ca_out
    CA_INF = ca_inf
    TAU_CA = tau_ca

def set_E(table : dict): # 设置所有离子的电位
    global E_TABLE
    E_TABLE = table

def set_hh_params(params):
    """从 C# 传入 HH 门控参数字典，更新 HH_PARAMS。"""
    global HH_PARAMS
    for key in params:
        if key in HH_PARAMS:
            HH_PARAMS[key] = float(params[key])

def set_ca_params(params):
    """从 C# 传入 Ca T-type 通道参数字典，更新 CA_PARAMS。"""
    global CA_PARAMS
    for key in params:
        if key in CA_PARAMS:
            CA_PARAMS[key] = float(params[key])

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
    ca_shell_depth_um: float = 0.1

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
    
    def get_absolute_P_max(self, ion_name):
        """提取渗透率并转换为与面积相关的绝对系数"""
        # 结果保留 cm/s * cm^2 = cm^3/s 的比例，后续 GHK 函数会统一转换量纲
        return self.channels.get(ion_name, 0.0) * self.surface_area_cm2
    
    @property
    def gamma_Ca(self):
        """
        计算法拉第几何转换系数 (浓度通量因子)
        单位换算推导：将绝对电流 (uA) 转化为浓度变化 (mM/ms)
        假设钙离子聚集在膜下 d = 0.1 um 的壳层中
        """
        d_um = self.ca_shell_depth_um
        shell_volume_L = self.surface_area_cm2 * (d_um * 1e-4) * 1e-3 # 升
        # d[Ca]/dt = - I / (Z * F * Volume)
        # I(uA)=1e-6 A, d[Ca]/dt 单位 mM/ms = mol/(L·s):
        # gamma = 1e-6 / (Z * F * Volume_L)
        return 1e-6 / (Z_CA * FARADAY * shell_volume_L)


def init_segment(uid: str, Ra: float, D: float, L: float, Cm: float, id: int, ca_shell_depth_um: float = 0.1):
    new_seg = Segment(uid=uid, Ra=Ra, D=D, L=L, Cm=Cm, id=id, ca_shell_depth_um = ca_shell_depth_um)
    SEGMENT[id] = new_seg


def add_connection(id, target_uid): # 连接区室
    SEGMENT[id].add_connection(target_uid)


def add_channel_to_segment(segment_id, channel_name, g_max):
    """为已初始化的区室添加离子通道。"""
    SEGMENT[segment_id].add_channels(channel_name, g_max)


def clear_environment():
    """重置所有全局状态，用于下一次仿真运行前清理。"""
    global SEGMENT, V, DT, DEL, STEPS, N_NODE
    global HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N
    global HISTORY_CA, HISTORY_MT, HISTORY_HT
    global PROBE_LIST, STIMULATION, PROBE_SAVE_DATA
    global CURRENT_STEP, SIMULATION_RUNNING, E_TABLE

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
    HISTORY_CA = None
    HISTORY_MT = None
    HISTORY_HT = None
    PROBE_LIST = []
    STIMULATION = []
    PROBE_SAVE_DATA = {}
    CURRENT_STEP = -1
    SIMULATION_RUNNING = False
    E_TABLE = {
        "Na": {"E": 55.0},
        "K":  {"E": -72.0},
        "L":  {"E": -54.3}
    }
    HH_PARAMS.update(_HH_PARAMS_DEFAULT)
    CA_PARAMS.update(_CA_PARAMS_DEFAULT)


def get_current_step():
    """返回当前仿真步数，供外部轮询进度。"""
    return CURRENT_STEP


def is_simulation_running():
    """返回仿真是否正在运行。"""
    return SIMULATION_RUNNING


# -----------------------------------------------------------------------------------
# simulation

def calculate_Kij(seg_i: Segment, seg_j: Segment):
    # 计算绝对电阻，单位转换为 kOhm
    # R (kOhm) = Ra (ohm*cm) * 10 * L (um) / Area (um^2)
    R_i = seg_i.Ra * 10.0 * (seg_i.L / 2) / seg_i.cross_area_um2
    R_j = seg_j.Ra * 10.0 * (seg_j.L / 2) / seg_j.cross_area_um2
    
    return 1.0 / (R_i + R_j)

def alpha_m(V):
    A  = HH_PARAMS["alpha_m_A"]
    Vs = HH_PARAMS["alpha_m_Vs"]
    k  = HH_PARAMS["alpha_m_k"]
    if abs(V + Vs) < 1e-6:
        return A * k
    return (A * (V + Vs)) / (1 - np.exp(-((V + Vs) / k)))

def alpha_n(V):
    A  = HH_PARAMS["alpha_n_A"]
    Vs = HH_PARAMS["alpha_n_Vs"]
    k  = HH_PARAMS["alpha_n_k"]
    if abs(V + Vs) < 1e-6:
        return A * k
    return (A * (V + Vs)) / (1 - np.exp(-((V + Vs) / k)))

def beta_m(V):
    A  = HH_PARAMS["beta_m_A"]
    Vs = HH_PARAMS["beta_m_Vs"]
    k  = HH_PARAMS["beta_m_k"]
    return A * np.exp(-(V + Vs) / k)

def alpha_h(V):
    A  = HH_PARAMS["alpha_h_A"]
    Vs = HH_PARAMS["alpha_h_Vs"]
    k  = HH_PARAMS["alpha_h_k"]
    return A * np.exp(-(V + Vs) / k)

def beta_h(V):
    A  = HH_PARAMS["beta_h_A"]
    Vs = HH_PARAMS["beta_h_Vs"]
    k  = HH_PARAMS["beta_h_k"]
    return A / (1 + np.exp(-((V + Vs) / k)))

def beta_n(V):
    A  = HH_PARAMS["beta_n_A"]
    Vs = HH_PARAMS["beta_n_Vs"]
    k  = HH_PARAMS["beta_n_k"]
    return A * np.exp(-(V + Vs) / k)

def inf_mT(V):
    shift = CA_PARAMS["shift"]
    actshift = CA_PARAMS["actshift"]
    Vh = CA_PARAMS["inf_mT_Vh"]
    k  = CA_PARAMS["inf_mT_k"]
    return 1.0 / (1.0 + np.exp(-(V + shift + actshift + Vh) / k))

def inf_hT(V):
    shift = CA_PARAMS["shift"]
    Vh = CA_PARAMS["inf_hT_Vh"]
    k  = CA_PARAMS["inf_hT_k"]
    return 1.0 / (1.0 + np.exp((V + shift + Vh) / k))

def tau_mT(V):
    shift = CA_PARAMS["shift"]
    actshift = CA_PARAMS["actshift"]
    base = CA_PARAMS["tau_mT_base"]
    V1   = CA_PARAMS["tau_mT_V1"]
    k1   = CA_PARAMS["tau_mT_k1"]
    V2   = CA_PARAMS["tau_mT_V2"]
    k2   = CA_PARAMS["tau_mT_k2"]
    Q10  = CA_PARAMS["tau_mT_Q10"]
    Tref = CA_PARAMS["tau_mT_Tref"]
    phi_m = Q10 ** ((CELSIUS - Tref) / 10.0)
    Vs = V + shift + actshift
    return (base + 1.0 / (np.exp(-(Vs + V1) / k1) + np.exp((Vs + V2) / k2))) / phi_m

def tau_hT(V):
    shift = CA_PARAMS["shift"]
    Vth  = CA_PARAMS["tau_hT_Vthresh"]
    V1   = CA_PARAMS["tau_hT_V1"]
    k1   = CA_PARAMS["tau_hT_k1"]
    base = CA_PARAMS["tau_hT_base"]
    V2   = CA_PARAMS["tau_hT_V2"]
    k2   = CA_PARAMS["tau_hT_k2"]
    Q10  = CA_PARAMS["tau_hT_Q10"]
    Tref = CA_PARAMS["tau_hT_Tref"]
    phi_h = Q10 ** ((CELSIUS - Tref) / 10.0)
    Vs = V + shift
    if Vs < Vth:
        return np.exp((Vs + V1) / k1) / phi_h
    else:
        return (base + np.exp(-(Vs + V2) / k2)) / phi_h

def gating_update_tau_inf(DEL, xi_old, inf, tau):
    """
    基于 tau 和 inf 的 Crank-Nicolson 门控更新 (与 gating_update 一致的全步长推进)
    使用 DEL=dt/2 实现 dt 步长的 CN 格式，匹配 NEURON cnexp 精度
    """
    decay = (1 - DEL / tau) / (1 + DEL / tau)
    drive = (2 * DEL / tau * inf) / (1 + DEL / tau)
    return xi_old * decay + drive

def init_gates(V):
    m0 = alpha_m(V) / (alpha_m(V) + beta_m(V))
    h0 = alpha_h(V) / (alpha_h(V) + beta_h(V))
    n0 = alpha_n(V) / (alpha_n(V) + beta_n(V))
    return m0, n0, h0

def gating_update(DEL, xi_old, alpha, beta, tadj=1.0):
    tau = (1.0 / (alpha + beta)) / tadj
    inf = alpha / (alpha + beta)
    decay = (1 - DEL / tau) / (1 + DEL / tau)
    drive = (2 * DEL / tau * inf) / (1 + DEL / tau)
    return xi_old * decay + drive


def evaluate_GHK_and_Jacobian(V, Cai, Cao, P_max_abs, m_T, h_T):

    # 如果该区室没有 T 通道，直接截断数据流
    if P_max_abs == 0.0:
        return 0.0, 0.0

    # 无量纲电压因子 k
    k = (Z_CA * FARADAY) / (1000.0 * R_GAS * TEMP_K)
    z = k * V
    
    # 概率因子与常数前缀
    # 量纲对齐：P_max_abs(cm^3/s) * 2F(C/mol) * 10^-3 -> mA (随后转为 uA 乘 1000)
    GHK_prefix = P_max_abs * (m_T**2) * h_T * (Z_CA * FARADAY * 1e-3) * 1000.0
    
    # 奇点状态截获 (V 极小)
    if abs(z) < 1e-4:
        # 泰勒展开极限状态 (L'Hôpital's limit)
        # f(z) = z*(Ci - Co*e^{-z})/(1-e^{-z}) ≈ (Ci-Co) + z*(Ci+Co)/2 + O(z²)
        I_T_abs = GHK_prefix * ((Cai - Cao) + z * (Cai + Cao) / 2.0)
        g_Ca_eq = GHK_prefix * k * ((Cai + Cao) / 2.0)
        return I_T_abs, g_Ca_eq

    # 正常状态空间推演
    exp_z = np.exp(z)
    exp_minus_z = np.exp(-z)
    
    # 1. 绝对电流求值
    f_z = z * (Cai - Cao * exp_minus_z) / (1.0 - exp_minus_z)
    I_T_abs = GHK_prefix * f_z
    
    # 2. 偏导数 (Jacobian) 求值 (链式求导解析解)
    denominator = (exp_z - 1.0)
    f_prime_z = ((Cai * exp_z * (1.0 + z) - Cao) * denominator - (Cai * z * exp_z - Cao * z) * exp_z) / (denominator**2)
    g_Ca_eq = GHK_prefix * k * f_prime_z
    
    return I_T_abs, g_Ca_eq


# -----------------------------------------------------------------------------------


# 状态容器：维度为 (steps + 1, N_NODE)
def start_simulation(progress_callback=None):
    global CURRENT_STEP, SIMULATION_RUNNING
    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("请先调用 set_env")
    
    if HISTORY_MT is None or HISTORY_HT is None or HISTORY_CA is None:
        raise RuntimeError("请先调用 set_env")
    SIMULATION_RUNNING = True
    CURRENT_STEP = 0

    try:
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

            HISTORY_MT[0, i] = inf_mT(V)
            HISTORY_HT[0, i] = inf_hT(V)
            HISTORY_CA[0, i] = CA_INF

        for step in range(STEPS):
            CURRENT_STEP = step
            if progress_callback is not None:
                progress_callback(step)
            t_current = step * DT  # 当前绝对时间 (ms)
            
            V_t = HISTORY_V[step]
            m_t = HISTORY_M[step]
            h_t = HISTORY_H[step]
            n_t = HISTORY_N[step]

            mT_t = HISTORY_MT[step]
            hT_t = HISTORY_HT[step]
            Ca_t = HISTORY_CA[step]
            
            m_half = np.zeros(N_NODE)
            h_half = np.zeros(N_NODE)
            n_half = np.zeros(N_NODE)
            g_Na_half = np.zeros(N_NODE)
            g_K_half = np.zeros(N_NODE)
            g_L_abs = np.zeros(N_NODE) 

            mT_half = np.zeros(N_NODE)
            hT_half = np.zeros(N_NODE)
            I_T_abs = np.zeros(N_NODE)
            g_Ca_eq = np.zeros(N_NODE)
            
            # Q10 温度校正 (hh2.mod: tadj = 3.0 ^ ((celsius-36)/10))
            tadj_hh = 3.0 ** ((CELSIUS - 36.0) / 10.0)

            # 步骤 1: 门控变量与电导状态更新
            for i, seg in enumerate(seg_list):
                v_current = V_t[i]

                m_half[i] = gating_update(DEL, m_t[i], alpha_m(v_current), beta_m(v_current), tadj_hh)
                h_half[i] = gating_update(DEL, h_t[i], alpha_h(v_current), beta_h(v_current), tadj_hh)
                n_half[i] = gating_update(DEL, n_t[i], alpha_n(v_current), beta_n(v_current), tadj_hh)

                mT_half[i] = gating_update_tau_inf(DEL, mT_t[i], inf_mT(v_current), tau_mT(v_current))
                hT_half[i] = gating_update_tau_inf(DEL, hT_t[i], inf_hT(v_current), tau_hT(v_current))
                
                g_Na_half[i] = (m_half[i]**3) * h_half[i] * seg.get_absolute_g_max("Na")
                g_K_half[i] = (n_half[i]**4) * seg.get_absolute_g_max("K")
                g_L_abs[i] = seg.get_absolute_g_max("L")

                P_Ca_abs = seg.get_absolute_P_max("CaT")
                I_T_abs[i], g_Ca_eq[i] = evaluate_GHK_and_Jacobian(
                    V_t[i], Ca_t[i], CA_OUT, P_Ca_abs, mT_half[i], hT_half[i]
                )
                
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
                
                A[i, i] = C_factor + g_Na_half[i] + g_K_half[i] + g_L_abs[i] + sum_Kij + g_Ca_eq[i]

                b[i] = (C_factor * V_t[i] + 
                        g_Na_half[i] * E_TABLE["Na"]["E"] + 
                        g_K_half[i] * E_TABLE["K"]["E"] + 
                        g_L_abs[i] * E_TABLE["L"]["E"] -
                        I_T_abs[i] + g_Ca_eq[i] * V_t[i])
                
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

            HISTORY_MT[step + 1] = mT_half
            HISTORY_HT[step + 1] = hT_half

            for i, seg in enumerate(seg_list):
                Ca_current = Ca_t[i]
                drive_channel = -seg.gamma_Ca * I_T_abs[i]
                if drive_channel <= 0.0:
                    drive_channel = 0.0  # Cannot pump inward (cadecay.mod)

                # 隐式 Euler 积分 (匹配 cadecay.mod 的 derivimplicit 方法)
                # cai' = drive + (cainf - cai)/taur
                # cai_new = (cai_old + DT*(drive + cainf/taur)) / (1 + DT/taur)
                HISTORY_CA[step + 1, i] = (Ca_current + DT * (drive_channel + CA_INF / TAU_CA)) / (1.0 + DT / TAU_CA)

            # --- 探针数据登记 ---
            # 检查当前步长是否触发探针，若触发则打包整个状态空间的标量与向量参数
            for probe in PROBE_LIST:
                probe_id, p_seg_id, probe_start_ms, probe_duration_ms = probe
                if probe_start_ms <= t_current <= (probe_start_ms + probe_duration_ms):
                    idx = seg_id_to_idx.get(p_seg_id)
                    if idx is None:
                        continue
                    
                    # 提取目标区室的 Segment 实例
                    target_seg = SEGMENT[p_seg_id]
                    
                    # 计算当前时刻的连续导数
                    dV_dt, dm_dt, dh_dt, dn_dt = compute_continuous_derivatives(
                        segment_id=p_seg_id, step=step
                    )

                    probe_data = {
                        "step": step,
                        "time_ms": t_current,
                        "segment_id": p_seg_id,
                        "V": float(V_t[idx]),
                        "m": float(m_t[idx]),
                        "h": float(h_t[idx]),
                        "n": float(n_t[idx]),

                        "dV_dt": float(dV_dt),
                        "dm_dt": float(dm_dt),
                        "dh_dt": float(dh_dt),
                        "dn_dt": float(dn_dt),

                        "g_Na_half": float(g_Na_half[idx]),
                        "g_K_half": float(g_K_half[idx]),
                        "g_L_abs": float(g_L_abs[idx]),
                        "mT": float(mT_t[idx]),
                        "hT": float(hT_t[idx]),
                        "Ca": float(Ca_t[idx]),
                        "I_T_abs": float(I_T_abs[idx]),
                        "g_Ca_eq": float(g_Ca_eq[idx]),
                        "A_matrix_row": A[idx, :].tolist(),
                        "b_vector_val": float(b[idx]),
                        "V_half_val": float(V_half[idx]),
                        "V_t_next": float(HISTORY_V[step + 1, idx])
                    }
                    save_data_HH(probe_id, probe_data)
        CURRENT_STEP = STEPS
    finally:
        SIMULATION_RUNNING = False
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


def export_calcium_history_matrices():
    """
    提供钙离子相关的时序状态流出口。
    保证 C-contiguous 连续内存状态。
    """
    global HISTORY_CA, HISTORY_MT, HISTORY_HT
    return (
        np.ascontiguousarray(HISTORY_CA, dtype=np.float64),
        np.ascontiguousarray(HISTORY_MT, dtype=np.float64),
        np.ascontiguousarray(HISTORY_HT, dtype=np.float64)
    )

def export_probe_data_json() -> str:
    """
    提供给外部运行时的探针数据出口。
    使用 JSON 字符串格式越过 C# 与 Python 之间的字典封送障碍。
    """
    return json.dumps(PROBE_SAVE_DATA)

# -----------------------------------------------------------------------------------

'''
seg_id_to_idx = {seg.id: i for i, seg in enumerate(seg_list)}
t_current = step * DT
V_t = HISTORY_V[step]
m_t = HISTORY_M[step]
h_t = HISTORY_H[step]
n_t = HISTORY_N[step]
'''

def compute_continuous_derivatives(segment_id: int, step: int, 
                                   V_override=None, m_override=None, 
                                   h_override=None, n_override=None):
    """
    计算局部区室连续导数。
    状态变更：通过 override 参数实现降维假设。未提供 override 的变量将提取其历史真实态。
    """
    global SEGMENT, HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N, STIMULATION, DT, E_TABLE

    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("请先调用 set_env")

    seg = SEGMENT[segment_id]
    seg_list = list(SEGMENT.values())
    seg_id_to_idx = {s.id: i for i, s in enumerate(seg_list)}
    idx = seg_id_to_idx[segment_id]

    V_t = HISTORY_V[step]
    m_t = HISTORY_M[step]
    h_t = HISTORY_H[step]
    n_t = HISTORY_N[step]
    t_current = step * DT

    # 状态路由：如果传入重载值(网格坐标)，则覆盖历史值
    V_i = V_override if V_override is not None else V_t[idx]
    m_i = m_override if m_override is not None else m_t[idx]
    h_i = h_override if h_override is not None else h_t[idx]
    n_i = n_override if n_override is not None else n_t[idx]

    # 计算门控变量导数 (含 Q10 温度校正，匹配 hh2.mod)
    tadj_hh = 3.0 ** ((CELSIUS - 36.0) / 10.0)
    dm_dt = tadj_hh * (alpha_m(V_i) * (1 - m_i) - beta_m(V_i) * m_i)
    dh_dt = tadj_hh * (alpha_h(V_i) * (1 - h_i) - beta_h(V_i) * h_i)
    dn_dt = tadj_hh * (alpha_n(V_i) * (1 - n_i) - beta_n(V_i) * n_i)

    # 计算电流状态
    g_Na = (m_i**3) * h_i * seg.get_absolute_g_max("Na")
    g_K = (n_i**4) * seg.get_absolute_g_max("K")
    g_L = seg.get_absolute_g_max("L")
    
    I_ion = (g_Na * (V_i - E_TABLE["Na"]["E"]) + 
             g_K * (V_i - E_TABLE["K"]["E"]) + 
             g_L * (V_i - E_TABLE["L"]["E"]))

    # T-type calcium current contribution
    if HISTORY_MT is not None and HISTORY_HT is not None and HISTORY_CA is not None:
        mT_i = HISTORY_MT[step, idx]
        hT_i = HISTORY_HT[step, idx]
        Ca_i = HISTORY_CA[step, idx]
    else:
        mT_i = inf_mT(V_i)
        hT_i = inf_hT(V_i)
        Ca_i = CA_INF
    P_Ca_abs = seg.get_absolute_P_max("CaT")
    I_T_ca, _ = evaluate_GHK_and_Jacobian(V_i, Ca_i, CA_OUT, P_Ca_abs, mT_i, hT_i)
    I_ion += I_T_ca

    I_axial = 0.0
    for connected_id in seg.connected_segments:
        target_seg = SEGMENT[connected_id]
        target_idx = seg_id_to_idx[connected_id]
        K_ij = calculate_Kij(seg, target_seg)
        V_j = V_t[target_idx] # 严格使用真实历史邻居电位
        I_axial += K_ij * (V_j - V_i)

    I_stim = 0.0
    for stim in STIMULATION:
        stim_id, s_id, stim_uA, stim_start, stim_duration = stim
        if seg.id == s_id and (stim_start <= t_current <= stim_start + stim_duration):
            I_stim += stim_uA

    dV_dt = (-I_ion + I_axial + I_stim) / seg.absolute_C

    # 必须完整返回所有 4 个维度的偏导数，由外部绘图函数进行二次提取
    return dV_dt, dm_dt, dh_dt, dn_dt


import numpy as np

def generate_phase_portrait_mesh(segment_id: int, step: int, Nx: int = 20, Ny: int = 20):
    """
    分离式的相图渲染数据接口。
    依赖：必须在 start_simulation() 执行完毕后调用，直接访问常驻内存的 HISTORY_V 等数据。
    """
    global HISTORY_V
    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("请先调用 set_env")
    
    # 建立网格空间
    v_space = np.linspace(-80, 40, Nx)
    n_space = np.linspace(0, 1, Ny)
    V_grid, N_grid = np.meshgrid(v_space, n_space)
    
    dV_grid = np.zeros((Ny, Nx))
    dN_grid = np.zeros((Ny, Nx))
    
    # 遍历计算网格状态 (复用之前重载后的 compute_continuous_derivatives)
    for i in range(Ny):
        for j in range(Nx):
            v_local = V_grid[i, j]
            n_local = N_grid[i, j]
            
            dV, _, _, dn = compute_continuous_derivatives(
                segment_id=segment_id, 
                step=step, 
                V_override=v_local, 
                n_override=n_local
            )
            dV_grid[i, j] = dV
            dN_grid[i, j] = dn

    # 返回给 C# 端：扁平化处理或保留多维结构
    # 为方便 pythonnet 传输，可以转为列表或一维数组
    return (
        V_grid.flatten().tolist(),
        N_grid.flatten().tolist(),
        dV_grid.flatten().tolist(),
        dN_grid.flatten().tolist()
    )


def show_dynamic_phase_portrait(probe_id, x_var: str = 'V', y_var: str = 'n', Nx: int = 20, Ny: int = 20, interval: int = 50):
    """
    基于指定参数组合重构相平面向量场。
    probe_id: 探针 ID（int，与 insert_probe 的 probe_id 一致）。
    x_var, y_var: 可选值为 'V', 'm', 'h', 'n'，不可相同。
    """
    global PROBE_LIST, SEGMENT, HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N, DT, STEPS

    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("请先调用 set_env")
    
    valid_vars = ['V', 'm', 'h', 'n']
    if x_var not in valid_vars or y_var not in valid_vars:
        raise ValueError(f"轴变量必须在 {valid_vars} 中选择。")
    if x_var == y_var:
        raise ValueError(f"x_var 和 y_var 不可相同: '{x_var}'。")

    # 1. 探针解析 (与前版相同)
    target_probe = None
    for p in PROBE_LIST:
        if p[0] == probe_id:
            target_probe = p
            break
    if target_probe is None:
        raise ValueError(f"探针解析失败: '{probe_id}'。")

    _, segment_id, start_ms, duration_ms = target_probe
    start_step = max(0, int(start_ms / DT))
    end_step = min(STEPS, int((start_ms + duration_ms) / DT))
    N_frames = end_step - start_step + 1

    seg_list = list(SEGMENT.values())
    seg_id_to_idx = {s.id: i for i, s in enumerate(seg_list)}
    idx = seg_id_to_idx[segment_id]

    # 2. 动态历史矩阵寻址流
    history_map = {'V': HISTORY_V, 'm': HISTORY_M, 'h': HISTORY_H, 'n': HISTORY_N}
    traj_x = history_map[x_var][start_step : end_step + 1, idx]
    traj_y = history_map[y_var][start_step : end_step + 1, idx]

    # 3. 动态网格空间分配
    def get_space(var_name, N_points):
        # 电压轴边界为 [-80, 40]，门控变量轴边界为 [0, 1]
        return np.linspace(-80, 40, N_points) if var_name == 'V' else np.linspace(0, 1, N_points)

    x_space = get_space(x_var, Nx)
    y_space = get_space(y_var, Ny)
    X_grid, Y_grid = np.meshgrid(x_space, y_space)
    
    dX_grid = np.zeros((Ny, Nx))
    dY_grid = np.zeros((Ny, Nx))

    # 4. 渲染器实例化
    fig, ax = plt.subplots(figsize=(8, 6))
    ax.set_xlim(x_space[0] - 0.05 * abs(x_space[0]), x_space[-1] + 0.05 * abs(x_space[-1]))
    ax.set_ylim(y_space[0] - 0.05, y_space[-1] + 0.05)
    ax.set_xlabel(f'Variable: {x_var}')
    ax.set_ylabel(f'Variable: {y_var}')
    ax.set_title(f'Dynamic Phase Portrait [{y_var} vs {x_var}] - Probe: {probe_id}')
    ax.grid(True, linestyle=':', alpha=0.6)

    Q = ax.quiver(X_grid, Y_grid, dX_grid, dY_grid, color='gray', alpha=0.6)
    traj_line, = ax.plot([], [], color='blue', linewidth=2)
    curr_point, = ax.plot([], [], 'ro', markersize=8)

    # 5. 状态更新回调定义
    def update(frame):
        current_step = start_step + frame

        for i in range(Ny):
            for j in range(Nx):
                x_val = X_grid[i, j]
                y_val = Y_grid[i, j]
                
                # 动态组装 kwargs 进行状态重载
                overrides = {
                    'V_override': None, 'm_override': None, 
                    'h_override': None, 'n_override': None
                }
                overrides[f'{x_var}_override'] = x_val
                overrides[f'{y_var}_override'] = y_val
                
                # 计算全维偏导
                dV, dm, dh, dn = compute_continuous_derivatives(
                    segment_id=segment_id, step=current_step, **overrides
                )
                
                # 输出结果字典路由
                deriv_map = {'V': dV, 'm': dm, 'h': dh, 'n': dn}
                dX_grid[i, j] = deriv_map[x_var]
                dY_grid[i, j] = deriv_map[y_var]

        Q.set_UVC(dX_grid, dY_grid)
        traj_line.set_data(traj_x[:frame+1], traj_y[:frame+1])
        curr_point.set_data([traj_x[frame]], [traj_y[frame]])

        return Q, traj_line, curr_point

    ani = animation.FuncAnimation(
        fig, update, frames=N_frames, interval=interval, blit=True, repeat=False
    )
    plt.show()


def plot_variable_over_time(segment_id: int, var_label: str, start_time_ms: float, end_time_ms: float):
    """
    绘制指定区室的指定状态变量在给定时间区间内随时间的变化曲线。
    必须在 start_simulation() 执行完毕后调用。

    Parameters:
        segment_id:    区室 ID（对应 init_segment 的 id 参数）
        var_label:     变量标签，可选 'V', 'm', 'h', 'n'
        start_time_ms: 起始时间 (ms)
        end_time_ms:   终止时间 (ms)
    """
    global HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N, DT, STEPS, SEGMENT
    global HISTORY_CA, HISTORY_MT, HISTORY_HT

    if HISTORY_V is None or HISTORY_M is None or HISTORY_H is None or HISTORY_N is None:
        raise RuntimeError("请先调用 set_env 并执行仿真。")

    valid_labels = ['V', 'm', 'h', 'n', 'Ca', 'mT', 'hT']
    if var_label not in valid_labels:
        raise ValueError(f"变量标签必须在 {valid_labels} 中选择，当前: '{var_label}'")

    if segment_id not in SEGMENT:
        raise ValueError(f"区室 ID {segment_id} 不存在。")

    seg_list = list(SEGMENT.values())
    seg_id_to_idx = {s.id: i for i, s in enumerate(seg_list)}
    idx = seg_id_to_idx[segment_id]

    start_step = max(0, int(start_time_ms / DT))
    end_step = min(STEPS, int(end_time_ms / DT))

    if start_step >= end_step:
        raise ValueError(f"时间范围无效: start={start_time_ms}ms, end={end_time_ms}ms")

    history_map = {
        'V': HISTORY_V, 'm': HISTORY_M, 'h': HISTORY_H, 'n': HISTORY_N,
        'Ca': HISTORY_CA, 'mT': HISTORY_MT, 'hT': HISTORY_HT
    }
    data = history_map[var_label][start_step:end_step + 1, idx]
    time_axis = np.arange(start_step, end_step + 1) * DT

    ylabel_map = {
        'V': 'Membrane Potential (mV)',
        'm': 'Na activation (m)',
        'h': 'Na inactivation (h)',
        'n': 'K activation (n)',
        'Ca': 'Intracellular Ca²⁺ (mM)',
        'mT': 'T-type Ca activation (mT)',
        'hT': 'T-type Ca inactivation (hT)'
    }

    fig, ax = plt.subplots(figsize=(10, 5))
    ax.plot(time_axis, data, linewidth=1.5)
    ax.set_xlabel('Time (ms)')
    ax.set_ylabel(ylabel_map[var_label])
    ax.set_title(f'Segment {segment_id} — {var_label} vs Time')
    ax.grid(True, linestyle=':', alpha=0.6)
    plt.tight_layout()
    plt.show()