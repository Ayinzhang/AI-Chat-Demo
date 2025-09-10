using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace AIChatDemo
{
    public class TTSManager : MonoBehaviour
    {
        [HideInInspector] public GameManager gm;
        public enum Language { zh, en, ja, ko, yue }
        [Tooltip("�����ϳ�API��ַ")]
        public string urlAddress = "http://127.0.0.1:9880";
        [Tooltip("�ο���Ƶ·����GPT-SoVITS��Ŀ�µ����·��")]
        public string referAudioPath;
        [Tooltip("�ο���Ƶ����������")]
        public string referenceText;
        [Tooltip("�ο���Ƶ������")]
        public Language referenceLan = Language.zh;
        [Tooltip("�ϳ���Ƶ������")]
        public Language targetLan = Language.zh;

        AudioSource audioSource;

        public struct RequestData
        {
            public string refer_wav_path, prompt_text, prompt_language, text, text_language;

            public RequestData(string referAudioPath, string referenceText, string message, Language referenceLan, Language targetLan)
            {
                refer_wav_path = referAudioPath; prompt_text = referenceText; text = message; prompt_language = referenceLan.ToString(); text_language = targetLan.ToString();
            }
        }

        public IEnumerator GetVoice(string message)
        {
            string _postJson = JsonUtility.ToJson(new RequestData(referAudioPath, referenceText, message, referenceLan, targetLan));

            using (UnityWebRequest request = new UnityWebRequest(urlAddress, "POST"))
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(_postJson);
                request.uploadHandler = (UploadHandler)new UploadHandlerRaw(data);
                request.downloadHandler = new DownloadHandlerAudioClip(urlAddress, AudioType.WAV);

                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.responseCode == 200)
                {
                    AudioClip audioClip = ((DownloadHandlerAudioClip)request.downloadHandler).audioClip;

                    if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>(); audioSource.clip = audioClip; audioSource.Play();
                    float timeStep = audioClip.length / message.Length;
                    for (int i = 0; i < message.Length; i++)
                    {
                        gm.outputText.text += message[i];
                        yield return new WaitForSeconds(timeStep);
                    }
                    gm.outputText.text += "\n";
                }
                else
                {
                    Debug.LogError("�����ϳ�ʧ��: " + request.error);
                }
            }
        }
    }
}