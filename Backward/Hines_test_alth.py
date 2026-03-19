from Hines_method import (
    set_env, init_segment, add_connection, add_channel_to_segment,
    insert_stimulation, start_simulation, plot_variable_over_time, clear_environment,
    export_calcium_history_matrices
)
import numpy as np

def test_multi_compartment_simulation():
    # 阶段 1: 运行环境与状态矩阵初始化
    clear_environment()
    # 使用 3 个区室，模拟 50ms 跨度
    set_env(V_init=-65.0, dt=0.02, steps=2500, n_node=3)

    # 阶段 2: 空间几何参数设定与绝对状态推导
    init_segment(uid="Soma",  Ra=100.0, D=20.0, L=20.0, Cm=1.0, id=0)
    init_segment(uid="Dend1", Ra=100.0, D=5.0,  L=50.0, Cm=1.0, id=1)
    init_segment(uid="Dend2", Ra=100.0, D=2.0,  L=50.0, Cm=1.0, id=2)

    # 阶段 3: 拓扑结构矩阵映射 (Soma <-> Dend1 <-> Dend2)
    add_connection(0, 1)
    add_connection(1, 0)
    add_connection(1, 2)
    add_connection(2, 1)

    # 阶段 4: 膜电导密度装载 (单位: mS/cm2) + T-type 钙通道
    HH_g_max = {"Na": 120.0, "K": 36.0, "L": 0.3}
    P_CaT_density = 1e-5  # T-type 钙通道渗透率密度 (cm/s)

    for seg_id in range(3):
        for ion, g_max in HH_g_max.items():
            actual_g = g_max if seg_id == 0 else g_max * 0.2
            add_channel_to_segment(seg_id, ion, actual_g)
        # T-type 钙通道：soma 密度较高，树突区减半
        actual_P = P_CaT_density if seg_id == 0 else P_CaT_density * 0.5
        add_channel_to_segment(seg_id, "CaT", actual_P)

    # 阶段 5: 刺激条件边界约束
    soma_area_cm2 = np.pi * 20.0 * 20.0 * 1e-8
    stim_density = 15.0  # uA/cm2
    absolute_stim_uA = stim_density * soma_area_cm2

    insert_stimulation(
        stimulation_id="Stim_Soma",
        segment_id=0,
        stimulation_uA=absolute_stim_uA,
        stim_start=5.0,
        stim_duration=1.0
    )

    # 阶段 6: 执行隐式求解推进
    print("开始多区室隐式求解模拟 (含 T-type 钙通道)...")
    start_simulation()
    print("模拟完成，进入数据渲染流程。")

    # 阶段 7: 局部状态提取与可视化验证 — 膜电位
    plot_variable_over_time(segment_id=0, var_label='V', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=1, var_label='V', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=2, var_label='V', start_time_ms=0, end_time_ms=50.0)

    # 阶段 8: 钙离子浓度与 T-type 门控变量可视化
    plot_variable_over_time(segment_id=0, var_label='Ca', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=1, var_label='Ca', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=0, var_label='mT', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=0, var_label='hT', start_time_ms=0, end_time_ms=50.0)

    # 阶段 9: 导出钙历史矩阵并验证
    hCa, hMT, hHT = export_calcium_history_matrices()
    print(f"HISTORY_CA shape: {hCa.shape}")
    print(f"Soma Ca range: [{hCa[:, 0].min():.6e}, {hCa[:, 0].max():.6e}] mM")
    print(f"Soma mT range: [{hMT[:, 0].min():.6f}, {hMT[:, 0].max():.6f}]")
    print(f"Soma hT range: [{hHT[:, 0].min():.6f}, {hHT[:, 0].max():.6f}]")

    assert np.all(np.isfinite(hCa)), "钙浓度出现非有限值"
    assert np.all(hCa >= 0), "钙浓度出现负值"
    assert np.all(hMT >= -1e-10) and np.all(hMT <= 1.0 + 1e-10), "mT 门控变量越界"
    assert np.all(hHT >= -1e-10) and np.all(hHT <= 1.0 + 1e-10), "hT 门控变量越界"
    print("所有钙通道验证通过。")

if __name__ == "__main__":
    test_multi_compartment_simulation()