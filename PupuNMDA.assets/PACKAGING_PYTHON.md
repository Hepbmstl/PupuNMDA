# Python Bundle 打包说明

## 目标

生成一个可放入 C# 发布目录的独立 Python 运行时目录：

```text
dist/python-bundle/
```

打包结果应复制到 C# 发布结果内：

```text
<publish-root>/runtime/python/
```

## 一键打包

在仓库根目录运行：

```powershell
python .\tools\build-python-bundle.py
```

脚本会自动完成：

- 使用 `tools/python-3.12.10-embed-amd64.zip` 作为 embeddable Python 骨架。
- 自动寻找本机完整 Python 3.12.10，复制标准库、`DLLs/` 和 `tcl/`。
- 按 `requirements-bundled.txt` 安装运行依赖到 `Lib/site-packages/`。
- 输出完整打包日志。
- 使用打包后的 `python.exe` 测试 `numpy`、`scipy`、`matplotlib`、`tkinter` 和 `Backward/Hines_method.py`。

如果自动寻找不到完整 Python，可以显式指定：

```powershell
python .\tools\build-python-bundle.py --source-python "C:\Users\Hepbmstl\AppData\Local\Programs\Python\Python312"
```

## 产物

```text
dist/python-bundle/
dist/python-bundle/build-python-bundle.log
dist/python-bundle/BUILD_INFO.txt
dist/python-bundle/requirements-bundled.txt
```

关键文件包括：

```text
dist/python-bundle/python312.dll
dist/python-bundle/python.exe
dist/python-bundle/pythonw.exe
dist/python-bundle/Lib/
dist/python-bundle/Lib/site-packages/numpy/
dist/python-bundle/Lib/site-packages/scipy/
dist/python-bundle/Lib/site-packages/matplotlib/
dist/python-bundle/DLLs/_tkinter.pyd
dist/python-bundle/tcl/
```

## C# 全量打包后复制

先发布 C#：

```powershell
dotnet publish .\NeuronCAD.csproj -c Release -r win-x64 --self-contained true
```

然后复制 Python bundle：

```powershell
New-Item -ItemType Directory -Force "<publish-root>\runtime" | Out-Null
Copy-Item -Recurse -Force ".\dist\python-bundle" "<publish-root>\runtime\python"
```

最终发布目录中应存在：

```text
<publish-root>/NeuronCAD.exe
<publish-root>/Backward/Hines_method.py
<publish-root>/runtime/python/python312.dll
<publish-root>/runtime/python/Lib/site-packages/numpy/
<publish-root>/runtime/python/Lib/site-packages/scipy/
<publish-root>/runtime/python/Lib/site-packages/matplotlib/
<publish-root>/runtime/python/tcl/
```
