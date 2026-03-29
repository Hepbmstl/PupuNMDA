from Hines_method import (
    set_env, init_segment, add_connection, add_channel_to_segment,
    insert_stimulation, start_simulation, plot_variable_over_time, clear_environment,
    export_calcium_history_matrices
)
import numpy as np

def test_multi_compartment_simulation():
    # Stage 1: Initialize environment and state matrices
    clear_environment()
    # Use 3 compartments, simulate a 50 ms span
    set_env(V_init=-65.0, dt=0.02, steps=2500, n_node=3)

    # Stage 2: Set spatial geometry parameters and derive absolute states
    init_segment(uid="Soma",  Ra=100.0, D=20.0, L=20.0, Cm=1.0, id=0)
    init_segment(uid="Dend1", Ra=100.0, D=5.0,  L=50.0, Cm=1.0, id=1)
    init_segment(uid="Dend2", Ra=100.0, D=2.0,  L=50.0, Cm=1.0, id=2)

    # Stage 3: Topology mapping (Soma <-> Dend1 <-> Dend2)
    add_connection(0, 1)
    add_connection(1, 0)
    add_connection(1, 2)
    add_connection(2, 1)

    # Stage 4: Load membrane conductance densities (units: mS/cm2) + T-type Ca channel
    HH_g_max = {"Na": 120.0, "K": 36.0, "L": 0.3}
    P_CaT_density = 1e-5  # T-type Ca channel permeability density (cm/s)

    for seg_id in range(3):
        for ion, g_max in HH_g_max.items():
            actual_g = g_max if seg_id == 0 else g_max * 0.2
            add_channel_to_segment(seg_id, ion, actual_g)
        # T-type Ca channel: higher density in soma, halved in dendrites
        actual_P = P_CaT_density if seg_id == 0 else P_CaT_density * 0.5
        add_channel_to_segment(seg_id, "CaT", actual_P)

    # Stage 5: Stimulation boundary conditions
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

    # Stage 6: Run implicit solver progression
    print("Starting multi-compartment implicit solver simulation (including T-type Ca channels)...")
    start_simulation()
    print("Simulation completed, proceeding to data rendering.")

    # Stage 7: Extract local states and visualize — membrane potential
    plot_variable_over_time(segment_id=0, var_label='V', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=1, var_label='V', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=2, var_label='V', start_time_ms=0, end_time_ms=50.0)

    # Stage 8: Visualize calcium concentration and T-type gating variables
    plot_variable_over_time(segment_id=0, var_label='Ca', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=1, var_label='Ca', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=0, var_label='mT', start_time_ms=0, end_time_ms=50.0)
    plot_variable_over_time(segment_id=0, var_label='hT', start_time_ms=0, end_time_ms=50.0)

    # Stage 9: Export calcium history matrices and validate
    hCa, hMT, hHT = export_calcium_history_matrices()
    print(f"HISTORY_CA shape: {hCa.shape}")
    print(f"Soma Ca range: [{hCa[:, 0].min():.6e}, {hCa[:, 0].max():.6e}] mM")
    print(f"Soma mT range: [{hMT[:, 0].min():.6f}, {hMT[:, 0].max():.6f}]")
    print(f"Soma hT range: [{hHT[:, 0].min():.6f}, {hHT[:, 0].max():.6f}]")

    assert np.all(np.isfinite(hCa)), "Non-finite calcium concentration detected"
    assert np.all(hCa >= 0), "Negative calcium concentration detected"
    assert np.all(hMT >= -1e-10) and np.all(hMT <= 1.0 + 1e-10), "mT gating variable out of bounds"
    assert np.all(hHT >= -1e-10) and np.all(hHT <= 1.0 + 1e-10), "hT gating variable out of bounds"
    print("All calcium channel checks passed.")

if __name__ == "__main__":
    test_multi_compartment_simulation()