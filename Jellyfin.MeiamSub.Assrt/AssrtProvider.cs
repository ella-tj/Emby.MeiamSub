using Jellyfin.MeiamSub.Assrt.Model;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.MeiamSub.Assrt
{
    /// <summary>
    /// Assrt (伪射手) 字幕提供程序
    /// </summary>
    public class AssrtProvider : ISubtitleProvider, IHasOrder
    {
        private const string ApiBaseUrl = "https://api.assrt.net/v1";
        
        public const string ASS = "ass";
        public const string SSA = "ssa";
        public const string SRT = "srt";

        private readonly ILogger<AssrtProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public int Order => 90; // 优先级高于旧版

        public string Name => "MeiamSub.Assrt";

        public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Movie, VideoContentType.Episode };

        public AssrtProvider(ILogger<AssrtProvider> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _logger.LogInformation($"{Name} Init");
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{Name} Search | Received request for: {(request?.MediaPath ?? "NULL")}");

            try
            {
                if (request == null)
                {
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                var config = Plugin.Instance?.Configuration;
                if (config == null || string.IsNullOrWhiteSpace(config.AssrtApiToken))
                {
                    _logger.LogWarning($"{Name} Search | AssrtApiToken is empty, skip search. Please configure the token in plugin settings.");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                var language = NormalizeLanguage(request.Language);
                if (language != "chi")
                {
                    _logger.LogInformation($"{Name} Search | Language '{request.Language}' not supported, skip search.");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                string queryText;
                if (config.EnableUseMetadata)
                {
                    if (request.ContentType == VideoContentType.Episode)
                    {
                        queryText = $"{request.SeriesName} S{request.ParentIndexNumber:D2}E{request.IndexNumber:D2}";
                    }
                    else if (request.ContentType == VideoContentType.Movie)
                    {
                        queryText = request.Name;
                    }
                    else
                    {
                        queryText = !string.IsNullOrEmpty(request.MediaPath)
                            ? Path.GetFileNameWithoutExtension(request.MediaPath)
                            : string.Empty;
                    }
                }
                else
                {
                    queryText = !string.IsNullOrEmpty(request.MediaPath)
                        ? Path.GetFileNameWithoutExtension(request.MediaPath)
                        : string.Empty;
                }

                if (string.IsNullOrWhiteSpace(queryText))
                {
                    _logger.LogWarning($"{Name} Search | Query text is empty, skip search.");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                _logger.LogInformation($"{Name} Search | Target -> '{queryText}' | Language -> '{language}'");

                using var httpClient = _httpClientFactory.CreateClient(Name);
                
                // 拼接搜索请求，is_file=1 用于对文件名检索进行关键词降噪
                var searchUrl = $"{ApiBaseUrl}/sub/search?token={Uri.EscapeDataString(config.AssrtApiToken)}&q={Uri.EscapeDataString(queryText)}&is_file=1&cnt=15";
                
                // 隐藏敏感 Token 信息以便记录日志
                var maskedSearchUrl = $"{ApiBaseUrl}/sub/search?token={MaskToken(config.AssrtApiToken)}&q={Uri.EscapeDataString(queryText)}&is_file=1&cnt=15";
                _logger.LogInformation($"{Name} Search | Request Url -> {maskedSearchUrl}");

                var response = await httpClient.GetAsync(searchUrl, cancellationToken);
                var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogInformation($"{Name} Search | Response Status -> {response.StatusCode} | ResponseBody -> {jsonString}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"{Name} Search | API responded with status: {response.StatusCode}");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                var searchResponse = JsonSerializer.Deserialize<AssrtSearchResponse>(jsonString);

                if (searchResponse == null || searchResponse.Status != 0 || searchResponse.Sub?.List == null || !searchResponse.Sub.List.Any())
                {
                    _logger.LogWarning($"{Name} Search | No subtitles found or API error. Request -> Target: '{queryText}', Language: '{language}', Url: '{maskedSearchUrl}' | Response -> Status: {searchResponse?.Status}, Content: '{jsonString}'");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                var remoteSubtitles = new List<RemoteSubtitleInfo>();

                foreach (var item in searchResponse.Sub.List)
                {
                    if (item.Id == null) continue;

                    var subtitleId = item.Id.ToString();
                    var format = ExtractFormat(item.Subtype) ?? ExtractFormat(item.Name) ?? SRT;
                    
                    var langDesc = item.Lang?.Desc ?? "中文";

                    var downloadInfo = new AssrtDownloadInfo
                    {
                        Id = subtitleId,
                        Format = format,
                        Language = request.Language,
                        TwoLetterISOLanguageName = request.TwoLetterISOLanguageName
                    };

                    var encodedId = Base64Encode(JsonSerializer.Serialize(downloadInfo));

                    remoteSubtitles.Add(new RemoteSubtitleInfo
                    {
                        Id = encodedId,
                        Name = $"[MEIAMSUB] {item.Name} | {langDesc} | 伪射手",
                        Author = "Assrt",
                        ProviderName = Name,
                        Format = format,
                        Comment = string.IsNullOrEmpty(item.VideoName) ? $"格式: {format}" : $"适配视频: {item.VideoName} (格式: {format})",
                        IsHashMatch = false // Assrt.net 接口主要是文本搜索，返回 false 以提示用户是名字检索匹配
                    });
                }

                _logger.LogInformation($"{Name} Search | Summary -> Found {remoteSubtitles.Count} subtitles.");
                return remoteSubtitles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{Name} Search | Exception occurred: {ex.Message}");
            }

            return Array.Empty<RemoteSubtitleInfo>();
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{Name} GetSubtitles | Requesting ID: {id}");
            AssrtDownloadInfo downloadInfo = null;

            try
            {
                var decodedString = Base64Decode(id);
                downloadInfo = JsonSerializer.Deserialize<AssrtDownloadInfo>(decodedString);
                if (downloadInfo == null || string.IsNullOrEmpty(downloadInfo.Id))
                {
                    throw new ArgumentException($"{Name} GetSubtitles | 无法解析字幕下载信息，ID 为空。");
                }

                var config = Plugin.Instance?.Configuration;
                if (config == null || string.IsNullOrWhiteSpace(config.AssrtApiToken))
                {
                    _logger.LogError($"{Name} GetSubtitles | AssrtApiToken is not configured.");
                    throw new InvalidOperationException($"{Name} GetSubtitles | AssrtApiToken 未配置，请在插件设置中填写 Token。");
                }

                using var httpClient = _httpClientFactory.CreateClient(Name);
                
                // 1. 请求详情接口获取真实的下载地址
                var detailUrl = $"{ApiBaseUrl}/sub/detail?token={Uri.EscapeDataString(config.AssrtApiToken)}&id={Uri.EscapeDataString(downloadInfo.Id)}";
                var detailResponseMsg = await httpClient.GetAsync(detailUrl, cancellationToken);
                if (!detailResponseMsg.IsSuccessStatusCode)
                {
                    _logger.LogError($"{Name} GetSubtitles | Failed to fetch details. Status: {detailResponseMsg.StatusCode}");
                    throw new HttpRequestException($"{Name} GetSubtitles | 获取字幕详情失败，HTTP 状态码: {detailResponseMsg.StatusCode}");
                }

                var detailJson = await detailResponseMsg.Content.ReadAsStringAsync(cancellationToken);
                var detailResponse = JsonSerializer.Deserialize<AssrtDetailResponse>(detailJson);

                var targetSub = detailResponse?.Sub?.Subs?.FirstOrDefault();
                if (targetSub == null || string.IsNullOrEmpty(targetSub.Url))
                {
                    _logger.LogError($"{Name} GetSubtitles | Subtitle download URL not found in API response.");
                    throw new InvalidOperationException($"{Name} GetSubtitles | API 响应中未找到字幕下载地址。");
                }

                _logger.LogInformation($"{Name} GetSubtitles | Downloading subtitle from: {targetSub.Url}");

                // 2. 下载字幕流 (手动接管重定向以确保每次重定向请求均带有正确的防盗链 Headers)
                var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var cleanHttpClient = new HttpClient(handler);
                
                var currentUrl = targetSub.Url;

                HttpResponseMessage downloadResponse = null;
                int redirectCount = 0;

                while (redirectCount < 5)
                {
                    var requestMsg = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                    requestMsg.Version = HttpVersion.Version11;
                    requestMsg.Headers.ConnectionClose = true; // 强制关闭长连接，防范服务器 Keep-Alive 超时关闭
                    requestMsg.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    requestMsg.Headers.Add("Referer", "https://assrt.net/");

                    downloadResponse = await cleanHttpClient.SendAsync(requestMsg, cancellationToken);

                    if (downloadResponse.StatusCode == HttpStatusCode.Redirect ||
                        downloadResponse.StatusCode == HttpStatusCode.MovedPermanently ||
                        downloadResponse.StatusCode == HttpStatusCode.Found ||
                        downloadResponse.StatusCode == HttpStatusCode.SeeOther ||
                        (int)downloadResponse.StatusCode == 307 ||
                        (int)downloadResponse.StatusCode == 308)
                    {
                        var location = downloadResponse.Headers.Location;
                        if (location != null)
                        {
                            if (!location.IsAbsoluteUri)
                            {
                                location = new Uri(new Uri(currentUrl), location);
                            }
                            currentUrl = location.AbsoluteUri;
                            redirectCount++;
                            _logger.LogInformation($"{Name} GetSubtitles | Redirecting to -> {currentUrl}");
                            downloadResponse.Dispose();
                            continue;
                        }
                    }
                    break;
                }

                if (downloadResponse == null || !downloadResponse.IsSuccessStatusCode)
                {
                    _logger.LogError($"{Name} GetSubtitles | Failed to download subtitle file. Status: {downloadResponse?.StatusCode}");
                    throw new HttpRequestException($"{Name} GetSubtitles | 字幕文件下载失败，HTTP 状态码: {downloadResponse?.StatusCode}");
                }

                var responseStream = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken);

                // 3. 防御性处理：检查是否为 ZIP 压缩包
                // 如果是 ZIP，解压出里面第一个符合格式要求的字幕流
                var memoryStream = new MemoryStream();
                await responseStream.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;

                // 判断 ZIP 标志 (PK.. / 0x50 0x4B)
                bool isZip = false;
                if (memoryStream.Length > 4)
                {
                    byte[] zipHeader = new byte[4];
                    await memoryStream.ReadExactlyAsync(zipHeader, 0, 4, cancellationToken);
                    if (zipHeader[0] == 0x50 && zipHeader[1] == 0x4B && zipHeader[2] == 0x03 && zipHeader[3] == 0x04)
                    {
                        isZip = true;
                    }
                    memoryStream.Position = 0;
                }

                if (isZip || targetSub.Filename?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogInformation($"{Name} GetSubtitles | ZIP file detected, extracting...");
                    try
                    {
                        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, true);
                        var entry = archive.Entries.FirstOrDefault(e =>
                            e.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
                            e.FullName.EndsWith(".ass", StringComparison.OrdinalIgnoreCase) ||
                            e.FullName.EndsWith(".ssa", StringComparison.OrdinalIgnoreCase));

                        if (entry != null)
                        {
                            var extractedStream = new MemoryStream();
                            using (var entryStream = entry.Open())
                            {
                                await entryStream.CopyToAsync(extractedStream, cancellationToken);
                            }
                            extractedStream.Position = 0;
                            var extFormat = ExtractFormat(entry.FullName) ?? downloadInfo.Format;

                            _logger.LogInformation($"{Name} GetSubtitles | Extracted file: '{entry.FullName}' with format: {extFormat}");

                            return new SubtitleResponse
                            {
                                Language = downloadInfo.Language,
                                IsForced = false,
                                Format = extFormat,
                                Stream = extractedStream
                            };
                        }
                        else
                        {
                            _logger.LogWarning($"{Name} GetSubtitles | No srt/ass/ssa file found inside the ZIP package.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"{Name} GetSubtitles | Failed to extract ZIP archive: {ex.Message}");
                    }
                }

                // 4. 不是 ZIP 或者解压失败时，默认返回原流
                memoryStream.Position = 0;
                return new SubtitleResponse
                {
                    Language = downloadInfo.Language,
                    IsForced = false,
                    Format = downloadInfo.Format,
                    Stream = memoryStream
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{Name} GetSubtitles | Exception: {ex.Message}");
                throw;
            }
        }

        #region 辅助工具函数

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        public static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }

        protected string ExtractFormat(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            text = text.ToLower();
            if (text.Contains(ASS)) return ASS;
            if (text.Contains(SSA)) return SSA;
            if (text.Contains(SRT)) return SRT;

            return null;
        }

        private static string NormalizeLanguage(string language)
        {
            if (string.IsNullOrEmpty(language)) return language;

            if (language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                language.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                language.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                language.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                language.Equals("zho", StringComparison.OrdinalIgnoreCase) ||
                language.Equals("chi", StringComparison.OrdinalIgnoreCase))
            {
                return "chi";
            }
            if (language.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                language.Equals("eng", StringComparison.OrdinalIgnoreCase))
            {
                return "eng";
            }
            return language;
        }

        private static string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            if (token.Length <= 8) return "***";
            return token.Substring(0, 4) + "***" + token.Substring(token.Length - 4);
        }

        #endregion
    }
}
