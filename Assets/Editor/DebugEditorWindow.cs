using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Linq;

#if UNITY_EDITOR
public class DebugEditorWindow : EditorWindow
{
    private Vector2 logScroll = Vector2.zero;
    private Vector2 codeScroll = Vector2.zero;
    private string codeToExecute = "";
    private bool autoScroll = true;
    private UnityRemoteLogger UnityRemoteLogger;
    private List<LogEntry> logs = new List<LogEntry>();
    private bool showStackTrace = true;
    private bool[] logTypeEnabled = new bool[] { true, true, true, true }; // Log, Warning, Error, Exception
    private string searchFilter = "";
    
    // 日志样式
    private GUIStyle logStyle;
    private GUIStyle warningStyle;
    private GUIStyle errorStyle;
    private GUIStyle exceptionStyle;
    
    // 示例代码下拉选择
    private int selectedExampleIndex = 0;
    private bool isExecuting = false;

    // 代码示例
    private Dictionary<string, string> codeExamples = new Dictionary<string, string>
    {
        {
            "Print Object Info",
            @"var go = GameObject.Find(""Main Camera"");
if (go != null) {
    var components = go.GetComponents<Component>();
    foreach (var comp in components) {
        Debug.Log($""Component: {comp.GetType().Name}"");
    }
    return $""Found {components.Length} components on {go.name}"";
} else {
    return ""Object not found"";
}"
        },
        {
            "List All Objects",
            @"var objects = UnityEngine.Object.FindObjectsOfType<GameObject>();
System.Text.StringBuilder sb = new System.Text.StringBuilder();
sb.AppendLine($""Found {objects.Length} objects in scene:"");
foreach (var obj in objects.Take(10)) {
    sb.AppendLine($""- {obj.name} ({obj.GetComponents<Component>().Length} components)"");
}
return sb.ToString();"
        },
        {
            "Scene Statistics",
            @"int totalObjects = UnityEngine.Object.FindObjectsOfType<GameObject>().Length;
int totalComponents = UnityEngine.Object.FindObjectsOfType<Component>().Length;
int totalLights = UnityEngine.Object.FindObjectsOfType<Light>().Length;
int totalColliders = UnityEngine.Object.FindObjectsOfType<Collider>().Length;

return $""Scene Statistics:\n"" +
       $""GameObjects: {totalObjects}\n"" +
       $""Components: {totalComponents}\n"" +
       $""Lights: {totalLights}\n"" +
       $""Colliders: {totalColliders}"";"
        },
        {
            "Transform Manipulation",
            @"var target = GameObject.Find(""Main Camera"");
if (target != null) {
    var originalPos = target.transform.position;
    target.transform.Translate(Vector3.up * 1f);
    var newPos = target.transform.position;
    return $""Moved object from {originalPos} to {newPos}"";
} else {
    return ""Target object not found"";
}"
        },
        {
            "Physics Check",
            @"var origin = Vector3.zero;
var radius = 5f;
var colliders = Physics.OverlapSphere(origin, radius);

System.Text.StringBuilder sb = new System.Text.StringBuilder();
sb.AppendLine($""Found {colliders.Length} colliders within {radius}m of origin:"");

foreach (var col in colliders) {
    sb.AppendLine($""- {col.name} at {col.transform.position}"");
}

return sb.ToString();"
        }
    };

    [MenuItem("Window/DebugTools/RuntimeLogger")]
    public static void ShowWindow()
    {
        var window = GetWindow<DebugEditorWindow>();
        window.titleContent = new GUIContent("Runtime Logger");
        window.Show();
    }

    private void OnEnable()
    {
        // 初始化日志样式
        InitializeStyles();

        // 查找或创建UnityRemoteLogger
        TryGenerateUnityRemoteLogger();

        // 订阅日志回调
        Application.logMessageReceived += HandleLog;
    }

    private void TryGenerateUnityRemoteLogger()
    {
        if (UnityRemoteLogger != null)
            return;
        if (Application.isPlaying)
        {
            UnityRemoteLogger = FindObjectOfType<UnityRemoteLogger>();
            if (UnityRemoteLogger == null)
            {
                var go = new GameObject("UnityRemoteLogger");
                UnityRemoteLogger = go.AddComponent<UnityRemoteLogger>();
                DontDestroyOnLoad(go);
            }
        }
    }

    private void InitializeStyles()
    {
        logStyle = new GUIStyle(EditorStyles.label);
        logStyle.normal.textColor = Color.white;
        logStyle.wordWrap = true;
        logStyle.richText = true;

        warningStyle = new GUIStyle(EditorStyles.label);
        warningStyle.normal.textColor = new Color(1f, 0.92f, 0.016f);
        warningStyle.wordWrap = true;
        warningStyle.richText = true;

        errorStyle = new GUIStyle(EditorStyles.label);
        errorStyle.normal.textColor = new Color(1f, 0.3f, 0.3f);
        errorStyle.wordWrap = true;
        errorStyle.richText = true;

        exceptionStyle = new GUIStyle(EditorStyles.label);
        exceptionStyle.normal.textColor = new Color(1f, 0.5f, 1f);
        exceptionStyle.wordWrap = true;
        exceptionStyle.richText = true;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (logs.Count > 1000) // 限制日志数量
        {
            logs.RemoveAt(0);
        }

        logs.Add(new LogEntry
        {
            message = logString,
            stackTrace = stackTrace,
            type = type,
            timestamp = DateTime.Now
        });

        // 如果启用了自动滚动，则滚动到底部
        if (autoScroll)
        {
            logScroll.y = float.MaxValue;
            Repaint();
        }
    }

    private void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Please enter Play Mode to use the Runtime Logger.", MessageType.Info);
            return;
        }

        DrawToolbar();
        EditorGUILayout.Space();
        DrawMainContent();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        // 清除按钮
        if (GUILayout.Button("Clear", EditorStyles.toolbarButton))
        {
            logs.Clear();
        }

        // 日志过滤器
        EditorGUILayout.Space();
        logTypeEnabled[0] = GUILayout.Toggle(logTypeEnabled[0], "Log", EditorStyles.toolbarButton);
        logTypeEnabled[1] = GUILayout.Toggle(logTypeEnabled[1], "Warning", EditorStyles.toolbarButton);
        logTypeEnabled[2] = GUILayout.Toggle(logTypeEnabled[2], "Error", EditorStyles.toolbarButton);
        logTypeEnabled[3] = GUILayout.Toggle(logTypeEnabled[3], "Exception", EditorStyles.toolbarButton);

        // 显示堆栈跟踪
        EditorGUILayout.Space();
        showStackTrace = GUILayout.Toggle(showStackTrace, "Stack Trace", EditorStyles.toolbarButton);
        
        // 自动滚动
        autoScroll = GUILayout.Toggle(autoScroll, "Auto Scroll", EditorStyles.toolbarButton);

        EditorGUILayout.EndHorizontal();

        // 搜索框
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
        searchFilter = EditorGUILayout.TextField(searchFilter);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMainContent()
    {
        // 创建分割视图
        EditorGUILayout.BeginVertical();

        // 日志视图区域
        GUILayout.Label("Logs", EditorStyles.boldLabel);
        float logHeight = position.height * 0.6f; // 60% 的窗口高度
        DrawLogView(logHeight);

        EditorGUILayout.Space();

        // 代码执行区域
        GUILayout.Label("Code Execution", EditorStyles.boldLabel);
        DrawCodeExecutor(position.height - logHeight - 60); // 减去标题和间距的高度

        EditorGUILayout.EndVertical();
    }

    private void DrawLogView(float height)
    {
        logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(height));

        foreach (var log in logs)
        {
            if (!ShouldShowLog(log))
                continue;

            EditorGUILayout.BeginHorizontal();

            // 时间戳
            EditorGUILayout.LabelField(log.timestamp.ToString("HH:mm:ss.fff"), GUILayout.Width(100));

            // 日志内容
            GUIStyle style = GetLogStyle(log.type);
            EditorGUILayout.LabelField(log.message, style);

            EditorGUILayout.EndHorizontal();

            // 堆栈跟踪
            if (showStackTrace && !string.IsNullOrEmpty(log.stackTrace))
            {
                EditorGUILayout.LabelField(log.stackTrace, EditorStyles.miniLabel);
            }

            EditorGUILayout.Space();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawCodeExecutor(float height)
    {
        // 代码示例选择
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Examples");
        var exampleNames = codeExamples.Keys.ToArray();
        selectedExampleIndex = EditorGUILayout.Popup(selectedExampleIndex, exampleNames);
        if (GUILayout.Button("Load", GUILayout.Width(60)))
        {
            codeToExecute = codeExamples[exampleNames[selectedExampleIndex]];
            GUI.FocusControl(null);
        }
        

        // 执行按钮
        EditorGUI.BeginDisabledGroup(isExecuting || string.IsNullOrWhiteSpace(codeToExecute));
        if (GUILayout.Button(isExecuting ? "Executing..." : "Execute Code"))
        {
            ExecuteCode();
        }
        EditorGUILayout.EndHorizontal();

        // 代码编辑区域
        EditorGUILayout.LabelField("Code:", EditorStyles.boldLabel);
        codeScroll = EditorGUILayout.BeginScrollView(codeScroll, GUILayout.Height(height - 60));
        codeToExecute = EditorGUILayout.TextArea(codeToExecute, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUI.EndDisabledGroup();
    }

    private async void ExecuteCode()
    {
        TryGenerateUnityRemoteLogger();
        if (UnityRemoteLogger == null)
        {
            Debug.LogError("UnityRemoteLogger not found in the scene!");
            return;
        }

        isExecuting = true;
        try
        {
#if !RELEASE
            UnityRemoteLogger.ExecuteCodeLocally(codeToExecute);
#endif
        }
        finally
        {
            isExecuting = false;
        }
    }

    private bool ShouldShowLog(LogEntry log)
    {
        // 检查日志类型是否启用
        bool typeEnabled = false;
        switch (log.type)
        {
            case LogType.Log:
                typeEnabled = logTypeEnabled[0];
                break;
            case LogType.Warning:
                typeEnabled = logTypeEnabled[1];
                break;
            case LogType.Error:
                typeEnabled = logTypeEnabled[2];
                break;
            case LogType.Exception:
                typeEnabled = logTypeEnabled[3];
                break;
            default:
                typeEnabled = true;
                break;
        }
        if (!typeEnabled)
            return false;

        // 检查搜索过滤器
        if (!string.IsNullOrEmpty(searchFilter))
        {
            return log.message.ToLower().Contains(searchFilter.ToLower()) ||
                   (showStackTrace && log.stackTrace?.ToLower().Contains(searchFilter.ToLower()) == true);
        }

        return true;
    }

    private GUIStyle GetLogStyle(LogType type)
    {
        switch (type)
        {
            case LogType.Warning:
                return warningStyle;
            case LogType.Error:
                return errorStyle;
            case LogType.Exception:
                return exceptionStyle;
            default:
                return logStyle;
        }
    }

    private class LogEntry
    {
        public string message;
        public string stackTrace;
        public LogType type;
        public DateTime timestamp;
    }
}
#endif