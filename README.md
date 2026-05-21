[PupuNMDA.md](https://github.com/user-attachments/files/28105901/PupuNMDA.md)
# 关于PupuNMDA

[TOC]

## 1. 平台简介
PupuNMDA 是集合了多区室神经元建模、仿真与动力学分析的计算软件。

<img width="1920" height="1180" alt="nmda-molecule-structure" src="https://github.com/user-attachments/assets/84697902-323e-45df-ad94-fcff402d0c7e" />


------

## 2. 核心特性

- **极其卡顿的可视化建模**：基于 WPF 与 Helix Toolkit 打造的三维形态展示与交互界面。

- **几乎没有扩展性**：支持自定义离子通道动力学参数与区室属性。

- **不太精确的拓扑结构**：为用户提供空间参考，并支持从各个方向移动或者旋转模块。

  

<img width="480" height="48" alt="neuroncad-splash-animation-original" src="https://github.com/user-attachments/assets/8a541b87-2a59-422f-8a03-67fa8c02b1a1" />


  

------

## 3. 生物物理模型与数值方法概览

PupuNMDA 的计算核心可以分成三层：单个膜片上的 Hodgkin-Huxley 离子通道模型，连接多个膜片或区室的电缆方程，以及用于稳定推进大规模多区室系统的 Crank-Nicolson 与 Hines 方法。第三部分只保留主线公式和建模直觉；更完整的推导放在附录 A 中。

### 3.1 经典 Hodgkin-Huxley (HH) 方程

Hodgkin-Huxley 方程把一小块神经元膜看作一个电容和若干个电压依赖电导组成的等效电路。膜电位的变化来自外部刺激电流与钠、钾、漏电流之间的平衡：

$$
C_m \frac{dV_m}{dt} = I_{ext} - (I_{Na} + I_K + I_L)
$$

其中 \(C_m\) 为单位面积膜电容，\(I_{ext}\) 为外部注入电流，\(I_{Na}\)、\(I_K\)、\(I_L\) 分别为钠电流、钾电流和漏电流。

<img width="852" height="467" alt="hodgkin-huxley-ion-channel-currents" src="https://github.com/user-attachments/assets/5ca3572b-0364-4b1a-9dcc-5e381243f113" />


(Hodgkin et al., J. Physiol., 1952)

图中可以看到 HH 模型的两个关键思想：第一，跨膜电流可以写成电导乘以驱动力；第二，电导不是常数，而是由门控变量控制。经典形式为：

$$
\begin{aligned}
I_{Na} &= \bar{g}_{Na} m^3 h (V_m - E_{Na}) \\
I_K &= \bar{g}_K n^4 (V_m - E_K) \\
I_L &= \bar{g}_L (V_m - E_L)
\end{aligned}
$$

这里 \(\bar g\) 表示最大电导，\(E\) 表示对应离子的反转电位。门控变量 \(m,h,n\) 描述通道开放或失活的比例，它们通常满足一阶动力学：

$$
\frac{dx}{dt} = \alpha_x(V_m)(1 - x) - \beta_x(V_m)x,\qquad x \in \{m,h,n\}
$$

因此，HH 方程不是单纯的电路方程，而是“膜电压”和“通道门控”相互耦合的动力学系统。电压改变门控速率，门控改变电导，电导再反过来改变电压。

### 3.2 电缆方程与区室模型

HH 方程描述的是一个局部膜片。如果要描述完整神经元，还需要考虑轴突、树突和胞体之间的轴向电流。电缆方程正是用来描述膜电位沿空间传播和衰减的方程：

$$
\frac{1}{2\pi a}
\frac{\partial}{\partial x}
\left(
\frac{\pi a^2}{R_a}\frac{\partial V}{\partial x}
\right)
=
C_m\frac{\partial V}{\partial t}+I_{ion}
$$

其中 \(a\) 是分支半径，\(R_a\) 是轴向电阻率，\(C_m\) 是比膜电容，\(I_{ion}\) 表示跨膜离子电流。左侧对应轴向电流的空间变化，右侧对应膜电容电流和离子通道电流。

<img width="782" height="487" alt="multicompartment-cable-discretization" src="https://github.com/user-attachments/assets/44009a27-b0c1-404f-9dd3-0e30e8363f3e" />


(Beaubois et al., Front. Neurosci., 2024)

在实际计算中，连续的树突或轴突会被离散成多个区室。每个区室内部近似为等电位，相邻区室通过轴向电导相连。离散以后，单个区室的膜电压可以写成：

$$
\begin{aligned}
C_i \frac{d V_i}{d t}
&=
- I_{ion,i}
+ I_{stim,i}
+ \sum_j g_{ij}(V_j - V_i)
\end{aligned}
$$

其中 \(\sum_j g_{ij}(V_j - V_i)\) 表示来自相邻区室的轴向电流。这样，局部的 HH 模型就被嵌入到了完整形态结构中，每个区室既有自己的离子通道，也会受到相邻区室电压的影响。

### 3.3 Crank-Nicolson 方法

多区室神经元模型通常是刚性的：离子通道、膜电容和轴向耦合同时存在，显式方法需要很小的时间步长才能稳定。Crank-Nicolson 方法使用时间步中点的斜率推进系统，兼顾稳定性与二阶时间精度：

$$
\frac{\partial V}{\partial t}\bigg|_{t+\Delta t/2}
\approx
\frac{V(t+\Delta t)-V(t)}{\Delta t}
+ O(\Delta t^2)
$$

对简单的线性膜方程

$$
C_m\frac{dV}{dt}=-gV+I
$$

Crank-Nicolson 离散为：

$$
C_m \frac{V^{n+1}-V^n}{\Delta t}
=
-g\frac{V^{n+1}+V^n}{2}
+ I^{n+1/2}
$$

整理后得到：

$$
\left(C_m+\frac{g\Delta t}{2}\right)V^{n+1}
=
\left(C_m-\frac{g\Delta t}{2}\right)V^n
+ I^{n+1/2}\Delta t
$$

这类半隐式形式适合电缆方程，因为轴向耦合项可以被组织成线性方程组，从而一次求出所有区室的新电压。

<img width="558" height="895" alt="time-integration-euler-error-comparison" src="https://github.com/user-attachments/assets/9874caba-1cfc-4305-8379-6297d612245c" />


(Hines et al., Neural Computation, 1997)

### 3.4 Hines 方法

Hines 方法可以理解为专门针对树状神经元结构优化的隐式电缆方程求解方法。离散后的电缆方程会形成一个稀疏线性系统：

$$
M V^{n+1/2} = b
$$

对于一条一维电缆，矩阵近似为三对角矩阵：

$$
\begin{bmatrix}
M_{1,1} & M_{1,2} & 0 & \dots & 0 \\
M_{2,1} & M_{2,2} & M_{2,3} & \dots & 0 \\
0 & M_{3,2} & M_{3,3} & \dots & 0 \\
\vdots & \vdots & \vdots & \ddots & M_{N-1,N} \\
0 & 0 & 0 & M_{N,N-1} & M_{N,N}
\end{bmatrix}
\begin{bmatrix}
V_1 \\ V_2 \\ V_3 \\ \vdots \\ V_N
\end{bmatrix}
=
\begin{bmatrix}
b_1 \\ b_2 \\ b_3 \\ \vdots \\ b_N
\end{bmatrix}
$$

对于真实神经元的分支结构，矩阵不是普通三对角，但如果按照树结构重新排序，就可以保持类似三对角消元的高效性。Hines 方法的核心价值就在于：它利用神经元形态本身的树状拓扑，把多区室隐式求解从一般稠密矩阵问题变成高度结构化的稀疏问题。

在工程实现中，离子通道门控通常先在半步上更新，再把通道电导视为当前时间步内的已知量，组装进电压方程。例如对门控变量 \(m\)：

$$
m(t+\delta)
=
\left[
\frac{\alpha_m}
{\frac{1}{2\delta}+\frac{1}{2}(\alpha_m+\beta_m)}
\right]
+
\left[
\frac{\frac{1}{2\delta}-\frac{1}{2}(\alpha_m+\beta_m)}
{\frac{1}{2\delta}+\frac{1}{2}(\alpha_m+\beta_m)}
\right]
m(t-\delta)
$$

这样做避免了每一个时间步都对完整非线性系统做牛顿迭代，同时保留了 Crank-Nicolson 半步格式的主要优点。

------

## 4. 应用价值与结果复现

神经元模拟器的意义不仅在于模拟正常放电，还在于帮助解释神经元如何从正常活动进入异常活动状态。PupuNMDA 当前的应用展示主要分为两类：一类是复现 Destexhe 等人关于丘脑皮层神经元放电的结果，另一类是复现和观察动力学相图中的轨迹、零流线与局部稳定性结构。

### 4.1 Destexhe 放电结果复现

Destexhe 等人的丘脑皮层神经元模型展示了 T 型钙电流在放电模式中的作用。正常条件下，在给定方波电流刺激下，TC 神经元可以表现出动作电位和由钙离子动力学带来的慢性电压隆起。

<img width="486" height="659" alt="destexhe-tc-neuron-t-current-response" src="https://github.com/user-attachments/assets/99b8b93a-729e-45d5-8ef5-8cdaf75736e6" />


​                                                                      (Destexhe et al., J. Neurosci., 1998)

按照相同思路设置 soma 与 dendrite 区室参数后，PupuNMDA 可以得到对应的胞体电压响应。该结果用于验证模型的基本电生理行为是否和经典 TC 神经元模型保持一致。

<img width="1000" height="500" alt="tc-soma-voltage-trace-reduced-cat" src="https://github.com/user-attachments/assets/6455fae5-e3c9-44ac-be48-f24a97682450" />


### 4.2 动力学复现

除了直接复现电压轨迹，PupuNMDA 还可以从仿真轨迹中抽取状态变量，绘制局部二维相图、向量场和零流线，用于观察不同变量之间的瞬时动力学关系。

<img width="2987" height="2401" alt="fast-slow-ultraslow-neuron-phase-portrait" src="https://github.com/user-attachments/assets/db81d8c2-aa87-4a8a-9200-857a6141c823" />


​                                                           (Jacquerie et al., Computational Biology, 2021)

PupuNMDA 的动力学复现并不是重新构造一个完整的二维降维模型，而是在完整多区室、多状态变量仿真结果的某个时间点，对指定两个变量做局部切片。因此它更适合作为诊断工具：观察某一时刻、某一区室、某两个变量之间的瞬时反馈关系。

<img width="1050" height="686" alt="tc-dendrite-high-cat-v-n-phase-portrait" src="https://github.com/user-attachments/assets/43b37ee9-c394-460d-bf9a-03e265b82f47" />


<img width="1000" height="500" alt="tc-dendrite-high-cat-voltage-trace" src="https://github.com/user-attachments/assets/b6fe208f-d9fa-4442-a662-1f3657478a27" />


这类图可以辅助解释 CaT 电流、门控变量和膜电位之间的相互作用，但不能直接等同于文献中通过快慢分解得到的全局相平面。更详细的动力学切片解释见附录 A.5。

------

## 附录 A. 详细推导

### A.1 Hodgkin-Huxley 方程与电缆方程补充

HH 方程将神经元的一个小区域抽象成均匀区块。这个区块上分布有许多离子通道，经典 HH 方程描述钠通道、钾通道和漏通道。每一个通道都通过门控变量描述开放或关闭行为，门控变化引起电流变化，电流变化进一步改变膜电压。

$$
\begin{aligned}
I_{Na} &= \bar{g}_{Na} m^3 h (V_m - E_{Na}) \\
I_K &= \bar{g}_K n^4 (V_m - E_K) \\
I_L &= \bar{g}_L (V_m - E_L)
\end{aligned}
$$

门控变量满足：

$$
\frac{dx}{dt} = \alpha_x(V_m)(1 - x) - \beta_x(V_m)x,\qquad x \in \{m,h,n\}
$$

其中 \(\alpha_x\) 和 \(\beta_x\) 是依赖膜电位 \(V_m\) 的经验函数。也可以用玻尔兹曼形式描述门控转移速率：

$$
\begin{aligned}
\alpha_x(V_m) &= A_x \exp\left( \frac{z_x \gamma F V_m}{RT} \right) \\
\beta_x(V_m) &= B_x \exp\left( -\frac{z_x(1-\gamma)F V_m}{RT} \right)
\end{aligned}
$$

其中 \(A_x\) 和 \(B_x\) 是基础速率常数，\(z_x\) 表示门控粒子的等效电荷数，\(\gamma\) 为非对称因子，\(F\) 为法拉第常数，\(R\) 为理想气体常数，\(T\) 为绝对温度。

对完整神经元而言，还需要把局部 HH 方程嵌入空间结构。经典一维分支电缆方程为：

$$
\frac{1}{2\pi a}\frac{\partial}{\partial x}
\left(\frac{\pi a^{2}}{R_a}\frac{\partial V}{\partial x}\right)
= C_m\frac{\partial V}{\partial t}+I_{HH}
$$

分支点处需要满足轴向电流守恒：

$$
\sum_{\text{branches}}
\pm
\frac{\pi a^2}{R_a}\frac{\partial V}{\partial x}
=
C_m\frac{\partial V}{\partial t}+I_{HH}
$$

离散成区室后，可以得到带有轴向耦合项的 HH 方程：

$$
\begin{aligned}
C \frac{d V_i}{d t}
&=
-\bar{g}_{Na}m_i^3h_i(V_i-E_{Na})
-\bar{g}_{K}n_i^4(V_i-E_K) \\
&\quad
-\bar{g}_{leak}(V_i-E_{leak})
+ I_{stim,i}
+ D\sum_j\epsilon_{ij}(V_j-V_i)
\end{aligned}
$$

### A.2 Crank-Nicolson 时间离散推导

设 \(\delta=\Delta t/2\)。对中点前后做泰勒展开：

$$
V(t+\delta)
=
V(t)+\delta V'(t)+\frac{\delta^2}{2!}V''(t)
+\frac{\delta^3}{3!}V'''(t)+O(\delta^4)
$$

$$
V(t-\delta)
=
V(t)-\delta V'(t)+\frac{\delta^2}{2!}V''(t)
-\frac{\delta^3}{3!}V'''(t)+O(\delta^4)
$$

两式相减可得：

$$
\frac{V(t+\delta)-V(t-\delta)}{2\delta}
=
V'(t)+O(\delta^2)
$$

因此：

$$
\frac{\partial V}{\partial t}\bigg|_{t+\Delta t/2}
\approx
\frac{V(t+\Delta t)-V(t)}{\Delta t}
=
V'(t+\delta)+O(\delta^2)
$$

对简单模型 \(C_m dV/dt=-gV+I\)，Crank-Nicolson 方法给出：

$$
C_m\frac{V(t+\Delta t)-V(t)}{\Delta t}
=
-g\frac{V(t)+V(t+\Delta t)}{2}
+I(t+\Delta t/2)
$$

整理得到：

$$
\left(C_m+\frac{g\Delta t}{2}\right)V(t+\Delta t)
=
\left(C_m-\frac{g\Delta t}{2}\right)V(t)
+I(t+\Delta t/2)\Delta t
$$

与全隐式欧拉法相比，Crank-Nicolson 在时间上达到二阶精度。全隐式欧拉为：

$$
C_m\frac{V(t+\Delta t)-V(t)}{\Delta t}
=
-gV(t+\Delta t)+I(t+\Delta t)
$$

$$
V(t+\Delta t)
=
\frac{C_mV(t)+I(t+\Delta t)\Delta t}
{C_m+g\Delta t}
$$

### A.3 电缆方程离散与 Hines 方程组

对电缆方程中的空间项做有限差分：

$$
\frac{\partial}{\partial x}
\left(
\frac{\pi a^2}{R_a}\frac{\partial V}{\partial x}
\right)
\approx
\frac{1}{\Delta x}
\left[
\left(
\frac{\pi a_{i+1/2}^2}{R_a}
\frac{V_{i+1}-V_i}{\Delta x}
\right)
-
\left(
\frac{\pi a_{i-1/2}^2}{R_a}
\frac{V_i-V_{i-1}}{\Delta x}
\right)
\right]
$$

定义耦合系数：

$$
A_{i,i+1}
=
\frac{1}{2\pi a_i\Delta x}
\frac{\pi a_{i+1/2}^2}{R_a\Delta x},
\qquad
A_{i,i-1}
=
\frac{1}{2\pi a_i\Delta x}
\frac{\pi a_{i-1/2}^2}{R_a\Delta x}
$$

则空间耦合项可写成：

$$
A_{i,i+1}V_{i+1}
-
(A_{i,i+1}+A_{i,i-1})V_i
+
A_{i,i-1}V_{i-1}
$$

采用半步时间格式后：

$$
\begin{aligned}
&A_{i,i+1}V_{i+1}(t+\frac{\Delta t}{2})
-(A_{i,i+1}+A_{i,i-1})V_i(t+\frac{\Delta t}{2})
+A_{i,i-1}V_{i-1}(t+\frac{\Delta t}{2}) \\
&=
\frac{2C_m}{\Delta t}
\left[
V_i(t+\frac{\Delta t}{2})-V_i(t)
\right]
+
I_{HH}(t+\frac{\Delta t}{2})
\end{aligned}
$$

门控变量也采用中心差分：

$$
\frac{dm}{dt}\bigg|_t
\approx
\frac{
m(t+\frac{\Delta t}{2})-m(t-\frac{\Delta t}{2})
}{\Delta t}
$$

且：

$$
m(t)
\approx
\frac{
m(t+\frac{\Delta t}{2})+m(t-\frac{\Delta t}{2})
}{2}
$$

将门控微分方程写为：

$$
\frac{dm}{dt}
=
\alpha_m(V(t))
-
[\alpha_m(V(t))+\beta_m(V(t))]m
$$

代入中心差分：

$$
\frac{m(t+\frac{\Delta t}{2})-m(t-\frac{\Delta t}{2})}{\Delta t}
=
\alpha_m(V(t))
-
(\alpha_m+\beta_m)
\frac{m(t+\frac{\Delta t}{2})+m(t-\frac{\Delta t}{2})}{2}
$$

移项得到：

$$
\left[
\frac{1}{\Delta t}
+\frac{1}{2}(\alpha_m+\beta_m)
\right]
m(t+\Delta t/2)
=
\alpha_m
+
\left[
\frac{1}{\Delta t}
-\frac{1}{2}(\alpha_m+\beta_m)
\right]
m(t-\Delta t/2)
$$

因此工程上可以写为：

$$
m(t+\delta)
=
\left[
\frac{\alpha_m}
{\frac{1}{2\delta}+\frac{1}{2}(\alpha_m+\beta_m)}
\right]
+
\left[
\frac{\frac{1}{2\delta}-\frac{1}{2}(\alpha_m+\beta_m)}
{\frac{1}{2\delta}+\frac{1}{2}(\alpha_m+\beta_m)}
\right]
m(t-\delta)
$$

以 HH 电流的一部分为例：

$$
I_{HH,i}(t+\frac{\Delta t}{2})
=
\bar g_{Na,i}m_i^3(t+\frac{\Delta t}{2})
h_i(t+\frac{\Delta t}{2})
\left[
V_i(t+\frac{\Delta t}{2})-E_{Na}
\right]
+\dots
$$

最终可以整理成线性方程：

$$
\begin{aligned}
&-A_{i,i-1}V_{i-1}(t+\frac{\Delta t}{2})
+
\left(
A_{i,i+1}+A_{i,i-1}
+\frac{2C_m}{\Delta t}
+g_{ion,i}
\right)
V_i(t+\frac{\Delta t}{2})
-
A_{i,i+1}V_{i+1}(t+\frac{\Delta t}{2}) \\
&=
\frac{2C_i}{\Delta t}V_i(t)
-g_{ion,i}E_{ion,i}
\end{aligned}
$$

矩阵元素可写为：

$$
\begin{aligned}
M_{i,i-1} &= -A_{i,i-1} \\
M_{i,i} &=
\frac{2C_i}{\Delta t}
+A_{i,i-1}+A_{i,i+1}
+\sum_x g_{x,i}(t+\frac{\Delta t}{2}) \\
M_{i,i+1} &= -A_{i,i+1} \\
b_i &=
\frac{2C_m}{\Delta t}V_i(t)
+\sum_x g_{x,i}(t+\frac{\Delta t}{2})E_x
\end{aligned}
$$

对于一维电缆，这会形成三对角线性方程组：

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
b_1 \\ b_2 \\ b_3 \\ \vdots \\ b_{N-1} \\ b_N
\end{bmatrix}
$$

### A.4 CaT 系统与 GHK 线性化

CaT 系统通过 GHK 通量方程描述 T 型钙电流，同时维护胞内钙浓度。由于钙离子在胞内外浓度差异很大，钙电流不能简单地用欧姆定律描述：

$$
G(V,Ca_o,Ca_i)
=
Z^2\frac{F^2V}{RT}
\frac{
[Ca]_i-[Ca]_o\exp(-ZFV/RT)
}{
1-\exp(-ZFV/RT)
}
$$

其中 \(Z=2\)，\(F\) 为法拉第常数，\(R\) 为气体常数，\(T\) 为绝对温度。

T 型钙电流写作：

$$
I_{T,i}
=
\bar P_{Ca,i}m_{T,i}^2h_{T,i}
G(V_i,Ca_o,Ca_i)
$$

门控动力学为：

$$
\frac{dm_{T,i}}{dt}
=
\frac{m_{T,\infty}(V)-m_{T,i}}{\tau_{m_T}(V)}
$$

$$
\frac{dh_{T,i}}{dt}
=
\frac{h_{T,\infty}(V)-h_{T,i}}{\tau_{h_T}(V)}
$$

$$
m_\infty(V)=\frac{1}{1+\exp[-(V+56)/6.2]},
\qquad
h_\infty(V)=\frac{1}{1+\exp[(V+80)/4]}
$$

时间常数为：

$$
\tau_m(V)
=
0.204
+
\frac{0.333}
{\exp[-(V+131)/16.7]+\exp[(V+15.8)/18.2]}
$$

$$
\tau_h(V)
=
\begin{cases}
0.333\exp[(V+466)/66.6], & V<-81\ \mathrm{mV} \\
9.32+0.333\exp[-(V+21)/10.5], & V\ge -81\ \mathrm{mV}
\end{cases}
$$

胞内钙浓度满足：

$$
\frac{d[Ca]_i}{dt}
=
\max(-\gamma_{Ca}I_{T,i},0)
+
\frac{Ca_\infty-Ca_i}{\tau_{Ca}}
$$

为了把 CaT 电流并入 Hines 方程组，需要对电流关于电压做线性化：

$$
I(V_{n+1})
\approx
I(V_n)
+
\frac{\partial I}{\partial V}\bigg|_{V_n}
(V_{n+1}-V_n)
$$

定义等效电导：

$$
g_{eq}
=
\frac{\partial I}{\partial V}\bigg|_{V_n}
$$

则：

$$
I(V_{n+1})
\approx
g_{eq}V_{n+1}
+
\underbrace{[I(V_n)-g_{eq}V_n]}_{\text{常数截距}}
$$

对 GHK 形式，令：

$$
k=\frac{ZF}{RT},\qquad z=kV,\qquad A=P_{Ca}m^2h\cdot ZF
$$

则：

$$
I(z)
=
A\frac{z([Ca]_ie^z-[Ca]_o)}{e^z-1}
$$

因此：

$$
g_{Ca\_eq}
=
\frac{\partial I}{\partial V}
=
A k
\frac{
\left([Ca]_ie^z(1+z)-[Ca]_o\right)(e^z-1)
-
\left(z[Ca]_ie^z-z[Ca]_o\right)e^z
}{
(e^z-1)^2
}
$$

并入矩阵后：

$$
\begin{aligned}
M_{i,i-1} &= -A_{i,i-1} \\
M_{i,i} &=
\frac{2C_m}{\Delta t}
+A_{i,i-1}+A_{i,i+1}
+g_{ion,i}+g_{Ca\_eq} \\
M_{i,i+1} &= -A_{i,i+1} \\
b_i &=
\frac{2C_m}{\Delta t}V_i(t)
+g_{ion,i}E_x
+I(V_n)-g_{Ca\_eq}V_n
\end{aligned}
$$

胞内钙浓度可用隐式形式更新：

$$
\frac{[Ca]_i^{n+1}-[Ca]_i^n}{\Delta t}
=
\frac{-I_T}{Z F Volume}
+
\frac{[Ca]_\infty-[Ca]_i^{n+1}}{\tau_{Ca}}
$$

$$
Ca_{n+1}
=
\frac{
Ca_n+\Delta t
\left(
\frac{-I_T}{ZFVolume}
+\frac{Ca_\infty}{\tau_{Ca}}
\right)
}{
1+\frac{\Delta t}{\tau_{Ca}}
}
$$

### A.5 动力学分析与相图切片

整体 ODE 系统可以写成：

$$
x=(V,m,h,n,Ca,m_T,h_T)
$$

连续时间动力学满足：

$$
C\frac{dV}{dt}
=
-\left[
\bar g_{Na}m^3h(V-E_{Na})
+\bar g_Kn^4(V-E_K)
+g_L(V-E_L)
+I_T(V,Ca,m_T,h_T)
\right]
+I_{axial}+I_{stim}
$$

$$
\frac{dm}{dt}
=
tadj_{HH}
\left[
\alpha_m(V)(1-m)-\beta_m(V)m
\right]
$$

$$
\frac{dh}{dt}
=
tadj_{HH}
\left[
\alpha_h(V)(1-h)-\beta_h(V)h
\right]
$$

$$
\frac{dn}{dt}
=
tadj_{HH}
\left[
\alpha_n(V)(1-n)-\beta_n(V)n
\right]
$$

$$
\frac{dm_T}{dt}
=
\frac{m_{T,\infty}(V)-m_T}{\tau_{m_T}(V)}
$$

$$
\frac{dh_T}{dt}
=
\frac{h_{T,\infty}(V)-h_T}{\tau_{h_T}(V)}
$$

$$
\frac{dCa}{dt}
=
\max(-\gamma_{Ca}I_T,0)
+\frac{Ca_\infty-Ca}{\tau_{Ca}}
$$

相图分析时，从状态变量中选择两个变量：

$$
x,y\in\{V,m,h,n,Ca,m_T,h_T\},\qquad x\ne y
$$

其余变量固定为指定时刻的历史值，得到二维切片系统：

$$
\mathcal N_x
=
\{(x,y)\mid \dot x=f(x,y;t_n)=0\}
$$

$$
\mathcal N_y
=
\{(x,y)\mid \dot y=g(x,y;t_n)=0\}
$$

零流线交点满足：

$$
(x^\ast,y^\ast)
\Longleftrightarrow
\begin{cases}
f(x^\ast,y^\ast;t_n)=0 \\
g(x^\ast,y^\ast;t_n)=0
\end{cases}
$$

平衡点附近的局部稳定性由 Jacobian 矩阵特征值决定：

$$
J
=
\begin{bmatrix}
\partial f/\partial x & \partial f/\partial y \\
\partial g/\partial x & \partial g/\partial y
\end{bmatrix}_{(x^\ast,y^\ast)}
$$

中心差分可用于数值计算偏导：

$$
\frac{\partial f}{\partial x}
\approx
\frac{
f(x^\ast+\varepsilon_x,y^\ast)
-
f(x^\ast-\varepsilon_x,y^\ast)
}{
2\varepsilon_x
}
$$

特征值满足：

$$
\lambda^2-\operatorname{tr}(J)\lambda+\det(J)=0
$$

当前实现的相图分析不是重新构造一个真正的二维神经元动力学模型，而是在完整多区室、多状态变量模型的仿真轨迹上，对某一个时间点做二维切片。其基本含义是：在时刻 \(t_n\)，取该区室以及相邻区室的真实历史状态，只允许被选择的两个变量 \(x,y\) 在网格上变化，其余变量保持为该时刻的历史值：

$$
\mathbf z(t_n)
=
\{V,m,h,n,Ca,m_T,h_T,V_{\mathrm{neighbor}},\ldots\}
$$

$$
\mathbf z_{x,y}(t_n)
=
\{x=X,\ y=Y,\ \text{others}=\text{history values at }t_n\}
$$

因此，图中的向量场和零倾线回答的是一个局部条件问题：

$$
\text{在当前背景状态固定时，如果只扰动 }x,y\text{，系统瞬时会怎样变化？}
$$

这种分析可以用于判断某个门控变量在当前时刻是否对膜电压具有强反馈，比较不同区室、不同通道密度、不同刺激时刻下的局部动力学差异，也可以辅助解释为什么某些轨迹在局部区域朝某个方向运动。

但是它的局限性也必须明确：该二维切片不是完整系统的全局相平面。真实模型中没有画出的变量并没有被消除或重新建模，而是被固定住了。因此，若某条零倾线近似为垂线，通常表示在当前切片中：

$$
\frac{\partial \dot V}{\partial y}\approx 0
$$

也就是纵轴变量 \(y\) 对 \(\dot V\) 的瞬时影响较弱，或者它的影响被漏电流、Na/K 电流、轴向电流、固定的 \(h_T\)、固定的 \(Ca\) 等背景项压制。这不一定说明该变量在完整动力学中没有作用，只说明在这一帧、这一组冻结条件下，它不是决定 \(\dot V=0\) 形状的主导变量。

以 \(V-m_T\) 图为例，T 型钙电流项为：

$$
I_T
\propto
\bar P_{Ca}m_T^2h_TG(V,Ca_o,Ca_i)
$$

在当前切片中，\(h_T\) 和 \(Ca_i\) 是固定的。如果此时 \(h_T\) 较低，或者 CaT 电流相对漏电流、Na/K 电流、轴向电流较弱，那么改变 \(m_T\) 对 \(\dot V\) 的影响就可能很小，\(V\)-nullcline 会近似表现为垂线。

这与文献中基于快慢分解的降维相图不同。文献 Fig. 6 一类分析通常先把高维 conductance-based model 系统性地约化为少数综合变量，例如：

$$
V,\qquad V_s,\qquad V_u
$$

其中 \(V_s\) 是慢时间尺度综合变量，\(V_u\) 是超慢时间尺度综合变量。这里的 \(V_s\) 并不等同于原模型中的单个门控变量 \(n\)、\(m_T\) 或 \(h_T\)，而是多个原始状态变量按时间尺度投影后的综合坐标。这样的相图解释的是降维系统的全局快慢几何结构，例如 lower branch、鞍结分岔和 bursting 机制。

因此，当前框架的定位应当是：

- 可以解释完整仿真轨迹附近的二维局部、条件、诊断性动力学。
- 可以观察特定变量在特定时刻、特定区室中对电压变化的瞬时贡献。
- 不能直接等同于文献中的 \(V-V_s\) 快慢相图。
- 不能仅凭某个二维切片中的零倾线形状，断言完整高维系统的全局分岔结构。

------

## Copyright

Copyright 2026 [Hepbmstl Hepupu]

Pupu NMDA / NeuronCAD  
A Multi-Compartment Neuron Physiological Simulation and Dynamics Analysis Platform

Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at:

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
