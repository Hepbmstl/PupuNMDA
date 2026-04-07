# NeuronCAD 项目数据结构与模块集成管线审阅

## 1. 项目实际主干

当前仓库的真实运行主干不是多个独立页面，而是一个单窗口壳层加共享内存态：

- MainWindow 负责顶层标签切换、工具栏、文件菜单、模拟启动、模拟结果导入导出。
- SharedSceneState 是三大模块共享的内存中心，统一持有 Entities、Devices、ConnectionController、SimulationRegistry、LastSimulationData。
- Modeling、Simulation、Reporting 三个模块都挂在同一个 3D 视口上，只是交互控制器和侧边面板不同。
- Backward 目录承担两类后端职责：
  - C# 端的持久化与 Python 桥接。
  - Python 端的 Hines_method 求解、结果缓存和分析绘图。

因此，这个项目的核心不是“页面之间传值”，而是“同一个场景状态对象在三个功能模式之间逐步增量充实”。

---

## 2. 核心数据结构

### 2.1 共享运行态

| 结构               | 位置                                    | 关键字段                                                                                       | 作用                                                                        |
| ------------------ | --------------------------------------- | ---------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| SharedSceneState   | Visuals/Tabs/Shared/SharedSceneState.cs | HelixViewport、Entities、Devices、ConnectionController、SimulationRegistry、LastSimulationData | 整个应用运行时的共享场景状态中心                                            |
| LastSimulationData | SharedSceneState 内                     | SimulationData                                                                                 | 保存最近一次模拟的“结构层结果”，供 Reporting 还原 compartment 和 probe 绑定 |

说明：

- SharedSceneState 保存的是 C# 侧运行态，不是持久化文件格式。
- Reporting 模块并不直接从建模对象重新推断所有结构，而是依赖 LastSimulationData 和实体上的 CompartmentCount/CompartmentIds。

### 2.2 建模对象层

| 结构                                 | 位置                                              | 关键字段                                                         | 作用                                                  |
| ------------------------------------ | ------------------------------------------------- | ---------------------------------------------------------------- | ----------------------------------------------------- |
| IVisualEntity / VisualEntityBase     | Visuals/Tabs/Modeling/Visuals                     | Id、Visual3D、Channels、Cm、Ra、CompartmentCount、CompartmentIds | 所有建模实体的统一接口                                |
| AxonVisual / DendVisual / SomaVisual | Visuals/Tabs/Modeling/Visuals                     | BaseRadius、TopRadius、Length、CenterPosition                    | 神经元几何对象；Soma 当前也是基于 AxonVisual 逻辑包装 |
| ChannelProperty                      | Visuals/Tabs/Modeling/Biophy.cs                   | Name、Color、G_ion_channel、IsPermeability                       | 单个离子通道密度或渗透率配置                          |
| GlobalBiophysics                     | Visuals/Tabs/Modeling/Biophy.cs                   | GlobalChannels                                                   | 全局通道模板，默认包含 Na、K、L、CaT                  |
| AnchorRef                            | Visuals/Tabs/Modeling/Visuals/VisualEntityBase.cs | Mode、AxialT、Angle                                              | 连接端点或设备锚点在实体表面上的参数化表示            |
| Connection                           | Visuals/Tabs/Modeling/Visuals/VisualEntityBase.cs | A、B、AnchorA、AnchorB、Weight                                   | 建模实体之间的连接数据                                |
| ConnectionController                 | Visuals/Tabs/Modeling/Connection.cs               | ConnectionsById、VisualsById                                     | 管理连接的生命周期和几何刷新                          |

说明：

- 建模模块的真正数据对象不是 XAML 控件，而是 IVisualEntity、Connection、ChannelProperty。
- Cm 与 Ra 已经在建模阶段写入实体，因此建模输出不是纯几何，还包含膜电特性。
- Channels 直接附着在实体对象上，后续会被 SimulationRegistry 复制到 compartment 级别。

### 2.3 模拟装置层

| 结构               | 位置                                       | 关键字段                                 | 作用             |
| ------------------ | ------------------------------------------ | ---------------------------------------- | ---------------- |
| IAttachedDevice    | Visuals/Tabs/Simulation/attacheddevices.cs | Id、Type、TargetEntity、Anchor、Visual3D | 仿真设备统一接口 |
| StimulationDevice  | 同上                                       | Stimulation_uA、StimStart、StimDuration  | 电流钳注入装置   |
| ProbeDevice        | 同上                                       | StartMs、DurationMs                      | 采样探针         |
| VoltageClampDevice | 同上                                       | Rs、Protocol                             | 电压钳装置       |
| VCStep             | 同上                                       | Duration、Amplitude                      | 电压钳协议步进   |

说明：

- 设备并不单独拥有空间坐标，而是通过 TargetEntity + Anchor 贴附在建模实体表面。
- 这意味着模拟模块的输入不仅依赖设备参数，还依赖建模几何与锚点解析。

### 2.4 模拟载荷层

| 结构                                        | 位置                                          | 关键字段                                                                                                  | 作用                                                  |
| ------------------------------------------- | --------------------------------------------- | --------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| SimulationRegistry                          | Visuals/Tabs/Simulation/SimulationRegistry.cs | Mode、NSeg、LSeg、RegisteredEntities                                                                      | 将建模实体离散化为 compartment，并完成设备绑定        |
| Compartment                                 | 同上                                          | GlobalId、ParentEntityId、ParentEntityType、Index、Length_um、Diameter_um、Cm、Ra、Channels、ConnectedIds | 离散化后的电缆段，是 Python 求解器的直接输入单元      |
| SimulationData                              | 同上                                          | Compartments、Probes、Stimulations、VoltageClamps                                                         | 一次模拟的完整 C# 侧输入载荷                          |
| SimProbe / SimStimulation / SimVoltageClamp | 同上                                          | SegmentId、时间参数、装置参数、SourceDeviceId                                                             | 设备从“几何贴附对象”转换成“绑定到 segment 的数值载荷” |

说明：

- SimulationData 是 C# 到 Python 的核心边界对象。
- BuildSimulationData 不仅返回 SimulationData，还会把 CompartmentCount 与 CompartmentIds 回写到实体对象，供 Reporting 可视化使用。

### 2.5 持久化文件层

| 结构                 | 位置                        | 关键字段                                                                                                               | 作用                          |
| -------------------- | --------------------------- | ---------------------------------------------------------------------------------------------------------------------- | ----------------------------- |
| ProjectData          | Backward/SaveLoadManager.cs | ProjectId、ProjectName、GlobalEnvironment、E_TABLE、HH_PARAMS、CA_PARAMS、Segmentation、Entities、Connections、Devices | 工程文件 JSON 的根结构        |
| EntityData           | 同上                        | 几何、颜色、Transform、Ra、Cm、Channels                                                                                | 建模实体的落盘格式            |
| ConnectionData       | 同上                        | EntityA_Id、EntityB_Id、AnchorA、AnchorB、Weight                                                                       | 连接的落盘格式                |
| DeviceData           | 同上                        | Type、TargetEntityId、Anchor、各类设备参数                                                                             | 仿真设备的落盘格式            |
| SimulationResultData | 同上                        | ProjectId、ProjectName、FullSimulationJson                                                                             | 仿真结果文件 simjson 的根结构 |

说明：

- 项目工程文件和模拟结果文件是两个不同的外部文件边界。
- 工程文件保存“可编辑模型 + 全局参数 + 设备配置”。
- 模拟结果文件保存“工程标识 + 完整 Python 结果 JSON”。

### 2.6 Python 求解与分析运行态

| 结构             | 位置                     | 关键字段                                            | 作用                                 |
| ---------------- | ------------------------ | --------------------------------------------------- | ------------------------------------ |
| Segment          | Backward/Hines_method.py | uid、Ra、D、L、Cm、id、channels、connected_segments | Python 侧单个 compartment 的求解对象 |
| SEGMENT          | 同上                     | segment_id -> Segment                               | 全部 compartment 的运行时字典        |
| HISTORY_V/M/H/N  | 同上                     | (steps + 1, n_node) 数组                            | HH 状态历史                          |
| HISTORY_CA/MT/HT | 同上                     | (steps + 1, n_node) 数组                            | CaT 与钙浓度状态历史                 |
| PROBE_LIST       | 同上                     | (probe_id, segment_id, start, duration)             | 探针配置                             |
| STIMULATION      | 同上                     | (stim_id, segment_id, current, start, duration)     | 电流钳配置                           |
| VOLTAGE_CLAMP    | 同上                     | (vc_id, segment_id, rs, protocol)                   | 电压钳配置                           |
| PROBE_SAVE_DATA  | 同上                     | probe_id -> list[dict]                              | 探针采样结果                         |

说明：

- Python 侧既是求解器，也是结果仓库和后处理分析引擎。
- Reporting 触发的图形分析并不是在 C# 中完成，而是继续调用 Hines_method.py 内部的历史数组和分析函数。

---

## 3. 端到端集成管线

### 3.1 运行时总管线

1. 用户在 Modeling 模式创建实体、编辑几何和通道、建立连接。
2. 这些对象进入 SharedSceneState.Entities 与 ConnectionController.ConnectionsById。
3. ModelingInteraction 的 OnEntityAdded / OnEntityRemoved 同步更新 SimulationRegistry.RegisteredEntities。
4. 用户在 Simulation 模式放置电流钳、探针、电压钳，这些对象进入 SharedSceneState.Devices。
5. 点击 Begin 后，MainWindow 从 UI 读取分段方式、环境参数、反转电位、温度和钙参数。
6. SimulationRegistry.BuildSimulationData 把实体离散成 compartments，并把设备锚点映射到具体 segment。
7. SimulationRunner 把 SimulationData 转成一串 Python 调用，交给 PythonWorker 在线程内执行。
8. Hines_method.py 完成求解，返回 ProbeResultJson 和 FullSimulationJson。
9. MainWindow 把结构层结果写入 LastSimulationData，把数值层结果写入 _lastFullSimulationJson。
10. Reporting 模块读取 LastSimulationData 生成 compartment 覆层与 probe 列表，再通过 SimulationRunner 静态方法回调 Python 画图分析。
11. 用户可以把 FullSimulationJson 包装成 simjson 导出，或重新导入以恢复报告分析能力。

### 3.2 关键桥接关系

- 建模到模拟的桥：SimulationRegistry
  - 负责把实体级数据转换为 compartment 级数据。
  - 负责把设备从几何锚点转换为 segment 绑定。

- 模拟到报告的桥：双轨结果
  - 结构轨：LastSimulationData。
  - 数值轨：FullSimulationJson 导回 Python 运行时。

这是本项目最关键的架构点：

- Reporting 不是只依赖一份结果。
- 它同时需要 C# 侧的结构映射和 Python 侧的历史状态。

---

## 4. 建模模块

### 4.1 建模模块需要的数据

建模模块需要的输入数据包括：

- 用户在 3D 视口中的交互位置与姿态。
- 实体几何参数：BaseRadius、TopRadius、Length。
- 实体膜电参数：Cm、Ra。
- 实体颜色与 Transform。
- 通道模板与通道参数：Na、K、L、CaT，以及每个实体上的局部通道值。
- 连接信息：两个实体的 Id，加上 AnchorA / AnchorB。

其中通道层分成两类：

- GlobalBiophysics.GlobalChannels：全局模板。
- entity.Channels：实体私有副本，允许每个实体单独改值。

### 4.2 建模模块产生的数据

建模模块直接产生：

- SharedSceneState.Entities 中的 IVisualEntity 列表。
- ConnectionController.ConnectionsById 中的连接字典。
- 每个实体上的 Channels、Cm、Ra、Transform。
- SimulationRegistry.RegisteredEntities 中的注册表条目。

这些数据还是“连续几何层 + 生物物理参数层”，尚未离散成 compartments。

### 4.3 建模模块输出到外部的数据

建模模块没有单独的外部导出格式；它通过工程保存流程贡献以下字段到 ProjectData：

- ProjectData.Entities
- ProjectData.Connections

如果用户执行保存工程，最终落到外部 JSON 的内容包括：

- 建模实体几何与颜色
- 通道分布
- Cm / Ra
- 连接拓扑与锚点

也就是说，建模模块对外输出的是“可重建模型状态”，不是数值模拟结果。

### 4.4 建模模块对后续模块的输出

建模模块向模拟模块输出：

- 实体几何，用于分段。
- AnchorRef 与连接关系，用于建立 compartment 间耦合。
- 通道密度 / 渗透率，用于映射到 Python Segment.channels。
- Cm / Ra，用于膜电容和轴向电阻求解。

---

## 5. 模拟模块

### 5.1 模拟模块需要的数据

模拟模块需要四类输入：

1. 来自建模模块的结构输入
   - Entities
   - Connections
   - Channels
   - Cm / Ra

2. 来自 Simulation 模式的设备输入
   - StimulationDevice
   - ProbeDevice
   - VoltageClampDevice

3. 来自模拟控制面板的全局环境输入
   - V_init
   - dt
   - STEPS
   - E_Na / E_K / E_L
   - celsius
   - CA_OUT / CA_INF / TAU_CA
   - 分段方式 NSeg 或 LSeg

4. 来自 IonChannelSettingWindow 的动力学参数输入
   - HH_PARAMS
   - CA_PARAMS

### 5.2 模拟模块产生的数据

模拟模块分两层产出：

#### A. C# 侧结构化产出

- SimulationData
  - Compartments
  - Probes
  - Stimulations
  - VoltageClamps

- 实体上的 CompartmentCount / CompartmentIds 回写
- SharedSceneState.LastSimulationData

#### B. Python 侧数值产出

- HISTORY_V / HISTORY_M / HISTORY_H / HISTORY_N
- HISTORY_CA / HISTORY_MT / HISTORY_HT
- PROBE_SAVE_DATA
- export_probe_data_json 的 JSON 字符串
- export_full_simulation_json 的完整 JSON 字符串

### 5.3 模拟模块输出到外部的数据

模拟模块当前对外有两种外部输出：

#### A. 工程文件中的模拟配置部分

当用户保存工程时，模拟模块会把以下配置写入 ProjectData：

- GlobalEnvironment
- E_TABLE
- HH_PARAMS
- CA_PARAMS
- Segmentation
- Devices

这部分是“模拟配置”，不是“模拟结果”。

#### B. 模拟结果文件 simjson

点击 Export Simulation Data 后，应用写出 SimulationResultData：

- ProjectId
- ProjectName
- FullSimulationJson

其中 FullSimulationJson 内部包含：

- metadata
- probe_list
- probe_save_data
- segments
- HISTORY_V / M / H / N
- HISTORY_CA / MT / HT

### 5.4 模拟模块与外部文件边界的真实含义

- 工程 JSON 负责复原“可编辑工程”。
- simjson 负责复原“结果分析上下文”。

这两个文件不是互相替代关系，而是前者保存模型，后者保存数值历史。

---

## 6. 分析报告模块

### 6.1 报告模块需要的数据

报告模块依赖两份不同来源的数据：

#### A. C# 结构层

- SharedSceneState.Entities
- SharedSceneState.Devices
- SharedSceneState.LastSimulationData
- 实体上的 CompartmentCount / CompartmentIds

这部分用于：

- 显示组件卡片
- 显示 probe 列表
- 在 3D 视口中绘制 compartment overlay
- 将用户点击的 overlay 映射回 segment_id

#### B. Python 数值层

- Hines_method.py 内存中的 HISTORY 数组
- PROBE_LIST
- PROBE_SAVE_DATA
- SEGMENT

这部分用于：

- plot_variable_over_time
- show_dynamic_phase_portrait
- nullcline / equilibrium / Jacobian / stability 分类

### 6.2 报告模块产生的数据

报告模块本身产生的是“分析视图和分析结果”，不是新的仿真状态：

- CompartmentOverlay 列表
- 当前选中 entity 与 compartment 的映射
- 某个 segment 的时间序列图
- 某个 probe 的动态相图
- 平衡点、Jacobian、特征值、稳定性分类
- 生物物理摘要信息面板

### 6.3 报告模块输出到外部的数据

当前代码中，报告模块没有专门的文件导出逻辑。

它对外的可见输出只有：

- matplotlib 图窗
- Tkinter 相图播放器窗口

也就是说，报告模块当前是“交互分析终端”，不是“报告文件生成器”。

如果要导出静态图或报告文件，目前需要依赖 matplotlib 窗口的手工保存能力，而不是项目代码中的专门导出通路。

---

## 7. Hines_method 在系统中的作用

Hines_method.py 在本项目中不是一个孤立算法文件，而是整个模拟与分析后端的核心运行时。

### 7.1 它承担的四个角色

#### A. 多 compartment 求解器

它接收来自 C# 的 compartment 网络，并在每个时间步：

- 更新 HH 门控变量 m、h、n
- 更新 CaT 门控变量 mT、hT
- 计算 Na、K、L 电流
- 用 GHK 方程计算 CaT 电流
- 计算轴向耦合 K_ij
- 把外部电流钳和电压钳并入矩阵系统
- 组装 Hines 型线性系统 A x = b
- 通过 scipy.linalg.solve 做隐式求解

因此，它是“离散神经电缆方程 + 通道动力学”的实际执行器。

#### B. Python 侧结果仓库

求解完成后，所有历史数组仍保留在模块全局变量中：

- HISTORY_* 系列
- SEGMENT
- PROBE_LIST
- PROBE_SAVE_DATA

Reporting 模块并不是重新计算这些历史，而是继续复用这些内存态。

#### C. 结果序列化与反序列化引擎

它负责：

- export_probe_data_json
- export_full_simulation_json
- import_full_simulation_json

这使得模拟结果可以脱离当前 Python 会话被重新载入，用于后续分析。

#### D. 报告分析引擎

它还直接负责：

- plot_variable_over_time
- show_dynamic_phase_portrait
- 连续导数估计
- nullcline 计算
- equilibrium 搜索
- Jacobian 与稳定性分类

因此，Reporting 模块本质上是 Python 分析能力的 UI 外壳，而不是独立分析器。

### 7.2 C# 与 Hines_method 的接口顺序

SimulationRunner.RunAsync 的调用顺序是：

1. clear_environment
2. set_env
3. set_E
4. set_hh_params
5. set_ca_params
6. init_segment
7. add_channel_to_segment
8. add_connection
9. insert_stimulation
10. insert_voltage_clamp
11. insert_probe
12. start_simulation
13. export_probe_data_json
14. export_full_simulation_json

这条顺序定义了整个项目中最核心的 C# -> Python 协议。

### 7.3 Hines_method 对模块边界的意义

对建模模块来说：

- 它要求几何和拓扑最终能被离散成 Segment 网络。

对模拟模块来说：

- 它定义了所有输入字段的最终语义，例如 Ra、D、L、Cm、channel_name、segment_id。

对报告模块来说：

- 它既保存历史结果，又提供分析 API，因此报告能力强依赖 Python 运行时仍然可用。

---

## 8. 外部文件与模块边界总结

### 8.1 外部文件类型

| 文件类型         | 生成模块                      | 内容                 | 用途                     |
| ---------------- | ----------------------------- | -------------------- | ------------------------ |
| 工程 JSON        | MainWindow + SaveLoadManager  | ProjectData          | 保存可编辑工程状态       |
| 模拟结果 simjson | MainWindow + SimulationRunner | SimulationResultData | 保存可导入的结果分析状态 |
| 图窗             | Reporting + Hines_method      | matplotlib / Tk 窗口 | 交互查看时间序列和相图   |

### 8.2 一个非常重要的实现细节

报告模块恢复数据时，实际上依赖两条恢复路径：

- 路径 1：C# 重新 BuildSimulationData，恢复结构映射。
- 路径 2：Python import_full_simulation_json，恢复历史数组与 probe 数据。

这意味着报告分析不是只读一个文件就结束，而是“当前模型结构 + 导入的数值历史”共同参与。

### 8.3 当前实现中的边界注意事项

#### A. 导入 simjson 时默认要求当前模型一致

代码显式校验 ProjectId，因此 simjson 不是独立自描述模型，它要求当前已经加载对应工程模型。

#### B. 导入 simjson 时，结构层使用当前界面的分段设置重新生成

MainWindow.OnImportSimulationDataClick 会重新读取 NSeg / LSeg 控件并 BuildSimulationData。

这说明：

- 导入结果时，当前分段设置最好与原始模拟时保持一致。
- 否则 C# 侧的 compartment 结构映射与 Python 侧导入的历史数组可能存在不一致风险。

#### C. FullSimulationJson 不是完整仿真环境快照

当前 export_full_simulation_json 保存了：

- metadata
- segments
- probe_list
- probe_save_data
- HISTORY 数组

但没有保存：

- STIMULATION 列表
- VOLTAGE_CLAMP 列表

因此导入 simjson 后，更准确的理解是：

- 它完整恢复了历史轨迹与 probe 结果。
- 但没有完整恢复所有外部激励配置。
- 报告模块更像是在“已保存历史”上做后处理，而不是完整重建原始模拟环境。

---

## 9. 结论

这个项目的核心集成逻辑可以概括成一句话：

> 建模模块生成连续几何与生物物理对象，模拟模块把它们离散成 compartment 网络并送入 Hines_method，报告模块再同时依赖 C# 侧结构映射与 Python 侧历史状态完成交互分析。

从数据流角度看，最重要的几个边界对象是：

- SharedSceneState：统一运行态
- SimulationData：C# 到 Python 的输入载荷
- FullSimulationJson：Python 结果态的外部化表示
- LastSimulationData：Reporting 使用的结构映射快照

从模块职责角度看：

- 建模模块负责“定义系统”。
- 模拟模块负责“离散系统并求解”。
- 报告模块负责“消费求解结果并做可视化分析”。
- Hines_method 则是这三者之间真正的数值与分析核心。

---

## 10. 本文档依据的关键源码

- Visuals/Windows/MainWindow.xaml.cs
- Visuals/Tabs/Shared/SharedSceneState.cs
- Visuals/Tabs/Modeling/Interaction.cs
- Visuals/Tabs/Modeling/Panels.cs
- Visuals/Tabs/Modeling/Visuals
- Visuals/Tabs/Simulation/SimulationRegistry.cs
- Visuals/Tabs/Simulation/SimulationInteraction.cs
- Visuals/Tabs/Simulation/SimulationPanels.cs
- Visuals/Tabs/Simulation/attacheddevices.cs
- Visuals/Tabs/Reporting/ReportingPanels.cs
- Visuals/Tabs/Reporting/ReportingInteraction.cs
- Backward/SaveLoadManager.cs
- Backward/SimulationRunner.cs
- Backward/PythonWorker.cs
- Backward/Hines_method.py