using System.Collections.Generic;

namespace Emby.MeiamSub.Assrt.Model
{
    public class AssrtSearchResponse
    {
        public int status { get; set; }
        public AssrtSearchSubInfo sub { get; set; }
    }

    public class AssrtSearchSubInfo
    {
        public string keyword { get; set; }
        public string result { get; set; }
        public List<AssrtSearchItem> subs { get; set; }
    }

    public class AssrtSearchItem
    {
        public object id { get; set; }
        public string name { get; set; }
        public string videoname { get; set; }
        public string subtype { get; set; }
        public AssrtLanguageInfo lang { get; set; }
    }

    public class AssrtLanguageInfo
    {
        public string desc { get; set; }
        public int lang { get; set; }
    }

    public class AssrtDetailResponse
    {
        public int status { get; set; }
        public AssrtDetailSubInfo sub { get; set; }
    }

    public class AssrtDetailSubInfo
    {
        public List<AssrtDetailItem> subs { get; set; }
    }

    public class AssrtDetailItem
    {
        public object id { get; set; }
        public string filename { get; set; }
        public string url { get; set; }
    }

    public class AssrtDownloadInfo
    {
        public string Id { get; set; }
        public string Format { get; set; }
        public string Language { get; set; }
    }
}
