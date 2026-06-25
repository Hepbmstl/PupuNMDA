
# PupuNMDA
Neuron Modeling and Dynamics Analysis XD

PupuNMDA 是一个面向多区室神经元的桌面建模、仿真与动力学分析平台。它把 WPF/Helix Toolkit 三维形态编辑、conductance-based 离子通道模型、Python Hines 求解器和报告分析工具放在同一个工作流里。

<img width="1920" height="1180" alt="nmda-molecule-structure" src="https://github.com/user-attachments/assets/6f632899-48d3-4014-86d0-1cf54f590bdb" />


> 当前项目仍处于研究和原型开发阶段。仿真结果适合用于模型构建、数值探索和文献结果复现，正式生物学结论仍应结合原始模型、参数来源和实验背景进行校验。

## 功能概览

- **三维神经元建模**：使用 WPF 与 Helix Toolkit 构建胞体、轴突、树突等形态单元，并支持移动、旋转、连接和属性编辑。
- **多区室仿真**：将可视化结构离散为 compartment，支持按 `NSeg` 或 `LSeg` 进行分段。
- **离子通道参数**：内置 Hodgkin-Huxley 型 Na/K/Leak 通道与 T 型钙电流 CaT/GHK 模型，并支持全局反转电位和通道参数配置。
- **刺激与记录设备**：支持 current clamp、voltage clamp 和 probe，设备位置会绑定到对应区室。
- **Python 数值后端**：通过 `pythonnet` 调用 `Backward/Hines_method.py`，执行 Hines 方法、CaT 线性化、探针数据导出和相图分析。
- **结果报告**：支持查看区室、绘制变量时间曲线、生成局部二维相图、向量场、零流线和平衡点稳定性信息。

<img width="480" height="48" alt="neuroncad-splash-animation" src="https://github.com/user-attachments/assets/cb996900-bdde-445a-9819-6567c5291360" />


## 示例结果

| TC 神经元电压响应 | TC 神经元的表现 |
| --- | --- |
|<img width="1000" height="500" alt="tc-soma-voltage-trace-reduced-cat" src="https://github.com/user-attachments/assets/3a409150-56e0-43e0-bb97-2da4bd73d23d" /> | <img width="486" height="659" alt="destexhe-tc-neuron-t-current-response" src="https://github.com/user-attachments/assets/8174a3f8-a31c-40c2-9aba-38b7971b99f6" />|

(Destexhe et al., J. Neurosci., 1998)

| 相图绘制 | 快慢动力学表现 |
| --- | --- |
| <img width="1050" height="686" alt="tc-dendrite-high-cat-v-n-phase-portrait" src="https://github.com/user-attachments/assets/362d8a02-1730-4660-b5e2-0ceccce0fbcd" /> | <img width="2987" height="2401" alt="fast-slow-ultraslow-neuron-phase-portrait" src="https://github.com/user-attachments/assets/6a4e49a0-8b13-4f65-a33d-cf5ce730490e" /> |

(Jacquerie et al., Computational Biology, 2021)


## 生物物理与数值方法

PupuNMDA 的计算核心可以分为三层：

1. 单个膜片上的 Hodgkin-Huxley 离子通道动力学。
2. 连接多个膜片或区室的电缆方程。
3. 用于稳定推进树状多区室系统的 Crank-Nicolson 与 Hines 方法。

典型区室电压方程可以写为：

```math
C_i \frac{d V_i}{d t}
= - I_{\mathrm{ion},i}
  + I_{\mathrm{stim},i}
  + \sum_j g_{ij}(V_j - V_i)
```

其中相邻区室之间的轴向耦合项会在隐式时间推进中形成稀疏线性系统。Hines 方法利用神经元形态的树状拓扑重新组织矩阵，使多区室电缆方程能够高效求解。

T 型钙电流使用 GHK 通量形式，并在每个时间步对电流关于电压进行局部线性化，使 CaT 电流可以并入 Hines 方程组。当前相图工具不是完整高维系统的全局降维，而是在某个仿真时刻固定其余状态变量，对指定两个变量做局部二维切片。

| 经典 HH 电流与门控变量 | 多区室电缆离散化 |
| --- | --- |
|  <img width="852" height="467" alt="hodgkin-huxley-ion-channel-currents" src="https://github.com/user-attachments/assets/9f1a2e01-868a-4b05-a270-c341c2c8fa15" /> |  <img width="782" height="487" alt="multicompartment-cable-discretization" src="https://github.com/user-attachments/assets/19c16994-9501-4d7b-a889-14c0f1b4b780" /> |

(Beaubois et al., Front. Neurosci., 2024)


## 仓库结构

```text
PupuNMDA/
|-- App.xaml, App.xaml.cs        WPF 应用入口
|-- NeuronCAD.csproj             .NET 8 WPF 项目文件
|-- Backward/                    Python 后端与 C# 调用桥
|   |-- Hines_method.py          多区室求解器、探针导出、相图分析
|   |-- PythonWorker.cs          Python 运行时与 GIL 工作线程
|   |-- SimulationRunner.cs      C# 到 Python 的仿真调度
|   `-- SaveLoadManager.cs       项目保存、加载和结果数据结构
|-- Visuals/                     WPF 窗口、建模、仿真、报告界面
|-- PupuNMDA.assets/             README 图片与说明素材
|-- runtime-payload/             发布时使用的 Python 运行时载荷，开发环境可能存在
`-- LICENSE                      Apache-2.0 许可证
```

## 构建与运行

### 环境要求

- Windows 10/11
- .NET 8 SDK
- 发布或仿真运行时需要 Python 3.12 运行时包，包含 `numpy`、`scipy`、`matplotlib`、`tkinter` 等依赖

### 从源码构建

```powershell
dotnet restore .\NeuronCAD.csproj
dotnet build .\NeuronCAD.csproj -c Debug
dotnet run --project .\NeuronCAD.csproj
```

如果运行仿真时提示缺少 bundled Python runtime，请确认生成目录中存在：

```text
bin/Debug/net8.0-windows/runtime/python/python312.dll
bin/Debug/net8.0-windows/runtime/python/Lib/site-packages/
```

在本地已有 `runtime-payload` 的情况下，可以把其中的 `runtime` 目录复制到构建输出目录：

```powershell
Copy-Item -Recurse -Force .\runtime-payload\runtime .\bin\Debug\net8.0-windows\
```

### 发布

```powershell
dotnet publish .\NeuronCAD.csproj -c Release -r win-x64 --self-contained true
```

发布目录应至少包含：

```text
NeuronCAD.exe
Backward/Hines_method.py
runtime/python/python312.dll
runtime/python/Lib/site-packages/numpy/
runtime/python/Lib/site-packages/scipy/
runtime/python/Lib/site-packages/matplotlib/
runtime/python/tcl/
```

## 基本工作流

1. 在 **Modeling** 中创建 soma、axon、dendrite，并调整几何尺寸、连接关系和通道参数。
2. 在 **Simulating** 中设置区室分段方式，放置 current clamp、voltage clamp 或 probe。
3. 点击 Begin 运行仿真，Python 后端会组装区室、设备和通道参数并执行 Hines 求解。
4. 在 **Reporting** 中查看区室、绘制变量随时间变化的曲线，或基于 probe 结果生成局部相图。
5. 使用保存和加载功能复用项目结构与仿真结果。

## 参考模型与资料

- Hodgkin-Huxley 型 Na/K/Leak 通道动力学
- 多区室 cable equation 与 Hines 隐式求解方法
- T 型钙电流 CaT/GHK 与胞内钙浓度更新
- Destexhe 等人丘脑皮层神经元放电结果复现
- 快慢动力学相图、零流线和局部稳定性分析

## License

 Copyright 2026 [Hepbmstl Hepupu]

 Pupu NMDA / NeuronCAD
 A Multi-Compartment Neuron Modeling and Dynamics Analysis Platform

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0

 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.


