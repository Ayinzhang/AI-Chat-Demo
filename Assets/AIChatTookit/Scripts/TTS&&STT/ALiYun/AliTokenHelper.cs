using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AliTokenHelper : MonoBehaviour
{
    #region Params
    [SerializeField] private string accessKeyId = "你的AccessKeyId";
    [SerializeField] private string accessKeySecret = "你的AccessKeySecret";
    [SerializeField] private string token = "";
    [SerializeField] private string appkey="语音服务的app key";//app key
    // API 地址
    private string endpoint = "http://nls-meta.cn-shanghai.aliyuncs.com"; 

    #endregion

    void Start()
    {
        StartCoroutine(SendAliyunApiRequest());
    }

    #region Public Mehod
    //读取Token
    public string OnGetToken()
    {
        return token;
    }
    //读取App key
    public string OnGetAppKey()
    {
        return appkey;
    }

    #endregion


    #region Private Method
    // 协程：发送阿里云 API 请求
    private IEnumerator SendAliyunApiRequest()
    {
        // 构造请求参数
        var parameters = new Dictionary<string, string>
        {
            { "AccessKeyId", accessKeyId },
            { "Format", "JSON" },
            { "RegionId", "cn-shanghai" },
            { "SignatureMethod", "HMAC-SHA1" },
            { "SignatureNonce", Guid.NewGuid().ToString() },
            { "SignatureVersion", "1.0" },
            { "Timestamp", GetUtcTimestamp() },
            { "Version", "2019-02-28" } // 根据具体服务版本调整
        };

        // 如果有 Action 参数，比如获取 Token 的 Action 是 CreateToken
        parameters.Add("Action", "CreateToken"); // 示例 Action，根据实际 API 文档填写

        // 1. 对参数进行排序
        var sortedParams = new SortedDictionary<string, string>(parameters);

        // 2. 构造待签名字符串
        var canonicalizedQueryString = GetCanonicalizedQueryString(sortedParams);
        var stringToSign = "GET&" + PercentEncode("/") + "&" + PercentEncode(canonicalizedQueryString);

        // 3. 计算签名
        var signature = SignString(stringToSign, accessKeySecret + "&");

        // 4. 添加签名到参数中
        parameters.Add("Signature", signature);

        // 5. 构造最终请求 URL
        var url = endpoint + "?" + GetQueryString(parameters);

        // 6. 发送 HTTP GET 请求
        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Token请求成功:");
                string _callback = webRequest.downloadHandler.text;
                var _data = JsonUtility.FromJson<TokenBody>(_callback);
                token = _data.Token.Id;
            }
            else
            {
                Debug.LogError("Token请求失败:");
                Debug.LogError(webRequest.error);
                Debug.LogError("尝试重新获取Token....");
                yield return new WaitForSeconds(1);//重新获取
                StartCoroutine(SendAliyunApiRequest());
            }
        }
    }

    // 获取 UTC 时间戳，格式为 yyyy-MM-ddTHH:mm:ssZ
    private string GetUtcTimestamp()
    {
        DateTime utcNow = DateTime.UtcNow;
        return utcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    // 对参数进行 URL 编码
    private string PercentEncode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        // 使用 UTF8 编码将字符串转换为字节数组
        byte[] bytes = Encoding.UTF8.GetBytes(value);

        // 构造编码后的字符串
        StringBuilder encoded = new StringBuilder();
        foreach (byte b in bytes)
        {
            // 阿里云要求保留的字符：A-Z, a-z, 0-9, '-', '_', '.', '~'
            if (
                (b >= 'A' && b <= 'Z') || // A-Z
                (b >= 'a' && b <= 'z') || // a-z
                (b >= '0' && b <= '9') || // 0-9
                b == '-' || b == '_' || b == '.' || b == '~' // 特殊字符不编码
            )
            {
                encoded.Append((char)b); // 直接保留这些字符
            }
            else
            {
                // 其他字符转为 %XX 格式
                encoded.Append('%');
                encoded.Append(BitConverter.ToString(new byte[] { b }).Replace("-", "").ToUpper());
            }
        }

        // 阿里云额外要求替换的字符
        string encodedString = encoded.ToString();
        encodedString = encodedString.Replace("+", "%20"); // 替换加号
        encodedString = encodedString.Replace("*", "%2A"); // 替换星号
        encodedString = encodedString.Replace("%7E", "~"); // 替换波浪号（实际上上面已经保留了 ~，此步可能多余，但为了完全兼容保留）

        return encodedString;
    }

    // 构造规范化的查询字符串（用于签名）
    private string GetCanonicalizedQueryString(SortedDictionary<string, string> parameters)
    {
        var sb = new StringBuilder();
        foreach (var param in parameters)
        {
            if (sb.Length > 0)
                sb.Append("&");
            sb.Append(PercentEncode(param.Key));
            sb.Append("=");
            sb.Append(PercentEncode(param.Value));
        }
        return sb.ToString();
    }

    // 构造查询字符串（用于构造 URL）
    private string GetQueryString(Dictionary<string, string> parameters)
    {
        var sb = new StringBuilder();
        foreach (var param in parameters)
        {
            if (sb.Length > 0)
                sb.Append("&");
            sb.Append(param.Key);
            sb.Append("=");
            sb.Append(param.Value);
        }
        return sb.ToString();
    }

    // 计算签名
    private string SignString(string data, string key)
    {
        using (var algorithm = new HMACSHA1(Encoding.UTF8.GetBytes(key)))
        {
            var hashBytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hashBytes);
        }
    }

    #endregion

    #region 数据定义

    [System.Serializable]
    public class TokenBody
    {
        public string ErrMsg=string.Empty;
        public TokenData Token=new TokenData();
    }
    [System.Serializable]
    public class TokenData
    {
        public string UserId = string.Empty;
        public string Id = string.Empty;//token
        public int ExpireTime;
    }
    #endregion
}
