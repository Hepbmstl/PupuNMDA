import numpy as np
import matplotlib.pyplot as plt
import matplotlib.animation as animation

E_TABLE = {}
G_TABLE = {}
def set_E(table: dict):
    global E_TABLE
    E_TABLE = table


def alpha_m(V): return np.where(np.abs(V + 35.0) < 1e-6, 1.0, (0.1 * (V + 35))/(1 - np.exp(-((V+35)/10))))
def alpha_n(V): return np.where(np.abs(V + 50.0) < 1e-6, 0.1, (0.01 * (V + 50))/(1 - np.exp(-((V+50)/10))))
def beta_m(V):  return 4 * np.exp(-(V+60)/18)
def alpha_h(V): return 0.07 * np.exp(-(V+60)/20)
def beta_h(V):  return 1/(1 + np.exp(-((V+30)/10)))
def beta_n(V):  return 0.125 * np.exp(-(V+60)/80)


# 状态导数计算
def get_derivatives(V_grid, n_grid, m_scalar, h_scalar, C_m, I_ext=0.0):

    g_Na = G_TABLE.get('Na', 120.0)
    g_K = G_TABLE.get('K',  36.0)
    g_L = G_TABLE.get('L',  0.3)

    E_Na = E_TABLE.get('Na', 50.0)
    E_K  = E_TABLE.get('K', -77.0)
    E_L  = E_TABLE.get('L', -54.387)

    # 计算跨膜电流
    I_Na = g_Na * (m_scalar**3) * h_scalar * (V_grid - E_Na)
    I_K  = g_K  * (n_grid**4) * (V_grid - E_K)
    I_L  = g_L  * (V_grid - E_L)

    dV_dt = (I_ext - I_Na - I_K - I_L) / C_m
    dn_dt = alpha_n(V_grid) * (1 - n_grid) - beta_n(V_grid) * n_grid
    
    return dV_dt, dn_dt

# ==========================================
# 2. 数据处理与场预计算
# ==========================================
def extract_nullclines(X, Y, Z):
    """
    内部过程：利用 contour 计算 Z=0 的等值线，提取线段顶点集合。
    输出格式：线段列表，每个元素为 shape (N, 2) 的 numpy 数组。
    """
    # 临时创建一个不显示的 figure 以计算 contour
    fig, ax = plt.subplots()
    contour = ax.contour(X, Y, Z, levels=[0.0])
    plt.close(fig)
    
    paths = []
    for collection in contour.collections:
        for path in collection.get_paths():
            paths.append(path.vertices)
    return paths

def precompute_phase_space(probe_list, segement_data, v_range=(-80, 40), n_range=(0, 1), grid_res=20):
    """
    核心数据流转换：将时间序列探针数据转化为渲染所需的帧序列数据。
    选择坐标轴：X = V (膜电位), Y = n (钾离子通道开放率)
    """
    N_frames = len(probe_list)

    G_TABLE['Na'] = segement_data.get('Na', 120.0)
    G_TABLE['K'] = segement_data.get('K', 120.0)
    G_TABLE['L'] = segement_data.get('L', 120.0)
    C_m = segement_data.get('Cm')
    
    # 1. 初始化空间网格
    V_coords = np.linspace(v_range[0], v_range[1], grid_res)
    N_coords = np.linspace(n_range[0], n_range[1], grid_res)
    V_grid, N_grid = np.meshgrid(V_coords, N_coords)
    
    # 2. 预分配存储空间
    U_frames = np.zeros((N_frames, grid_res, grid_res))
    V_frames = np.zeros((N_frames, grid_res, grid_res))
    
    traj_V = np.zeros(N_frames)
    traj_n = np.zeros(N_frames)
    
    v_nullclines = [] # dV/dt = 0
    n_nullclines = [] # dn/dt = 0

    # 3. 遍历时间步，计算当前帧的物理状态
    for i, probe in enumerate(probe_list):
        # 提取当前步的标量数据
        v_current = probe["V"]
        m_current = probe["m"]
        h_current = probe["h"]
        n_current = probe["n"]
        
        # 记录轨迹节点
        traj_V[i] = v_current
        traj_n[i] = n_current
        
        # 计算该时刻相空间切片上的导数场
        dV_grid, dn_dt_grid = get_derivatives(V_grid, N_grid, m_current, h_current, C_m)
        
        U_frames[i] = dV_grid
        V_frames[i] = dn_dt_grid
        
        # 提取该时刻的零倾线坐标集合
        v_nullclines.append(extract_nullclines(V_grid, N_grid, dV_grid))
        n_nullclines.append(extract_nullclines(V_grid, N_grid, dn_dt_grid))

    return {
        "V_grid": V_grid, "N_grid": N_grid,
        "U": U_frames, "V": V_frames,
        "traj_V": traj_V, "traj_n": traj_n,
        "null_V": v_nullclines, "null_n": n_nullclines,
        "N_frames": N_frames
    }

# ==========================================
# 3. 渲染输出
# ==========================================
def render_animation(anim_data):
    fig, ax = plt.subplots(figsize=(8, 6))
    ax.set_xlim(anim_data["V_grid"].min(), anim_data["V_grid"].max())
    ax.set_ylim(anim_data["N_grid"].min(), anim_data["N_grid"].max())
    ax.set_xlabel('Membrane Potential V (mV)')
    ax.set_ylabel('Gating Variable n')
    ax.set_title('Phase Space Evolution (V vs n)')
    
    # 初始化绘图对象状态
    # 1. 向量场
    Q = ax.quiver(anim_data["V_grid"], anim_data["N_grid"], 
                  anim_data["U"][0], anim_data["V"][0], color='gray', alpha=0.6)
    
    # 2. 轨迹线与当前点
    traj_line, = ax.plot([], [], 'g-', linewidth=2, label="Trajectory")
    current_pt, = ax.plot([], [], 'ro', markersize=6)
    
    # 3. 零倾线集合 (由于零倾线可能有多段，需要维护对象列表)
    v_lines_artists = []
    n_lines_artists = []
    
    def update(frame):
        # 状态修改: 向量场
        Q.set_UVC(anim_data["U"][frame], anim_data["V"][frame])
        
        # 状态修改: 轨迹截取
        traj_line.set_data(anim_data["traj_V"][:frame+1], anim_data["traj_n"][:frame+1])
        current_pt.set_data([anim_data["traj_V"][frame]], [anim_data["traj_n"][frame]])
        
        # 状态修改: 清理并重绘本帧的零倾线
        nonlocal v_lines_artists, n_lines_artists
        for line in v_lines_artists + n_lines_artists:
            line.remove()
        v_lines_artists.clear()
        n_lines_artists.clear()
        
        # 重新实例化当前帧的线段
        for path in anim_data["null_V"][frame]:
            line, = ax.plot(path[:, 0], path[:, 1], 'r--', linewidth=1.5, alpha=0.7)
            v_lines_artists.append(line)
            
        for path in anim_data["null_n"][frame]:
            line, = ax.plot(path[:, 0], path[:, 1], 'b--', linewidth=1.5, alpha=0.7)
            n_lines_artists.append(line)
            
        return [Q, traj_line, current_pt] + v_lines_artists + n_lines_artists

    ani = animation.FuncAnimation(fig, update, frames=anim_data["N_frames"], 
                                  interval=50, blit=False) # 零倾线数量变化导致 blit 极易出错，此处设定为 False
    plt.legend(['Trajectory', 'Current State', 'V-nullcline', 'n-nullcline'], loc='upper left')
    plt.show()

# ==========================================
# 4. 执行入口 (示例化调用)
# ==========================================
if __name__ == "__main__":
    
    #set_E()
    #c_sharp_probe_list = [] 
    #segement_data = {}
    
    processed_data = precompute_phase_space(c_sharp_probe_list, segement_data)
    render_animation(processed_data)