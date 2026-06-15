using Emby.MeiamSub.Assrt.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

namespace Emby.MeiamSub.Assrt
{
    /// <summary>
    /// Assrt (伪射手) 字幕提供程序 (Emby 适配版)
    /// </summary>
    public class AssrtProvider : ISubtitleProvider, IHasOrder
    {
        private const string ApiBaseUrl = "https://api.assrt.net/v1";

        public const string ASS = "ass";
        public const string SSA = "ssa";
        public const string SRT = "srt";

        protected readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IHttpClient _httpClient;

        public int Order => 90; // 优先级高于老旧源

        public string Name => "MeiamSub.Assrt";

        public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Movie, VideoContentType.Episode };

        public AssrtProvider(ILogManager logManager, IJsonSerializer jsonSerializer, IHttpClient httpClient)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            _logger.Info($"{Name} Init");
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            _logger.Info($"{Name} Search | Received request for: {(request?.MediaPath ?? "NULL")}");

            try
            {
                if (request == null)
                {
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                var config = Plugin.Instance?.Options;
                if (config == null || string.IsNullOrWhiteSpace(config.AssrtApiToken))
                {
                    _logger.Warn($"{Name} Search | AssrtApiToken is empty, skip search. Please configure the token in plugin settings.");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                var language = NormalizeLanguage(request.Language);
                if (language != "chi")
                {
                    _logger.Info($"{Name} Search | Language '{request.Language}' not supported, skip search.");
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
                    _logger.Warn($"{Name} Search | Query text is empty, skip search.");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                _logger.Info($"{Name} Search | Target -> '{queryText}' | Language -> '{language}'");

                var searchUrl = $"{ApiBaseUrl}/sub/search?token={Uri.EscapeDataString(config.AssrtApiToken)}&q={Uri.EscapeDataString(queryText)}&is_file=1&cnt=15";
                var maskedSearchUrl = $"{ApiBaseUrl}/sub/search?token={MaskToken(config.AssrtApiToken)}&q={Uri.EscapeDataString(queryText)}&is_file=1&cnt=15";
                _logger.Info($"{Name} Search | Request Url -> {maskedSearchUrl}");

                var options = new HttpRequestOptions
                {
                    Url = searchUrl,
                    UserAgent = Name,
                    TimeoutMs = 20000,
                    AcceptHeader = "application/json"
                };

                var response = await _httpClient.GetResponse(options);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Error($"{Name} Search | API responded with status: {response.StatusCode}");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                string jsonString;
                using (var reader = new StreamReader(response.Content, Encoding.UTF8))
                {
                    jsonString = await reader.ReadToEndAsync();
                }

                _logger.Info($"{Name} Search | Response Status -> {response.StatusCode} | ResponseBody -> {jsonString}");

                var searchResponse = _jsonSerializer.DeserializeFromString<AssrtSearchResponse>(jsonString);

                if (searchResponse == null || searchResponse.status != 0 || searchResponse.sub?.subs == null || !searchResponse.sub.subs.Any())
                {
                    _logger.Warn($"{Name} Search | No subtitles found or API error. Request -> Target: '{queryText}', Language: '{language}', Url: '{maskedSearchUrl}' | Response -> Status: {searchResponse?.status}, Content: '{jsonString}'");
                    return Array.Empty<RemoteSubtitleInfo>();
                }

                var remoteSubtitles = new List<RemoteSubtitleInfo>();

                foreach (var item in searchResponse.sub.subs)
                {
                    if (item.id == null) continue;

                    var subtitleId = item.id.ToString();
                    var format = ExtractFormat(item.subtype) ?? ExtractFormat(item.name) ?? SRT;

                    var langDesc = item.lang?.desc ?? "中文";

                    var downloadInfo = new AssrtDownloadInfo
                    {
                        Id = subtitleId,
                        Format = format,
                        Language = request.Language
                    };

                    var encodedId = Base64Encode(_jsonSerializer.SerializeToString(downloadInfo));

                    remoteSubtitles.Add(new RemoteSubtitleInfo
                    {
                        Id = encodedId,
                        Name = $"[MEIAMSUB] {item.name} | {langDesc} | 伪射手",
                        Author = "Assrt",
                        ProviderName = Name,
                        Format = format,
                        Comment = string.IsNullOrEmpty(item.videoname) ? $"格式: {format}" : $"适配视频: {item.videoname} (格式: {format})",
                        IsHashMatch = false
                    });
                }

                _logger.Info($"{Name} Search | Summary -> Found {remoteSubtitles.Count} subtitles.");
                return remoteSubtitles;
            }
            catch (Exception ex)
            {
                _logger.Error($"{Name} Search | Exception occurred: [{ex.GetType().Name}] {ex.Message}");
            }

            return Array.Empty<RemoteSubtitleInfo>();
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            _logger.Info($"{Name} GetSubtitles | Requesting ID: {id}");
            AssrtDownloadInfo downloadInfo = null;

            try
            {
                var decodedString = Base64Decode(id);
                downloadInfo = _jsonSerializer.DeserializeFromString<AssrtDownloadInfo>(decodedString);
                if (downloadInfo == null || string.IsNullOrEmpty(downloadInfo.Id))
                {
                    return new SubtitleResponse { Language = "chi", Format = SRT, Stream = new MemoryStream() };
                }

                var config = Plugin.Instance?.Options;
                if (config == null || string.IsNullOrWhiteSpace(config.AssrtApiToken))
                {
                    _logger.Error($"{Name} GetSubtitles | AssrtApiToken is not configured.");
                    return new SubtitleResponse { Language = downloadInfo.Language, Format = downloadInfo.Format, Stream = new MemoryStream() };
                }

                // 1. 请求详情接口获取下载地址
                var detailUrl = $"{ApiBaseUrl}/sub/detail?token={Uri.EscapeDataString(config.AssrtApiToken)}&id={Uri.EscapeDataString(downloadInfo.Id)}";
                
                var detailOptions = new HttpRequestOptions
                {
                    Url = detailUrl,
                    UserAgent = Name,
                    TimeoutMs = 20000,
                    AcceptHeader = "application/json"
                };

                var detailResponseMsg = await _httpClient.GetResponse(detailOptions);
                if (detailResponseMsg.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Error($"{Name} GetSubtitles | Failed to fetch details. Status: {detailResponseMsg.StatusCode}");
                    return new SubtitleResponse { Language = downloadInfo.Language, Format = downloadInfo.Format, Stream = new MemoryStream() };
                }

                string detailJson;
                using (var reader = new StreamReader(detailResponseMsg.Content, Encoding.UTF8))
                {
                    detailJson = await reader.ReadToEndAsync();
                }

                var detailResponse = _jsonSerializer.DeserializeFromString<AssrtDetailResponse>(detailJson);

                var targetSub = detailResponse?.sub?.subs?.FirstOrDefault();
                if (targetSub == null || string.IsNullOrEmpty(targetSub.url))
                {
                    _logger.Error($"{Name} GetSubtitles | Subtitle download URL not found in API response.");
                    return new SubtitleResponse { Language = downloadInfo.Language, Format = downloadInfo.Format, Stream = new MemoryStream() };
                }

                _logger.Info($"{Name} GetSubtitles | Downloading subtitle from: {targetSub.url}");

                // 2. 下载字幕流 (手动接管重定向以确保每次重定向请求均带有正确的防盗链 Headers)
                var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var cleanHttpClient = new HttpClient(handler);
                
                var currentUrl = targetSub.url;
                HttpResponseMessage downloadResponse = null;
                int redirectCount = 0;

                while (redirectCount < 5)
                {
                    var requestMsg = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                    requestMsg.Version = HttpVersion.Version11;
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
                            _logger.Info($"{Name} GetSubtitles | Redirecting to -> {currentUrl}");
                            downloadResponse.Dispose();
                            continue;
                        }
                    }
                    break;
                }

                if (downloadResponse == null || !downloadResponse.IsSuccessStatusCode)
                {
                    _logger.Error($"{Name} GetSubtitles | Failed to download subtitle file. Status: {downloadResponse?.StatusCode}");
                    return new SubtitleResponse { Language = downloadInfo.Language, Format = downloadInfo.Format, Stream = new MemoryStream() };
                }

                var responseStream = await downloadResponse.Content.ReadAsStreamAsync();

                // 3. 防御性处理：检查是否为 ZIP 压缩包并提取
                var memoryStream = new MemoryStream();
                await responseStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                bool isZip = false;
                if (memoryStream.Length > 4)
                {
                    byte[] zipHeader = new byte[4];
                    memoryStream.Read(zipHeader, 0, 4);
                    if (zipHeader[0] == 0x50 && zipHeader[1] == 0x4B && zipHeader[2] == 0x03 && zipHeader[3] == 0x04)
                    {
                        isZip = true;
                    }
                    memoryStream.Position = 0;
                }

                if (isZip || targetSub.filename?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.Info($"{Name} GetSubtitles | ZIP file detected, extracting...");
                    try
                    {
                        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, true))
                        {
                            var entry = archive.Entries.FirstOrDefault(e =>
                                e.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
                                e.FullName.EndsWith(".ass", StringComparison.OrdinalIgnoreCase) ||
                                e.FullName.EndsWith(".ssa", StringComparison.OrdinalIgnoreCase));

                            if (entry != null)
                            {
                                var extractedStream = new MemoryStream();
                                using (var entryStream = entry.Open())
                                {
                                    await entryStream.CopyToAsync(extractedStream);
                                }
                                extractedStream.Position = 0;
                                var extFormat = ExtractFormat(entry.FullName) ?? downloadInfo.Format;

                                _logger.Info($"{Name} GetSubtitles | Extracted file: '{entry.FullName}' with format: {extFormat}");

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
                                _logger.Warn($"{Name} GetSubtitles | No srt/ass/ssa file found inside the ZIP package.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"{Name} GetSubtitles | Failed to extract ZIP archive: {ex.Message}");
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
                _logger.Error($"{Name} GetSubtitles | Exception: {ex.Message}");
            }

            return new SubtitleResponse { Language = downloadInfo?.Language ?? "chi", Format = downloadInfo?.Format ?? SRT, Stream = new MemoryStream() };
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
