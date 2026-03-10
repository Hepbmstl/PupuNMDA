

using System;
using Python.Runtime; // 引入 pythonnet 命名空间
/*
namespace NeuronSimulationApp
{
    public class SimulationController
    {
        public SimulationController(string pythonDllLocation)
        {
            Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", pythonDllLocation);
            PythonEngine.Initialize();
        }

        public void RunAndFetchData(string scriptDirectory)
        {
            // 步骤 2: 获取全局解释器锁 (GIL)，确保线程安全的 Python 状态访问
            using (Py.GIL())
            {
                // 将脚本所在目录加入 Python 的 sys.path 搜索树
                dynamic sys = Py.Import("sys");
                sys.path.append(scriptDirectory);

                // 步骤 3: 导入模块
                dynamic simModule = Py.Import("Backward\\Hines_method.py");

                try
                {
                    simModule.set_env(V_init: -65.0, dt: 0.02, steps: 1000, n_node: 2);
                    
                    foreach (var item in List_of_component) //....
                    {
                        if (item.name == "dend")
                        {
                            simModule.init_segment(uid: "dend", Ra: 35.4, D: 5.0, L: 100.0, Cm: 1.0, id: 1);
                        }
                    }
                    
                    
                    
                    simModule.start_simulation();

                    
                    dynamic results = simModule.export_history_matrices();

                    
                    double[,] historyV = results[0].As<double[,]>();
                    double[,] historyM = results[1].As<double[,]>();
                    double[,] historyH = results[2].As<double[,]>();
                    double[,] historyN = results[3].As<double[,]>();

                    
                    int timeSteps = historyV.GetLength(0);
                    int nodes = historyV.GetLength(1);
                    Console.WriteLine($"数据提取成功: 时间步长 {timeSteps}, 节点数 {nodes}");
                    Console.WriteLine($"Soma 初始电压: {historyV[0, 0]}");
                    Console.WriteLine($"Soma 终态电压: {historyV[timeSteps - 1, 0]}");

                }
                catch (PythonException ex)
                {
                    // 捕获 Python 虚拟机抛出的异常并映射到 C# 异常流
                    Console.WriteLine($"Python 运行时错误: {ex.Message}\n{ex.StackTrace}");
                }
                finally
                {
                    // 步骤 7: 销毁 Python 端全局状态，释放内存
                    simModule.clear_environment();
                }
            }
        }

        public void Shutdown()
        {
            PythonEngine.Shutdown();
        }
    }
}
*/