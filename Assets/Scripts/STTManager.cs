using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.UI;
using Whisper;
using Whisper.Native;

public class STTManager : MonoBehaviour
{
    public enum ModelType { Tiny, Base, Small, Medium, Large };
    [Header("Model")]
    [Tooltip("模型位于StreamingAssets/Whisper中")]
    public ModelType modelType = ModelType.Small;
    public bool useGpu = false;
    public bool flashAttention = false;

    [Header("Language")]
    [Tooltip("输出语言。空串表示自动检测")]
    public string language = "zh";
    public bool translateToEnglish = false;

    [Header("Streaming")]
    [Tooltip("每隔多少秒触发一次增量转写")]
    public float stepSec = 3f;
    [Tooltip("启用简单能量VAD（静音跳过一次推理）")]
    public bool useVad = true;
    [Tooltip("VAD 计算能量的窗口（秒）")]
    public float vadLastSec = 1.0f;
    [Tooltip("VAD 触发阈值（均方根能量）")]
    public float vadRmsThreshold = 0.005f;
    [Tooltip("推理时不使用历史上下文（流式建议保持 true）")]
    public bool noContext = true;
    [Tooltip("是否仅输出单段（一般流式建议 false 以便多段滚动）")]
    public bool singleSegment = false;

    [Header("Microphone")]
    [Tooltip("采样率（常用 16000）")]
    public int micFrequency = 16000;
    [Tooltip("麦克风循环缓冲最大长度（秒）")]
    public int micMaxLengthSec = 60;
    [Tooltip("录音时是否循环写入缓冲区")]
    public bool micLoop = false;
    [Tooltip("麦克风输入设备名（留空使用默认设备）")]
    public string micDevice = null;

    [Header("UI")]
    public Button startStopButton;
    public Text startStopButtonText;
    public InputField outputField; // 用于展示增量文本（可为空）
    public string currentText = "";// 当前识别文本（增量）

    WhisperWrapper whisper;
    WhisperParams wparams;

    // Mic
    AudioClip micClip;
    bool isRecording; int lastMicPos, channels = 1;

    // Ring buffer for last `lengthSec` audio
    readonly List<float> ring = new List<float>(256000);
    int RingMaxSamples => Mathf.CeilToInt(micMaxLengthSec * micFrequency * channels);

    // Streaming
    bool isStreaming, isInferencing; float nextInferTime;
    readonly ConcurrentQueue<Action> mainThread = new();

    void Awake()
    {
        _ = InitModel(); startStopButton?.onClick.AddListener(ToggleStartStop);
        if (micDevice == null) micDevice = Microphone.devices[Microphone.devices.Length - 1];
    }

    void Update()
    {
        // 主线程执行队列（确保UI/Unity对象在主线程更新）
        while (mainThread.TryDequeue(out var a)) a?.Invoke();

        // 自动在录满时停止
        // if (isRecording && !Microphone.IsRecording(micDevice)) { StopStreaming(); return; }
        if (isRecording) AppendNewMicData();
        if (isStreaming && !isInferencing && Time.realtimeSinceStartup >= nextInferTime)
        {
            int need = Mathf.CeilToInt(stepSec * micFrequency * channels);
            if (ring.Count >= need) TriggerInferenceOnce();
            else nextInferTime = Time.realtimeSinceStartup + stepSec;
        }
    }

    void OnDestroy()
    {
        StopStreaming(); if (micClip != null) { Microphone.End(micDevice); Destroy(micClip); }
    }

    public async Task InitModel()
    {
        if (whisper != null) return;

        var fullPath = Path.Combine(Application.streamingAssetsPath, $"Whisper/ggml-{modelType.ToString().ToLower()}.bin");

        var ctx = WhisperContextParams.GetDefaultParams();
        ctx.UseGpu = useGpu; ctx.FlashAttn = flashAttention;

        whisper = await WhisperWrapper.InitFromFileAsync(fullPath, ctx);
        if (whisper == null)
        {
            Debug.LogError("Failed to init Whisper model.");
            return;
        }

        wparams = WhisperParams.GetDefaultParams(WhisperSamplingStrategy.WHISPER_SAMPLING_BEAM_SEARCH);
        wparams.Language = language;
        wparams.Translate = translateToEnglish;
        wparams.NoContext = noContext;
        wparams.SingleSegment = singleSegment;
        wparams.EnableTokens = false;
        wparams.TokenTimestamps = false;
        wparams.InitialPrompt = null;
        wparams.AudioCtx = 0;
    }

    public async void ToggleStartStop()
    {
        if (isStreaming) StopStreaming();
        else await StartStreaming();
    }

    public Task StartStreaming()
    {
        if (isStreaming) return Task.CompletedTask;
        StartMic();

        isStreaming = true;
        nextInferTime = Time.realtimeSinceStartup + stepSec;
        UpdateButtonText();

        Debug.Log("[Whisper] Streaming started.");
        return Task.CompletedTask;
    }

    public void StopStreaming()
    {
        if (!isStreaming) return;

        isStreaming = isInferencing = false;
        StopMic(); UpdateButtonText();

        Debug.Log("[Whisper] Streaming stopped.");
    }

    void StartMic()
    {
        if (isRecording) return;

        micClip = Microphone.Start(micDevice, micLoop, micMaxLengthSec, micFrequency);
        channels = micClip != null ? micClip.channels : 1;
        lastMicPos = 0; isRecording = true; ring.Clear();

        Debug.Log($"[Mic] Start: {micDevice ?? "Default"}, {micFrequency} Hz, ch={channels}");
    }

    void StopMic()
    {
        if (!isRecording) return;

        Microphone.End(micDevice);
        if (micClip) Destroy(micClip);
        micClip = null; isRecording = false;

        Debug.Log("[Mic] Stopped.");
    }

    void AppendNewMicData()
    {
        if (micClip == null) return;
        int micPos = Microphone.GetPosition(micDevice), clipSamples = micClip.samples * channels;

        // 尚未录到任何数据
        if (micPos == 0 && lastMicPos == 0) return;

        int toRead;
        if (micPos >= lastMicPos)
        {
            toRead = micPos - lastMicPos;
            if (toRead > 0) { var buf = new float[toRead * channels]; micClip.GetData(buf, lastMicPos); AppendToRing(buf); }
        }
        else
        {
            // 环形缓冲回绕
            int part1 = clipSamples - lastMicPos;
            if (part1 > 0) { var buf1 = new float[part1]; micClip.GetData(buf1, lastMicPos); AppendToRing(buf1); }
            if (micPos > 0) { var buf2 = new float[micPos]; micClip.GetData(buf2, 0); AppendToRing(buf2); }
        }

        lastMicPos = micPos;
    }

    void AppendToRing(float[] data)
    {
        ring.AddRange(data);
        if (ring.Count > RingMaxSamples) ring.RemoveRange(0, ring.Count - RingMaxSamples);
    }

    void TriggerInferenceOnce()
    {
        // 简单VAD：若最近 vadLastSec 语音能量低于阈值则跳过本次推理
        if (useVad && !HasVoiceActivity()) { nextInferTime = Time.realtimeSinceStartup + stepSec; return; }

        // 取窗口样本
        int windowSamples = Mathf.Min(ring.Count, Mathf.CeilToInt(micMaxLengthSec * micFrequency * channels));
        if (windowSamples <= 0) { nextInferTime = Time.realtimeSinceStartup + stepSec; return; }

        float[] segment = new float[windowSamples];
        ring.CopyTo(ring.Count - windowSamples, segment, 0, windowSamples);

        isInferencing = true; _ = RunInferenceAsync(segment, micFrequency, channels);
        nextInferTime = Time.realtimeSinceStartup + stepSec;
    }

    bool HasVoiceActivity()
    {
        int vadSamples = Mathf.Min(ring.Count, Mathf.CeilToInt(vadLastSec * micFrequency * channels));
        if (vadSamples <= 0) return false; double sumSq = 0;
        for (int i = ring.Count - vadSamples; i < ring.Count; i++) sumSq += ring[i] * ring[i];
        return Math.Sqrt(sumSq / vadSamples) >= vadRmsThreshold;
    }

    async Task RunInferenceAsync(float[] samples, int frequency, int channels)
    {
        try
        {
            var res = await whisper.GetTextAsync(samples, frequency, channels, wparams);
            string text = res != null ? res.Result : "";
            mainThread.Enqueue(() => { currentText = text; if (outputField != null) outputField.text = currentText; });// 将结果派发回主线程（避免跨线程操作UI）
        }
        catch (Exception e) { mainThread.Enqueue(() => Debug.LogException(e)); }
        finally { isInferencing = false; }
    }

    void UpdateButtonText()
    {
        if (startStopButtonText != null) startStopButtonText.text = isStreaming ? "Stop" : "Start";
    }
}