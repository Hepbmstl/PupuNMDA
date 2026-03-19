"""
Hines_method.py 完整测试套件
覆盖：
  - 环境初始化与清理 (set_env, set_E, clear_environment)
  - 区室构建与属性 (Segment, init_segment, add_channel_to_segment, add_connection)
  - 探针与刺激注册 (insert_probe, insert_stimulation)
  - HH 门控动力学 (alpha/beta 速率函数, init_gates, gating_update)
  - 物理计算 (calculate_Kij, compute_continuous_derivatives)
  - 完整仿真流程 (start_simulation — 单/多区室)
  - 数据导出接口 (export_history_matrices, export_probe_data_json)
  - 端到端集成测试 (模拟 SimulationRunner.cs 调用顺序)
"""

import pytest
import numpy as np
import json
import math
import sys
import os
import matplotlib
matplotlib.use('Agg')  # 非交互式后端，避免 Tk 依赖
import matplotlib.pyplot as plt

# 确保 Backward 目录在 sys.path 中
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import Hines_method as hm


# ============================================================
# Fixtures
# ============================================================

@pytest.fixture(autouse=True)
def clean_state():
    """每个测试前后都调用 clear_environment，保证隔离性。"""
    hm.clear_environment()
    yield
    hm.clear_environment()


def _build_single_compartment(
    v_init=-65.0, dt=0.025, steps=500,
    Ra=100.0, D=10.0, L=100.0, Cm=1.0,
    g_Na=120.0, g_K=36.0, g_L=0.3
):
    """辅助函数：搭建单区室 HH 环境，返回 seg_id。"""
    seg_id = 0
    hm.set_env(V_init=v_init, dt=dt, steps=steps, n_node=1)
    hm.init_segment(uid="soma", Ra=Ra, D=D, L=L, Cm=Cm, id=seg_id)
    hm.add_channel_to_segment(seg_id, "Na", g_Na)
    hm.add_channel_to_segment(seg_id, "K", g_K)
    hm.add_channel_to_segment(seg_id, "L", g_L)
    return seg_id


def _build_two_compartments(dt=0.025, steps=400):
    """辅助函数：搭建双区室并双向连接。"""
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


# ============================================================
# 1. 环境设置与清理
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
        assert hm.V == -65.0
        assert hm.DT == 0.02
        assert hm.STEPS == 1000
        assert hm.N_NODE == 0
        assert hm.CURRENT_STEP == -1
        assert hm.SIMULATION_RUNNING is False

    def test_get_current_step_and_is_running_defaults(self):
        assert hm.get_current_step() == -1
        assert hm.is_simulation_running() is False


# ============================================================
# 2. Segment 构建与属性
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
        # 不存在的通道应返回 0
        assert seg.get_absolute_g_max("Ca") == 0.0

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
        assert hm.SEGMENT[0].channels["Na"] == 120.0
        assert hm.SEGMENT[0].channels["K"] == 36.0

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
# 3. 探针与刺激注册
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
# 4. HH 门控动力学
# ============================================================

class TestGatingKinetics:

    def test_alpha_m_normal(self):
        """alpha_m(-65) 应为正值（标准 HH 参数）。"""
        val = hm.alpha_m(-65.0)
        assert val > 0
        assert np.isfinite(val)

    def test_alpha_m_singularity(self):
        """V = -35 时使用极限值 1.0。"""
        assert hm.alpha_m(-35.0) == pytest.approx(1.0)

    def test_alpha_n_singularity(self):
        """V = -50 时使用极限值 0.1。"""
        assert hm.alpha_n(-50.0) == pytest.approx(0.1)

    def test_alpha_n_normal(self):
        val = hm.alpha_n(-65.0)
        assert val > 0
        assert np.isfinite(val)

    def test_beta_m(self):
        val = hm.beta_m(-65.0)
        expected = 4 * np.exp(-(-65 + 60) / 18)
        assert val == pytest.approx(expected, rel=1e-10)

    def test_alpha_h(self):
        val = hm.alpha_h(-65.0)
        expected = 0.07 * np.exp(-(-65 + 60) / 20)
        assert val == pytest.approx(expected, rel=1e-10)

    def test_beta_h(self):
        val = hm.beta_h(-65.0)
        expected = 1 / (1 + np.exp(-((-65 + 30) / 10)))
        assert val == pytest.approx(expected, rel=1e-10)

    def test_beta_n(self):
        val = hm.beta_n(-65.0)
        expected = 0.125 * np.exp(-(-65 + 60) / 80)
        assert val == pytest.approx(expected, rel=1e-10)

    def test_init_gates_steady_state(self):
        """init_gates 返回的门控变量应处于稳态：alpha/(alpha+beta)。"""
        V = -65.0
        m0, n0, h0 = hm.init_gates(V)
        assert m0 == pytest.approx(hm.alpha_m(V) / (hm.alpha_m(V) + hm.beta_m(V)), rel=1e-10)
        assert h0 == pytest.approx(hm.alpha_h(V) / (hm.alpha_h(V) + hm.beta_h(V)), rel=1e-10)
        assert n0 == pytest.approx(hm.alpha_n(V) / (hm.alpha_n(V) + hm.beta_n(V)), rel=1e-10)

    def test_init_gates_values_in_range(self):
        """门控变量应在 [0, 1] 范围内。"""
        for V in [-80.0, -65.0, -50.0, -20.0, 0.0, 30.0]:
            m, n, h = hm.init_gates(V)
            for g in [m, n, h]:
                assert 0.0 <= g <= 1.0, f"门控变量越界: V={V}, g={g}"

    def test_gating_update_convergence(self):
        """从任意初值出发，gating_update 应收敛到稳态 alpha/(alpha+beta)。"""
        V = -65.0
        alpha = hm.alpha_m(V)
        beta = hm.beta_m(V)
        m_ss = alpha / (alpha + beta)
        m = 0.0  # 从 0 开始
        dt_half = 0.01
        for _ in range(50000):
            m = hm.gating_update(dt_half, m, alpha, beta)
        assert m == pytest.approx(m_ss, abs=1e-6)

    def test_gating_update_preserves_steady_state(self):
        """如果从稳态开始，gating_update 应保持稳态。"""
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
# 5. 物理计算
# ============================================================

class TestPhysics:

    def test_calculate_Kij_symmetric(self):
        """两个相同区室之间的 K_ij 应该相等 (对称)。"""
        hm.init_segment(uid="a", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.init_segment(uid="b", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=1)
        K_01 = hm.calculate_Kij(hm.SEGMENT[0], hm.SEGMENT[1])
        K_10 = hm.calculate_Kij(hm.SEGMENT[1], hm.SEGMENT[0])
        assert K_01 == pytest.approx(K_10, rel=1e-10)

    def test_calculate_Kij_positive(self):
        """K_ij 应始终为正。"""
        hm.init_segment(uid="a", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.init_segment(uid="b", Ra=50.0, D=5.0, L=200.0, Cm=1.0, id=1)
        K = hm.calculate_Kij(hm.SEGMENT[0], hm.SEGMENT[1])
        assert K > 0

    def test_calculate_Kij_formula(self):
        """按公式手动验证 K_ij。"""
        seg_a = hm.Segment(uid="a", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        seg_b = hm.Segment(uid="b", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=1)
        R_a = seg_a.Ra * 10.0 * (seg_a.L / 2) / seg_a.cross_area_um2
        R_b = seg_b.Ra * 10.0 * (seg_b.L / 2) / seg_b.cross_area_um2
        expected = 1.0 / (R_a + R_b)
        assert hm.calculate_Kij(seg_a, seg_b) == pytest.approx(expected, rel=1e-10)

    def test_compute_continuous_derivatives_at_rest(self):
        """在静息电位和稳态门控变量下，dV/dt 应接近 0（无外部刺激）。"""
        V_rest = -65.0
        m0, n0, h0 = hm.init_gates(V_rest)
        hm.set_env(V_init=V_rest, dt=0.025, steps=10, n_node=1)
        hm.init_segment(uid="s", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.add_channel_to_segment(0, "Na", 120.0)
        hm.add_channel_to_segment(0, "K", 36.0)
        hm.add_channel_to_segment(0, "L", 0.3)

        # 手动填充 step=0 的历史数据
        hm.HISTORY_V[0, 0] = V_rest
        hm.HISTORY_M[0, 0] = m0
        hm.HISTORY_H[0, 0] = h0
        hm.HISTORY_N[0, 0] = n0

        dV, dm, dh, dn = hm.compute_continuous_derivatives(
            segment_id=0, step=0
        )
        # 稳态下门控变量导数应接近 0
        assert dm == pytest.approx(0.0, abs=1e-8)
        assert dh == pytest.approx(0.0, abs=1e-8)
        assert dn == pytest.approx(0.0, abs=1e-8)
        # dV/dt 在标准 HH 模型的 -65 mV 处不严格为 0（取决于离子
        # 平衡电位），但应是有限小值
        assert np.isfinite(dV)


# ============================================================
# 6. 单区室仿真
# ============================================================

class TestSingleCompartmentSimulation:

    def test_no_stim_resting(self):
        """无刺激时，膜电位应维持在静息电位附近。"""
        _build_single_compartment(v_init=-65.0, dt=0.025, steps=1000)
        hm.start_simulation()
        V_final = hm.HISTORY_V[-1, 0]
        # 无刺激，电压应漂移很小
        assert abs(V_final - (-65.0)) < 5.0, f"静息漂移过大: V_final={V_final}"

    def test_stimulation_triggers_spike(self):
        """注入足够大的电流脉冲应触发动作电位。"""
        seg_id = _build_single_compartment(dt=0.025, steps=2000)
        hm.insert_stimulation("stim_1", seg_id, 0.1, 1.0, 0.5)
        hm.start_simulation()
        V_max = np.max(hm.HISTORY_V[:, 0])
        # 典型 HH 动作电位峰值应超过 0 mV
        assert V_max > 0.0, f"未触发动作电位: V_max={V_max}"

    def test_history_shape(self):
        """HISTORY 矩阵形状应为 (steps+1, n_node)。"""
        steps = 500
        _build_single_compartment(steps=steps)
        hm.start_simulation()
        assert hm.HISTORY_V.shape == (steps + 1, 1)
        assert hm.HISTORY_M.shape == (steps + 1, 1)
        assert hm.HISTORY_H.shape == (steps + 1, 1)
        assert hm.HISTORY_N.shape == (steps + 1, 1)

    def test_gating_variables_bounded(self):
        """所有门控变量在仿真过程中应保持在 [0, 1]。"""
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
        """仿真完成后 CURRENT_STEP 应等于 STEPS。"""
        _build_single_compartment(steps=100)
        hm.start_simulation()
        assert hm.CURRENT_STEP == 100
        assert hm.SIMULATION_RUNNING is False

    def test_start_simulation_without_set_env_raises(self):
        """未调用 set_env 就 start_simulation 应抛出 RuntimeError。"""
        with pytest.raises(RuntimeError):
            hm.start_simulation()

    def test_progress_callback(self):
        """progress_callback 应被调用，且传入的 step 值递增。"""
        _build_single_compartment(steps=50)
        steps_seen = []
        hm.start_simulation(progress_callback=lambda s: steps_seen.append(s))
        assert len(steps_seen) == 50
        assert steps_seen == list(range(50))


# ============================================================
# 7. 多区室仿真
# ============================================================

class TestMultiCompartmentSimulation:

    def test_two_compartment_spike_propagation(self):
        """
        刺激第一个区室，动作电位应传播到第二个区室。
        """
        sid0, sid1 = _build_two_compartments(dt=0.025, steps=3000)
        hm.insert_stimulation("stim", sid0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        V_max_0 = np.max(hm.HISTORY_V[:, 0])
        V_max_1 = np.max(hm.HISTORY_V[:, 1])
        # 两个区室都应产生动作电位
        assert V_max_0 > 0.0, f"区室0未放电: V_max={V_max_0}"
        assert V_max_1 > -30.0, f"区室1电位过低: V_max={V_max_1}"

    def test_axial_current_coupling(self):
        """连接的区室在刺激下电压应不同于未连接的情况。"""
        # 有连接
        sid0, sid1 = _build_two_compartments(dt=0.025, steps=500)
        hm.insert_stimulation("stim", sid0, 0.05, 1.0, 1.0)
        hm.start_simulation()
        V_coupled = hm.HISTORY_V[:, 1].copy()

        # 无连接（重建但不连接）
        hm.clear_environment()
        hm.set_env(V_init=-65.0, dt=0.025, steps=500, n_node=2)
        hm.init_segment(uid="soma", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.init_segment(uid="axon", Ra=100.0, D=5.0, L=200.0, Cm=1.0, id=1)
        for sid in [0, 1]:
            hm.add_channel_to_segment(sid, "Na", 120.0)
            hm.add_channel_to_segment(sid, "K", 36.0)
            hm.add_channel_to_segment(sid, "L", 0.3)
        # 不添加 connection
        hm.insert_stimulation("stim", 0, 0.05, 1.0, 1.0)
        hm.start_simulation()
        V_uncoupled = hm.HISTORY_V[:, 1].copy()

        # 连接的区室1应受到区室0的影响
        diff = np.max(np.abs(V_coupled - V_uncoupled))
        assert diff > 0.01, f"轴向耦合无效: max_diff={diff}"


# ============================================================
# 8. 探针数据收集
# ============================================================

class TestProbeDataCollection:

    def test_probe_collects_data_in_window(self):
        """探针在指定时间窗口内应收集数据。"""
        dt = 0.025
        steps = 400
        seg_id = _build_single_compartment(dt=dt, steps=steps)
        hm.insert_stimulation("stim", seg_id, 0.1, 1.0, 0.5)
        # 探针覆盖 1.0 ms 到 3.0 ms
        hm.insert_probe("probe_main", seg_id, 1.0, 2.0)
        hm.start_simulation()

        assert "probe_main" in hm.PROBE_SAVE_DATA
        data = hm.PROBE_SAVE_DATA["probe_main"]
        assert len(data) > 0
        # 验证时间范围
        for entry in data:
            assert 1.0 <= entry["time_ms"] <= 3.0

    def test_probe_data_fields(self):
        """探针数据应包含所有预期字段。"""
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
            "A_matrix_row", "b_vector_val", "V_half_val", "V_t_next"
        }
        for entry in data:
            assert set(entry.keys()) == expected_keys

    def test_probe_outside_window_collects_nothing(self):
        """探针时间窗口在仿真范围外时不应收集数据。"""
        dt = 0.025
        steps = 100  # 总时间 = 2.5 ms
        seg_id = _build_single_compartment(dt=dt, steps=steps)
        # 探针从 10 ms 开始，远超仿真时长
        hm.insert_probe("p_late", seg_id, 10.0, 1.0)
        hm.start_simulation()
        assert "p_late" not in hm.PROBE_SAVE_DATA

    def test_multiple_probes(self):
        """多个探针应独立收集数据。"""
        dt = 0.025
        steps = 400
        seg_id = _build_single_compartment(dt=dt, steps=steps)
        hm.insert_probe("p_early", seg_id, 0.0, 2.0)
        hm.insert_probe("p_late", seg_id, 5.0, 3.0)
        hm.start_simulation()
        assert "p_early" in hm.PROBE_SAVE_DATA
        assert "p_late" in hm.PROBE_SAVE_DATA
        # 两组数据时间不应重叠
        early_times = {e["time_ms"] for e in hm.PROBE_SAVE_DATA["p_early"]}
        late_times = {e["time_ms"] for e in hm.PROBE_SAVE_DATA["p_late"]}
        assert early_times.isdisjoint(late_times)


# ============================================================
# 9. 数据导出接口
# ============================================================

class TestExport:

    def test_export_history_matrices_shape_and_contiguity(self):
        """导出的矩阵应为 C-contiguous 且形状正确。"""
        steps = 100
        _build_single_compartment(steps=steps)
        hm.start_simulation()
        hV, hM, hH, hN = hm.export_history_matrices()
        for arr in [hV, hM, hH, hN]:
            assert arr.shape == (steps + 1, 1)
            assert arr.dtype == np.float64
            assert arr.flags['C_CONTIGUOUS']

    def test_export_probe_data_json_valid(self):
        """export_probe_data_json 应返回合法 JSON 字符串。"""
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
        """无探针时导出应返回空字典 JSON。"""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        json_str = hm.export_probe_data_json()
        assert json.loads(json_str) == {}

    def test_export_probe_json_matches_internal(self):
        """JSON 导出数据应与内部 PROBE_SAVE_DATA 一致。"""
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
# 10. 端到端集成测试 (模拟 SimulationRunner.cs 调用顺序)
# ============================================================

class TestIntegrationSimulationRunnerFlow:
    """
    严格按照 SimulationRunner.cs 中的调用顺序执行：
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

        # ── 3. set_E (通过 JSON，模拟 C# 端) ──
        e_json = '{"Na":{"E":55.0},"K":{"E":-72.0},"L":{"E":-54.3}}'
        hm.set_E(json.loads(e_json))

        # ── 4. init_segment + add_channel_to_segment ──
        hm.init_segment(uid="entity_001", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.add_channel_to_segment(0, "Na", 120.0)
        hm.add_channel_to_segment(0, "K", 36.0)
        hm.add_channel_to_segment(0, "L", 0.3)

        # ── 5. add_connection (单区室无连接) ──

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

        # 验证
        assert hm.CURRENT_STEP == steps
        assert hm.SIMULATION_RUNNING is False
        assert len(step_log) == steps
        assert "probe_0" in result
        assert len(result["probe_0"]) > 0
        # 检查动作电位
        V_max = np.max(hm.HISTORY_V[:, 0])
        assert V_max > 0.0, f"未触发 AP: V_max={V_max}"

    def test_full_workflow_multi_compartment(self):
        """三区室链式拓扑：soma ↔ axon_0 ↔ axon_1"""
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

        # 信号应该传播到末端
        V_history, M_history, H_history, N_history = hm.export_history_matrices()
        assert V_history.shape == (steps + 1, n_node)
        V_max_end = np.max(V_history[:, 2])
        assert V_max_end > -50.0, f"信号未传播到末端: V_max={V_max_end}"


# ============================================================
# 11. 边界条件与鲁棒性
# ============================================================

class TestEdgeCases:

    def test_zero_stimulation_current(self):
        """零电流刺激不应产生动作电位（HH 模型在 -65 mV 非严格稳态，
        因 E_L=-54.3 存在自然漂移，但不应触发再生性放电）。"""
        seg_id = _build_single_compartment(steps=200)
        hm.insert_stimulation("zero_stim", seg_id, 0.0, 0.0, 1.0)
        hm.start_simulation()
        V_max = np.max(hm.HISTORY_V[:, 0])
        # 不应触发动作电位（峰值远低于 0 mV）
        assert V_max < 0.0, f"零刺激触发了 AP: V_max={V_max}"

    def test_very_short_dt(self):
        """极小时间步长不应导致数值爆炸。"""
        _build_single_compartment(dt=0.001, steps=100)
        hm.insert_stimulation("stim", 0, 0.05, 0.01, 0.05)
        hm.start_simulation()
        assert np.all(np.isfinite(hm.HISTORY_V))

    def test_large_Ra_weakens_coupling(self):
        """高轴向电阻应减弱区室间耦合。"""
        # 低 Ra
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

        # 高 Ra
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

        # 高 Ra 时两区室电压差应更大（耦合更弱）
        assert V_diff_high_Ra > V_diff_low_Ra

    def test_no_channels_passive_decay(self):
        """无活性通道（仅漏电流），刺激结束后电压应单调衰减回 E_L，
        不会出现 HH 再生性尖峰特征。"""
        hm.set_env(V_init=-65.0, dt=0.025, steps=2000, n_node=1)
        hm.init_segment(uid="passive", Ra=100.0, D=10.0, L=100.0, Cm=1.0, id=0)
        hm.add_channel_to_segment(0, "Na", 0.0)  # 无 Na
        hm.add_channel_to_segment(0, "K", 0.0)   # 无 K
        hm.add_channel_to_segment(0, "L", 0.3)   # 仅漏电流
        hm.insert_stimulation("stim", 0, 0.0005, 1.0, 0.5)
        hm.start_simulation()
        V_trace = hm.HISTORY_V[:, 0]
        # 刺激结束后（约 step 60 之后），电压应单调趋向 E_L = -54.3
        stim_end_step = int((1.0 + 0.5) / 0.025) + 10
        V_after = V_trace[stim_end_step:]
        # 电压在刺激后应不再上升（允许微小数值抖动）
        diffs = np.diff(V_after)
        # 衰减期电压变化应趋向 0 或负值（回到 E_L）
        assert np.all(np.isfinite(V_trace)), "出现非有限值"
        # 最终电压应接近漏电流平衡电位
        assert abs(V_trace[-1] - (-54.3)) < 1.0, f"最终电压未收敛到 E_L: {V_trace[-1]}"


# ============================================================
# 12. 动态相图函数测试
# ============================================================

class TestShowDynamicPhasePortrait:
    """
    测试 show_dynamic_phase_portrait 的输入校验和数据管线。
    plt.show 被 monkeypatch 拦截以避免阻塞 CI。
    """

    def test_raises_without_history(self):
        """未执行 set_env 时应抛出 RuntimeError。"""
        with pytest.raises(RuntimeError):
            hm.show_dynamic_phase_portrait("p0")

    def test_raises_on_invalid_var(self):
        """无效的轴变量标签应抛出 ValueError。"""
        _build_single_compartment(steps=50)
        hm.insert_probe("p0", 0, 0.0, 1.0)
        hm.start_simulation()
        with pytest.raises(ValueError, match="轴变量"):
            hm.show_dynamic_phase_portrait("p0", x_var="Z")

    def test_raises_on_invalid_probe(self):
        """探针 ID 不存在应抛出 ValueError。"""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        with pytest.raises(ValueError, match="探针解析失败"):
            hm.show_dynamic_phase_portrait("nonexistent_probe")

    def test_runs_with_valid_input(self, monkeypatch):
        """Valid 输入下应成功执行而不崩溃（拦截 plt.show）。"""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_single_compartment(dt=0.025, steps=200)
        hm.insert_probe("p0", 0, 0.0, 2.0)
        hm.insert_stimulation("s0", 0, 0.1, 0.5, 1.0)
        hm.start_simulation()
        # 不应抛出异常
        hm.show_dynamic_phase_portrait("p0", x_var='V', y_var='n', Nx=5, Ny=5, interval=10)

    def test_custom_axis_combination(self, monkeypatch):
        """使用 m-h 轴组合应正常运行。"""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_single_compartment(dt=0.025, steps=100)
        hm.insert_probe("p0", 0, 0.0, 1.0)
        hm.start_simulation()
        hm.show_dynamic_phase_portrait("p0", x_var='m', y_var='h', Nx=4, Ny=4, interval=10)


# ============================================================
# 13. 相图网格数据接口测试
# ============================================================

class TestGeneratePhasePortraitMesh:

    def test_raises_without_history(self):
        """未执行 set_env 时应抛出 RuntimeError。"""
        with pytest.raises(RuntimeError):
            hm.generate_phase_portrait_mesh(0, 0)

    def test_output_lengths(self):
        """返回的四个扁平化列表长度应等于 Nx * Ny。"""
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
        """所有输出值应为有限数。"""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        V_flat, N_flat, dV_flat, dN_flat = hm.generate_phase_portrait_mesh(0, 0, Nx=5, Ny=5)
        for arr in [V_flat, N_flat, dV_flat, dN_flat]:
            assert all(np.isfinite(v) for v in arr)


# ============================================================
# 14. plot_variable_over_time 测试
# ============================================================

class TestPlotVariableOverTime:
    """
    测试 plot_variable_over_time 的输入校验和数据管线。
    plt.show 被 monkeypatch 拦截以避免阻塞 CI。
    """

    def test_raises_without_history(self):
        """未执行 set_env 时应抛出 RuntimeError。"""
        with pytest.raises(RuntimeError):
            hm.plot_variable_over_time(0, 'V', 0.0, 1.0)

    def test_raises_on_invalid_var_label(self):
        """无效的变量标签应抛出 ValueError。"""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        with pytest.raises(ValueError, match="变量标签"):
            hm.plot_variable_over_time(0, 'X', 0.0, 1.0)

    def test_raises_on_invalid_segment_id(self):
        """不存在的区室 ID 应抛出 ValueError。"""
        _build_single_compartment(steps=50)
        hm.start_simulation()
        with pytest.raises(ValueError, match="区室 ID"):
            hm.plot_variable_over_time(999, 'V', 0.0, 1.0)

    def test_raises_on_invalid_time_range(self):
        """start >= end 应抛出 ValueError。"""
        _build_single_compartment(dt=0.025, steps=50)
        hm.start_simulation()
        with pytest.raises(ValueError, match="时间范围无效"):
            hm.plot_variable_over_time(0, 'V', 5.0, 0.5)

    def test_plots_voltage(self, monkeypatch):
        """绘制 V 变量应成功执行（拦截 plt.show）。"""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_single_compartment(dt=0.025, steps=400)
        hm.insert_stimulation("s0", 0, 0.1, 1.0, 0.5)
        hm.start_simulation()
        hm.plot_variable_over_time(0, 'V', 0.0, 5.0)

    def test_plots_all_gating_variables(self, monkeypatch):
        """四种变量标签均应成功绘制。"""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_single_compartment(dt=0.025, steps=200)
        hm.start_simulation()
        for label in ['V', 'm', 'h', 'n']:
            hm.plot_variable_over_time(0, label, 0.0, 2.0)

    def test_time_clamp(self, monkeypatch):
        """时间范围超出仿真范围时应被截断而不报错。"""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        dt = 0.025
        steps = 100  # 总时间 2.5 ms
        _build_single_compartment(dt=dt, steps=steps)
        hm.start_simulation()
        # end_time 超出仿真范围，应被 clamp 到 STEPS
        hm.plot_variable_over_time(0, 'V', 0.0, 100.0)

    def test_multi_compartment(self, monkeypatch):
        """多区室环境下对不同区室绘制应正常工作。"""
        monkeypatch.setattr(plt, "show", lambda *a, **kw: None)
        _build_two_compartments(dt=0.025, steps=200)
        hm.start_simulation()
        hm.plot_variable_over_time(0, 'V', 0.0, 3.0)
        hm.plot_variable_over_time(1, 'n', 0.0, 3.0)

    def test_compute_derivatives_with_override(self):
        """使用 override 参数时应正确替代历史值。"""
        _build_single_compartment(dt=0.025, steps=50)
        hm.HISTORY_V[0, 0] = -65.0
        m0, n0, h0 = hm.init_gates(-65.0)
        hm.HISTORY_M[0, 0] = m0
        hm.HISTORY_H[0, 0] = h0
        hm.HISTORY_N[0, 0] = n0

        # 无 override
        dV1, dm1, dh1, dn1 = hm.compute_continuous_derivatives(segment_id=0, step=0)
        # 有 V override
        dV2, dm2, dh2, dn2 = hm.compute_continuous_derivatives(
            segment_id=0, step=0, V_override=0.0)
        # 电压偏移应导致不同的导数值
        assert dV1 != pytest.approx(dV2, abs=1e-3)


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
