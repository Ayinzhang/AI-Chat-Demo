using UnityEngine;
using UnityEngine.UI;

namespace AIChatDemo
{
    [ExecuteAlways]
    public class GameManager : MonoBehaviour
    {
        public bool isSynthesizedVoice = true;
        public Button voiceButton, sendButton;
        public InputField inputField;
        public Text outputText;

        [HideInInspector] public STTManager sttManager;
        [HideInInspector] public LLMManager llmManager;
        [HideInInspector] public TTSManager ttsManager;

        void Awake()
        {
            sttManager = GetComponent<STTManager>(); sttManager.gm = this;
            llmManager = GetComponent<LLMManager>(); llmManager.gm = this;
            ttsManager = GetComponent<TTSManager>(); ttsManager.gm = this;
        }
    }
}