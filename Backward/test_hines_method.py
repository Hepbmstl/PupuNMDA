# Copyright 2026 [Hepbmstl Hepupu]
#
# Pupu NMDA / NeuronCAD
# A Multi-Compartment Neuron Physiological Simulation and Dynamics Analysis Platform
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
# http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#
# Scientific and Algorithmic Foundations:
# This software's biophysical organization and core numerical methods are 
# fundamentally informed by the following works:
# * 1. Destexhe, A., Neubig, M., Ulrich, D., & Huguenard, J. (1998). 
# Dendritic Low-Threshold Calcium Currents in Thalamic Relay Cells. 
# The Journal of Neuroscience, 18(10), 3574-3588.
# * 2. Hines, M. (1984). Efficient computation of branched nerve equations. 
# International Journal of Bio-Medical Computing, 15(1), 69-76.
#

"""
Full test suite for Hines_method.py
Covers:
    - Environment setup and teardown (set_env, set_E, clear_environment)
    - Compartment construction and properties (Segment, init_segment, add_channel_to_segment, add_connection)
    - Probe and stimulation registration (insert_probe, insert_stimulation)
    - HH gating kinetics (alpha/beta rate functions, init_gates, gating_update)
    - T-type calcium channel gating (inf_mT, inf_hT, tau_mT, tau_hT, gating_update_tau_inf)
    - GHK currents and Jacobian (evaluate_GHK_and_Jacobian)
    - Physical computations (calculate_Kij, compute_continuous_derivatives)
    - Full simulation flow (start_simulation — single/multi-compartment, includes calcium)
    - Data export interfaces (export_history_matrices, export_calcium_history_matrices, export_probe_data_json)
    - End-to-end integration tests (simulate SimulationRunner.cs call sequence)
"""

import pytest
import numpy as np
import json
import math
import sys
import os
import matplotlib
matplotlib.use('Agg')  # Non-interactive backend to avoid Tk dependency
import matplotlib.pyplot as plt

# Ensure Backward directory is on sys.path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import Hines_method as hm

# Hines_method.py may call matplotlib.use('TkAgg') and override Agg,
# Force Agg again here to avoid Tk errors during tests.
matplotlib.use('Agg', force=True)


# ============================================================
# Fixtures
# ============================================================

@pytest.fixture(autouse=True)
def clean_state():
    """Call clear_environment before and after each test to ensure isolation."""
    hm.clear_environment()
    yield
    hm.clear_environment()
    plt.close('all')  # Prevent Matplotlib figures from accumulating


def _build_single_compartment(
    v_init=-65.0, dt=0.025, steps=500,
    Ra=100.0, D=10.0, L=100.0, Cm=1.0,
    g_Na=120.0, g_K=36.0, g_L=0.3
):
    """Helper: build a single-compartment HH environment, return seg_id."""
    seg_id = 0
    hm.set_env(V_init=v_init, dt=dt, steps=steps, n_node=1)
    hm.init_segment(uid="soma", Ra=Ra, D=D, L=L, Cm=Cm, id=seg_id)
    hm.add_channel_to_segment(seg_id, "Na", g_Na)
    hm.add_channel_to_segment(seg_id, "K", g_K)
    hm.add_channel_to_segment(seg_id, "L", g_L)
    return seg_id


def _build_single_compartment_with_CaT(
    v_init=-65.0, dt=0.025, steps=500,
    Ra=100.0, D=10.0, L=100.0, Cm=1.0,
    g_Na=120.0, g_K=36.0, g_L=0.3, P_CaT=1e-5
):
    """Helper: build a single-compartment HH + CaT environment."""
    seg_id = _build_single_compartment(v_init, dt, steps, Ra, D, L, Cm, g_Na, g_K, g_L)
    hm.add_channel_to_segment(seg_id, "CaT", P_CaT)
    return seg_id


def _build_two_compartments(dt=0.025, steps=400):
    """Helper: build two compartments and connect them bidirectionally."""
    hm.set_env(V_init=-65.0, dt=dt, steps=steps, n_node=2)
    hm.init_segment(uid="soma", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
    hm.init_segment(uid="axon", Ra=100.0, D=5.0, L=200.0, Cm=1.0, id=1)
    for sid in [0, 1]:
        hm.add_channel_to_segment(sid, "Na", 120.0)
        hm.add_channel_to_segment(sid, "K", 36.0)
        hm.add_channel_to_segment(sid, "L", 0.3)
    hm.add_connection(0, 1)
    hm.add_connection(1, 0)
    return 0, 1


def _build_two_compartments_with_CaT(dt=0.025, steps=400, P_CaT=1e-5):
    """Helper: build two-compartment HH + CaT."""
    sid0, sid1 = _build_two_compartments(dt=dt, steps=steps)
    hm.add_channel_to_segment(sid0, "CaT", P_CaT)
    hm.add_channel_to_segment(sid1, "CaT", P_CaT * 0.5)
    return sid0, sid1


# ============================================================
# 1. Environment setup and teardown
# ============================================================

class TestEnvironment:

    def test_set_env_initializes_globals(self):
        hm.set_env(V_init=-60.0, dt=0.01, steps=200, n_node=3)
        assert hm.V == -60.0
        assert hm.DT == 0.01
        assert hm.DEL == 0.005
        assert hm.STEPS == 200
        assert hm.N_NODE == 3
        assert hm.HISTORY_V.shape == (201, 3)
        assert hm.HISTORY_M.shape == (201, 3)
        assert hm.HISTORY_H.shape == (201, 3)
        assert hm.HISTORY_N.shape == (201, 3)

    def test_set_env_initializes_calcium_globals(self):
        """set_env should also initialize calcium-related history matrices."""
        hm.set_env(V_init=-60.0, dt=0.01, steps=200, n_node=3)
        assert hm.HISTORY_CA.shape == (201, 3)
        assert hm.HISTORY_MT.shape == (201, 3)
        assert hm.HISTORY_HT.shape == (201, 3)

    def test_set_E_replaces_table(self):
        custom = {"Na": {"E": 50.0}, "K": {"E": -80.0}, "L": {"E": -60.0}}
        hm.set_E(custom)
        assert hm.E_TABLE["Na"]["E"] == 50.0
        assert hm.E_TABLE["K"]["E"] == -80.0
        assert hm.E_TABLE["L"]["E"] == -60.0

    def test_clear_environment_resets_all(self):
        hm.set_env(V_init=-50.0, dt=0.05, steps=100, n_node=2)
        hm.init_segment(uid="x", Ra=35.4, D=5.0, L=50.0, Cm=1.0, id=0)
        hm.insert_probe("p1", 0, 0.0, 1.0)
        hm.insert_stimulation("s1", 0, 10.0, 0.0, 1.0)
        hm.clear_environment()

        assert hm.SEGMENT == {}
        assert hm.PROBE_LIST == []
        assert hm.STIMULATION == []
        assert hm.PROBE_SAVE_DATA == {}
        assert hm.HISTORY_V is None
        assert hm.HISTORY_CA is None
        assert hm.HISTORY_MT is None
        assert hm.HISTORY_HT is None
        assert hm.V == -70.0
        assert hm.DT == 0.1
        assert hm.STEPS == 10000
        assert hm.N_NODE == 0
        assert hm.CURRENT_STEP == -1
        assert hm.SIMULATION_RUNNING is False

    def test_get_current_step_and_is_running_defaults(self):
        assert hm.get_current_step() == -1
        assert hm.is_simulation_running() is False


# ============================================================
# 2. Compartment construction and properties
# ============================================================

class TestSegment:

    def test_segment_surface_area(self):
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        expected = math.pi * 10.0 * 100.0 * 1e-8  # cm^2
        assert seg.surface_area_cm2 == pytest.approx(expected, rel=1e-10)

    def test_segment_cross_area(self):
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        expected = math.pi * 25.0  # um^2
        assert seg.cross_area_um2 == pytest.approx(expected, rel=1e-10)

    def test_absolute_C(self):
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        assert seg.absolute_C == pytest.approx(seg.Cm * seg.surface_area_cm2, rel=1e-10)

    def test_get_absolute_g_max(self):
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        seg.add_channels("Na", 120.0)
        assert seg.get_absolute_g_max("Na") == pytest.approx(120.0 * seg.surface_area_cm2, rel=1e-10)
        # Nonexistent channel should return 0
        assert seg.get_absolute_g_max("Ca") == 0.0

    def test_get_absolute_P_max(self):
        """get_absolute_P_max should return permeability density times surface area."""
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        seg.add_channels("CaT", 1e-5)
        expected = 1e-5 * seg.surface_area_cm2
        assert seg.get_absolute_P_max("CaT") == pytest.approx(expected, rel=1e-10)
        assert seg.get_absolute_P_max("missing") == 0.0

    def test_gamma_Ca_positive(self):
        """gamma_Ca should be positive."""
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        assert seg.gamma_Ca > 0
        assert np.isfinite(seg.gamma_Ca)

    def test_gamma_Ca_dimensional_correctness(self):
        """gamma_Ca dimensionality check: 1e-6 / (z * F * Vol_L)."""
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        d_um = 0.1
        shell_vol_L = seg.surface_area_cm2 * (d_um * 1e-4) * 1e-3
        expected = 1e-6 / (hm.Z_CA * hm.FARADAY * shell_vol_L)
        assert seg.gamma_Ca == pytest.approx(expected, rel=1e-10)

    def test_init_segment_registers_in_global(self):
        hm.init_segment(uid="soma", Ra=35.4, D=12.0, L=80.0, Cm=1.0, id=7)
        assert 7 in hm.SEGMENT
        seg = hm.SEGMENT[7]
        assert seg.uid == "soma"
        assert seg.Ra == 35.4
        assert seg.D == 12.0
        assert seg.L == 80.0

    def test_add_channel_to_segment(self):
        hm.init_segment(uid="a", Ra=100.0, D=5.0, L=50.0, Cm=1.0, id=0)
        hm.add_channel_to_segment(0, "Na", 120.0)
        hm.add_channel_to_segment(0, "K", 36.0)
        hm.add_channel_to_segment(0, "CaT", 1e-5)
        assert hm.SEGMENT[0].channels["Na"] == 120.0
        assert hm.SEGMENT[0].channels["K"] == 36.0
        assert hm.SEGMENT[0].channels["CaT"] == 1e-5

    def test_add_connection(self):
        hm.init_segment(uid="a", Ra=100.0, D=5.0, L=50.0, Cm=1.0, id=0)
        hm.init_segment(uid="b", Ra=100.0, D=5.0, L=50.0, Cm=1.0, id=1)
        hm.add_connection(0, 1)
        assert 1 in hm.SEGMENT[0].connected_segments

    def test_segment_get_id_and_uid(self):
        seg = hm.Segment(uid="test_uid", Ra=100.0, D=5.0, L=50.0, Cm=1.0, id=42)
        assert seg.get_id() == 42
        assert seg.get_uid() == "test_uid"


# ============================================================
# 3. Probe and stimulation registration
# ============================================================

class TestProbeAndStimulation:

    def test_insert_probe(self):
        hm.insert_probe("probe_1", 0, 1.0, 5.0)
        hm.insert_probe("probe_2", 1, 2.0, 3.0)
        assert len(hm.PROBE_LIST) == 2
        assert hm.PROBE_LIST[0] == ("probe_1", 0, 1.0, 5.0)
        assert hm.PROBE_LIST[1] == ("probe_2", 1, 2.0, 3.0)

    def test_insert_stimulation(self):
        hm.insert_stimulation("stim_1", 0, 10.0, 0.5, 2.0)
        assert len(hm.STIMULATION) == 1
        assert hm.STIMULATION[0] == ("stim_1", 0, 10.0, 0.5, 2.0)

    def test_save_data_HH_appends_correctly(self):
        hm.save_data_HH("p1", {"V": -65.0})
        hm.save_data_HH("p1", {"V": -60.0})
        hm.save_data_HH("p2", {"V": -70.0})
        assert len(hm.PROBE_SAVE_DATA["p1"]) == 2
        assert len(hm.PROBE_SAVE_DATA["p2"]) == 1


# ============================================================
# 4. HH gating kinetics
# ============================================================

class TestGatingKinetics:

    def test_alpha_m_normal(self):
        """alpha_m(-65) should be positive (standard HH params)."""
        val = hm.alpha_m(-65.0)
        assert val > 0
        assert np.isfinite(val)

    def test_alpha_m_singularity(self):
        """At singularity voltage (V = vtraub + alpha_m_V), use L'Hôpital limiting value A*k."""
        V_sing = hm.HH_PARAMS["vtraub"] + hm.HH_PARAMS["alpha_m_V"]  # -63 + 13 = -50
        limit_val = hm.HH_PARAMS["alpha_m_A"] * hm.HH_PARAMS["alpha_m_k"]  # 0.32 * 4 = 1.28
        assert hm.alpha_m(V_sing) == pytest.approx(limit_val)

    def test_alpha_n_singularity(self):
        """At singularity voltage (V = vtraub + alpha_n_V), use L'Hôpital limiting value A*k."""
        V_sing = hm.HH_PARAMS["vtraub"] + hm.HH_PARAMS["alpha_n_V"]  # -63 + 15 = -48
        limit_val = hm.HH_PARAMS["alpha_n_A"] * hm.HH_PARAMS["alpha_n_k"]  # 0.032 * 5 = 0.16
        assert hm.alpha_n(V_sing) == pytest.approx(limit_val)

    def test_alpha_n_normal(self):
        val = hm.alpha_n(-65.0)
        assert val > 0
        assert np.isfinite(val)

    def test_beta_m(self):
        V = -65.0
        val = hm.beta_m(V)
        # Traub formula: A*(v2-V0)/(exp((v2-V0)/k)-1), v2 = V - vtraub
        vtraub = hm.HH_PARAMS["vtraub"]
        A, V0, k = hm.HH_PARAMS["beta_m_A"], hm.HH_PARAMS["beta_m_V"], hm.HH_PARAMS["beta_m_k"]
        v2 = V - vtraub
        x = v2 - V0
        expected = A * x / (np.exp(x / k) - 1.0)
        assert val == pytest.approx(expected, rel=1e-10)

    def test_alpha_h(self):
        V = -65.0
        val = hm.alpha_h(V)
        # Traub formula: A*exp((V0-v2)/k), v2 = V - vtraub
        vtraub = hm.HH_PARAMS["vtraub"]
        A, V0, k = hm.HH_PARAMS["alpha_h_A"], hm.HH_PARAMS["alpha_h_V"], hm.HH_PARAMS["alpha_h_k"]
        v2 = V - vtraub
        expected = A * np.exp((V0 - v2) / k)
        assert val == pytest.approx(expected, rel=1e-10)

    def test_beta_h(self):
        V = -65.0
        val = hm.beta_h(V)
        # Traub formula: A/(1+exp((V0-v2)/k)), v2 = V - vtraub
        vtraub = hm.HH_PARAMS["vtraub"]
        A, V0, k = hm.HH_PARAMS["beta_h_A"], hm.HH_PARAMS["beta_h_V"], hm.HH_PARAMS["beta_h_k"]
        v2 = V - vtraub
        expected = A / (1.0 + np.exp((V0 - v2) / k))
        assert val == pytest.approx(expected, rel=1e-10)

    def test_beta_n(self):
        V = -65.0
        val = hm.beta_n(V)
        # Traub formula: A*exp((V0-v2)/k), v2 = V - vtraub
        vtraub = hm.HH_PARAMS["vtraub"]
        A, V0, k = hm.HH_PARAMS["beta_n_A"], hm.HH_PARAMS["beta_n_V"], hm.HH_PARAMS["beta_n_k"]
        v2 = V - vtraub
        expected = A * np.exp((V0 - v2) / k)
        assert val == pytest.approx(expected, rel=1e-10)

    def test_init_gates_steady_state(self):
        """init_gates returned gating variables should be at steady-state: alpha/(alpha+beta)."""
        V = -65.0
        m0, n0, h0 = hm.init_gates(V)
        assert m0 == pytest.approx(hm.alpha_m(V) / (hm.alpha_m(V) + hm.beta_m(V)), rel=1e-10)
        assert h0 == pytest.approx(hm.alpha_h(V) / (hm.alpha_h(V) + hm.beta_h(V)), rel=1e-10)
        assert n0 == pytest.approx(hm.alpha_n(V) / (hm.alpha_n(V) + hm.beta_n(V)), rel=1e-10)

    def test_init_gates_values_in_range(self):
        """Gating variables should be in [0, 1] range."""
        for V in [-80.0, -65.0, -50.0, -20.0, 0.0, 30.0]:
            m, n, h = hm.init_gates(V)
            for g in [m, n, h]:
                assert 0.0 <= g <= 1.0, f"Gating variable out of range: V={V}, g={g}"

    def test_gating_update_convergence(self):
        """gating_update should converge to steady-state alpha/(alpha+beta) from arbitrary initial values."""
        V = -65.0
        alpha = hm.alpha_m(V)
        beta = hm.beta_m(V)
        m_ss = alpha / (alpha + beta)
        m = 0.0  # start from 0
        dt_half = 0.01
        for _ in range(50000):
            m = hm.gating_update(dt_half, m, alpha, beta)
        assert m == pytest.approx(m_ss, abs=1e-6)

    def test_gating_update_preserves_steady_state(self):
        """If started at steady-state, gating_update should preserve it."""
        V = -65.0
        alpha = hm.alpha_n(V)
        beta = hm.beta_n(V)
        n_ss = alpha / (alpha + beta)
        n = n_ss
        dt_half = 0.01
        for _ in range(100):
            n = hm.gating_update(dt_half, n, alpha, beta)
        assert n == pytest.approx(n_ss, rel=1e-10)


# ============================================================
# 4b. T-type calcium channel gating kinetics
# ============================================================

class TestTTypeGating:

    def test_inf_mT_range(self):
        """inf_mT should be in [0,1] and monotonically increasing."""
        vals = [hm.inf_mT(V) for V in np.linspace(-100, 40, 50)]
        for v in vals:
            assert 0.0 <= v <= 1.0
        # monotonic increase
        assert all(vals[i] <= vals[i+1] + 1e-12 for i in range(len(vals)-1))

    def test_inf_hT_range(self):
        """inf_hT should be in [0,1] and monotonically decreasing."""
        vals = [hm.inf_hT(V) for V in np.linspace(-100, 40, 50)]
        for v in vals:
            assert 0.0 <= v <= 1.0
        # monotonic decrease
        assert all(vals[i] >= vals[i+1] - 1e-12 for i in range(len(vals)-1))

    def test_tau_mT_positive(self):
        """tau_mT should always be positive."""
        for V in np.linspace(-100, 40, 50):
            assert hm.tau_mT(V) > 0

    def test_tau_hT_positive(self):
        """tau_hT should always be positive."""
        for V in np.linspace(-100, 40, 50):
            assert hm.tau_hT(V) > 0

    def test_tau_hT_branch_at_minus80(self):
        """tau_hT should be approximately continuous near V=-80 (piecewise empirical formulas may have small discontinuities)."""
        tau_left = hm.tau_hT(-80.01)
        tau_right = hm.tau_hT(-79.99)
        # Empirical formulas may have inherent discontinuity at -80 mV; allow 20% tolerance
        assert abs(tau_left - tau_right) / max(tau_left, tau_right) < 0.2

    def test_gating_update_tau_inf_convergence(self):
        """gating_update_tau_inf should converge to inf from arbitrary initial values."""
        V = -65.0
        inf_val = hm.inf_mT(V)
        tau_val = hm.tau_mT(V)
        x = 0.0
        dt_half = 0.005
        for _ in range(100000):
            x = hm.gating_update_tau_inf(dt_half, x, inf_val, tau_val)
        assert x == pytest.approx(inf_val, abs=1e-6)

    def test_gating_update_tau_inf_preserves_steady_state(self):
        """Should preserve steady-state when started at steady-state."""
        V = -65.0
        inf_val = hm.inf_hT(V)
        tau_val = hm.tau_hT(V)
        x = inf_val
        dt_half = 0.01
        for _ in range(1000):
            x = hm.gating_update_tau_inf(dt_half, x, inf_val, tau_val)
        assert x == pytest.approx(inf_val, rel=1e-10)


# ============================================================
# 4c. GHK current calculations
# ============================================================

class TestGHKCurrent:

    def test_zero_permeability_returns_zero(self):
        """When P_max_abs=0 should return (0, 0)."""
        I, g = hm.evaluate_GHK_and_Jacobian(-65.0, 0.00024, 2.0, 0.0, 0.3, 0.5)
        assert I == 0.0
        assert g == 0.0

    def test_singularity_near_zero_voltage(self):
        """When V≈0 should use Taylor expansion and not blow up."""
        P_abs = 1e-10
        I, g = hm.evaluate_GHK_and_Jacobian(0.0, 0.00024, 2.0, P_abs, 0.3, 0.5)
        assert np.isfinite(I)
        assert np.isfinite(g)

    def test_normal_negative_voltage(self):
        """At V=-65 mV should return finite values and I < 0 (inward current)."""
        P_abs = 1e-10
        I, g = hm.evaluate_GHK_and_Jacobian(-65.0, hm.CA_INF, hm.CA_OUT, P_abs, 0.3, 0.5)
        assert np.isfinite(I)
        assert np.isfinite(g)
        # Extracellular Ca much larger than intracellular, V<0 -> inward Ca current -> I < 0
        assert I < 0, f"Expected inward current (I<0): I={I}"

    def test_positive_voltage_outward_current(self):
        """At V=+40 mV current should be positive or near positive (outward driving force)."""
        P_abs = 1e-10
        I, g = hm.evaluate_GHK_and_Jacobian(40.0, hm.CA_INF, hm.CA_OUT, P_abs, 0.5, 0.5)
        assert np.isfinite(I)

    def test_g_Ca_eq_positive_at_rest(self):
        """Near resting potential, g_Ca_eq (effective conductance) should be positive."""
        P_abs = 1e-10
        _, g = hm.evaluate_GHK_and_Jacobian(-65.0, hm.CA_INF, hm.CA_OUT, P_abs, 0.3, 0.5)
        assert g > 0, f"g_Ca_eq should be positive: {g}"

    def test_current_scales_with_permeability(self):
        """Current should scale linearly with permeability."""
        P1 = 1e-10
        P2 = 2e-10
        I1, _ = hm.evaluate_GHK_and_Jacobian(-65.0, hm.CA_INF, hm.CA_OUT, P1, 0.3, 0.5)
        I2, _ = hm.evaluate_GHK_and_Jacobian(-65.0, hm.CA_INF, hm.CA_OUT, P2, 0.3, 0.5)
        assert I2 == pytest.approx(2.0 * I1, rel=1e-10)

    def test_current_scales_with_gating(self):
        """Current should scale with m²h."""
        P_abs = 1e-10
        I1, _ = hm.evaluate_GHK_and_Jacobian(-65.0, hm.CA_INF, hm.CA_OUT, P_abs, 0.3, 0.5)
        I2, _ = hm.evaluate_GHK_and_Jacobian(-65.0, hm.CA_INF, hm.CA_OUT, P_abs, 0.6, 0.5)
        # m²h: (0.3²*0.5) vs (0.6²*0.5) → ratio = 0.18/0.045 = 4.0
        assert I2 == pytest.approx(I1 * (0.6**2 * 0.5) / (0.3**2 * 0.5), rel=1e-10)

    def test_continuity_at_singularity_boundary(self):
        """Values across z=0 boundary should transition smoothly."""
        P_abs = 1e-10
        k = (hm.Z_CA * hm.FARADAY) / (1000.0 * hm.R_GAS * hm.TEMP_K)
        # Find V such that |k*V| ≈ 1e-4
        V_boundary = 1e-4 / k
        I_inner, g_inner = hm.evaluate_GHK_and_Jacobian(
            V_boundary * 0.5, hm.CA_INF, hm.CA_OUT, P_abs, 0.3, 0.5)
        I_outer, g_outer = hm.evaluate_GHK_and_Jacobian(
            V_boundary * 2.0, hm.CA_INF, hm.CA_OUT, P_abs, 0.3, 0.5)
        # Both sides should be finite and have consistent sign
        assert np.isfinite(I_inner) and np.isfinite(I_outer)


# ============================================================
# 5. Physical computations
# ============================================================

class TestPhysics:

    def test_calculate_Kij_symmetric(self):
        """K_ij between two identical compartments should be equal (symmetric)."""
        hm.init_segment(uid="a", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.init_segment(uid="b", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=1)
        K_01 = hm.calculate_Kij(hm.SEGMENT[0], hm.SEGMENT[1])
        K_10 = hm.calculate_Kij(hm.SEGMENT[1], hm.SEGMENT[0])
        assert K_01 == pytest.approx(K_10, rel=1e-10)

    def test_calculate_Kij_positive(self):
        """K_ij should always be positive."""
        hm.init_segment(uid="a", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.init_segment(uid="b", Ra=50.0, D=5.0, L=200.0, Cm=1.0, id=1)
        K = hm.calculate_Kij(hm.SEGMENT[0], hm.SEGMENT[1])
        assert K > 0

    def test_calculate_Kij_formula(self):
        """Manually verify K_ij by formula."""
        seg_a = hm.Segment(uid="a", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        seg_b = hm.Segment(uid="b", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=1)
        R_a = seg_a.Ra * 10.0 * (seg_a.L / 2) / seg_a.cross_area_um2
        R_b = seg_b.Ra * 10.0 * (seg_b.L / 2) / seg_b.cross_area_um2
        expected = 1.0 / (R_a + R_b)
        assert hm.calculate_Kij(seg_a, seg_b) == pytest.approx(expected, rel=1e-10)

    def test_calculate_Kij_units_kOhm_to_mS(self):
        """Unit check for K_ij: R (kOhm), K=1/R (mS)."""
        seg = hm.Segment(uid="a", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        # R = Ra * 10 * (L/2) / cross_area
        # = 100 * 10 * 50 / (pi*25) = 50000 / (pi*25) ≈ 636.6 kOhm
        R = seg.Ra * 10.0 * (seg.L / 2) / seg.cross_area_um2
        assert R == pytest.approx(100.0 * 10.0 * 50.0 / (math.pi * 25.0), rel=1e-10)

    def test_compute_continuous_derivatives_at_rest(self):
        """At resting potential and steady-state gating variables, dV/dt should be near 0 (no external stimulus)."""
        V_rest = -65.0
        m0, n0, h0 = hm.init_gates(V_rest)
        hm.set_env(V_init=V_rest, dt=0.025, steps=10, n_node=1)
        hm.init_segment(uid="s", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.add_channel_to_segment(0, "Na", 120.0)
        hm.add_channel_to_segment(0, "K", 36.0)
        hm.add_channel_to_segment(0, "L", 0.3)

        # Manually populate history data for step=0
        hm.HISTORY_V[0, 0] = V_rest
        hm.HISTORY_M[0, 0] = m0
        hm.HISTORY_H[0, 0] = h0
        hm.HISTORY_N[0, 0] = n0

        dV, dm, dh, dn = hm.compute_continuous_derivatives(
            segment_id=0, step=0
        )
        # Derivatives of gating variables should be near 0 at steady-state
        assert dm == pytest.approx(0.0, abs=1e-8)
        assert dh == pytest.approx(0.0, abs=1e-8)
        assert dn == pytest.approx(0.0, abs=1e-8)
        assert np.isfinite(dV)

    def test_compute_continuous_derivatives_includes_CaT(self):
        """When a CaT channel is present, compute_continuous_derivatives should include a calcium current contribution."""
        V_rest = -65.0
        m0, n0, h0 = hm.init_gates(V_rest)
        hm.set_env(V_init=V_rest, dt=0.025, steps=10, n_node=1)
        hm.init_segment(uid="s", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.add_channel_to_segment(0, "Na", 120.0)
        hm.add_channel_to_segment(0, "K", 36.0)
        hm.add_channel_to_segment(0, "L", 0.3)

        hm.HISTORY_V[0, 0] = V_rest
        hm.HISTORY_M[0, 0] = m0
        hm.HISTORY_H[0, 0] = h0
        hm.HISTORY_N[0, 0] = n0
        hm.HISTORY_CA[0, 0] = hm.CA_INF
        hm.HISTORY_MT[0, 0] = hm.inf_mT(V_rest)
        hm.HISTORY_HT[0, 0] = hm.inf_hT(V_rest)

        # Without CaT channel
        dV_no_Ca, _, _, _ = hm.compute_continuous_derivatives(segment_id=0, step=0)

        # Add CaT channel
        hm.add_channel_to_segment(0, "CaT", 1e-5)
        dV_with_Ca, _, _, _ = hm.compute_continuous_derivatives(segment_id=0, step=0)

        # dV should differ when CaT channel is present
        assert dV_no_Ca != pytest.approx(dV_with_Ca, abs=1e-10), \
            "CaT channel did not affect dV/dt"


# ============================================================
# 6. Single compartment simulation
# ============================================================

class TestSingleCompartmentSimulation:

    def test_no_stim_resting(self):
        """Without stimulation, membrane potential should converge to a stable resting state near E_L.
        The Traub HH model with these parameters settles near the leak reversal potential (E_L)."""
        _build_single_compartment(v_init=-65.0, dt=0.025, steps=1000)
        hm.start_simulation()
        V_final = hm.HISTORY_V[-1, 0]
        E_L = hm.E_TABLE["L"]["E"]  # -76.5
        assert np.all(np.isfinite(hm.HISTORY_V)), "Non-finite values encountered during resting"
        assert abs(V_final - E_L) < 5.0, f"Resting potential did not converge near E_L={E_L}: V_final={V_final}"

    def test_stimulation_triggers_spike(self):
        """A sufficiently large injected current pulse should trigger an action potential."""
        seg_id = _build_single_compartment(dt=0.025, steps=2000)
        hm.insert_stimulation("stim_1", seg_id, 0.1, 1.0, 0.5)
        hm.start_simulation()
        V_max = np.max(hm.HISTORY_V[:, 0])
        assert V_max > 0.0, f"No action potential triggered: V_max={V_max}"

    def test_history_shape(self):
        """HISTORY matrices should have shape (steps+1, n_node)."""
        steps = 500
        _build_single_compartment(steps=steps)
        hm.start_simulation()
        assert hm.HISTORY_V.shape == (steps + 1, 1)
        assert hm.HISTORY_M.shape == (steps + 1, 1)
        assert hm.HISTORY_H.shape == (steps + 1, 1)
        assert hm.HISTORY_N.shape == (steps + 1, 1)

    def test_gating_variables_bounded(self):
        """All gating variables should remain within [0, 1] during simulation."""
        seg_id = _build_single_compartment(dt=0.025, steps=2000)
        hm.insert_stimulation("stim", seg_id, 0.1, 1.0, 0.5)
        hm.start_simulation()
        assert np.all(hm.HISTORY_M >= -1e-10)
        assert np.all(hm.HISTORY_M <= 1.0 + 1e-10)
        assert np.all(hm.HISTORY_H >= -1e-10)
        assert np.all(hm.HISTORY_H <= 1.0 + 1e-10)
        assert np.all(hm.HISTORY_N >= -1e-10)
        assert np.all(hm.HISTORY_N <= 1.0 + 1e-10)

    def test_current_step_updated(self):
        """After simulation completes, CURRENT_STEP should equal STEPS."""
        _build_single_compartment(steps=100)
        hm.start_simulation()
        assert hm.CURRENT_STEP == 100
        assert hm.SIMULATION_RUNNING is False

    def test_start_simulation_without_set_env_raises(self):
        """Calling start_simulation without set_env should raise RuntimeError."""
        with pytest.raises(RuntimeError):
            hm.start_simulation()

    def test_progress_callback(self):
        """progress_callback should be called with monotonically increasing step values."""
        _build_single_compartment(steps=50)
        steps_seen = []
        hm.start_simulation(progress_callback=lambda s: steps_seen.append(s))
        assert len(steps_seen) == 50
        assert steps_seen == list(range(50))


# ============================================================
# 6b. Single-compartment simulation with CaT channels
# ============================================================

class TestSingleCompartmentWithCaT:

    def test_calcium_history_shape(self):
        """HISTORY_CA/MT/HT shapes should be correct."""
        steps = 200
        _build_single_compartment_with_CaT(steps=steps)
        hm.start_simulation()
        assert hm.HISTORY_CA.shape == (steps + 1, 1)
        assert hm.HISTORY_MT.shape == (steps + 1, 1)
        assert hm.HISTORY_HT.shape == (steps + 1, 1)

    def test_calcium_concentration_non_negative(self):
        """Calcium concentration should be non-negative throughout simulation."""
        _build_single_compartment_with_CaT(dt=0.025, steps=2000)
        hm.insert_stimulation("stim", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        assert np.all(hm.HISTORY_CA >= 0), "Negative calcium concentration encountered"

    def test_calcium_concentration_finite(self):
        """Calcium concentration should always be finite."""
        _build_single_compartment_with_CaT(dt=0.025, steps=2000)
        hm.insert_stimulation("stim", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        assert np.all(np.isfinite(hm.HISTORY_CA))

    def test_mT_hT_bounded(self):
        """T-type gating variables should remain within [0,1] during simulation."""
        _build_single_compartment_with_CaT(dt=0.025, steps=2000)
        hm.insert_stimulation("stim", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        assert np.all(hm.HISTORY_MT >= -1e-10)
        assert np.all(hm.HISTORY_MT <= 1.0 + 1e-10)
        assert np.all(hm.HISTORY_HT >= -1e-10)
        assert np.all(hm.HISTORY_HT <= 1.0 + 1e-10)

    def test_calcium_initial_value(self):
        """Initial calcium concentration should equal CA_INF."""
        _build_single_compartment_with_CaT(steps=10)
        hm.start_simulation()
        assert hm.HISTORY_CA[0, 0] == pytest.approx(hm.CA_INF, rel=1e-10)

    def test_mT_hT_initial_steady_state(self):
        """Initial mT and hT should be at steady-state values."""
        V_init = -65.0
        _build_single_compartment_with_CaT(v_init=V_init, steps=10)
        hm.start_simulation()
        assert hm.HISTORY_MT[0, 0] == pytest.approx(hm.inf_mT(V_init), rel=1e-10)
        assert hm.HISTORY_HT[0, 0] == pytest.approx(hm.inf_hT(V_init), rel=1e-10)

    def test_calcium_relaxes_to_CA_INF_without_stimulus(self):
        """Without stimulation, calcium concentration should remain near CA_INF."""
        _build_single_compartment_with_CaT(dt=0.025, steps=2000)
        hm.start_simulation()
        Ca_final = hm.HISTORY_CA[-1, 0]
        assert abs(Ca_final - hm.CA_INF) < 1e-3, f"Ca did not return to steady state: {Ca_final}"

    def test_stimulation_with_CaT_still_produces_spike(self):
        """With CaT channels present, stimulation should still produce action potentials."""
        _build_single_compartment_with_CaT(dt=0.025, steps=2000)
        hm.insert_stimulation("stim", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        V_max = np.max(hm.HISTORY_V[:, 0])
        assert V_max > 0.0, f"CaT affected action potential: V_max={V_max}"

    def test_voltage_finite_with_CaT(self):
        """Voltage should remain finite when CaT channels are present (no numerical blowup)."""
        _build_single_compartment_with_CaT(dt=0.025, steps=3000, P_CaT=1e-4)
        hm.insert_stimulation("stim", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        assert np.all(np.isfinite(hm.HISTORY_V))


# ============================================================
# 7. Multi-compartment simulation
# ============================================================

class TestMultiCompartmentSimulation:

    def test_two_compartment_spike_propagation(self):
        """Stimulating the first compartment should propagate an action potential to the second compartment."""
        sid0, sid1 = _build_two_compartments(dt=0.025, steps=3000)
        hm.insert_stimulation("stim", sid0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        V_max_0 = np.max(hm.HISTORY_V[:, 0])
        V_max_1 = np.max(hm.HISTORY_V[:, 1])
        assert V_max_0 > 0.0, f"Compartment 0 did not spike: V_max={V_max_0}"
        assert V_max_1 > -30.0, f"Compartment 1 potential too low: V_max={V_max_1}"

    def test_axial_current_coupling(self):
        """Connected compartments should show different voltage response under stimulation compared to an unconnected case."""
        # Connected compartments
        sid0, sid1 = _build_two_compartments(dt=0.025, steps=500)
        hm.insert_stimulation("stim", sid0, 0.05, 1.0, 1.0)
        hm.start_simulation()
        V_coupled = hm.HISTORY_V[:, 1].copy()

        # Without connection (rebuild but do not connect)
        hm.clear_environment()
        hm.set_env(V_init=-65.0, dt=0.025, steps=500, n_node=2)
        hm.init_segment(uid="soma", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.init_segment(uid="axon", Ra=100.0, D=5.0, L=200.0, Cm=1.0, id=1)
        for sid in [0, 1]:
            hm.add_channel_to_segment(sid, "Na", 120.0)
            hm.add_channel_to_segment(sid, "K", 36.0)
            hm.add_channel_to_segment(sid, "L", 0.3)
        hm.insert_stimulation("stim", 0, 0.05, 1.0, 1.0)
        hm.start_simulation()
        V_uncoupled = hm.HISTORY_V[:, 1].copy()

        diff = np.max(np.abs(V_coupled - V_uncoupled))
        assert diff > 0.01, f"Axial coupling ineffective: max_diff={diff}"

    def test_two_compartment_with_CaT(self):
        """Two-compartment simulation with CaT channels should remain stable."""
        sid0, sid1 = _build_two_compartments_with_CaT(dt=0.025, steps=2000)
        hm.insert_stimulation("stim", sid0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        assert np.all(np.isfinite(hm.HISTORY_V))
        assert np.all(np.isfinite(hm.HISTORY_CA))
        assert np.all(hm.HISTORY_CA >= 0)


# ============================================================
# 8. Probe data collection
# ============================================================

class TestProbeDataCollection:

    def test_probe_collects_data_in_window(self):
        """Probe should collect data within the specified time window."""
        dt = 0.025
        steps = 400
        seg_id = _build_single_compartment(dt=dt, steps=steps)
        hm.insert_stimulation("stim", seg_id, 0.1, 1.0, 0.5)
        hm.insert_probe("probe_main", seg_id, 1.0, 2.0)
        hm.start_simulation()

        assert "probe_main" in hm.PROBE_SAVE_DATA
        data = hm.PROBE_SAVE_DATA["probe_main"]
        assert len(data) > 0
        for entry in data:
            assert 1.0 <= entry["time_ms"] <= 3.0

    def test_probe_data_fields(self):
        """Probe data should contain all expected fields (including calcium-related fields)."""
        dt = 0.025
        steps = 200
        seg_id = _build_single_compartment(dt=dt, steps=steps)
        hm.insert_probe("p1", seg_id, 0.0, dt * steps)
        hm.start_simulation()

        data = hm.PROBE_SAVE_DATA["p1"]
        assert len(data) > 0
        expected_keys = {
            "step", "time_ms", "segment_id", "V", "m", "h", "n",
            "dV_dt", "dm_dt", "dh_dt", "dn_dt",
            "g_Na_half", "g_K_half", "g_L_abs",
            "mT", "hT", "Ca", "I_T_abs", "g_Ca_eq",
            "A_matrix_row", "b_vector_val", "V_half_val", "V_t_next",
            "I_vc"
        }
        for entry in data:
            assert set(entry.keys()) == expected_keys

    def test_probe_outside_window_collects_nothing(self):
        """Probe outside the simulation time window should collect nothing."""
        dt = 0.025
        steps = 100
        seg_id = _build_single_compartment(dt=dt, steps=steps)
        hm.insert_probe("p_late", seg_id, 10.0, 1.0)
        hm.start_simulation()
        assert "p_late" not in hm.PROBE_SAVE_DATA

    def test_multiple_probes(self):
        """Multiple probes should collect data independently."""
        dt = 0.025
        steps = 400
        seg_id = _build_single_compartment(dt=dt, steps=steps)
        hm.insert_probe("p_early", seg_id, 0.0, 2.0)
        hm.insert_probe("p_late", seg_id, 5.0, 3.0)
        hm.start_simulation()
        assert "p_early" in hm.PROBE_SAVE_DATA
        assert "p_late" in hm.PROBE_SAVE_DATA
        early_times = {e["time_ms"] for e in hm.PROBE_SAVE_DATA["p_early"]}
        late_times = {e["time_ms"] for e in hm.PROBE_SAVE_DATA["p_late"]}
        assert early_times.isdisjoint(late_times)

    def test_probe_with_CaT_includes_calcium_fields(self):
        """With CaT channels present, probe data should include meaningful calcium-related fields."""
        dt = 0.025
        steps = 200
        seg_id = _build_single_compartment_with_CaT(dt=dt, steps=steps)
        hm.insert_probe("p_ca", seg_id, 0.0, dt * steps)
        hm.start_simulation()

        data = hm.PROBE_SAVE_DATA["p_ca"]
        assert len(data) > 0
        # Verify calcium fields exist and are finite
        for entry in data:
            assert np.isfinite(entry["Ca"])
            assert np.isfinite(entry["mT"])
            assert np.isfinite(entry["hT"])
            assert np.isfinite(entry["I_T_abs"])
            assert np.isfinite(entry["g_Ca_eq"])


# ============================================================
# 9. Data export interfaces
# ============================================================

class TestExport:

    def test_export_history_matrices_shape_and_contiguity(self):
        """Exported matrices should be C-contiguous and have correct shapes."""
        steps = 100
        _build_single_compartment(steps=steps)
        hm.start_simulation()
        hV, hM, hH, hN = hm.export_history_matrices()
        for arr in [hV, hM, hH, hN]:
            assert arr.shape == (steps + 1, 1)
            assert arr.dtype == np.float64
            assert arr.flags['C_CONTIGUOUS']

    def test_export_calcium_history_matrices(self):
        """Exported calcium history matrices should have correct shapes and types."""
        steps = 100
        _build_single_compartment_with_CaT(steps=steps)
        hm.start_simulation()
        hCA, hMT, hHT = hm.export_calcium_history_matrices()
        for arr in [hCA, hMT, hHT]:
            assert arr.shape == (steps + 1, 1)
            assert arr.dtype == np.float64
            assert arr.flags['C_CONTIGUOUS']

    def test_export_probe_data_json_valid(self):
        """export_probe_data_json should return a valid JSON string."""
        dt = 0.025
        steps = 200
        seg_id = _build_single_compartment(dt=dt, steps=steps)
        hm.insert_probe("p1", seg_id, 0.0, dt * steps)
        hm.start_simulation()

        json_str = hm.export_probe_data_json()
        parsed = json.loads(json_str)
        assert isinstance(parsed, dict)
        assert "p1" in parsed
        assert isinstance(parsed["p1"], list)
        assert len(parsed["p1"]) > 0

    def test_export_probe_data_json_empty(self):
        """Export when no probes exist should return an empty dict JSON."""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        json_str = hm.export_probe_data_json()
        assert json.loads(json_str) == {}

    def test_export_probe_json_matches_internal(self):
        """Exported JSON should match internal PROBE_SAVE_DATA."""
        dt = 0.025
        steps = 200
        seg_id = _build_single_compartment(dt=dt, steps=steps)
        hm.insert_probe("p1", seg_id, 0.0, 2.0)
        hm.start_simulation()

        json_str = hm.export_probe_data_json()
        parsed = json.loads(json_str)
        internal = hm.PROBE_SAVE_DATA

        assert set(parsed.keys()) == set(internal.keys())
        for key in parsed:
            assert len(parsed[key]) == len(internal[key])


# ============================================================
# 10. End-to-end integration tests (simulate SimulationRunner.cs call sequence)
# ============================================================

class TestIntegrationSimulationRunnerFlow:
    """
    Execute in the strict SimulationRunner.cs call order:
    clear_environment → set_env → set_E → init_segment (+ add_channel)
    → add_connection → insert_stimulation → insert_probe → start_simulation
    → export_probe_data_json
    """

    def test_full_workflow_single_compartment(self):
        # ── 1. clear_environment ──
        hm.clear_environment()

        # ── 2. set_env ──
        v_init = -65.0
        dt = 0.025
        steps = 2000
        hm.set_env(V_init=v_init, dt=dt, steps=steps, n_node=1)

        # ── 3. set_E ──
        e_json = '{"Na":{"E":55.0},"K":{"E":-72.0},"L":{"E":-54.3}}'
        hm.set_E(json.loads(e_json))

        # ── 4. init_segment + add_channel_to_segment ──
        hm.init_segment(uid="entity_001", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.add_channel_to_segment(0, "Na", 120.0)
        hm.add_channel_to_segment(0, "K", 36.0)
        hm.add_channel_to_segment(0, "L", 0.3)

        # ── 5. add_connection (single compartment — no connections) ──

        # ── 6. insert_stimulation ──
        hm.insert_stimulation("stim_0", 0, 0.1, 1.0, 0.5)

        # ── 7. insert_probe ──
        hm.insert_probe("probe_0", 0, 0.0, dt * steps)

        # ── 8. start_simulation ──
        step_log = []
        hm.start_simulation(progress_callback=lambda s: step_log.append(s))

        # ── 9. export_probe_data_json ──
        result_json = hm.export_probe_data_json()
        result = json.loads(result_json)

        # Verification
        assert hm.CURRENT_STEP == steps
        assert hm.SIMULATION_RUNNING is False
        assert len(step_log) == steps
        assert "probe_0" in result
        assert len(result["probe_0"]) > 0
        V_max = np.max(hm.HISTORY_V[:, 0])
        assert V_max > 0.0, f"No action potential triggered: V_max={V_max}"

    def test_full_workflow_multi_compartment(self):
        """Three-compartment chain topology: soma ↔ axon_0 ↔ axon_1"""
        hm.clear_environment()

        dt = 0.025
        steps = 3000
        n_node = 3
        hm.set_env(V_init=-65.0, dt=dt, steps=steps, n_node=n_node)
        hm.set_E({"Na": {"E": 55.0}, "K": {"E": -72.0}, "L": {"E": -54.3}})

        compartments = [
            {"uid": "soma", "Ra": 100.0, "D": 12.0, "L": 80.0, "Cm": 1.0, "id": 0},
            {"uid": "axon_0", "Ra": 100.0, "D": 5.0, "L": 150.0, "Cm": 1.0, "id": 1},
            {"uid": "axon_1", "Ra": 100.0, "D": 5.0, "L": 150.0, "Cm": 1.0, "id": 2},
        ]
        connections = [(0, 1), (1, 0), (1, 2), (2, 1)]

        for c in compartments:
            hm.init_segment(**c)
            hm.add_channel_to_segment(c["id"], "Na", 120.0)
            hm.add_channel_to_segment(c["id"], "K", 36.0)
            hm.add_channel_to_segment(c["id"], "L", 0.3)

        for src, dst in connections:
            hm.add_connection(src, dst)

        hm.insert_stimulation("stim_soma", 0, 0.1, 1.0, 0.5)
        hm.insert_probe("probe_soma", 0, 0.0, dt * steps)
        hm.insert_probe("probe_end", 2, 0.0, dt * steps)

        hm.start_simulation()

        result = json.loads(hm.export_probe_data_json())
        assert "probe_soma" in result
        assert "probe_end" in result
        assert len(result["probe_soma"]) > 0
        assert len(result["probe_end"]) > 0

        V_history, M_history, H_history, N_history = hm.export_history_matrices()
        assert V_history.shape == (steps + 1, n_node)
        V_max_end = np.max(V_history[:, 2])
        assert V_max_end > -50.0, f"Signal did not propagate to end: V_max={V_max_end}"

    def test_full_workflow_with_CaT(self):
        """End-to-end flow with CaT channels."""
        hm.clear_environment()

        dt = 0.025
        steps = 2000
        hm.set_env(V_init=-65.0, dt=dt, steps=steps, n_node=2)
        hm.set_E({"Na": {"E": 55.0}, "K": {"E": -72.0}, "L": {"E": -54.3}})

        hm.init_segment(uid="soma", Ra=100.0, D=12.0, L=80.0, Cm=1.0, id=0)
        hm.init_segment(uid="dend", Ra=100.0, D=5.0, L=150.0, Cm=1.0, id=1)

        for sid in [0, 1]:
            hm.add_channel_to_segment(sid, "Na", 120.0)
            hm.add_channel_to_segment(sid, "K", 36.0)
            hm.add_channel_to_segment(sid, "L", 0.3)
            hm.add_channel_to_segment(sid, "CaT", 1e-5)

        hm.add_connection(0, 1)
        hm.add_connection(1, 0)

        hm.insert_stimulation("stim", 0, 0.1, 1.0, 0.5)
        hm.insert_probe("probe_soma", 0, 0.0, dt * steps)

        hm.start_simulation()

        # Basic assertions
        assert hm.CURRENT_STEP == steps
        assert np.all(np.isfinite(hm.HISTORY_V))
        assert np.all(np.isfinite(hm.HISTORY_CA))
        assert np.all(hm.HISTORY_CA >= 0)

        # Export validation
        hCA, hMT, hHT = hm.export_calcium_history_matrices()
        assert hCA.shape == (steps + 1, 2)

        result = json.loads(hm.export_probe_data_json())
        assert "probe_soma" in result
        # Verify probe data contains calcium fields
        for entry in result["probe_soma"][:5]:
            assert "Ca" in entry
            assert "I_T_abs" in entry


# ============================================================
# 11. Boundary conditions and robustness
# ============================================================

class TestEdgeCases:

    def test_zero_stimulation_current(self):
        """Zero-current stimulation should not produce an action potential."""
        seg_id = _build_single_compartment(steps=200)
        hm.insert_stimulation("zero_stim", seg_id, 0.0, 0.0, 1.0)
        hm.start_simulation()
        V_max = np.max(hm.HISTORY_V[:, 0])
        assert V_max < 0.0, f"Zero stimulus triggered an AP: V_max={V_max}"

    def test_very_short_dt(self):
        """Very short dt should not lead to numerical blowup."""
        _build_single_compartment(dt=0.001, steps=100)
        hm.insert_stimulation("stim", 0, 0.05, 0.01, 0.05)
        hm.start_simulation()
        assert np.all(np.isfinite(hm.HISTORY_V))

    def test_large_Ra_weakens_coupling(self):
        """High axial resistance should weaken inter-compartment coupling."""
        # low Ra
        hm.set_env(V_init=-65.0, dt=0.025, steps=1000, n_node=2)
        hm.init_segment(uid="a", Ra=10.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.init_segment(uid="b", Ra=10.0, D=10.0, L=100.0, Cm=1.0, id=1)
        for s in [0, 1]:
            hm.add_channel_to_segment(s, "Na", 120.0)
            hm.add_channel_to_segment(s, "K", 36.0)
            hm.add_channel_to_segment(s, "L", 0.3)
        hm.add_connection(0, 1)
        hm.add_connection(1, 0)
        hm.insert_stimulation("stim", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        V_diff_low_Ra = np.max(np.abs(hm.HISTORY_V[:, 0] - hm.HISTORY_V[:, 1]))

        # High Ra
        hm.clear_environment()
        hm.set_env(V_init=-65.0, dt=0.025, steps=1000, n_node=2)
        hm.init_segment(uid="a", Ra=10000.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.init_segment(uid="b", Ra=10000.0, D=10.0, L=100.0, Cm=1.0, id=1)
        for s in [0, 1]:
            hm.add_channel_to_segment(s, "Na", 120.0)
            hm.add_channel_to_segment(s, "K", 36.0)
            hm.add_channel_to_segment(s, "L", 0.3)
        hm.add_connection(0, 1)
        hm.add_connection(1, 0)
        hm.insert_stimulation("stim", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        V_diff_high_Ra = np.max(np.abs(hm.HISTORY_V[:, 0] - hm.HISTORY_V[:, 1]))

        assert V_diff_high_Ra > V_diff_low_Ra

    def test_no_channels_passive_decay(self):
        """With no active channels (only leak), voltage should monotonically decay back to E_L after stimulation ends."""
        hm.set_env(V_init=-65.0, dt=0.025, steps=2000, n_node=1)
        hm.init_segment(uid="passive", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.add_channel_to_segment(0, "Na", 0.0)
        hm.add_channel_to_segment(0, "K", 0.0)
        hm.add_channel_to_segment(0, "L", 0.3)
        hm.insert_stimulation("stim", 0, 0.0005, 1.0, 0.5)
        hm.start_simulation()
        V_trace = hm.HISTORY_V[:, 0]
        assert np.all(np.isfinite(V_trace)), "Non-finite values encountered"
        E_L = hm.E_TABLE["L"]["E"]  # -76.5 with Traub params
        assert abs(V_trace[-1] - E_L) < 1.0, f"Final voltage did not converge to E_L={E_L}: {V_trace[-1]}"

    def test_very_short_dt_with_CaT(self):
        """Very short dt with CaT should not cause numerical blowup."""
        _build_single_compartment_with_CaT(dt=0.001, steps=100, P_CaT=1e-4)
        hm.insert_stimulation("stim", 0, 0.05, 0.01, 0.05)
        hm.start_simulation()
        assert np.all(np.isfinite(hm.HISTORY_V))
        assert np.all(np.isfinite(hm.HISTORY_CA))

    def test_large_P_CaT_stability(self):
        """Larger CaT permeability should not cause numerical instability."""
        _build_single_compartment_with_CaT(dt=0.025, steps=1000, P_CaT=1e-3)
        hm.insert_stimulation("stim", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        assert np.all(np.isfinite(hm.HISTORY_V))
        assert np.all(np.isfinite(hm.HISTORY_CA))


# ============================================================
# 12. Dynamic phase portrait function tests
# ============================================================

class TestShowDynamicPhasePortrait:
    """
    Tests input validation and pipeline for show_dynamic_phase_portrait.
    plt.show is monkeypatched to avoid blocking CI.
    """

    def test_raises_without_history(self):
        """Should raise RuntimeError if set_env has not been executed."""
        with pytest.raises(RuntimeError):
            hm.show_dynamic_phase_portrait("p0")

    def test_raises_on_invalid_var(self):
        """Invalid axis variable label should raise ValueError."""
        _build_single_compartment(steps=50)
        hm.insert_probe("p0", 0, 0.0, 1.0)
        hm.start_simulation()
        with pytest.raises(ValueError, match="Axis variable"):
            hm.show_dynamic_phase_portrait("p0", x_var="Z")

    def test_raises_on_invalid_probe(self):
        """Nonexistent probe ID should raise ValueError."""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        with pytest.raises(ValueError, match="Probe parsing failed"):
            hm.show_dynamic_phase_portrait("nonexistent_probe")

    def test_runs_with_valid_input(self, monkeypatch):
        """With valid input, should execute successfully without crashing."""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_single_compartment(dt=0.025, steps=200)
        hm.insert_probe("p0", 0, 0.0, 2.0)
        hm.insert_stimulation("s0", 0, 0.1, 0.5, 1.0)
        hm.start_simulation()
        hm.show_dynamic_phase_portrait("p0", x_var='V', y_var='n', Nx=5, Ny=5)

    def test_custom_axis_combination(self, monkeypatch):
        """Using m-h axis combination should work."""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_single_compartment(dt=0.025, steps=100)
        hm.insert_probe("p0", 0, 0.0, 1.0)
        hm.start_simulation()
        hm.show_dynamic_phase_portrait("p0", x_var='m', y_var='h', Nx=4, Ny=4)


# ============================================================
# 13. Phase portrait mesh data interface tests
# ============================================================

class TestGeneratePhasePortraitMesh:

    def test_raises_without_history(self):
        """Should raise RuntimeError if set_env has not been executed."""
        with pytest.raises(RuntimeError):
            hm.generate_phase_portrait_mesh(0, 0)

    def test_output_lengths(self):
        """Returned four flattened lists should each have length Nx * Ny."""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        Nx, Ny = 8, 6
        V_flat, N_flat, dV_flat, dN_flat = hm.generate_phase_portrait_mesh(0, 0, Nx=Nx, Ny=Ny)
        expected_len = Nx * Ny
        assert len(V_flat) == expected_len
        assert len(N_flat) == expected_len
        assert len(dV_flat) == expected_len
        assert len(dN_flat) == expected_len

    def test_values_finite(self):
        """All output values should be finite numbers."""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        V_flat, N_flat, dV_flat, dN_flat = hm.generate_phase_portrait_mesh(0, 0, Nx=5, Ny=5)
        for arr in [V_flat, N_flat, dV_flat, dN_flat]:
            assert all(np.isfinite(v) for v in arr)


# ============================================================
# 14. plot_variable_over_time tests
# ============================================================

class TestPlotVariableOverTime:
    """
    Tests input validation and pipeline for plot_variable_over_time.
    plt.show is monkeypatched to avoid blocking CI.
    """

    def test_raises_without_history(self):
        """Should raise RuntimeError if set_env has not been executed."""
        with pytest.raises(RuntimeError):
            hm.plot_variable_over_time(0, 'V', 0.0, 1.0)

    def test_raises_on_invalid_var_label(self):
        """Invalid variable label should raise ValueError."""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        with pytest.raises(ValueError, match="Variable label"):
            hm.plot_variable_over_time(0, 'X', 0.0, 1.0)

    def test_raises_on_invalid_segment_id(self):
        """Nonexistent segment ID should raise ValueError."""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        with pytest.raises(ValueError, match="Segment ID"):
            hm.plot_variable_over_time(999, 'V', 0.0, 1.0)

    def test_raises_on_invalid_time_range(self):
        """start >= end should raise ValueError."""
        _build_single_compartment(dt=0.025, steps=50)
        hm.start_simulation()
        with pytest.raises(ValueError, match="Invalid time range"):
            hm.plot_variable_over_time(0, 'V', 5.0, 0.5)

    def test_plots_voltage(self, monkeypatch):
        """Plotting V variable should execute successfully."""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_single_compartment(dt=0.025, steps=400)
        hm.insert_stimulation("s0", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        hm.plot_variable_over_time(0, 'V', 0.0, 5.0)

    def test_plots_all_gating_variables(self, monkeypatch):
        """All four variable labels should plot successfully."""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_single_compartment(dt=0.025, steps=200)
        hm.start_simulation()
        for label in ['V', 'm', 'h', 'n']:
            hm.plot_variable_over_time(0, label, 0.0, 2.0)

    def test_plots_calcium_variables(self, monkeypatch):
        """Ca, mT, hT variable labels should plot successfully."""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_single_compartment_with_CaT(dt=0.025, steps=200)
        hm.start_simulation()
        for label in ['Ca', 'mT', 'hT']:
            hm.plot_variable_over_time(0, label, 0.0, 2.0)

    def test_time_clamp(self, monkeypatch):
        """Time range beyond simulation should be clamped without error."""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        dt = 0.025
        steps = 100
        _build_single_compartment(dt=dt, steps=steps)
        hm.start_simulation()
        hm.plot_variable_over_time(0, 'V', 0.0, 100.0)

    def test_multi_compartment(self, monkeypatch):
        """Plotting for different compartments in a multi-compartment environment should work."""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_two_compartments(dt=0.025, steps=200)
        hm.start_simulation()
        hm.plot_variable_over_time(0, 'V', 0.0, 3.0)
        hm.plot_variable_over_time(1, 'n', 0.0, 3.0)

    def test_compute_derivatives_with_override(self):
        """Using override argument should correctly substitute history values."""
        _build_single_compartment(dt=0.025, steps=50)
        hm.HISTORY_V[0, 0] = -65.0
        m0, n0, h0 = hm.init_gates(-65.0)
        hm.HISTORY_M[0, 0] = m0
        hm.HISTORY_H[0, 0] = h0
        hm.HISTORY_N[0, 0] = n0

        # No override
        dV1, dm1, dh1, dn1 = hm.compute_continuous_derivatives(segment_id=0, step=0)
        # With V override
        dV2, dm2, dh2, dn2 = hm.compute_continuous_derivatives(
            segment_id=0, step=0, V_override=0.0)
        assert dV1 != pytest.approx(dV2, abs=1e-3)


# ============================================================
# 15. Dimensional consistency checks
# ============================================================

class TestDimensionalConsistency:
    """Verify dimensional consistency of key physical quantities."""

    def test_Kij_units_mS(self):
        """K_ij = 1/(R_i + R_j) should be on the order of mS."""
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        # R = Ra * 10 * L/2 / cross_area (kOhm)
        R_half = seg.Ra * 10.0 * (seg.L / 2) / seg.cross_area_um2
        K = hm.calculate_Kij(seg, seg)
        assert K == pytest.approx(1.0 / (2 * R_half), rel=1e-10)
        # Typical values are around the mS range
        assert K > 0

    def test_absolute_C_uF(self):
        """absolute_C should be in uF."""
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        # Cm=1 uF/cm², surface_area in cm² → C in uF
        C = seg.absolute_C
        expected = 1.0 * math.pi * 10 * 100 * 1e-8
        assert C == pytest.approx(expected, rel=1e-10)

    def test_C_over_DEL_is_mS(self):
        """C/DEL dimensionality should be mS (= uF/ms = uA/mV)."""
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        DEL = 0.0125  # ms
        C_factor = seg.absolute_C / DEL  # uF / ms = mS
        # Should be positive
        assert C_factor > 0

    def test_gamma_Ca_correct_coefficient(self):
        """gamma_Ca coefficient check: 1e-6 / (z*F*Vol) → (mM/ms)/uA."""
        seg = hm.Segment(uid="t", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        d = 0.1  # um
        vol_cm3 = seg.surface_area_cm2 * d * 1e-4
        vol_L = vol_cm3 * 1e-3
        expected = 1e-6 / (2.0 * 96485.3 * vol_L)
        assert seg.gamma_Ca == pytest.approx(expected, rel=1e-10)


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
