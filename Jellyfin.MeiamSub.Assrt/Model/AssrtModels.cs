using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.MeiamSub.Assrt.Model
{
    /// <summary>
    /// 字幕搜索响应根结构
    /// </summary>
    public class AssrtSearchResponse
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("sub")]
        public AssrtSearchSubInfo Sub { get; set; }
    }

    public class AssrtSearchSubInfo
    {
        [JsonPropertyName("keyword")]
        public string Keyword { get; set; }

        [JsonPropertyName("result")]
        public string Result { get; set; }

        [JsonPropertyName("subs")]
        public List<AssrtSearchItem> List { get; set; }
    }

    public class AssrtSearchItem
    {
        [JsonPropertyName("id")]
        public object Id { get; set; } // 可以是数字也可以是字符串，用 object 接收再 ToString()

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("videoname")]
        public string VideoName { get; set; }

        [JsonPropertyName("subtype")]
        public string Subtype { get; set; }

        [JsonPropertyName("lang")]
        public AssrtLanguageInfo Lang { get; set; }
    }

    public class AssrtLanguageInfo
    {
        [JsonPropertyName("desc")]
        public string Desc { get; set; }

        [JsonPropertyName("lang")]
        public int Lang { get; set; }
    }

    /// <summary>
    /// 字幕详情响应根结构
    /// </summary>
    public class AssrtDetailResponse
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("sub")]
        public AssrtDetailSubInfo Sub { get; set; }
    }

    public class AssrtDetailSubInfo
    {
        [JsonPropertyName("subs")]
        public List<AssrtDetailItem> Subs { get; set; }
    }

    public class AssrtDetailItem
    {
        [JsonPropertyName("id")]
        public object Id { get; set; }

        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    /// <summary>
    /// 插件内部传递的下载参数
    /// </summary>
    public class AssrtDownloadInfo
    {
        public string Id { get; set; }
        public string Format { get; set; }
        public string Language { get; set; }
        public string TwoLetterISOLanguageName { get; set; }
    }
}
