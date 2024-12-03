const { useState, useEffect, useRef } = React;
const { createRoot } = ReactDOM;

// 预定义的代码示例
const CODE_EXAMPLES = {
    "Print Object Info": `var go = GameObject.Find("Main Camera");
if (go != null) {
    var components = go.GetComponents<Component>();
    foreach (var comp in components) {
        Debug.Log($"Component: {comp.GetType().Name}");
    }
    return $"Found {components.Length} components on {go.name}";
} else {
    return "Object not found";
}`,
    "List All Objects": `var objects = UnityEngine.Object.FindObjectsOfType<GameObject>();
System.Text.StringBuilder sb = new System.Text.StringBuilder();
sb.AppendLine($"Found {objects.Length} objects in scene:");
foreach (var obj in objects.Take(10)) {
    sb.AppendLine($"- {obj.name} ({obj.GetComponents<Component>().Length} components)");
}
return sb.ToString();`,
    "Scene Statistics": `int totalObjects = UnityEngine.Object.FindObjectsOfType<GameObject>().Length;
int totalComponents = UnityEngine.Object.FindObjectsOfType<Component>().Length;
int totalLights = UnityEngine.Object.FindObjectsOfType<Light>().Length;
int totalColliders = UnityEngine.Object.FindObjectsOfType<Collider>().Length;

return $"Scene Statistics:\\n" +
       $"GameObjects: {totalObjects}\\n" +
       $"Components: {totalComponents}\\n" +
       $"Lights: {totalLights}\\n" +
       $"Colliders: {totalColliders}";`
};

// 解析Unity富文本标签
const parseUnityRichText = (text) => {
    if (!text) return '';
    return text.replace(/<color=[^>]+>([^<]*)<\/color>/g, 
        (match, content) => `<span class="text-emerald-400">${content}</span>`);
};

const App = () => {
    const [ws, setWs] = useState(null);
    const [isConnected, setIsConnected] = useState(false);
    const [devices, setDevices] = useState([]);
    const [selectedDevice, setSelectedDevice] = useState(null);
    const [logs, setLogs] = useState([]);
    const [code, setCode] = useState('');
    const [selectedExample, setSelectedExample] = useState('');
    const [logFilters, setLogFilters] = useState({
        Log: true,
        Warning: true,
        Error: true,
        Exception: true
    });
    const [showStackTrace, setShowStackTrace] = useState(true);
    const logsEndRef = useRef(null);
    const [isExecuting, setIsExecuting] = useState(false);

    useEffect(() => {
        connectWebSocket();
        return () => {
            if (ws) ws.close();
        };
    }, []);

    useEffect(() => {
        if (logsEndRef.current) {
            logsEndRef.current.scrollIntoView({ behavior: 'smooth' });
        }
    }, [logs]);

    const connectWebSocket = () => {
        console.log('Connecting to WebSocket...');
        const socket = new WebSocket(`ws://${window.location.host}`);
        
        socket.onopen = () => {
            console.log('WebSocket connected');
            setIsConnected(true);
        };

        socket.onclose = () => {
            console.log('WebSocket disconnected');
            setIsConnected(false);
            setTimeout(connectWebSocket, 5000);
        };

        socket.onmessage = (event) => {
            try {
                const data = JSON.parse(event.data);
                console.log('Received message:', data);
                handleWebSocketMessage(data);
            } catch (e) {
                console.error('Error parsing websocket message:', e);
            }
        };

        setWs(socket);
    };

    const handleWebSocketMessage = (data) => {
        switch (data.type) {
            case 'device_list_updated':
                console.log('Devices updated:', data.devices);
                setDevices(data.devices);
                break;
            case 'log':
                console.log('Processing log:', data);
                // if (data.deviceId === selectedDevice) {
                    const logEntry = {
                        id: Date.now() + Math.random(),
                        type: data.data.type || 'Log',
                        message: data.data.message || '',
                        stackTrace: data.data.stackTrace || '',
                        timestamp: data.data.timestamp || new Date().toISOString()
                    };
                    setLogs(prevLogs => [...prevLogs, logEntry]);
                // }
                break;
        }
    };

    const loadExample = () => {
        if (selectedExample && CODE_EXAMPLES[selectedExample]) {
            setCode(CODE_EXAMPLES[selectedExample]);
        }
    };

    const handleDeviceSelect = (deviceId) => {
        console.log('Selecting device:', deviceId);
        setSelectedDevice(deviceId);
        setLogs([]); // 清空之前的日志
        if (ws && ws.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify({
                type: 'select_device',
                deviceId: deviceId
            }));
        }
    };

    const handleExecuteCode = () => {
        if (!ws || !isConnected || !selectedDevice || !code.trim() || isExecuting) return;
        
        setIsExecuting(true);
        console.log('Executing code for device:', selectedDevice);
        ws.send(JSON.stringify({
            type: 'execute_code',
            deviceId: selectedDevice,
            code: code
        }));

        // 5秒后重置执行状态
        setTimeout(() => setIsExecuting(false), 5000);
    };

    const getLogStyle = (logType) => {
        switch (logType.toLowerCase()) {
            case 'warning':
                return 'text-yellow-400';
            case 'error':
                return 'text-red-400';
            case 'exception':
                return 'text-pink-400';
            default:
                return 'text-white';
        }
    };

    const filteredLogs = logs.filter(log => {
        const type = log.type || 'Log';
        return logFilters[type] || false;
    });

    return (
        <div className="flex flex-col h-screen bg-gray-900 text-white">
            {/* 设备列表 */}
            <div className="p-4 bg-gray-800 border-b border-gray-700">
                <div className="flex items-center space-x-4">
                    <span className="text-sm text-gray-400">可用设备:</span>
                    <div className="flex flex-wrap gap-2">
                        {devices.map(device => (
                            <button
                                key={device.id}
                                onClick={() => handleDeviceSelect(device.id)}
                                className={`px-3 py-1 rounded text-sm ${
                                    selectedDevice === device.id ? 'bg-blue-600' : 'bg-gray-700 hover:bg-gray-600'
                                }`}
                            >
                                {device.deviceName}
                                <span className="ml-2 text-xs text-gray-400">
                                    ({device.deviceModel})
                                </span>
                            </button>
                        ))}
                    </div>
                    {devices.length === 0 && (
                        <span className="text-sm text-gray-500">没有可用设备</span>
                    )}
                </div>
            </div>

            {selectedDevice ? (
                <div className="flex flex-1 overflow-hidden">
                    {/* 日志面板 - 固定50%宽度 */}
                    <div className="w-1/2 flex flex-col border-r border-gray-700">
                        <div className="p-2 bg-gray-800 flex items-center space-x-2">
                            <div className="flex space-x-2">
                                {Object.entries(logFilters).map(([type, enabled]) => (
                                    <button
                                        key={type}
                                        onClick={() => setLogFilters(prev => ({
                                            ...prev,
                                            [type]: !enabled
                                        }))}
                                        className={`px-2 py-1 rounded ${
                                            enabled ?
                                                type === 'Error' ? 'bg-red-600' :
                                                type === 'Warning' ? 'bg-yellow-600' :
                                                'bg-blue-600'
                                                : 'bg-gray-700'
                                        }`}
                                    >
                                        {type}
                                    </button>
                                ))}
                            </div>
                            <button
                                onClick={() => setShowStackTrace(!showStackTrace)}
                                className={`px-2 py-1 rounded ${
                                    showStackTrace ? 'bg-purple-600' : 'bg-gray-700'
                                }`}
                            >
                                堆栈跟踪
                            </button>
                            <button
                                onClick={() => setLogs([])}
                                className="px-2 py-1 rounded bg-gray-700 hover:bg-gray-600"
                            >
                                清除
                            </button>
                        </div>

                        <div className="flex-1 overflow-auto p-4 space-y-2">
                            {filteredLogs.map((log) => (
                                <div key={log.id} className="font-mono text-sm">
                                    <div className="flex items-start space-x-2">
                                        <span className="text-gray-500 whitespace-nowrap">
                                            {new Date(log.timestamp).toLocaleTimeString()}
                                        </span>
                                        <span 
                                            className={getLogStyle(log.type)}
                                            dangerouslySetInnerHTML={{ 
                                                __html: parseUnityRichText(log.message) 
                                            }}
                                        />
                                    </div>
                                    {showStackTrace && log.stackTrace && (
                                        <pre className="text-gray-500 text-xs mt-1 ml-24 whitespace-pre-wrap">
                                            {log.stackTrace.replace(/\\n/g, '\n')}
                                        </pre>
                                    )}
                                </div>
                            ))}
                            <div ref={logsEndRef} />
                        </div>
                    </div>

                    {/* 代码执行面板 - 固定50%宽度 */}
                    <div className="w-1/2 flex flex-col">
                        <div className="p-2 bg-gray-800 flex items-center space-x-2">
                            <select
                                value={selectedExample}
                                onChange={(e) => setSelectedExample(e.target.value)}
                                className="bg-gray-700 px-2 py-1 rounded text-sm"
                            >
                                <option value="">选择示例</option>
                                {Object.keys(CODE_EXAMPLES).map(example => (
                                    <option key={example} value={example}>
                                        {example}
                                    </option>
                                ))}
                            </select>
                            <button
                                onClick={loadExample}
                                className="px-2 py-1 bg-blue-600 rounded text-sm hover:bg-blue-500"
                            >
                                加载
                            </button>
                            <button
                                onClick={handleExecuteCode}
                                disabled={!isConnected || isExecuting}
                                className={`ml-auto px-3 py-1 rounded text-sm 
                                    ${isExecuting ? 
                                        'bg-gray-600' : 
                                        'bg-green-600 hover:bg-green-500'} 
                                    disabled:opacity-50`}
                            >
                                {isExecuting ? '执行中...' : '执行'}
                            </button>
                        </div>
                        <textarea
                            value={code}
                            onChange={(e) => setCode(e.target.value)}
                            className="flex-1 w-full bg-gray-900 text-white p-4 font-mono text-sm resize-none 
                                     focus:outline-none border border-gray-800"
                            placeholder="在此输入C#代码..."
                        />
                    </div>
                </div>
            ) : (
                <div className="flex-1 flex items-center justify-center text-gray-500">
                    请选择一个设备开始调试
                </div>
            )}

            {/* 连接状态提示 */}
            {!isConnected && (
                <div className="absolute bottom-4 right-4 max-w-sm bg-red-900 text-white px-4 py-2 rounded shadow-lg">
                    连接断开。正在尝试重新连接...
                </div>
            )}
        </div>
    );
};

// 渲染应用
const root = createRoot(document.getElementById('root'));
root.render(<App />);