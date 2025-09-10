using LLMUnity;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace AIChatDemo
{
    public class LLMManager : MonoBehaviour
    {
        [HideInInspector] public GameManager gm;

        [Header("Server Settings")]
        public bool remote = false; // 保留字段，当前仅支持本地
        public int port = 13333;
        public bool dontDestroyOnLoad = true;
        public bool debugServer = false;
        public int numThreads = -1;
        public int numGPULayers = 0;
        public int contextSize = 8192;
        public int batchSize = 512;

        [Header("Model")]
        public string model = ""; // 选中的模型（相对 StreamingAssets/LLMManager 的路径）
        public string chatTemplate = ChatTemplate.DefaultTemplate;
        public List<string> loras = new List<string>(); // 选中的 LoRA（相对路径或管理名）

        [Header("Chat Settings")]
        public bool stream = true;
        public string playerName = "user";
        public string AIName = "assistant";
        [TextArea(5, 10)]
        public string prompt = "A chat between a curious human and an artificial intelligence assistant. The assistant gives helpful, detailed, and polite answers to the human's questions.";
        public bool clearHistoryOnStart = true;

        [Header("Generation")]
        public int numPredict = -1;
        public int topK = 40;
        [Range(0f, 1f)] public float topP = 0.9f;
        [Range(0f, 1f)] public float minP = 0.05f;
        [Range(0f, 2f)] public float temperature = 0.2f;
        [Range(0f, 1f)] public float typicalP = 1f;
        [Range(0f, 2f)] public float repeatPenalty = 1.1f;
        public int repeatLastN = 64;
        public bool penalizeNl = true;
        [Range(0f, 1f)] public float presencePenalty = 0f;
        [Range(0f, 1f)] public float frequencyPenalty = 0f;
        public int mirostat = 0;
        [Range(0f, 10f)] public float mirostatTau = 5f;
        [Range(0f, 1f)] public float mirostatEta = 0.1f;
        public bool ignoreEos = false;
        public int nProbs = 0;
        public bool cachePrompt = true;
        public int nKeep = -1; // 不强制计算，保持默认

        readonly List<ChatMessage> chat = new List<ChatMessage>();
        ChatTemplate template = null;

        LLM llm;

        // 并发保护
        bool isChatting = false;

        // 流式解析缓存与结果累积（最简正则）
        readonly StringBuilder _resultBuilder = new StringBuilder();
        int _sseMatchCount = 0;
        static readonly Regex sContentRegex = new Regex("\"content\"\\s*:\\s*\"(.*?)\"", RegexOptions.Compiled | RegexOptions.Singleline);

        void Start()
        {
            if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
            if (clearHistoryOnStart) ClearChat();
            else InitSystemPromptIfMissing();

            _ = EnsureServer();

            if (gm.sendButton != null)
            {
                gm.sendButton.onClick.RemoveAllListeners();
                gm.sendButton.onClick.AddListener(() =>
                {
                    string text = gm.inputField != null ? gm.inputField.text : "";
                    _ = Chat(text);
                });
            }
        }

        void OnDestroy()
        {
            if (llm != null) llm.Destroy();
        }

        // Chat接口：输入对话，根据GameManager是否勾选语音模式走流式输出/收集完全部输出后走TTS转语音
        public async Task Chat(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (isChatting)
            {
                Debug.LogWarning("Chat is already running, please wait until it finishes.");
                return;
            }

            isChatting = true;
            if (gm.outputText != null) gm.outputText.text += $"{playerName}: {message}\n";
            if (gm.inputField != null) gm.inputField.text = "";
            if (gm.sendButton != null) gm.sendButton.interactable = false;

            try
            {
                await EnsureServer();
                await EnsureTemplate();

                // 记录“LLM: ”之前的所有文本，后续流式在此基础上拼接
                string transcriptPrefix = gm.outputText != null ? gm.outputText.text += $"{AIName}: " : "";

                // 构建带历史的 prompt
                string promptText = ComputePromptWithQuery(message);

                // 组装请求参数并序列化
                ChatRequest req = BuildChatRequest(promptText);
                string json = ChatRequestToJson(req);

                // 准备流式缓冲
                _resultBuilder.Clear();
                _sseMatchCount = 0;

                // 流式回调：只拼接 chunk 中所有新增的 "content"
                Callback<string> streamCb = (chunk) =>
                {
                    if (LooksLikeJsonSSE(chunk))
                    {
                        // 只处理“新出现”的匹配
                        var matches = sContentRegex.Matches(chunk);
                        if (matches.Count < _sseMatchCount) _sseMatchCount = 0;

                        for (int i = _sseMatchCount; i < matches.Count; i++)
                        {
                            string captured = matches[i].Groups[1].Value;
                            string add = Regex.Unescape(captured);
                            if (!string.IsNullOrEmpty(add)) _resultBuilder.Append(add);
                        }
                        _sseMatchCount = matches.Count;
                    }
                    else _resultBuilder.Append(chunk);

                    // 实时显示：以“LLM: ”之前的前缀 + 已累积的新增文本
                    if (gm.outputText != null && !gm.isSynthesizedVoice) gm.outputText.text = transcriptPrefix + _resultBuilder.ToString();
                };

                // 发起请求
                string fullRaw = await llm.Completion(json, stream ? streamCb : null);

                // 收尾：stream 模式直接拿累积结果；非 stream 模式解析一次性输出
                string finalText = stream ? _resultBuilder.ToString() : ExtractAllContent(fullRaw);
                if (gm.isSynthesizedVoice) StartCoroutine(gm.ttsManager.GetVoice(finalText));
                // 更新历史
                AddPlayerMessage(message); AddAIMessage(finalText);

                Debug.Log($"LLMManager.Chat - full reply: {finalText}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Chat failed: {e.Message}");
                if (gm.outputText != null) gm.outputText.text += "\n(error) " + e.Message + "\n";
            }
            finally
            {
                // UI 恢复
                if (gm.inputField != null) gm.inputField.Select();
                if (gm.outputText != null && !gm.isSynthesizedVoice) gm.outputText.text += '\n';
                if (gm.sendButton != null) gm.sendButton.interactable = true;

                // 清理状态
                _sseMatchCount = 0;
                isChatting = false;
            }
        }

        public void ClearChat()
        {
            chat.Clear();
            chat.Add(new ChatMessage { role = "system", content = prompt });
        }

        public void SetPrompt(string newPrompt, bool clear = true)
        {
            prompt = newPrompt;
            if (clear) ClearChat();
            else
            {
                if (chat.Count == 0) chat.Add(new ChatMessage { role = "system", content = prompt });
                else chat[0] = new ChatMessage { role = "system", content = prompt };
            }
        }

        async Task EnsureServer()
        {
            if (llm != null) return;
            if (string.IsNullOrEmpty(model))
            {
                Debug.LogError("LLMManager: No model selected. Please pick a model in the inspector.");
                throw new Exception("No model selected");
            }

            // 用“先禁用后启用”的方式确保在 Awake 前设置参数
            GameObject go = new GameObject("LLM_Server");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            llm = go.AddComponent<LLM>();

            // 映射 server 配置
            llm.remote = remote;
            llm.port = port;
            llm.debug = debugServer;
            llm.dontDestroyOnLoad = dontDestroyOnLoad;
            llm.numThreads = numThreads;
            llm.numGPULayers = numGPULayers;
            llm.contextSize = contextSize;
            llm.batchSize = batchSize;

            // 模型/模板/LoRA
            llm.SetTemplate(chatTemplate, setDirty: false);
            llm.SetModel(model);
            llm.RemoveLoras();
            foreach (var l in loras) llm.AddLora(l);

            // 启动
            go.SetActive(true);

            // 等待 ready
            await llm.WaitUntilReady();
        }

        async Task EnsureTemplate()
        {
            if (template != null) return;
            string tmplName = llm.GetTemplate();
            template = ChatTemplate.GetTemplate(tmplName);
            await Task.Yield();
        }

        string ComputePromptWithQuery(string userQuery)
        {
            // 构建一个临时历史（不立即加入）
            List<ChatMessage> temp = new List<ChatMessage>(chat);
            temp.Add(new ChatMessage { role = playerName, content = userQuery });
            string promptText = template.ComputePrompt(temp, playerName, AIName);
            return promptText;
        }

        ChatRequest BuildChatRequest(string promptText)
        {
            ChatRequest r = new ChatRequest
            {
                prompt = promptText,
                id_slot = -1,
                temperature = temperature,
                top_k = topK,
                top_p = topP,
                min_p = minP,
                n_predict = numPredict,
                n_keep = nKeep,
                stream = stream,
                stop = new List<string>(template.GetStop(playerName, AIName)),
                typical_p = typicalP,
                repeat_penalty = repeatPenalty,
                repeat_last_n = repeatLastN,
                penalize_nl = penalizeNl,
                presence_penalty = presencePenalty,
                frequency_penalty = frequencyPenalty,
                penalty_prompt = null,
                mirostat = mirostat,
                mirostat_tau = mirostatTau,
                mirostat_eta = mirostatEta,
                grammar = null,
                json_schema = null,
                seed = 0,
                ignore_eos = ignoreEos,
                logit_bias = null,
                n_probs = nProbs,
                cache_prompt = cachePrompt
            };
            return r;
        }

        // 与 LLMCharacter 同步的拼接（处理 grammar/json_schema）
        class GrammarWrapper { public string grammar; }

        string ChatRequestToJson(ChatRequest request)
        {
            string json = JsonUtility.ToJson(request);
            int idx = json.LastIndexOf('}');
            if (!string.IsNullOrEmpty(request.grammar))
            {
                GrammarWrapper gw = new GrammarWrapper { grammar = request.grammar };
                string gwJson = JsonUtility.ToJson(gw);
                int s = gwJson.IndexOf(":\"") + 2;
                int e = gwJson.LastIndexOf("\"");
                string g = gwJson.Substring(s, e - s);
                json = json.Insert(idx, $",\"grammar\": \"{g}\"");
            }
            else if (!string.IsNullOrEmpty(request.json_schema))
            {
                json = json.Insert(idx, $",\"json_schema\":{request.json_schema}");
            }
            return json;
        }

        void InitSystemPromptIfMissing()
        {
            if (chat.Count == 0 || chat[0].role != "system")
            {
                ClearChat();
            }
        }

        void AddPlayerMessage(string content) => chat.Add(new ChatMessage { role = playerName, content = content });
        void AddAIMessage(string content) => chat.Add(new ChatMessage { role = AIName, content = content });

        public void SetModel(string filename)
        {
            model = filename;
            chatTemplate = ChatTemplate.DefaultTemplate;
        }

        public void AddLora(string filename)
        {
            if (!loras.Contains(filename)) loras.Add(filename);
        }

        public void RemoveLora(string filename)
        {
            loras.Remove(filename);
        }

        static string ExtractAllContent(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            var matches = sContentRegex.Matches(s);
            foreach (Match m in matches)
            {
                string captured = m.Groups[1].Value;
                string add = Regex.Unescape(captured);
                if (!string.IsNullOrEmpty(add)) sb.Append(add);
            }
            return sb.ToString();
        }

        static bool LooksLikeJsonSSE(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Contains("data:")) return true;
            int brace = s.IndexOf('{');
            return brace >= 0 && s.IndexOf('}', brace + 1) > brace;
        }
    }
}