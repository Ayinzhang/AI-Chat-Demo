using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Networking;

public class ALiYunSpeechToText : STT
{
    #region Params

    [SerializeField] private AliTokenHelper m_AliHelper;//token
    #endregion

    private void Awake()
    {
        m_AliHelper = this.GetComponent<AliTokenHelper>();
        m_SpeechRecognizeURL = "https://nls-gateway-cn-shanghai.aliyuncs.com/stream/v1/asr";
    }

    #region Method

    /// <summary>
    /// 语音识别
    /// </summary>
    /// <param name="_clip"></param>
    /// <param name="_callback"></param>
    public override void SpeechToText(AudioClip _clip, Action<string> _callback)
    {
        StartCoroutine(GetVoiceRecognize(_clip, _callback));
    }


    /// <summary>
    /// 阿里语音识别
    /// </summary>
    /// <param name="_callback"></param>
    /// <returns></returns>
    private IEnumerator GetVoiceRecognize(AudioClip _audioClip, System.Action<string> _callback)
    {
        stopwatch.Start();

        string asrResult = string.Empty;

        //音频转为Byte[]
        float[] samples = new float[_audioClip.samples];
        _audioClip.GetData(samples, 0);
        var samplesShort = new short[samples.Length];
        for (var index = 0; index < samples.Length; index++)
        {
            samplesShort[index] = (short)(samples[index] * short.MaxValue);
        }
        byte[] datas = new byte[samplesShort.Length * 2];

        Buffer.BlockCopy(samplesShort, 0, datas, 0, datas.Length);


        string url = m_SpeechRecognizeURL + "?appkey="+ m_AliHelper.OnGetAppKey()+"&format=pcm&sample_rate=16000";

        WWWForm wwwForm = new WWWForm();
        wwwForm.AddBinaryData("audio", datas);

        using (UnityWebRequest unityWebRequest = UnityWebRequest.Post(url, wwwForm))
        {
            //header
            unityWebRequest.SetRequestHeader("X-NLS-Token", m_AliHelper.OnGetToken());//token
            unityWebRequest.SetRequestHeader("Content-Type", "application/octet-stream");
            unityWebRequest.SetRequestHeader("Content-Length", datas.Length.ToString());
            unityWebRequest.SetRequestHeader("Host", "nls-gateway-cn-shanghai.aliyuncs.com");


            yield return unityWebRequest.SendWebRequest();

            if (string.IsNullOrEmpty(unityWebRequest.error))
            {
                asrResult = unityWebRequest.downloadHandler.text;
                RecogizeCallback _data = JsonUtility.FromJson<RecogizeCallback>(asrResult);
                if(_data.status== 20000000)
                {
                    Debug.Log("阿里语音识别返回：" + _data.result);
                    _callback(_data.result);
                }
                else
                {
                    Debug.LogError("语音识别失败："+ _data.message);
                }

            }
        }

        stopwatch.Stop();
        Debug.Log("阿里语音识别耗时：" + stopwatch.Elapsed.TotalSeconds);
    }

    #endregion

    #region 数据定义

    [Serializable]
    public class RecogizeCallback
    {
        public string task_id=string.Empty;
        public string result = string.Empty;
        public int status =0;
        public string message = string.Empty;
    }

    #endregion


}
