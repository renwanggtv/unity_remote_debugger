using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using System.Linq;
using System.Collections;
using System.IO;
using Newtonsoft.Json;

public class UnityRemoteLogger : MonoBehaviour
{
    private TcpClient tcpClient;
    private NetworkStream networkStream;
    private Thread receiveThread;
    private bool isConnected = false;
    private Queue<string> messageQueue = new Queue<string>();
    private object queueLock = new object();
    private const int MAX_QUEUE_SIZE = 1000;
    
    // 服务器配置
    [Header("Server Configuration")]
    public string serverIP = "172.16.200.65";
    public int serverPort = 8002;
    public float reconnectDelay = 5f;
    public float heartbeatInterval = 30f;

    // 设备标识符
    private string deviceId;
    private bool isReconnecting = false;

    // 上下文对象
    private Dictionary<string, object> runtimeContext = new Dictionary<string, object>();

    // 编译器缓存
    private CSharpCodeProvider codeProvider;
    private Dictionary<string, Assembly> compiledAssemblies = new Dictionary<string, Assembly>();

    void Awake()
    {
        codeProvider = new CSharpCodeProvider();
        deviceId = SystemInfo.deviceUniqueIdentifier;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        Application.logMessageReceived += HandleLog;
        InitializeRuntimeContext();
        ConnectToServer();
        StartCoroutine(HeartbeatCoroutine());
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
        DisconnectFromServer();
        if (codeProvider != null)
        {
            codeProvider.Dispose();
            codeProvider = null;
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus && !isConnected && !isReconnecting)
        {
            ConnectToServer();
        }
    }

    void OnApplicationQuit()
    {
        DisconnectFromServer();
    }

    private void InitializeRuntimeContext()
    {
        try
        {
            // 基本组件
            runtimeContext["gameObject"] = gameObject;
            runtimeContext["transform"] = transform;
            runtimeContext["camera"] = Camera.main;
            
            // Unity引擎类
            runtimeContext["Time"] = typeof(Time);
            runtimeContext["Physics"] = typeof(Physics);
            runtimeContext["Input"] = typeof(Input);
            runtimeContext["Application"] = typeof(Application);
            runtimeContext["Resources"] = typeof(Resources);
            runtimeContext["SceneManager"] = typeof(UnityEngine.SceneManagement.SceneManager);
            
            // 获取场景中的所有GameObject
            RefreshSceneObjects();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error initializing runtime context: {e.Message}\n{e.StackTrace}");
        }
    }

    public void RefreshSceneObjects()
    {
        var allObjects = FindObjectsOfType<GameObject>();
        foreach (var obj in allObjects)
        {
            if (!string.IsNullOrEmpty(obj.name))
            {
                string safeName = obj.name.Replace(" ", "_").Replace("-", "_");
                runtimeContext[safeName] = obj;
            }
        }
    }

    private void SendDeviceInfo()
    {
        var deviceInfo = new
        {
            type = "device_info",
            data = new
            {
                id = deviceId,
                deviceName = SystemInfo.deviceName,
                deviceModel = SystemInfo.deviceModel,
                operatingSystem = SystemInfo.operatingSystem,
                processorType = SystemInfo.processorType,
                systemMemorySize = SystemInfo.systemMemorySize,
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsMemorySize = SystemInfo.graphicsMemorySize,
                isEditor = Application.isEditor,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                buildGUID = Application.buildGUID,
                productName = Application.productName,
                version = Application.version,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            }
        };

        SendMessage(JsonConvert.SerializeObject(deviceInfo));
    }

    private IEnumerator HeartbeatCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(heartbeatInterval);
            if (isConnected)
            {
                SendDeviceInfo();
            }
        }
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        try
        {
            // 清理日志内容中可能导致JSON解析错误的字符
            logString = logString.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            if (!string.IsNullOrEmpty(stackTrace))
            {
                stackTrace = stackTrace.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
            }

            var logData = new
            {
                type = "log",
                data = new
                {
                    type = type.ToString(),
                    message = logString,
                    stackTrace = stackTrace,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                }
            };

            string jsonMessage = JsonConvert.SerializeObject(logData);
            SendMessage(jsonMessage + "\n");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error handling log: {e.Message}\n{e.StackTrace}");
        }
    }

    private void ConnectToServer()
    {
        if (isConnected || isReconnecting) return;

        try
        {
            isReconnecting = true;
            tcpClient = new TcpClient();
            IAsyncResult result = tcpClient.BeginConnect(serverIP, serverPort, null, null);
            
            bool success = result.AsyncWaitHandle.WaitOne(5000); // 5秒超时
            if (!success)
            {
                throw new Exception("Connection attempt timed out");
            }

            tcpClient.EndConnect(result);
            networkStream = tcpClient.GetStream();
            isConnected = true;
            isReconnecting = false;

            SendDeviceInfo();

            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log("Connected to debug server successfully!");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to connect to server: {e.Message}");
            isConnected = false;
            isReconnecting = false;
            StartCoroutine(RetryConnection());
        }
    }

    private IEnumerator RetryConnection()
    {
        while (!isConnected && !isReconnecting)
        {
            yield return new WaitForSeconds(reconnectDelay);
            ConnectToServer();
        }
    }

    private void SendMessage(string message)
    {
        if (!isConnected) return;

        try
        {
            // 确保消息以换行符结尾
            if (!message.EndsWith("\n"))
            {
                message += "\n";
            }
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            networkStream.Write(messageBytes, 0, messageBytes.Length);
            networkStream.Flush(); // 确保数据立即发送
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to send message: {e.Message}");
            DisconnectFromServer();
        }
    }

    private void ReceiveData()
    {
        byte[] buffer = new byte[4096];
        StringBuilder messageBuilder = new StringBuilder();

        while (isConnected)
        {
            try
            {
                if (networkStream.DataAvailable)
                {
                    int bytesRead = networkStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string received = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        messageBuilder.Append(received);

                        // 处理完整的消息
                        int newlineIndex;
                        while ((newlineIndex = messageBuilder.ToString().IndexOf('\n')) != -1)
                        {
                            string message = messageBuilder.ToString(0, newlineIndex);
                            messageBuilder.Remove(0, newlineIndex + 1);
                            ProcessCommand(message);
                        }
                    }
                }
                Thread.Sleep(10);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error receiving data: {e.Message}");
                DisconnectFromServer();
                break;
            }
        }
    }

    private void ProcessCommand(string command)
    {
        try
        {
            var commandData = JsonConvert.DeserializeObject<CommandData>(command);
            if (commandData.type == "execute_code")
            {
                // 确保在主线程中执行
                UnityEngine.Debug.Log($"Received code execution command: {commandData.data.code}");
                ExecuteOnMainThread(() => {
                    try 
                    {
                        ExecuteCodeLocally(commandData.data.code);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error executing code: {e.Message}\n{e.StackTrace}");
                    }
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing command: {e.Message}");
        }
    }

    private void ExecuteOnMainThread(Action action)
    {
        try 
        {
            if (UnityMainThreadDispatcher.Instance() != null)
            {
                UnityMainThreadDispatcher.Instance().Enqueue(action);
            }
            else
            {
                Debug.LogError("UnityMainThreadDispatcher instance is null!");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error enqueueing action to main thread: {e.Message}\n{e.StackTrace}");
        }
    }

    public object ExecuteCodeLocally(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogError("Code is null or empty");
            return null;
        }

        try
        {
            string codeHash = GetCodeHash(code);
            Assembly assembly;

            if (!compiledAssemblies.TryGetValue(codeHash, out assembly))
            {
                assembly = CompileCode(code);
                if (assembly != null)
                {
                    compiledAssemblies[codeHash] = assembly;
                }
                else
                {
                    Debug.LogError("Failed to compile code");
                    return null;
                }
            }

            Type scriptType = assembly.GetType("RuntimeScript");
            if (scriptType == null)
            {
                Debug.LogError("Failed to get RuntimeScript type");
                return null;
            }

            MethodInfo executeMethod = scriptType.GetMethod("Execute");
            if (executeMethod == null)
            {
                Debug.LogError("Failed to get Execute method");
                return null;
            }

            object result = executeMethod.Invoke(null, new object[] { runtimeContext });
            if (result != null)
            {
                Debug.Log($"Execution result: {result}");
            }
            return result;
        }
        catch (Exception e)
        {
            while (e.InnerException != null)
            {
                e = e.InnerException;
            }
            Debug.LogError($"Error executing code: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    private string GetCodeHash(string code)
    {
        using (var sha = new System.Security.Cryptography.SHA256Managed())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(code);
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }

    private Assembly CompileCode(string code)
    {
        var options = new CompilerParameters
        {
            GenerateInMemory = true,
            GenerateExecutable = false,
            TreatWarningsAsErrors = false,
            WarningLevel = 3
        };

        // 添加程序集引用
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var requiredAssemblies = loadedAssemblies
            .Where(a => !a.IsDynamic)
            .Where(a => 
                a.GetName().Name.StartsWith("UnityEngine") ||
                a.GetName().Name.StartsWith("System") ||
                a.GetName().Name == "mscorlib" ||
                a.GetName().Name.StartsWith("netstandard")
            )
            .Select(a => a.Location)
            .Where(loc => !string.IsNullOrEmpty(loc));

        options.ReferencedAssemblies.AddRange(requiredAssemblies.ToArray());

        string wrappedCode = @"
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

public class RuntimeScript
{
    public static object Execute(Dictionary<string, object> context)
    {
        try
        {
            " + code + @"
        }
        catch (Exception e)
        {
            Debug.LogError($""Runtime error: {e.Message}"");
            return null;
        }
    }
}";

        CompilerResults results = codeProvider.CompileAssemblyFromSource(options, wrappedCode);

        if (results.Errors.HasErrors)
        {
            StringBuilder errorBuilder = new StringBuilder();
            errorBuilder.AppendLine("Compilation failed:");
            foreach (CompilerError error in results.Errors)
            {
                errorBuilder.AppendLine($"Line {error.Line}: {error.ErrorText}");
            }
            Debug.LogError(errorBuilder.ToString());
            return null;
        }

        return results.CompiledAssembly;
    }

    private void DisconnectFromServer()
    {
        isConnected = false;

        if (networkStream != null)
        {
            networkStream.Close();
            networkStream = null;
        }

        if (tcpClient != null)
        {
            tcpClient.Close();
            tcpClient = null;
        }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Abort();
            receiveThread = null;
        }

        if (!isReconnecting)
        {
            StartCoroutine(RetryConnection());
        }
    }

    private class CommandData
    {
        public string type { get; set; }
        public CodeData data { get; set; }
    }

    private class CodeData
    {
        public string code { get; set; }
    }
}

// 主线程调度器
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private static readonly object Lock = new object();
    private static bool _initialized;
    
    private readonly Queue<Action> _actionQueue = new Queue<Action>();
    private readonly object _queueLock = new object();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        
        // 创建一个新的GameObject并添加dispatcher组件
        var go = new GameObject("UnityMainThreadDispatcher");
        _instance = go.AddComponent<UnityMainThreadDispatcher>();
        DontDestroyOnLoad(go);
    }

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            lock (Lock)
            {
                if (_instance == null)
                {
                    var go = GameObject.Find("UnityMainThreadDispatcher");
                    if (go != null)
                    {
                        _instance = go.GetComponent<UnityMainThreadDispatcher>();
                    }
                    
                    if (_instance == null)
                    {
                        Debug.LogWarning("Creating new UnityMainThreadDispatcher instance...");
                        go = new GameObject("UnityMainThreadDispatcher");
                        _instance = go.AddComponent<UnityMainThreadDispatcher>();
                        DontDestroyOnLoad(go);
                    }
                }
            }
        }
        return _instance;
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    public void Enqueue(Action action)
    {
        lock (_queueLock)
        {
            _actionQueue.Enqueue(action);
        }
    }

    private void Update()
    {
        while (true)
        {
            Action action;
            lock (_queueLock)
            {
                if (_actionQueue.Count == 0) break;
                action = _actionQueue.Dequeue();
            }

            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"Error executing queued action: {e.Message}\n{e.StackTrace}");
            }
        }
    }
}