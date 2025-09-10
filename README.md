## AI聊天演示

[中文版](README_zh.md)

&emsp;&emsp;This project integrates local speech-to-text conversion and large language model dialogue. Speech-to-text is implemented by [whisper.unity](https://github.com/Macoron/whisper.unity), large language model dialogue is implemented by [LLM for Unity](https://github.com/undreamai/LLMUnity), and text-to-speech is implemented by [GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS).

&emsp;&emsp;The project divides these three functions into three managers: STTManager, LLMManager, and TTSManager. The models used by the STTManager are located in (Assets/StreamingAssets/Whisper). Initially, only the Tiny model is available, with average recognition capabilities. If you need a larger model, you can download it, place it in the specified location, and name it correctly (ggml-<type>.bin). The model used by the LLMManager can be downloaded in the Inspector, or you can download other models and import them. The prompt words can also be customized. TTSManager is relatively complicated. You need to copy go_webui.bat and change the called webui.py zh_CN to api.py in the file, and then enable the service.

<div align=center>
<img src="demo.png"/>
</div> 