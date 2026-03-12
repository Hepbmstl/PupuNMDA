from Hines_method import (
    set_env, init_segment, add_connection, add_channel_to_segment,
    insert_stimulation, start_simulation, plot_variable_over_time, clear_environment
)
import numpy as np

def test_multi_compartment_simulation():
    # 阶段 1: 运行环境与状态矩阵初始化
    clear_environment()
    # 使用 3 个区室，模拟 50ms 跨度
    set_env(V_init=-65.0, dt=0.02, steps=2500, n_node=3)

    # 阶段 2: 空间几何参数设定与绝对状态推导
    # Soma: 直径 20um, 长度 20um (表面积约 1.256e-5 cm2)
    init_segment(uid="Soma",  Ra=100.0, D=20.0, L=20.0, Cm=1.0, id=0)
    # Dend1: 直径 5um, 长度 50um
    init_segment(uid="Dend1", Ra=100.0, D=5.0,  L=50.0, Cm=1.0, id=1)
    # Dend2: 直径 2um, 长度 50um
    init_segment(uid="Dend2", Ra=100.0, D=2.0,  L=50.0, Cm=1.0, id=2)

    # 阶段 3: 拓扑结构矩阵映射 (Soma <-> Dend1 <-> Dend2)
    # 注意：这里直接传入目标区室的 int 类型的 id
    add_connection(0, 1)
    add_connection(1, 0)
    add_connection(1, 2)
    add_connection(2, 1)

    # 阶段 4: 膜电导密度装载 (单位: mS/cm2)
    # 内部将通过表面积自动放缩为绝对电导 (mS)
    HH_g_max = {"Na": 120.0, "K": 36.0, "L": 0.3}
    for seg_id in range(3):
        for ion, g_max in HH_g_max.items():
            # 为演示传播衰减，树突区的钠通道密度可适当降低
            actual_g = g_max if seg_id == 0 else g_max * 0.2
            add_channel_to_segment(seg_id, ion, actual_g)

    # 阶段 5: 刺激条件边界约束
    # 计算生理强度的刺激电流: 假设需要 15 uA/cm2 的电流密度
    soma_area_cm2 = np.pi * 20.0 * 20.0 * 1e-8
    stim_density = 15.0 # uA/cm2
    absolute_stim_uA = stim_density * soma_area_cm2 # 约 0.000188 uA
    
    # 将该绝对电流注入到 Soma (id=0) 中，持续 1ms
    insert_stimulation(
        stimulation_id="Stim_Soma", 
        segment_id=0, 
        stimulation_uA=absolute_stim_uA, 
        stim_start=5.0, 
        stim_duration=1.0
    )

    # 阶段 6: 执行隐式求解推进
    print("开始多区室隐式求解模拟...")
    start_simulation()
    print("模拟完成，进入数据渲染流程。")

    # 阶段 7: 局部状态提取与可视化验证
    plot_variable_over_time(segment_id=0, var_label='V', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=1, var_label='V', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=2, var_label='V', start_time_ms=0, end_time_ms=50.0)

if __name__ == "__main__":
    test_multi_compartment_simulation()