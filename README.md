# Unity Remote Debugger

Unity远程调试工具，支持在网页上查看Unity设备日志和远程执行C#代码。

## 项目结构

```
unity-remote-debugger/
├── Assets/
│   ├── Editor/
│   │   └── DebugEditorWindow.cs    # Unity编辑器扩展窗口
│   └── Scripts/
│       └── UnityRemoteLogger.cs     # Unity远程日志和代码执行组件
├── unity-debugger/                  # Web调试界面
│   ├── public/
│   │   ├── index.html              # 网页入口
│   │   └── app.js                  # React应用代码
│   ├── server.js                   # WebSocket服务器
│   ├── package.json                # 项目配置
│   └── README.md                   # Web项目说明
└── README.md                       # 项目说明
```

## 功能特性

- 远程设备连接和管理
- 实时日志查看和过滤
- 远程C#代码执行
- 代码示例库
- Unity富文本支持
- 堆栈跟踪显示

## Unity端设置

1. 将 `Assets` 文件夹下的内容复制到你的Unity项目中
2. 在Unity场景中创建空GameObject
3. 添加 UnityRemoteLogger 组件
4. 配置服务器连接信息：
   - 本地调试：serverIP = "127.0.0.1"
   - 远程调试：serverIP = "你的电脑IP地址"
   - serverPort = 8002

## Web服务器设置

1. 安装依赖
```bash
cd unity-debugger
npm install
```

2. 启动服务器
```bash
node server.js
```

3. 访问调试界面
```
http://localhost:3001
```

## 使用方法

### 编辑器中使用
1. 打开Unity编辑器
2. 选择 Window > DebugTools > RuntimeLogger
3. 进入Play模式即可看到日志

### 远程设备调试
1. 构建Unity应用到移动设备
2. 确保设备和电脑在同一网络
3. 在UnityRemoteLogger组件中设置正确的服务器IP
4. 运行应用
5. 在Web界面中选择设备并开始调试

## 功能说明

### 日志查看
- 支持Log/Warning/Error/Exception分类
- 支持堆栈跟踪显示/隐藏
- 支持富文本颜色显示
- 支持日志清除

### 代码执行
- 预设代码示例
- 实时代码执行
- 执行结果实时反馈
- 支持Unity API调用

## 注意事项

1. 确保防火墙允许TCP端口8002和3001的连接
2. 移动设备调试时需要设置正确的服务器IP地址
3. 代码执行在设备上运行，注意安全性
4. 建议在开发环境使用，不要在生产环境开启

## 依赖项

- Unity 2018 或更高版本
- Node.js 14.0 或更高版本
- NPM 依赖:
  - express
  - ws
  - path

## 许可证

MIT License