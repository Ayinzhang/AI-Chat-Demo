using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class ALiYunTextToSpeech : TTS
{
    #region Params

    [SerializeField] private AliTokenHelper m_AliHelper;//token
    [SerializeField] private PostSettings m_PostData;//发送报文

    #endregion

    private void Awake()
    {
        m_AliHelper = this.GetComponent<AliTokenHelper>();
        m_PostURL = "https://nls-gateway-cn-shanghai.aliyuncs.com/stream/v1/tts";
    }


    #region Public Method

    /// <summary>
    /// 语音合成，返回合成文本
    /// </summary>
    /// <param name="_msg"></param>
    /// <param name="_callback"></param>
    public override void Speak(string _msg, Action<AudioClip, string> _callback)
    {
        StartCoroutine(GetSpeech(_msg, _callback));
    }

    #endregion

    #region Private Method

    /// <summary>
    /// 语音合成的方法
    /// </summary>
    /// <param name="_msg"></param>
    /// <param name="callback"></param>
    /// <returns></returns>
    private IEnumerator GetSpeech(string _msg, Action<AudioClip, string> _callback)
    {
        stopwatch.Start();
        var _url = m_PostURL;
        m_PostData.text = _msg;
        m_PostData.appkey = m_AliHelper.OnGetAppKey();
        m_PostData.token = m_AliHelper.OnGetToken();

        using (UnityWebRequest request = UnityWebRequest.Post(m_PostURL, new WWWForm()))
        {
            string _jsonText = JsonUtility.ToJson(m_PostData).Trim();
            byte[] data = System.Text.Encoding.UTF8.GetBytes(_jsonText);
            request.uploadHandler = (UploadHandler)new UploadHandlerRaw(data);
            request.downloadHandler = new DownloadHandlerAudioClip(m_PostURL, AudioType.MPEG);

            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.responseCode == 200)
            {
                AudioClip audioClip = ((DownloadHandlerAudioClip)request.downloadHandler).audioClip;
                _callback(audioClip, _msg);

            }
            else
            {
                Debug.LogError("语音合成失败: " + request.error);
            }


        }

        stopwatch.Stop();
        Debug.Log("阿里语音合成耗时：" + stopwatch.Elapsed.TotalSeconds);
    }


    #endregion


    #region 数据定义

    /// <summary>
    /// 语音合成配置
    /// </summary>
    [System.Serializable]
    public class PostSettings
    {  
        public string appkey=string.Empty;//appkey
        public string text = string.Empty;
        public string token = string.Empty;
        public string format = "mp3";//PCM/WAV/MP3
        public int sample_rate = 16000;//16000 , 8000
        public string voice = "zhixiaobai";//音色 
        public int volume = 50;//音量，取值范围：0~100
        public int speech_rate = 0;//语速，取值范围：-500~500
        public int pitch_rate = 0;//语调，取值范围：-500~500
    }


    #endregion


}
