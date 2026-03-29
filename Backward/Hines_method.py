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

FARADAY = 96485.3       # Coulomb per mole (C/mol)
R_GAS = 8.314           # Joules per (mol*K) (J/(mol*K))
CELSIUS = 24.0          # Simulation temperature (degC) — tcD_vc.oc: celsius=24
TEMP_K = CELSIUS + 273.15
Z_CA = 2.0              # Calcium ion valence

CA_OUT = 2.0            # Extracellular Ca concentration (mM)
CA_INF = 2.4e-4         # Intracellular steady-state free Ca (mM) — cadecay.mod: cainf=2.4e-4
TAU_CA = 5.0            # Ca decay time constant (ms) — cadecay.mod: taur=5

E_TABLE = {
    "Na": {"E": 50.0},
    "K": {"E": -90.0},
    "L": {"E": -76.5}
}

SEGMENT = {}

V = -70.0
DT = 0.1
DEL = DT / 2
STEPS = 10000
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

VOLTAGE_CLAMP = []
# Each entry: (vc_id, segment_id, rs_MOhm, protocol)
# protocol is a list of (duration_ms, amplitude_mV) tuples, executed sequentially.
# E.g. [(1000, -115), (1000, -65), (1000, -65)] mimics SEClamp with 3 steps.

PROBE_SAVE_DATA = {}
#key:stimulation_id value:{all_params_name:params_value}

CURRENT_STEP = -1
SIMULATION_RUNNING = False

# HH gating parameters — Traub-modified (hh2.mod)
# v2 = V - vtraub;  rate functions use v2 instead of V directly.
HH_PARAMS = {
    "vtraub": -63.0,
    "alpha_m_A": 0.32,  "alpha_m_V": 13.0, "alpha_m_k": 4.0,
    "beta_m_A":  0.28,  "beta_m_V":  40.0, "beta_m_k":  5.0,
    "alpha_h_A": 0.128, "alpha_h_V": 17.0, "alpha_h_k": 18.0,
    "beta_h_A":  4.0,   "beta_h_V":  40.0, "beta_h_k":  5.0,
    "alpha_n_A": 0.032, "alpha_n_V": 15.0, "alpha_n_k":  5.0,
    "beta_n_A":  0.5,   "beta_n_V":  10.0, "beta_n_k": 40.0,
}

# Ca T-type channel parameters — ITGHK.mod + tcD_vc.oc overrides
# Raw Vh values; shift/actshift applied in kinetic functions.
# Q10 values: qm=2.5, qh=2.5 (tcD_vc.oc), shift=-1 (tcD_vc.oc)
CA_PARAMS = {
    "shift": -1.0, "actshift": 0.0,
    "inf_mT_Vh": 57.0, "inf_mT_k": 6.2,
    "inf_hT_Vh": 81.0, "inf_hT_k": 4.0,
    "tau_mT_base": 0.612, "tau_mT_V1": 132.0, "tau_mT_k1": 16.7,
    "tau_mT_V2": 16.8, "tau_mT_k2": 18.2, "tau_mT_Q10": 2.5, "tau_mT_Tref": 24.0,
    "tau_hT_Vthresh": -80.0,
    "tau_hT_V1": 467.0, "tau_hT_k1": 66.6,
    "tau_hT_base": 28.0, "tau_hT_V2": 22.0, "tau_hT_k2": 10.5,
    "tau_hT_Q10": 2.5, "tau_hT_Tref": 24.0,
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
def set_env(V_init: float= -70.0,
        dt: float = 0.1,
        steps: int = 10000,
        n_node:int = 0,
        celsius: float = 24.0,
        ca_out: float = 2.0,
        ca_inf: float = 2.4e-4,
        tau_ca: float = 5.0):
    global V, DT, DEL, STEPS, N_NODE, HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N
    global HISTORY_CA, HISTORY_MT, HISTORY_HT
    global CELSIUS, TEMP_K, CA_OUT, CA_INF, TAU_CA

    V = V_init # Initialize membrane potential for all segments
    DT = dt # Simulation timestep (ms)
    DEL = dt / 2
    STEPS = steps # Number of simulation steps
    N_NODE = n_node # Total number of compartments (segments)
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

def set_E(table : dict): # Set reversal potentials for ions
    global E_TABLE
    E_TABLE = table

def set_hh_params(params):
    """Update HH_PARAMS from a dictionary passed from C#."""
    global HH_PARAMS
    for key in params:
        if key in HH_PARAMS:
            HH_PARAMS[key] = float(params[key])

def set_ca_params(params):
    """Update CA_PARAMS from a dictionary passed from C#."""
    global CA_PARAMS
    for key in params:
        if key in CA_PARAMS:
            CA_PARAMS[key] = float(params[key])

def insert_probe(probe_id, segment_id, probe_start_ms, probe_duration_ms):
    """
    Register an interval probe. See `insert_stimulation` for parameter ordering.
    - probe_id: unique probe identifier
    - segment_id: segment id to monitor
    - probe_start_ms: probe start time (ms)
    - probe_duration_ms: probe duration (ms)
    """
    global PROBE_LIST
    PROBE_LIST.append((probe_id, segment_id, probe_start_ms, probe_duration_ms))

def insert_stimulation(stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration):
    global STIMULATION
    STIMULATION.append((stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration))

def insert_voltage_clamp(vc_id, segment_id, rs_MOhm, protocol):
        """
        Register a voltage clamp device, similar to NEURON's SEClamp.

        Parameters:
        - vc_id:       unique voltage clamp id (int)
        - segment_id:  target segment id (int)
        - rs_MOhm:     series resistance in MΩ (corresponds to SEClamp.rs)
        - protocol:    list of [duration_ms, amplitude_mV] steps executed sequentially,
                                     e.g. [[100, -115], [1000, -65], [1000, -65]]

        In the Hines matrix, the clamp contributes an effective conductance:
            g_vc = 1e-3 / rs_MOhm (mS)
            A[i,i] += g_vc
            b[i]   += g_vc * V_cmd
        """
        global VOLTAGE_CLAMP
        VOLTAGE_CLAMP.append((vc_id, segment_id, rs_MOhm, protocol))

@dataclass
class Segment:
    uid: str
    Ra: float          # Local axial resistivity (ohm * cm), typical values ~35.4–100
    D: float           # Diameter (um)
    L: float           # Length (um)
    Cm: float          # Specific membrane capacitance (uF/cm^2), typical ~1.0
    id: int
    ca_shell_depth_um: float = 0.1

    channels: dict = field(default_factory=dict)
    connected_segments: list = field(default_factory=list)
    @property
    def surface_area_cm2(self):
        # Convert surface area from um^2 to cm^2 (multiply by 1e-8)
        return np.pi * self.D * self.L * 1e-8
    
    @property
    def cross_area_um2(self):
        # Cross-sectional area in um^2 for axial resistance calculations
        return np.pi * (self.D / 2)**2
    
    @property
    def absolute_C(self):
        # Result units: uF
        return self.Cm * self.surface_area_cm2
        
    def get_absolute_g_max(self, ion_name):
        # Result units: mS
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
        """Extract permeability and convert to an absolute, area-related coefficient."""
        # Returns value scaled as cm/s * cm^2 = cm^3/s; units are handled in GHK evaluation
        return self.channels.get(ion_name, 0.0) * self.surface_area_cm2
    
    @property
    def gamma_Ca(self):
        """
        Compute Faraday geometric conversion factor (concentration flux factor).
        Unit derivation: convert absolute current (uA) to concentration change (mM/ms).
        Assumes Ca accumulates in a submembrane shell of depth d = 0.1 um.
        """
        d_um = self.ca_shell_depth_um
        shell_volume_L = self.surface_area_cm2 * (d_um * 1e-4) * 1e-3 # liters
        # d[Ca]/dt = - I / (Z * F * Volume)
        # I(uA)=1e-6 A, d[Ca]/dt units mM/ms = mol/(L·s)
        # gamma = 1e-6 / (Z * F * Volume_L)
        return 1e-6 / (Z_CA * FARADAY * shell_volume_L)


def init_segment(uid: str, Ra: float, D: float, L: float, Cm: float, id: int, ca_shell_depth_um: float = 0.1):
    new_seg = Segment(uid=uid, Ra=Ra, D=D, L=L, Cm=Cm, id=id, ca_shell_depth_um = ca_shell_depth_um)
    SEGMENT[id] = new_seg


def add_connection(id, target_uid):  # Connect compartments
    SEGMENT[id].add_connection(target_uid)


def add_channel_to_segment(segment_id, channel_name, g_max):
    """Add an ion channel to an initialized segment."""
    SEGMENT[segment_id].add_channels(channel_name, g_max)


def clear_environment():
    """Reset all global state to prepare for the next simulation run."""
    global SEGMENT, V, DT, DEL, STEPS, N_NODE
    global HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N
    global HISTORY_CA, HISTORY_MT, HISTORY_HT
    global PROBE_LIST, STIMULATION, VOLTAGE_CLAMP, PROBE_SAVE_DATA
    global CURRENT_STEP, SIMULATION_RUNNING, E_TABLE

    SEGMENT = {}
    V = -70.0
    DT = 0.1
    DEL = DT / 2
    STEPS = 10000
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
    VOLTAGE_CLAMP = []
    PROBE_SAVE_DATA = {}
    CURRENT_STEP = -1
    SIMULATION_RUNNING = False
    E_TABLE = {
        "Na": {"E": 50.0},
        "K":  {"E": -90.0},
        "L":  {"E": -76.5}
    }
    HH_PARAMS.update(_HH_PARAMS_DEFAULT)
    CA_PARAMS.update(_CA_PARAMS_DEFAULT)


def get_current_step():
    """Return the current simulation step for external polling."""
    return CURRENT_STEP


def is_simulation_running():
    """Return whether the simulation is currently running."""
    return SIMULATION_RUNNING


# -----------------------------------------------------------------------------------
# simulation

def calculate_Kij(seg_i: Segment, seg_j: Segment):
    # Compute axial resistance with unit conversion to kOhm
    # R (kOhm) = Ra (ohm*cm) * 10 * L (um) / Area (um^2)
    R_i = seg_i.Ra * 10.0 * (seg_i.L / 2) / seg_i.cross_area_um2
    R_j = seg_j.Ra * 10.0 * (seg_j.L / 2) / seg_j.cross_area_um2
    
    return 1.0 / (R_i + R_j)

def alpha_m(V):
    """hh2.mod Traub: alpha_m = A*(V0-v2)/(exp((V0-v2)/k)-1), v2=V-vtraub"""
    vtraub = HH_PARAMS["vtraub"]
    A  = HH_PARAMS["alpha_m_A"]
    V0 = HH_PARAMS["alpha_m_V"]
    k  = HH_PARAMS["alpha_m_k"]
    v2 = V - vtraub
    x = V0 - v2
    if abs(x) < 1e-6:
        return A * k          # L'Hôpital limit
    return A * x / (np.exp(x / k) - 1.0)

def alpha_n(V):
    """hh2.mod Traub: alpha_n = A*(V0-v2)/(exp((V0-v2)/k)-1)"""
    vtraub = HH_PARAMS["vtraub"]
    A  = HH_PARAMS["alpha_n_A"]
    V0 = HH_PARAMS["alpha_n_V"]
    k  = HH_PARAMS["alpha_n_k"]
    v2 = V - vtraub
    x = V0 - v2
    if abs(x) < 1e-6:
        return A * k
    return A * x / (np.exp(x / k) - 1.0)

def beta_m(V):
    """hh2.mod Traub: beta_m = A*(v2-V0)/(exp((v2-V0)/k)-1)"""
    vtraub = HH_PARAMS["vtraub"]
    A  = HH_PARAMS["beta_m_A"]
    V0 = HH_PARAMS["beta_m_V"]
    k  = HH_PARAMS["beta_m_k"]
    v2 = V - vtraub
    x = v2 - V0
    if abs(x) < 1e-6:
        return A * k
    return A * x / (np.exp(x / k) - 1.0)

def alpha_h(V):
    """hh2.mod Traub: alpha_h = A*exp((V0-v2)/k)"""
    vtraub = HH_PARAMS["vtraub"]
    A  = HH_PARAMS["alpha_h_A"]
    V0 = HH_PARAMS["alpha_h_V"]
    k  = HH_PARAMS["alpha_h_k"]
    v2 = V - vtraub
    return A * np.exp((V0 - v2) / k)

def beta_h(V):
    """hh2.mod Traub: beta_h = A/(1+exp((V0-v2)/k))"""
    vtraub = HH_PARAMS["vtraub"]
    A  = HH_PARAMS["beta_h_A"]
    V0 = HH_PARAMS["beta_h_V"]
    k  = HH_PARAMS["beta_h_k"]
    v2 = V - vtraub
    return A / (1.0 + np.exp((V0 - v2) / k))

def beta_n(V):
    """hh2.mod Traub: beta_n = A*exp((V0-v2)/k)"""
    vtraub = HH_PARAMS["vtraub"]
    A  = HH_PARAMS["beta_n_A"]
    V0 = HH_PARAMS["beta_n_V"]
    k  = HH_PARAMS["beta_n_k"]
    v2 = V - vtraub
    return A * np.exp((V0 - v2) / k)

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
    Crank-Nicolson gating update based on tau and inf (full-step update consistent with gating_update).
    Using DEL=dt/2 implements a CN format over a dt step to match NEURON's cnexp accuracy.
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

    # If the segment has no T-type channel, short-circuit
    if P_max_abs == 0.0:
        return 0.0, 0.0

    # Dimensionless voltage factor k
    k = (Z_CA * FARADAY) / (1000.0 * R_GAS * TEMP_K)
    z = k * V
    
    # Probability factor and constant prefix
    # Unit alignment: P_max_abs(cm^3/s) * 2F(C/mol) * 1e-3 -> mA (then converted to uA by *1000)
    GHK_prefix = P_max_abs * (m_T**2) * h_T * (Z_CA * FARADAY * 1e-3) * 1000.0
    
    # Handle singular case (V near zero)
    if abs(z) < 1e-4:
        # Taylor expansion for the limit (L'Hôpital's rule)
        # f(z) = z*(Ci - Co*e^{-z})/(1-e^{-z}) ≈ (Ci-Co) + z*(Ci+Co)/2 + O(z^2)
        I_T_abs = GHK_prefix * ((Cai - Cao) + z * (Cai + Cao) / 2.0)
        g_Ca_eq = GHK_prefix * k * ((Cai + Cao) / 2.0)
        return I_T_abs, g_Ca_eq

    # Normal case
    exp_z = np.exp(z)
    exp_minus_z = np.exp(-z)
    
    # 1. Evaluate absolute current
    f_z = z * (Cai - Cao * exp_minus_z) / (1.0 - exp_minus_z)
    I_T_abs = GHK_prefix * f_z
    
    # 2. Compute Jacobian (analytic derivative via chain rule)
    denominator = (exp_z - 1.0)
    f_prime_z = ((Cai * exp_z * (1.0 + z) - Cao) * denominator - (Cai * z * exp_z - Cao * z) * exp_z) / (denominator**2)
    g_Ca_eq = GHK_prefix * k * f_prime_z
    
    return I_T_abs, g_Ca_eq


# -----------------------------------------------------------------------------------


def _compute_vc_current(segment_id, V_membrane, t_current):
    """
    Compute the voltage-clamp current (µA) for a given segment at a given time.
    I_vc = g_vc * (V_cmd - V_membrane), g_vc = 1e-3 / rs_MOhm (mS)
    Positive = injected current, negative = withdrawn current.
    If no clamp applies to the segment, return 0.0.
    """
    total_I = 0.0
    for vc in VOLTAGE_CLAMP:
        vc_id, vc_seg_id, rs_MOhm, protocol = vc
        if segment_id != vc_seg_id:
            continue
        t_acc = 0.0
        v_cmd = None
        for step_dur, step_amp in protocol:
            if t_current < t_acc + step_dur:
                v_cmd = step_amp
                break
            t_acc += step_dur
        if v_cmd is None:
            v_cmd = protocol[-1][1] if protocol else V
        g_vc = 1e-3 / rs_MOhm
        total_I += g_vc * (v_cmd - V_membrane)
    return total_I


# State containers: shape (steps + 1, N_NODE)
def start_simulation(progress_callback=None):
    global CURRENT_STEP, SIMULATION_RUNNING
    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("Please call set_env before starting the simulation.")
    
    if HISTORY_MT is None or HISTORY_HT is None or HISTORY_CA is None:
        raise RuntimeError("Please call set_env before starting the simulation.")
    SIMULATION_RUNNING = True
    CURRENT_STEP = 0

    try:
        # Build mapping from segment_id to matrix index i for O(1) lookup
        seg_list = list(SEGMENT.values())
        seg_id_to_idx = {seg.id: i for i, seg in enumerate(seg_list)}
        
        # Load initial state
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
            t_current = step * DT  # Current absolute time (ms)
            
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
            
            # Q10 temperature adjustment (hh2.mod: tadj = 3.0 ** ((celsius-36)/10))
            tadj_hh = 3.0 ** ((CELSIUS - 36.0) / 10.0)

            # Step 1: update gating variables and conductances
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
            
            # Step 2: assemble matrix and inject stimulation currents
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
                
                # --- External stimulation (current clamp) ---
                # Iterate stim configurations; if active for this segment/time, add current to RHS vector b
                for stim in STIMULATION:
                    stim_id, s_id, stim_uA, stim_start, stim_duration = stim
                    if seg.id == s_id:
                        if stim_start <= t_current <= (stim_start + stim_duration):
                            b[i] += stim_uA

                # --- Voltage clamp handling ---
                # Iterate voltage clamp configs and inject an effective conductance to clamp voltage
                # g_vc ~ 1/rs (rs in MΩ -> g_vc in µS = 1e-3 mS)
                # Note: align units with other conductances in the matrix (mS)
                # NEURON SEClamp uses i = (V_cmd - V)/rs in nA, our units are µA,
                # so i = (V_cmd - V)/rs * 1e-3 (nA -> µA), equivalently g_vc = 1e-3 / rs (mS)
                for vc in VOLTAGE_CLAMP:
                    vc_id, vc_seg_id, rs_MOhm, protocol = vc
                    if seg.id == vc_seg_id:
                        # Determine the protocol step corresponding to the current time
                        t_elapsed = t_current
                        v_cmd = None
                        t_acc = 0.0
                        for step_dur, step_amp in protocol:
                            if t_elapsed < t_acc + step_dur:
                                v_cmd = step_amp
                                break
                            t_acc += step_dur
                        if v_cmd is None:
                            # If beyond protocol duration, use the last step's voltage
                            v_cmd = protocol[-1][1] if protocol else V
                        g_vc = 1e-3 / rs_MOhm  # mS
                        A[i, i] += g_vc
                        b[i] += g_vc * v_cmd

            # Step 3: implicit solve and time-step advance
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
                    drive_channel = 0.0  # Cannot pump inward (matching cadecay.mod behavior)

                # Implicit Euler integration (matching cadecay.mod's derivimplicit):
                # cai' = drive + (cainf - cai)/taur
                # cai_new = (cai_old + DT*(drive + cainf/taur)) / (1 + DT/taur)
                HISTORY_CA[step + 1, i] = (Ca_current + DT * (drive_channel + CA_INF / TAU_CA)) / (1.0 + DT / TAU_CA)

            # --- Probe data logging ---
            # Check whether probes are active at the current step; if so, package scalars/vectors
            for probe in PROBE_LIST:
                probe_id, p_seg_id, probe_start_ms, probe_duration_ms = probe
                if probe_start_ms <= t_current <= (probe_start_ms + probe_duration_ms):
                    idx = seg_id_to_idx.get(p_seg_id)
                    if idx is None:
                        continue
                    
                    # Extract the target Segment instance
                    target_seg = SEGMENT[p_seg_id]
                    
                    # Compute continuous derivatives at the current time
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
                        "V_t_next": float(HISTORY_V[step + 1, idx]),
                        "I_vc": float(_compute_vc_current(p_seg_id, V_t[idx], t_current))
                    }
                    save_data_HH(probe_id, probe_data)
        CURRENT_STEP = STEPS
    finally:
        SIMULATION_RUNNING = False
# -----------------------------------------------------------------------------------
# External interface layer

def export_history_matrices():
    """
    Export time-series state matrices for external runtimes.
    Ensures C-contiguous memory layout.
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
    Export calcium-related time-series matrices.
    Ensures C-contiguous memory layout.
    """
    global HISTORY_CA, HISTORY_MT, HISTORY_HT
    return (
        np.ascontiguousarray(HISTORY_CA, dtype=np.float64),
        np.ascontiguousarray(HISTORY_MT, dtype=np.float64),
        np.ascontiguousarray(HISTORY_HT, dtype=np.float64)
    )

def export_probe_data_json() -> str:
    """
    Export probe data for external runtimes as a JSON string.
    This bypasses dictionary marshaling between C# and Python.
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
    Compute continuous derivatives for a local segment.
    State overrides: provide reduced-dimension values via override parameters;
    if not provided, historical values are used.
    """
    global SEGMENT, HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N, STIMULATION, DT, E_TABLE

    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("Please call set_env before using this function.")

    seg = SEGMENT[segment_id]
    seg_list = list(SEGMENT.values())
    seg_id_to_idx = {s.id: i for i, s in enumerate(seg_list)}
    idx = seg_id_to_idx[segment_id]

    V_t = HISTORY_V[step]
    m_t = HISTORY_M[step]
    h_t = HISTORY_H[step]
    n_t = HISTORY_N[step]
    t_current = step * DT

    # State routing: if override values (e.g., grid coordinates) are provided, they replace history
    V_i = V_override if V_override is not None else V_t[idx]
    m_i = m_override if m_override is not None else m_t[idx]
    h_i = h_override if h_override is not None else h_t[idx]
    n_i = n_override if n_override is not None else n_t[idx]

    # Compute gating variable derivatives (includes Q10 temperature correction, matching hh2.mod)
    tadj_hh = 3.0 ** ((CELSIUS - 36.0) / 10.0)
    dm_dt = tadj_hh * (alpha_m(V_i) * (1 - m_i) - beta_m(V_i) * m_i)
    dh_dt = tadj_hh * (alpha_h(V_i) * (1 - h_i) - beta_h(V_i) * h_i)
    dn_dt = tadj_hh * (alpha_n(V_i) * (1 - n_i) - beta_n(V_i) * n_i)

    # Compute ionic currents
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
        V_j = V_t[target_idx] # Strictly use historical neighbor voltages
        I_axial += K_ij * (V_j - V_i)

    I_stim = 0.0
    for stim in STIMULATION:
        stim_id, s_id, stim_uA, stim_start, stim_duration = stim
        if seg.id == s_id and (stim_start <= t_current <= stim_start + stim_duration):
            I_stim += stim_uA

    dV_dt = (-I_ion + I_axial + I_stim) / seg.absolute_C

    # Must return all four derivative dimensions; external plotting functions will extract what they need
    return dV_dt, dm_dt, dh_dt, dn_dt


from scipy.optimize import fsolve

# -----------------------------------------------------------------------------------
# Phase portrait helpers (vectorized)

def _get_var_range(var_name):
    """Return plotting range for a given variable."""
    if var_name == 'V':
        return -100.0, 60.0
    elif var_name == 'Ca':
        return 0.0, 0.01
    else:
        return 0.0, 1.0

def _alpha_m_vec(V):
    vtraub = HH_PARAMS["vtraub"]
    A  = HH_PARAMS["alpha_m_A"]; V0 = HH_PARAMS["alpha_m_V"]; k = HH_PARAMS["alpha_m_k"]
    v2 = V - vtraub; x = V0 - v2
    safe = np.where(np.abs(x) < 1e-6, A * k, A * x / (np.exp(np.clip(x / k, -500, 500)) - 1.0 + 1e-30))
    return safe

def _beta_m_vec(V):
    vtraub = HH_PARAMS["vtraub"]
    A  = HH_PARAMS["beta_m_A"]; V0 = HH_PARAMS["beta_m_V"]; k = HH_PARAMS["beta_m_k"]
    v2 = V - vtraub; x = v2 - V0
    safe = np.where(np.abs(x) < 1e-6, A * k, A * x / (np.exp(np.clip(x / k, -500, 500)) - 1.0 + 1e-30))
    return safe

def _alpha_h_vec(V):
    vtraub = HH_PARAMS["vtraub"]
    A = HH_PARAMS["alpha_h_A"]; V0 = HH_PARAMS["alpha_h_V"]; k = HH_PARAMS["alpha_h_k"]
    v2 = V - vtraub
    return A * np.exp(np.clip((V0 - v2) / k, -500, 500))

def _beta_h_vec(V):
    vtraub = HH_PARAMS["vtraub"]
    A = HH_PARAMS["beta_h_A"]; V0 = HH_PARAMS["beta_h_V"]; k = HH_PARAMS["beta_h_k"]
    v2 = V - vtraub
    return A / (1.0 + np.exp(np.clip((V0 - v2) / k, -500, 500)))

def _alpha_n_vec(V):
    vtraub = HH_PARAMS["vtraub"]
    A  = HH_PARAMS["alpha_n_A"]; V0 = HH_PARAMS["alpha_n_V"]; k = HH_PARAMS["alpha_n_k"]
    v2 = V - vtraub; x = V0 - v2
    safe = np.where(np.abs(x) < 1e-6, A * k, A * x / (np.exp(np.clip(x / k, -500, 500)) - 1.0 + 1e-30))
    return safe

def _beta_n_vec(V):
    vtraub = HH_PARAMS["vtraub"]
    A = HH_PARAMS["beta_n_A"]; V0 = HH_PARAMS["beta_n_V"]; k = HH_PARAMS["beta_n_k"]
    v2 = V - vtraub
    return A * np.exp(np.clip((V0 - v2) / k, -500, 500))

def _inf_mT_vec(V):
    shift = CA_PARAMS["shift"]; actshift = CA_PARAMS["actshift"]
    Vh = CA_PARAMS["inf_mT_Vh"]; k = CA_PARAMS["inf_mT_k"]
    return 1.0 / (1.0 + np.exp(np.clip(-(V + shift + actshift + Vh) / k, -500, 500)))

def _inf_hT_vec(V):
    shift = CA_PARAMS["shift"]
    Vh = CA_PARAMS["inf_hT_Vh"]; k = CA_PARAMS["inf_hT_k"]
    return 1.0 / (1.0 + np.exp(np.clip((V + shift + Vh) / k, -500, 500)))

def _tau_mT_vec(V):
    shift = CA_PARAMS["shift"]; actshift = CA_PARAMS["actshift"]
    base = CA_PARAMS["tau_mT_base"]; V1 = CA_PARAMS["tau_mT_V1"]; k1 = CA_PARAMS["tau_mT_k1"]
    V2 = CA_PARAMS["tau_mT_V2"]; k2 = CA_PARAMS["tau_mT_k2"]
    Q10 = CA_PARAMS["tau_mT_Q10"]; Tref = CA_PARAMS["tau_mT_Tref"]
    phi_m = Q10 ** ((CELSIUS - Tref) / 10.0)
    Vs = V + shift + actshift
    return (base + 1.0 / (np.exp(np.clip(-(Vs + V1) / k1, -500, 500)) + np.exp(np.clip((Vs + V2) / k2, -500, 500)))) / phi_m

def _tau_hT_vec(V):
    shift = CA_PARAMS["shift"]; Vth = CA_PARAMS["tau_hT_Vthresh"]
    V1 = CA_PARAMS["tau_hT_V1"]; k1 = CA_PARAMS["tau_hT_k1"]
    base = CA_PARAMS["tau_hT_base"]; V2 = CA_PARAMS["tau_hT_V2"]; k2 = CA_PARAMS["tau_hT_k2"]
    Q10 = CA_PARAMS["tau_hT_Q10"]; Tref = CA_PARAMS["tau_hT_Tref"]
    phi_h = Q10 ** ((CELSIUS - Tref) / 10.0)
    Vs = V + shift
    branch_low  = np.exp(np.clip((Vs + V1) / k1, -500, 500)) / phi_h
    branch_high = (base + np.exp(np.clip(-(Vs + V2) / k2, -500, 500))) / phi_h
    return np.where(Vs < Vth, branch_low, branch_high)

def _evaluate_GHK_vec(V, Cai, Cao, P_max_abs, m_T, h_T):
    """Vectorized GHK current evaluation (absolute current only; does not return Jacobian)."""
    if P_max_abs == 0.0:
        return np.zeros_like(V) if isinstance(V, np.ndarray) else 0.0
    k_ghk = (Z_CA * FARADAY) / (1000.0 * R_GAS * TEMP_K)
    z = k_ghk * V
    GHK_prefix = P_max_abs * (m_T**2) * h_T * (Z_CA * FARADAY * 1e-3) * 1000.0
    exp_z = np.exp(np.clip(z, -500, 500))
    exp_minus_z = np.exp(np.clip(-z, -500, 500))
    denom = 1.0 - exp_minus_z
    f_z_normal = z * (Cai - Cao * exp_minus_z) / (denom + 1e-30)
    f_z_small  = (Cai - Cao) + z * (Cai + Cao) / 2.0
    singular = np.abs(z) < 1e-4 if isinstance(z, np.ndarray) else abs(z) < 1e-4
    f_z = np.where(singular, f_z_small, f_z_normal)
    return GHK_prefix * f_z


def _phase_derivatives_grid(segment_id, step, x_var, y_var, X_grid, Y_grid):
    """
    Vectorized computation of derivatives on a phase-plane grid.
    Returns (dX_grid, dY_grid, all_derivs_dict).
    """
    seg = SEGMENT[segment_id]
    seg_list = list(SEGMENT.values())
    seg_id_to_idx = {s.id: i for i, s in enumerate(seg_list)}
    idx = seg_id_to_idx[segment_id]
    t_current = step * DT

    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("Please call set_env before using this function.")
    # Historical state
    V_hist = float(HISTORY_V[step, idx])
    m_hist = float(HISTORY_M[step, idx])
    h_hist = float(HISTORY_H[step, idx])
    n_hist = float(HISTORY_N[step, idx])
    Ca_hist = float(HISTORY_CA[step, idx]) if HISTORY_CA is not None else CA_INF
    mT_hist = float(HISTORY_MT[step, idx]) if HISTORY_MT is not None else inf_mT(V_hist)
    hT_hist = float(HISTORY_HT[step, idx]) if HISTORY_HT is not None else inf_hT(V_hist)

    # Build state: axis variables use grid values, others use historical values
    state = {'V': V_hist, 'm': m_hist, 'h': h_hist, 'n': n_hist,
             'Ca': Ca_hist, 'mT': mT_hist, 'hT': hT_hist}
    state[x_var] = X_grid
    state[y_var] = Y_grid

    V_s = state['V']; m_s = state['m']; h_s = state['h']; n_s = state['n']
    Ca_s = state['Ca']; mT_s = state['mT']; hT_s = state['hT']

    # Ensure all variables can be broadcast
    shape = X_grid.shape
    V_s  = np.broadcast_to(np.asarray(V_s, dtype=float), shape).copy()
    m_s  = np.broadcast_to(np.asarray(m_s, dtype=float), shape).copy()
    h_s  = np.broadcast_to(np.asarray(h_s, dtype=float), shape).copy()
    n_s  = np.broadcast_to(np.asarray(n_s, dtype=float), shape).copy()
    Ca_s = np.broadcast_to(np.asarray(Ca_s, dtype=float), shape).copy()
    mT_s = np.broadcast_to(np.asarray(mT_s, dtype=float), shape).copy()
    hT_s = np.broadcast_to(np.asarray(hT_s, dtype=float), shape).copy()

    # --- HH gating derivatives ---
    tadj_hh = 3.0 ** ((CELSIUS - 36.0) / 10.0)
    am = _alpha_m_vec(V_s); bm = _beta_m_vec(V_s)
    ah = _alpha_h_vec(V_s); bh = _beta_h_vec(V_s)
    an = _alpha_n_vec(V_s); bn = _beta_n_vec(V_s)
    dm = tadj_hh * (am * (1 - m_s) - bm * m_s)
    dh = tadj_hh * (ah * (1 - h_s) - bh * h_s)
    dn = tadj_hh * (an * (1 - n_s) - bn * n_s)

    # --- CaT gating derivatives ---
    dmT = (_inf_mT_vec(V_s) - mT_s) / _tau_mT_vec(V_s)
    dhT = (_inf_hT_vec(V_s) - hT_s) / _tau_hT_vec(V_s)

    # --- Ionic currents ---
    g_Na = (m_s**3) * h_s * seg.get_absolute_g_max("Na")
    g_K  = (n_s**4) * seg.get_absolute_g_max("K")
    g_L  = seg.get_absolute_g_max("L")
    I_ion = g_Na * (V_s - E_TABLE["Na"]["E"]) + g_K * (V_s - E_TABLE["K"]["E"]) + g_L * (V_s - E_TABLE["L"]["E"])

    # T-type Ca current
    P_Ca_abs = seg.get_absolute_P_max("CaT")
    I_T = _evaluate_GHK_vec(V_s, Ca_s, CA_OUT, P_Ca_abs, mT_s, hT_s)
    I_ion = I_ion + I_T

    # Axial currents (neighbor voltages use historical values)
    I_axial = np.zeros(shape)
    V_t = HISTORY_V[step]
    for connected_id in seg.connected_segments:
        target_seg = SEGMENT[connected_id]
        target_idx = seg_id_to_idx[connected_id]
        K_ij = calculate_Kij(seg, target_seg)
        I_axial += K_ij * (V_t[target_idx] - V_s)

    # External stimulation
    I_stim = 0.0
    for stim in STIMULATION:
        _, s_id, stim_uA, stim_start, stim_duration = stim
        if seg.id == s_id and stim_start <= t_current <= stim_start + stim_duration:
            I_stim += stim_uA

    dV = (-I_ion + I_axial + I_stim) / seg.absolute_C

    # Ca decay
    drive_ca = np.maximum(-seg.gamma_Ca * I_T, 0.0) if P_Ca_abs > 0 else 0.0
    dCa = drive_ca + (CA_INF - Ca_s) / TAU_CA

    deriv_map = {'V': dV, 'm': dm, 'h': dh, 'n': dn, 'Ca': dCa, 'mT': dmT, 'hT': dhT}
    return deriv_map[x_var], deriv_map[y_var], deriv_map


def _phase_derivatives_scalar(segment_id, step, x_var, y_var, x_val, y_val):
    """Scalar version — used by fsolve to solve for equilibria."""
    X_arr = np.array([[x_val]]); Y_arr = np.array([[y_val]])
    dX, dY, _ = _phase_derivatives_grid(segment_id, step, x_var, y_var, X_arr, Y_arr)
    return [float(dX[0, 0]), float(dY[0, 0])]


def find_equilibria(segment_id, step, x_var, y_var, Nx=40, Ny=40, tol=1e-6):
    """
    Search for equilibria on the phase plane (intersections where dX=0 and dY=0).
    Returns a list of (x, y) coordinates that satisfy the conditions.
    """
    x_lo, x_hi = _get_var_range(x_var)
    y_lo, y_hi = _get_var_range(y_var)
    x_space = np.linspace(x_lo, x_hi, Nx)
    y_space = np.linspace(y_lo, y_hi, Ny)
    X_grid, Y_grid = np.meshgrid(x_space, y_space)

    dX, dY, _ = _phase_derivatives_grid(segment_id, step, x_var, y_var, X_grid, Y_grid)

    # Search the grid for candidate seeds where dX and dY are near zero or change sign
    candidates = []
    for i in range(Ny - 1):
        for j in range(Nx - 1):
            # Check whether dX changes sign between adjacent grid cells
            sx = (np.sign(dX[i, j]) != np.sign(dX[i, j+1]) or
                  np.sign(dX[i, j]) != np.sign(dX[i+1, j]))
            sy = (np.sign(dY[i, j]) != np.sign(dY[i, j+1]) or
                  np.sign(dY[i, j]) != np.sign(dY[i+1, j]))
            if sx and sy:
                candidates.append((X_grid[i, j], Y_grid[i, j]))

    # Refine candidates using fsolve
    equilibria = []
    seen = set()
    for x0, y0 in candidates:
        try:
            sol, info, ier, _ = fsolve(
                lambda xy: _phase_derivatives_scalar(segment_id, step, x_var, y_var, xy[0], xy[1]),
                [x0, y0], full_output=True)
            if ier == 1:
                sx, sy = float(sol[0]), float(sol[1])
                if x_lo <= sx <= x_hi and y_lo <= sy <= y_hi:
                    key = (round(sx, 4), round(sy, 4))
                    if key not in seen:
                        seen.add(key)
                        equilibria.append((sx, sy))
        except Exception:
            pass

    return equilibria


def classify_equilibrium(eigenvalues):
    """Classify equilibrium stability type from Jacobian eigenvalues (Lyapunov first method)."""
    lam1, lam2 = eigenvalues
    re1, re2 = lam1.real, lam2.real
    im1, im2 = lam1.imag, lam2.imag
    # Use relative tolerance based on eigenvalue magnitudes
    mag = max(abs(lam1), abs(lam2), 1e-12)
    tol = mag * 1e-6

    has_imag = abs(im1) > tol or abs(im2) > tol

    if has_imag:
        if abs(re1) < tol and abs(re2) < tol:
            return "Center", "#00bcd4"
        elif re1 < -tol and re2 < -tol:
            return "Stable Focus", "#66bb6a"
        elif re1 > tol and re2 > tol:
            return "Unstable Focus", "#ef5350"
        else:
            return "Spiral Saddle", "#ffd740"
    else:
        if re1 < -tol and re2 < -tol:
            return "Stable Node", "#66bb6a"
        elif re1 > tol and re2 > tol:
            return "Unstable Node", "#ef5350"
        elif (re1 > tol and re2 < -tol) or (re1 < -tol and re2 > tol):
            return "Saddle Point", "#ffd740"
        elif abs(re1) < tol and abs(re2) < tol:
            return "Non-isolated", "#9e9e9e"
        else:
            return "Non-hyperbolic", "#9e9e9e"


def compute_jacobian_at_equilibrium(segment_id, step, x_var, y_var, x_eq, y_eq):
    """
    Compute the 2×2 Jacobian of the reduced system at equilibrium (x_eq, y_eq) using central differences,
    then compute eigenvalues for stability classification (Lyapunov first method).

    The reduced system includes HH equations, GHK current, and first-order Ca decay; non-axis variables
    are held at their historical values for the given timestep.

    Returns:
        J:              2x2 Jacobian matrix (numpy array)
        eigenvalues:    eigenvalues array (complex)
        classification: stability classification string
        color:          associated display color
    """
    # Adapt central difference step sizes based on variable ranges
    x_lo, x_hi = _get_var_range(x_var)
    y_lo, y_hi = _get_var_range(y_var)
    eps_x = max((x_hi - x_lo) * 1e-6, 1e-12)
    eps_y = max((y_hi - y_lo) * 1e-6, 1e-12)

    # Central difference: J[i,j] = ∂f_i / ∂x_j
    # f = (d(x_var)/dt, d(y_var)/dt),  x = (x_var, y_var)
    fx_p = _phase_derivatives_scalar(segment_id, step, x_var, y_var, x_eq + eps_x, y_eq)
    fx_m = _phase_derivatives_scalar(segment_id, step, x_var, y_var, x_eq - eps_x, y_eq)
    fy_p = _phase_derivatives_scalar(segment_id, step, x_var, y_var, x_eq, y_eq + eps_y)
    fy_m = _phase_derivatives_scalar(segment_id, step, x_var, y_var, x_eq, y_eq - eps_y)

    J = np.array([
        [(fx_p[0] - fx_m[0]) / (2 * eps_x), (fy_p[0] - fy_m[0]) / (2 * eps_y)],
        [(fx_p[1] - fx_m[1]) / (2 * eps_x), (fy_p[1] - fy_m[1]) / (2 * eps_y)]
    ])

    eigenvalues = np.linalg.eigvals(J)
    classification, color = classify_equilibrium(eigenvalues)

    return J, eigenvalues, classification, color


def get_biophysical_info(segment_id, step):
    """
    Get a biophysical summary for the specified segment at the given timestep.
    """
    seg = SEGMENT[segment_id]
    seg_list = list(SEGMENT.values())
    seg_id_to_idx = {s.id: i for i, s in enumerate(seg_list)}
    idx = seg_id_to_idx[segment_id]
    t_ms = step * DT
    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("Please call set_env before using this function.")
    V_val = float(HISTORY_V[step, idx])
    m_val = float(HISTORY_M[step, idx])
    h_val = float(HISTORY_H[step, idx])
    n_val = float(HISTORY_N[step, idx])
    Ca_val = float(HISTORY_CA[step, idx]) if HISTORY_CA is not None else CA_INF
    mT_val = float(HISTORY_MT[step, idx]) if HISTORY_MT is not None else inf_mT(V_val)
    hT_val = float(HISTORY_HT[step, idx]) if HISTORY_HT is not None else inf_hT(V_val)

    g_Na = (m_val**3) * h_val * seg.get_absolute_g_max("Na")
    g_K  = (n_val**4) * seg.get_absolute_g_max("K")
    g_L  = seg.get_absolute_g_max("L")

    P_Ca_abs = seg.get_absolute_P_max("CaT")
    I_T, _ = evaluate_GHK_and_Jacobian(V_val, Ca_val, CA_OUT, P_Ca_abs, mT_val, hT_val)

    return {
        't_ms': t_ms, 'V': V_val, 'm': m_val, 'h': h_val, 'n': n_val,
        'Ca': Ca_val, 'mT': mT_val, 'hT': hT_val,
        'g_Na': g_Na, 'g_K': g_K, 'g_L': g_L,
        'I_T': I_T, 'P_Ca_abs': P_Ca_abs,
        'E_Na': E_TABLE["Na"]["E"], 'E_K': E_TABLE["K"]["E"], 'E_L': E_TABLE["L"]["E"],
    }


def generate_phase_portrait_mesh(segment_id: int, step: int, Nx: int = 20, Ny: int = 20):
    """
    Standalone phase-portrait mesh data provider.
    Depends on in-memory HISTORY_* data; must be called after start_simulation().
    """
    global HISTORY_V
    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("Please call set_env before generating phase portrait mesh.")

    v_space = np.linspace(-80, 40, Nx)
    n_space = np.linspace(0, 1, Ny)
    V_grid, N_grid = np.meshgrid(v_space, n_space)

    dV_grid, dN_grid, _ = _phase_derivatives_grid(segment_id, step, 'V', 'n', V_grid, N_grid)

    return (
        V_grid.flatten().tolist(),
        N_grid.flatten().tolist(),
        dV_grid.flatten().tolist(),
        dN_grid.flatten().tolist()
    )


def show_dynamic_phase_portrait(probe_id, x_var: str = 'V', y_var: str = 'n', Nx: int = 25, Ny: int = 25):
    """
    Display a precomputed phase portrait trajectory using a Tkinter player with a seek bar.
    Includes nullclines, equilibrium points, and a biophysical info panel.

    probe_id: probe ID (int), same as used in insert_probe().
    x_var, y_var: selectable variables among 'V', 'm', 'h', 'n', 'Ca', 'mT', 'hT' (must differ).
    """
    import tkinter as tk
    from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg, NavigationToolbar2Tk

    global PROBE_LIST, SEGMENT, HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N
    global HISTORY_CA, HISTORY_MT, HISTORY_HT, DT, STEPS

    if HISTORY_V is None or HISTORY_M is None or HISTORY_N is None or HISTORY_H is None:
        raise RuntimeError("Please call set_env before starting the simulation.")

    valid_vars = ['V', 'm', 'h', 'n', 'Ca', 'mT', 'hT']
    if x_var not in valid_vars or y_var not in valid_vars:
        raise ValueError(f"Axis variable must be one of {valid_vars}.")
    if x_var == y_var:
        raise ValueError(f"x_var and y_var must be different: '{x_var}'")

    # -- 1. Probe parsing --
    target_probe = None
    for p in PROBE_LIST:
        if p[0] == probe_id:
            target_probe = p
            break
    if target_probe is None:
        raise ValueError(f"Probe parsing failed: '{probe_id}'")

    _, segment_id, start_ms, duration_ms = target_probe
    start_step = max(0, int(start_ms / DT))
    end_step = min(STEPS, int((start_ms + duration_ms) / DT))
    N_frames = end_step - start_step + 1

    # Downsample if frame count is too high
    max_display_frames = 2000
    if N_frames > max_display_frames:
        subsample = max(1, N_frames // max_display_frames)
    else:
        subsample = 1
    frame_steps = list(range(start_step, end_step + 1, subsample))
    N_display = len(frame_steps)

    seg_list = list(SEGMENT.values())
    seg_id_to_idx = {s.id: i for i, s in enumerate(seg_list)}
    idx = seg_id_to_idx[segment_id]

    # -- 2. Historical trajectory --
    history_map = {
        'V': HISTORY_V, 'm': HISTORY_M, 'h': HISTORY_H, 'n': HISTORY_N,
        'Ca': HISTORY_CA, 'mT': HISTORY_MT, 'hT': HISTORY_HT
    }
    traj_x_full = history_map[x_var][start_step:end_step + 1, idx]
    traj_y_full = history_map[y_var][start_step:end_step + 1, idx]

    # -- 3. Grid space --
    x_lo, x_hi = _get_var_range(x_var)
    y_lo, y_hi = _get_var_range(y_var)
    x_space = np.linspace(x_lo, x_hi, Nx)
    y_space = np.linspace(y_lo, y_hi, Ny)
    X_grid, Y_grid = np.meshgrid(x_space, y_space)

    # -- 4. Precompute vector fields for all frames --
    all_dX = np.zeros((N_display, Ny, Nx))
    all_dY = np.zeros((N_display, Ny, Nx))
    for fi, step in enumerate(frame_steps):
        dX, dY, _ = _phase_derivatives_grid(segment_id, step, x_var, y_var, X_grid, Y_grid)
        all_dX[fi] = dX
        all_dY[fi] = dY

    # -- 5. Build Tkinter window --
    root = tk.Tk()
    root.title(f"Phase Portrait [{y_var} vs {x_var}] — Probe #{probe_id}")
    root.configure(bg='#1e1e1e')
    root.geometry("1050x820")

    # Main plotting area
    fig, ax = plt.subplots(figsize=(9, 6.2))
    fig.patch.set_facecolor('#1e1e1e')
    ax.set_facecolor('#252526')
    ax.tick_params(colors='#cccccc')
    for spine in ax.spines.values():
        spine.set_color('#555555')
    ax.set_xlabel(x_var, color='#cccccc', fontsize=12)
    ax.set_ylabel(y_var, color='#cccccc', fontsize=12)
    ax.set_title(f'Phase Portrait [{y_var} vs {x_var}]', color='white', fontsize=13)
    ax.grid(True, linestyle=':', alpha=0.3, color='#555555')
    x_margin = 0.05 * (x_hi - x_lo)
    y_margin = 0.05 * (y_hi - y_lo)
    ax.set_xlim(x_lo - x_margin, x_hi + x_margin)
    ax.set_ylim(y_lo - y_margin, y_hi + y_margin)

    # Initial artists
    # angles='xy': arrows rendered in data coordinates so visual direction matches trajectories
    # scale_units='xy', scale=1: arrow length is in data units, controlled via set_UVC
    Q = ax.quiver(X_grid, Y_grid, np.zeros_like(X_grid), np.zeros_like(Y_grid),
                  color='#888888', alpha=0.6,
                  angles='xy', scale_units='xy', scale=1, width=0.002)
    traj_line, = ax.plot([], [], color='#4fc3f7', linewidth=1.8, alpha=0.85)
    curr_point, = ax.plot([], [], 'o', color='#ff5252', markersize=7, zorder=5)
    nullcline_x_artist = [None]
    nullcline_y_artist = [None]
    eq_scatter = [None]
    eq_text_artists = [[]]

    canvas = FigureCanvasTkAgg(fig, master=root)
    canvas.get_tk_widget().pack(side=tk.TOP, fill=tk.BOTH, expand=True)
    toolbar = NavigationToolbar2Tk(canvas, root)
    toolbar.update()

    # -- 6. Info panel --
    info_frame = tk.Frame(root, bg='#1e1e1e')
    info_frame.pack(side=tk.TOP, fill=tk.X, padx=8, pady=(0, 2))
    info_var = tk.StringVar(value="")
    info_label = tk.Label(info_frame, textvariable=info_var, bg='#1e1e1e', fg='#cccccc',
                          font=('Consolas', 9), justify=tk.LEFT, anchor='w', wraplength=1020)
    info_label.pack(fill=tk.X)

    # -- 7. Control bar --
    ctrl_frame = tk.Frame(root, bg='#2d2d30')
    ctrl_frame.pack(side=tk.BOTTOM, fill=tk.X, padx=4, pady=4)

    is_playing = [False]
    play_speed = [50]  # ms per frame
    current_frame = [0]

    def update_display(fi):
        current_frame[0] = fi
        dX = all_dX[fi]
        dY = all_dY[fi]

        # -- Fix arrow directions and lengths for the vector field --
        # Issue 1: different axis scales (V ~160 mV vs n ~1) bias normalized directions
        # Fix: normalize derivatives by each axis range before computing directions
        # Issue 2: original code normalized all arrows to equal length, losing magnitude info
        # Fix: use the 95th percentile of magnitudes as a reference and scale arrows by relative magnitude
        x_span = x_hi - x_lo
        y_span = y_hi - y_lo
        grid_spacing_x = x_span / max(Nx - 1, 1)
        grid_spacing_y = y_span / max(Ny - 1, 1)

        # Convert derivatives to dimensionless normalized coordinates (remove unit differences)
        dX_n = dX / x_span
        dY_n = dY / y_span
        mag_n = np.sqrt(dX_n**2 + dY_n**2)

        # Relative magnitude: use 95th percentile of valid points as full-scale reference to suppress outliers
        valid = mag_n > 1e-30
        mag_ref = float(np.percentile(mag_n[valid], 95)) if valid.any() else 1.0
        mag_ref = max(mag_ref, 1e-30)
        rel_mag = np.clip(mag_n / mag_ref, 0.05, 1.0)  # Enforce a minimum visible length of 5%

        # Normalize direction unit vectors (in normalized coordinate system)
        mag_safe = np.where(valid, mag_n, 1.0)
        dX_dir = dX_n / mag_safe
        dY_dir = dY_n / mag_safe

        # Convert back to data coordinates: arrow length = rel_mag * grid spacing * scale factor
        # angles='xy' ensures (U, V) angles are rendered correctly in data coordinates
        arrow_factor = 0.45
        U_arrow = dX_dir * rel_mag * grid_spacing_x * arrow_factor
        V_arrow = dY_dir * rel_mag * grid_spacing_y * arrow_factor
        Q.set_UVC(U_arrow, V_arrow)

        # Trajectory (mapped to full-frame indices)
        traj_end = fi * subsample + 1
        traj_line.set_data(traj_x_full[:traj_end], traj_y_full[:traj_end])
        curr_point.set_data([traj_x_full[min(fi * subsample, len(traj_x_full) - 1)]],
                            [traj_y_full[min(fi * subsample, len(traj_y_full) - 1)]])

        # Clear old nullclines
        if nullcline_x_artist[0] is not None:
            for coll in nullcline_x_artist[0].collections:
                coll.remove()
        if nullcline_y_artist[0] is not None:
            for coll in nullcline_y_artist[0].collections:
                coll.remove()
        if eq_scatter[0] is not None:
            eq_scatter[0].remove()
            eq_scatter[0] = None
        for t in eq_text_artists[0]:
            t.remove()
        eq_text_artists[0] = []

        # Draw nullclines (d{x_var}/dt = 0 and d{y_var}/dt = 0)
        try:
            cs_x = ax.contour(X_grid, Y_grid, dX, levels=[0], colors=['#66bb6a'], linewidths=1.5, linestyles='--')
            nullcline_x_artist[0] = cs_x
        except Exception:
            nullcline_x_artist[0] = None
        try:
            cs_y = ax.contour(X_grid, Y_grid, dY, levels=[0], colors=['#ef5350'], linewidths=1.5, linestyles='--')
            nullcline_y_artist[0] = cs_y
        except Exception:
            nullcline_y_artist[0] = None

        # Compute equilibria and their stability (Lyapunov first method)
        step = frame_steps[fi]
        eqs = find_equilibria(segment_id, step, x_var, y_var, Nx=30, Ny=30)
        eq_stability = []
        if eqs:
            for x_eq, y_eq in eqs:
                try:
                    J, eigvals, classif, color = compute_jacobian_at_equilibrium(
                        segment_id, step, x_var, y_var, x_eq, y_eq)
                    eq_stability.append((x_eq, y_eq, J, eigvals, classif, color))
                except Exception:
                    eq_stability.append((x_eq, y_eq, None, None, "Unknown", "#9e9e9e"))
            for x_eq, y_eq, J, eigvals, classif, color in eq_stability:
                sc = ax.scatter([x_eq], [y_eq], marker='*', s=220,
                                c=color, edgecolors='black',
                                linewidths=0.8, zorder=10)
                eq_text_artists[0].append(sc)

        # Info text
        bio = get_biophysical_info(segment_id, step)
        t_ms = bio['t_ms']
        lines = [f"t = {t_ms:.1f} ms  |  Frame {fi+1}/{N_display}"]
        lines.append(f"V={bio['V']:.2f} mV  m={bio['m']:.4f}  h={bio['h']:.4f}  n={bio['n']:.4f}")
        lines.append(f"Ca={bio['Ca']:.6f} mM  mT={bio['mT']:.4f}  hT={bio['hT']:.4f}")
        lines.append(f"g_Na={bio['g_Na']:.4e}  g_K={bio['g_K']:.4e}  g_L={bio['g_L']:.4e}  I_T={bio['I_T']:.4e}")
        eq_info_lines = []
        if eq_stability:
            for i_eq, (x_eq, y_eq, J, eigvals, classif, color) in enumerate(eq_stability):
                if eigvals is not None:
                    lam_str = ", ".join(
                        f"{ev.real:+.4f}{ev.imag:+.4f}j" if abs(ev.imag) > 1e-10
                        else f"{ev.real:+.6f}" for ev in eigvals)
                    eq_info_lines.append(
                        f"Eq#{i_eq} ({x_var}={x_eq:.4f}, {y_var}={y_eq:.4f}): "
                        f"\u03bb=[{lam_str}] \u2192 {classif}")
                else:
                    eq_info_lines.append(
                        f"Eq#{i_eq} ({x_var}={x_eq:.4f}, {y_var}={y_eq:.4f}): Computation failed")
                # Annotate stability type on the plot
                ann_text = f"({x_eq:.3f}, {y_eq:.3f})\n{classif}" if eigvals is not None \
                    else f"({x_eq:.3f}, {y_eq:.3f})"
                eq_text_artists[0].append(
                    ax.annotate(ann_text, (x_eq, y_eq),
                                textcoords="offset points", xytext=(8, -16),
                                fontsize=7, color=color, zorder=11))
        else:
            eq_info_lines.append("No equilibrium found in visible range.")
        info_line1 = "  |  ".join(lines[:2])
        info_line2 = "  |  ".join(lines[2:])
        info_text = info_line1 + "\n" + info_line2
        if eq_info_lines:
            info_text += "\n" + "\n".join(eq_info_lines)
        info_var.set(info_text)

        canvas.draw_idle()

    # Buttons and progress bar
    def on_play_pause():
        is_playing[0] = not is_playing[0]
        btn_play.config(text="⏸" if is_playing[0] else "▶")
        if is_playing[0]:
            play_next()

    def play_next():
        if not is_playing[0]:
            return
        fi = current_frame[0] + 1
        if fi >= N_display:
            is_playing[0] = False
            btn_play.config(text="▶")
            return
        slider.set(fi)
        root.after(play_speed[0], play_next)

    def on_reset():
        is_playing[0] = False
        btn_play.config(text="▶")
        slider.set(0)

    def on_slider(val):
        fi = int(float(val))
        update_display(fi)

    btn_reset = tk.Button(ctrl_frame, text="⏮", command=on_reset, width=3,
                          bg='#3c3c3c', fg='white', relief=tk.FLAT, font=('Arial', 12))
    btn_reset.pack(side=tk.LEFT, padx=2)

    btn_play = tk.Button(ctrl_frame, text="▶", command=on_play_pause, width=3,
                         bg='#3c3c3c', fg='white', relief=tk.FLAT, font=('Arial', 12))
    btn_play.pack(side=tk.LEFT, padx=2)

    # Legend description
    legend_lbl = tk.Label(ctrl_frame, text=f"  ── {x_var}-nullcline (green)  ── {y_var}-nullcline (red)  ★ equilibrium",
                          bg='#2d2d30', fg='#aaaaaa', font=('Arial', 9))
    legend_lbl.pack(side=tk.LEFT, padx=8)

    slider = tk.Scale(ctrl_frame, from_=0, to=max(0, N_display - 1), orient=tk.HORIZONTAL,
                      command=on_slider, bg='#2d2d30', fg='white', troughcolor='#555555',
                      highlightthickness=0, length=450, sliderlength=20)
    slider.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=4)

    frame_lbl_var = tk.StringVar(value=f"0/{N_display}")
    frame_lbl = tk.Label(ctrl_frame, textvariable=frame_lbl_var, bg='#2d2d30', fg='white', font=('Arial', 10))
    frame_lbl.pack(side=tk.LEFT, padx=4)

    # Update frame label
    orig_update = update_display
    def update_display_wrapped(fi):
        orig_update(fi)
        frame_lbl_var.set(f"{fi+1}/{N_display}")
    # patch
    update_display = update_display_wrapped

    # Rebind slider callback
    slider.config(command=lambda val: update_display(int(float(val))))

    # Show first frame
    update_display(0)

    root.protocol("WM_DELETE_WINDOW", root.destroy)
    root.mainloop()
    plt.close(fig)


def plot_variable_over_time(segment_id: int, var_label: str, start_time_ms: float, end_time_ms: float):
    """
    Plot a state variable for a given segment over a specified time interval.
    Must be called after start_simulation() has completed.

    Parameters:
        segment_id:    segment ID (as used in init_segment)
        var_label:     variable label; options: 'V', 'm', 'h', 'n', 'Ca', 'mT', 'hT'
        start_time_ms: start time (ms)
        end_time_ms:   end time (ms)
    """
    global HISTORY_V, HISTORY_M, HISTORY_H, HISTORY_N, DT, STEPS, SEGMENT
    global HISTORY_CA, HISTORY_MT, HISTORY_HT

    if HISTORY_V is None or HISTORY_M is None or HISTORY_H is None or HISTORY_N is None:
        raise RuntimeError("Please call set_env and run the simulation first.")

    valid_labels = ['V', 'm', 'h', 'n', 'Ca', 'mT', 'hT']
    if var_label not in valid_labels:
        raise ValueError(f"Variable label must be one of {valid_labels}, current: '{var_label}'")

    if segment_id not in SEGMENT:
        raise ValueError(f"Segment ID {segment_id} does not exist.")

    seg_list = list(SEGMENT.values())
    seg_id_to_idx = {s.id: i for i, s in enumerate(seg_list)}
    idx = seg_id_to_idx[segment_id]

    start_step = max(0, int(start_time_ms / DT))
    end_step = min(STEPS, int(end_time_ms / DT))

    if start_step >= end_step:
        raise ValueError(f"Invalid time range: start={start_time_ms}ms, end={end_time_ms}ms")

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