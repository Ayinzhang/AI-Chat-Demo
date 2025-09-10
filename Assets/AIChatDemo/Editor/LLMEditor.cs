using LLMUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AIChatDemo
{
    [CustomEditor(typeof(LLMManager))]
    public class LLMEditor : PropertyEditor
    {
        private ReorderableList modelList;
        static float nameColumnWidth = 150f;
        static float templateColumnWidth = 150f;
        static float textColumnWidth = 150f;
        static float includeInBuildColumnWidth = 30f;
        static float actionColumnWidth = 20f;
        static int elementPadding = 10;
        static GUIContent trashIcon;
        static List<string> modelNames;
        static List<string> modelOptions;
        static List<string> modelLicenses;
        static List<string> modelURLs;
        string elementFocus = "";
        bool showCustomURL = false;
        string customURL = "";
        bool customURLLora = false;
        bool customURLFocus = false;
        bool expandedView = false;


        void OnEnable()
        {
            ResetModelOptions();
            trashIcon = new GUIContent(Resources.Load<Texture2D>("llmunity_trash_icon"), "Delete Model");
            Texture2D loraLineTexture = new Texture2D(1, 1);
            loraLineTexture.SetPixel(0, 0, Color.black);
            loraLineTexture.Apply();
            modelList = new ReorderableList(LLMUnity.LLMManager.modelEntries, typeof(ModelEntry), false, true, false, false)
            {
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index >= LLMUnity.LLMManager.modelEntries.Count) return;
                    var mgr = (LLMManager)target;
                    ModelEntry entry = LLMUnity.LLMManager.modelEntries[index];
                    List<Rect> rects = CreateColumnRects(rect);
                    int col = 0;
                    Rect selectRect = rects[col++];
                    Rect nameRect = rects[col++];
                    Rect templateRect = rects[col++];
                    Rect urlRect = new Rect();
                    Rect pathRect = new Rect();
                    if (expandedView)
                    {
                        urlRect = rects[col++];
                        pathRect = rects[col++];
                    }
                    Rect includeInBuildRect = rects[col++];
                    Rect actionRect = rects[col++];
                    bool isSelected = false;
                    if (!entry.lora)
                    {
                        isSelected = mgr.model == entry.filename;
                        bool newSelected = EditorGUI.Toggle(selectRect, isSelected, EditorStyles.radioButton);
                        if (newSelected && !isSelected)
                        {
                            mgr.SetModel(entry.filename);
                            mgr.chatTemplate = entry.chatTemplate;
                            EditorUtility.SetDirty(mgr);
                        }
                    }
                    else
                    {
                        isSelected = mgr.loras.Contains(entry.filename);
                        bool newSelected = EditorGUI.Toggle(selectRect, isSelected);
                        if (newSelected && !isSelected) mgr.AddLora(entry.filename);
                        else if (!newSelected && isSelected) mgr.RemoveLora(entry.filename);
                        EditorUtility.SetDirty(mgr);
                    }
                    DrawCopyableLabel(nameRect, entry.label, entry.filename);
                    if (!entry.lora)
                    {
                        string[] templateDescriptions = ChatTemplate.templatesDescription.Keys.ToList().ToArray();
                        string[] templates = ChatTemplate.templatesDescription.Values.ToList().ToArray();
                        int templateIndex = Array.IndexOf(templates, entry.chatTemplate);
                        int newTemplateIndex = EditorGUI.Popup(templateRect, templateIndex, templateDescriptions);
                        if (newTemplateIndex != templateIndex)
                        {
                            LLMUnity.LLMManager.SetTemplate(entry.filename, templates[newTemplateIndex]);
                            // 如果当前选中该模型，同步管理器的模板
                            if (mgr.model == entry.filename) mgr.chatTemplate = templates[newTemplateIndex];
                            Repaint();
                        }
                    }
                    if (expandedView)
                    {
                        bool hasURL = !string.IsNullOrEmpty(entry.url);
                        if (hasURL) DrawCopyableLabel(urlRect, entry.url);
                        else
                        {
                            string newURL = EditorGUI.TextField(urlRect, entry.url);
                            if (newURL != entry.url)
                            {
                                LLMUnity.LLMManager.SetURL(entry, newURL);
                                Repaint();
                            }
                        }
                        DrawCopyableLabel(pathRect, entry.path);
                    }
                    bool includeInBuild = EditorGUI.ToggleLeft(includeInBuildRect, "", entry.includeInBuild);
                    if (includeInBuild != entry.includeInBuild)
                    {
                        LLMUnity.LLMManager.SetIncludeInBuild(entry, includeInBuild);
                        Repaint();
                    }
                    if (GUI.Button(actionRect, trashIcon))
                    {
                        if (isSelected)
                        {
                            if (!entry.lora) mgr.SetModel("");
                            else mgr.RemoveLora(entry.filename);
                            EditorUtility.SetDirty(mgr);
                        }
                        LLMUnity.LLMManager.Remove(entry);
                        ResetModelOptions();
                        Repaint();
                    }
                    if (!entry.lora && index < LLMUnity.LLMManager.modelEntries.Count - 1 && LLMUnity.LLMManager.modelEntries[index + 1].lora)
                    {
                        GUI.DrawTexture(new Rect(rect.x - ReorderableList.Defaults.padding, rect.yMax, rect.width + ReorderableList.Defaults.padding * 2, 1), loraLineTexture);
                    }
                },
                drawHeaderCallback = (rect) =>
                {
                    List<Rect> rects = CreateColumnRects(rect);
                    int col = 0;
                    EditorGUI.LabelField(rects[col++], "");
                    EditorGUI.LabelField(rects[col++], "Model");
                    EditorGUI.LabelField(rects[col++], "Chat template");
                    if (expandedView)
                    {
                        EditorGUI.LabelField(rects[col++], "URL");
                        EditorGUI.LabelField(rects[col++], "Path");
                    }
                    EditorGUI.LabelField(rects[col++], "Build");
                    EditorGUI.LabelField(rects[col++], "");
                },
                drawFooterCallback = { },
                footerHeight = 0
            };
        }
        public override void OnInspectorGUI()
        {
            if (elementFocus != "")
            {
                EditorGUI.FocusTextInControl(elementFocus);
                elementFocus = "";
            }
            var mgr = (LLMManager)target;
            SerializedObject so = new SerializedObject(mgr);
            OnInspectorGUIStart(so);
            // 进度条
            // ShowProgress(LLMUnity.LLMUnitySetup.libraryProgress, "Setup Library");
            ShowProgress(LLMUnity.LLMManager.modelProgress, "Model Downloading");
            ShowProgress(LLMUnity.LLMManager.loraProgress, "LoRA Downloading");
            GUI.enabled = LLMUnity.LLMManager.modelProgress == 1 && LLMUnity.LLMManager.loraProgress == 1;
            // 顶部选项
            AddDebugModeToggle();
            EditorGUILayout.Space();
            // 服务器设置
            EditorGUILayout.LabelField("Server Settings", EditorStyles.boldLabel);
            mgr.remote = EditorGUILayout.Toggle("Remote", mgr.remote);
            mgr.port = EditorGUILayout.IntField("Port", mgr.port);
            mgr.dontDestroyOnLoad = EditorGUILayout.Toggle("Dont Destroy On Load", mgr.dontDestroyOnLoad);
            mgr.debugServer = EditorGUILayout.Toggle("Debug Server Log", mgr.debugServer);
            mgr.numThreads = EditorGUILayout.IntField("Threads", mgr.numThreads);
            mgr.numGPULayers = EditorGUILayout.IntField("GPU Layers", mgr.numGPULayers);
            mgr.contextSize = EditorGUILayout.IntField("Context Size", mgr.contextSize);
            mgr.batchSize = EditorGUILayout.IntField("Batch Size", mgr.batchSize);
            Space();
            // 模型选择
            AddModelLoaders(so, mgr);
            // 对话设置
            EditorGUILayout.LabelField("Chat Settings", EditorStyles.boldLabel);
            mgr.stream = EditorGUILayout.Toggle("Stream", mgr.stream);
            mgr.playerName = EditorGUILayout.TextField("Player Name", mgr.playerName);
            mgr.AIName = EditorGUILayout.TextField("AI Name", mgr.AIName);
            EditorGUILayout.LabelField("System Prompt");
            mgr.prompt = GUILayout.TextArea(mgr.prompt, GUILayout.MinHeight(60));
            Space();
            // 生成设置
            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);
            mgr.numPredict = EditorGUILayout.IntField("Max Tokens (n_predict)", mgr.numPredict);
            mgr.temperature = EditorGUILayout.Slider("Temperature", mgr.temperature, 0f, 2f);
            mgr.topK = EditorGUILayout.IntSlider("Top-K", mgr.topK, -1, 100);
            mgr.topP = EditorGUILayout.Slider("Top-P", mgr.topP, 0f, 1f);
            mgr.minP = EditorGUILayout.Slider("Min-P", mgr.minP, 0f, 1f);
            mgr.typicalP = EditorGUILayout.Slider("Typical-P", mgr.typicalP, 0f, 1f);
            mgr.repeatPenalty = EditorGUILayout.Slider("Repeat Penalty", mgr.repeatPenalty, 0f, 2f);
            mgr.repeatLastN = EditorGUILayout.IntSlider("Repeat Last N", mgr.repeatLastN, 0, 2048);
            mgr.penalizeNl = EditorGUILayout.Toggle("Penalize Newline", mgr.penalizeNl);
            mgr.presencePenalty = EditorGUILayout.Slider("Presence Penalty", mgr.presencePenalty, 0f, 1f);
            mgr.frequencyPenalty = EditorGUILayout.Slider("Frequency Penalty", mgr.frequencyPenalty, 0f, 1f);
            mgr.mirostat = EditorGUILayout.IntSlider("Mirostat", mgr.mirostat, 0, 2);
            mgr.mirostatTau = EditorGUILayout.Slider("Mirostat Tau", mgr.mirostatTau, 0f, 10f);
            mgr.mirostatEta = EditorGUILayout.Slider("Mirostat Eta", mgr.mirostatEta, 0f, 1f);
            mgr.ignoreEos = EditorGUILayout.Toggle("Ignore EOS", mgr.ignoreEos);
            mgr.nProbs = EditorGUILayout.IntSlider("Top-N Probs", mgr.nProbs, 0, 10);
            mgr.cachePrompt = EditorGUILayout.Toggle("Cache Prompt", mgr.cachePrompt);
            OnInspectorGUIEnd(so);
        }

        public void AddModelLoaders(SerializedObject so, LLMManager mgr)
        {
            if (LLMUnity.LLMManager.modelEntries.Count > 0)
            {
                float[] widths = GetColumnWidths(expandedView);
                float listWidth = 2 * ReorderableList.Defaults.padding;
                foreach (float width in widths) listWidth += width + (listWidth == 0 ? 0 : elementPadding);
                EditorGUILayout.BeginHorizontal(GUILayout.Width(listWidth + actionColumnWidth));
                EditorGUILayout.BeginVertical(GUILayout.Width(listWidth));
                modelList.DoLayoutList();
                EditorGUILayout.EndVertical();
                Rect expandedRect = GUILayoutUtility.GetRect(actionColumnWidth, modelList.elementHeight + ReorderableList.Defaults.padding);
                expandedRect.y += modelList.GetHeight() - modelList.elementHeight - ReorderableList.Defaults.padding;
                if (GUI.Button(expandedRect, expandedView ? "«" : "»")) { expandedView = !expandedView; Repaint(); }
                EditorGUILayout.EndHorizontal();
            }
            _ = AddLoadButtons(mgr);
            bool downloadOnStart = EditorGUILayout.Toggle("Download on Start", LLMUnity.LLMManager.downloadOnStart);
            if (downloadOnStart != LLMUnity.LLMManager.downloadOnStart) LLMUnity.LLMManager.SetDownloadOnStart(downloadOnStart);
            Space();
        }

        async Task AddLoadButtons(LLMManager mgr)
        {
            if (showCustomURL) await createCustomURLField(mgr);
            else await createButtons(mgr);
        }

        async Task createButtons(LLMManager mgr)
        {
            EditorGUILayout.BeginHorizontal();
            GUIStyle centeredPopupStyle = new GUIStyle(EditorStyles.popup) { alignment = TextAnchor.MiddleCenter };
            int modelIndex = EditorGUILayout.Popup(0, modelOptions.ToArray(), centeredPopupStyle, GUILayout.Width(buttonWidth));
            if (modelIndex == 1) showCustomURLField(false);
            else if (modelIndex > 1)
            {
                if (modelLicenses[modelIndex] != null)
                    LLMUnitySetup.LogWarning($"The {modelNames[modelIndex]} model is released under: {modelLicenses[modelIndex]}");
                string filename = await LLMUnity.LLMManager.DownloadModel(modelURLs[modelIndex], true, modelNames[modelIndex]);
                SetModelIfNone(mgr, filename, false);
                ResetModelOptions();
                Repaint();
            }
            if (GUILayout.Button("Load model", GUILayout.Width(buttonWidth)))
            {
                EditorApplication.delayCall += () =>
                {
                    string path = EditorUtility.OpenFilePanelWithFilters("Select a gguf model file", "", new string[] { "Model Files", "gguf" });
                    if (!string.IsNullOrEmpty(path))
                    {
                        string filename = LLMUnity.LLMManager.LoadModel(path, true);
                        SetModelIfNone(mgr, filename, false); Repaint();
                    }
                };
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Download LoRA", GUILayout.Width(buttonWidth)))
            {
                showCustomURLField(true);
            }
            if (GUILayout.Button("Load LoRA", GUILayout.Width(buttonWidth)))
            {
                EditorApplication.delayCall += () =>
                {
                    string path = EditorUtility.OpenFilePanelWithFilters("Select a gguf lora file", "", new string[] { "Model Files", "gguf" });
                    if (!string.IsNullOrEmpty(path))
                    {
                        string filename = LLMUnity.LLMManager.LoadLora(path, true);
                        SetModelIfNone(mgr, filename, true); Repaint();
                    }
                };
            }
            EditorGUILayout.EndHorizontal();
        }

        void SetModelIfNone(LLMManager mgr, string filename, bool lora)
        {
            int num = Num(lora);
            if (!lora && string.IsNullOrEmpty(mgr.model) && num == 1) mgr.SetModel(filename);
            if (lora) mgr.AddLora(filename);
        }

        int Num(bool lora)
        {
            int cnt = 0;
            foreach (var e in LLMUnity.LLMManager.modelEntries) if (e.lora == lora) cnt++;
            return cnt;
        }

        async Task createCustomURLField(LLMManager mgr)
        {
            bool submit = false;
            bool exit = false;
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { submit = true; e.Use(); }
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape)) { exit = true; e.Use(); }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Enter URL", GUILayout.Width(100));
                GUI.SetNextControlName("customURLFocus");
                customURL = EditorGUILayout.TextField(customURL, GUILayout.Width(buttonWidth));
                submit = GUILayout.Button("Submit", GUILayout.Width(buttonWidth / 2));
                exit = GUILayout.Button("Back", GUILayout.Width(buttonWidth / 2));
                EditorGUILayout.EndHorizontal();
                if (customURLFocus) { customURLFocus = false; elementFocus = "customURLFocus"; }
            }
            if (exit || submit)
            {
                showCustomURL = false; elementFocus = "dummy"; Repaint();
                if (submit && customURL != "")
                {
                    string filename = await LLMUnity.LLMManager.Download(customURL, customURLLora, true);
                    SetModelIfNone(mgr, filename, customURLLora);
                    ResetModelOptions(); Repaint();
                }
            }
        }

        static void ResetModelOptions()
        {
            List<string> existingOptions = new List<string>();
            foreach (ModelEntry entry in LLMUnity.LLMManager.modelEntries) existingOptions.Add(entry.url);
            modelOptions = new List<string>() { "Download model", "Custom URL" };
            modelNames = new List<string>() { null, null };
            modelURLs = new List<string>() { null, null };
            modelLicenses = new List<string>() { null, null };
            foreach (var entry in LLMUnitySetup.modelOptions)
            {
                string category = entry.Key;
                foreach ((string name, string url, string license) in entry.Value)
                {
                    if (url != null && existingOptions.Contains(url)) continue;
                    modelOptions.Add(category + "/" + name);
                    modelNames.Add(name);
                    modelURLs.Add(url);
                    modelLicenses.Add(license);
                }
            }
        }

        float[] GetColumnWidths(bool expanded)
        {
            List<float> widths = new List<float>() { actionColumnWidth, nameColumnWidth, templateColumnWidth };
            if (expanded) widths.AddRange(new List<float>() { textColumnWidth, textColumnWidth });
            widths.AddRange(new List<float>() { includeInBuildColumnWidth, actionColumnWidth });
            return widths.ToArray();
        }

        List<Rect> CreateColumnRects(Rect rect)
        {
            float[] widths = GetColumnWidths(expandedView);
            float offsetX = rect.x;
            float offsetY = rect.y + (rect.height - EditorGUIUtility.singleLineHeight) / 2;
            List<Rect> rects = new List<Rect>();
            foreach (float width in widths)
            {
                rects.Add(new Rect(offsetX, offsetY, width, EditorGUIUtility.singleLineHeight));
                offsetX += width + elementPadding;
            }
            return rects;
        }

        void showCustomURLField(bool lora)
        {
            customURL = ""; customURLLora = lora;
            showCustomURL = true; customURLFocus = true; Repaint();
        }

        void ShowProgress(float progress, string progressText)
        {
            if (progress != 1) EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, progressText);
        }

        void DrawCopyableLabel(Rect rect, string label, string text = "")
        {
            if (text == "") text = label;
            EditorGUI.LabelField(rect, label);
            if (Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition))
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Copy"), false, () => CopyToClipboard(text));
                menu.ShowAsContext(); Event.current.Use();
            }
        }

        void CopyToClipboard(string text)
        {
            TextEditor te = new TextEditor { text = text };
            te.SelectAll(); te.Copy();
        }
    }
}