			**经典 Hodgkin-Huxley 方程与 Cable 方程**

​			Hodgkin-Huxley 方程把一小块神经元膜看作一个电容和若干个依赖电导组成的等效电路。
$$
C_m \frac{dV_m}{dt} = I_{ext} - (I_{Na} + I_K + I_L)
$$
​			而电缆方程则连续化表达。
$$
\frac{1}{2\pi a}
\frac{\partial}{\partial x}
\left(
\frac{\pi a^2}{R_a}\frac{\partial V}{\partial x}
\right)
=
C_m\frac{\partial V}{\partial t}+I_{ion}
$$

$$
\begin{aligned}
I_{Na} &= \bar{g}_{Na} m^3 h (V_m - E_{Na}) \\
I_K &= \bar{g}_K n^4 (V_m - E_K) \\
I_L &= \bar{g}_L (V_m - E_L)
\end{aligned}
$$

​			这里 \(\bar g\) 表示最大电导，\(E\) 表示离子的反转电位。

​			门控变量 \(m,h,n\) 描述通道开放失活比例，它们通常满足一阶动力学：
$$
\frac{dx}{dt} = \alpha_x(V_m)(1 - x) - \beta_x(V_m)x,\qquad x \in \{m,h,n\}
$$
​			在实际计算中，连续的树突或轴突会被离散成多个区室。

​			每个区室内部近似为等电位，相邻区室通过轴向电导相连。
$$
\begin{aligned}
C_i \frac{d V_i}{d t}
&=
- I_{ion,i}
+ I_{stim,i}
+ \sum_j g_{ij}(V_j - V_i)
\end{aligned}
$$




​			**Crank-Nicolson 方法与Hines方法**	

​			二阶方法的基本表达式为
$$
\frac{\partial V}{\partial t} \bigg|_{t+\Delta t/2} \approx \frac{V(t+\Delta t) - V(t)}{\Delta t} =  V'(t + \delta) +O(\delta^2)
$$
​			带入离子通道后得到离子通道表达式
$$
\frac{m(t+\frac{\Delta t}{2}) - m(t-\frac{\Delta t}{2})}{\Delta t} = \alpha_m(V(t)) - (\alpha_m(V(t)) + \beta_m(V(t))) \frac{m(t+\frac{\Delta t}{2}) + m(t-\frac{\Delta t}{2})}{2}
$$
​			经过移项就得到了工程上可实现的Hines方法的表达式
$$
m(t+\delta) = \left[ \frac{\alpha_m}{\frac{1}{2\delta} + \frac{1}{2}(\alpha_m + \beta_m)} \right] + \left[ \frac{\frac{1}{2\delta} - \frac{1}{2}(\alpha_m + \beta_m)}{\frac{1}{2\delta} + \frac{1}{2}(\alpha_m + \beta_m)} \right] m(t-\delta)
$$
​			对于电缆方程同样采取差分，采取一阶差分
$$
\frac{\partial}{\partial x}\left(\frac{\pi a^{2}}{R_{a}}\frac{\partial V}{\partial x}\right) \approx \frac{1}{\Delta x} \left[ \left(\frac{\pi a_{i+1/2}^2}{R_a} \frac{V_{i+1} - V_i}{\Delta x}\right) - \left(\frac{\pi a_{i-1/2}^2}{R_a} \frac{V_i - V_{i-1}}{\Delta x}\right) \right]
$$
​			将系数合并定义系数
$$
A_{i,i+1} = \frac{1}{2\pi a_i \Delta x}\frac{\pi a_{i+1/2}^2}{R_a \Delta x},A_{i,i-1} = \frac{1}{2\pi a_i \Delta x}\frac{\pi a_{i-1/2}^2}{R_a \Delta x}
$$
​			采取半步时间步展开得到的完整表达式，工程上可以有如下形式
$$
-A_{i,i-1}V_{i-1}(t+\delta) + \left(A_{i,i+1} + A_{i,i-1} + \frac{2C_m}{\delta} + g_{ion, i}\right) V_i(t+\delta) - A_{i,i+1}V_{i+1}(t+\delta) 
\\= \frac{C_i}{\delta} V_i(t) - g_{ion, i} E_{ion,i}
$$
‘

​			**CaT系统**

​			钙离子系统稍微复杂，需要考虑GHK方程和钙离子泵系统
$$
G(V, Ca_o, Ca_i) = Z^2 \frac{F^2 V}{R T} \frac{[Ca]_i - [Ca]_o \exp(-ZFV/RT)}{1 - \exp(-ZFV/RT)}
$$

$$
其中 Z=2 为化合价，F 为法拉第常数，R 为气体常数，T 为绝对温度
$$

$$
I_{T,i} = \bar{P}_{Ca,i} {m_{T,i}}^2 h_{T,i} \cdot G(V_{i}, Ca_o, Ca_i)\\
$$

$$
\tau_m(V) = 0.204 + \frac{0.333}{\exp[-(V+131)/16.7] + \exp[(V+15.8)/18.2]}
$$

$$
当 V < -81 mV 时：\tau_h(V) = 0.333 \exp[(V+466)/66.6] \\当 V > -81 mV 时：\tau_h(V) = 9.32 + 0.333 \exp[-(V+21)/10.5]
$$

​			此外钙离子还有钙泵，也是一个一阶系统描述钙离子行为
$$
\frac{d[Ca]_i}{dt} = max(−γCa​I_{T,i}​,0) + \frac{Ca_{\infty} - Ca_i}{\tau_{Ca}}
$$
​			需要得到钙的电导，需要对于电压电流求偏导
$$
I(V_{n+1}) \approx I(V_n) + \frac{\partial I}{\partial V}\bigg|_{V_n} \cdot (V_{n+1} - V_n)\\
g_{Ca\_eq} = \frac{\partial I}{\partial V} = A \cdot k \cdot \frac{\Big( [Ca]_i e^z (1+z) - [Ca]_o \Big) (e^z - 1) - \Big( z [Ca]_i e^z - z [Ca]_o \Big) e^z}{(e^z - 1)^2}
$$

$$
I(V_{n+1}) \approx g_{Ca\_eq} \cdot V_{n+1} + \underbrace{\left( I(V_n) - g_{Ca\_eq} \cdot V_n \right)}_{\text{常数截距}}\\
$$

​			**组装方程组与动力学分析**
$$
\begin{bmatrix}
M_{1,1} & M_{1,2} & 0 & 0 & \dots & 0 \\
M_{2,1} & M_{2,2} & M_{2,3} & 0 & \dots & 0 \\
0 & M_{3,2} & M_{3,3} & M_{3,4} & \dots & 0 \\
0 & 0 & \ddots & \ddots & \ddots & \vdots \\
\vdots & \vdots & \ddots & M_{N-1,N-2} & M_{N-1,N-1} & M_{N-1,N} \\
0 & 0 & \dots & 0 & M_{N,N-1} & M_{N,N}
\end{bmatrix}
\begin{bmatrix}
V_1(t+\Delta t/2) \\
V_2(t+\Delta t/2) \\
V_3(t+\Delta t/2) \\
\vdots \\
V_{N-1}(t+\Delta t/2) \\
V_N(t+\Delta t/2)
\end{bmatrix}
=
\begin{bmatrix}
b_1 \\
b_2 \\
b_3 \\
\vdots \\
b_{N-1} \\
b_N
\end{bmatrix}
$$

$$
下对角线元素 (连接左侧邻居): M_{i, i-1} = -A_{i, i-1}\\
主对角线元素 (节点自身的属性): M_{i, i} = \frac{2C_m}{\Delta t} + A_{i, i-1} + A_{i, i+1} + g_{ion, i} + g_{Ca\_eq}\\
上对角线元素 (连接右侧邻居): M_{i, i+1} = -A_{i, i+1}\\
等号右侧常数项 (驱动力和历史记忆): b_i = \frac{2C_m}{\Delta t} V_i(t) + g_{ion, i} E_{x}+{\left( -I(V_n) + g_{Ca\_eq} \cdot V_n \right)}\\
$$

​			相图分析时，从状态变量中选择两个变量，其余变量固定为指定时刻的历史值，得到二维约化系统
$$
x,y\in\{V,m,h,n,Ca,m_T,h_T\},\qquad x\ne y,
$$
$$
\mathcal N_x
=
\{(x,y)\mid \dot x=f(x,y;t_n)=0\},
\qquad
\mathcal N_y
=
\{(x,y)\mid \dot y=g(x,y;t_n)=0\}.
$$

​			计算雅可比矩阵采取中心差分方法
$$
\frac{\partial f}{\partial x}
\approx
\frac{f(x+\varepsilon_x,y)-f(x-\varepsilon_x,y)}
{2\varepsilon_x},
$$

$$
J
=
\begin{bmatrix}
\partial f/\partial x & \partial f/\partial y\\
\partial g/\partial x & \partial g/\partial y
\end{bmatrix}_{(x,y)}.
$$

$$
\lambda^2-\operatorname{tr}(J)\lambda+\det(J)=0.
$$

