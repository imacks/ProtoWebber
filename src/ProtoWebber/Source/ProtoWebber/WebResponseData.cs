using System;
using System.Collections;
using System.IO;

namespace ProtoWebber
{
    public class WebResponseData
    {
        public Stream Stream { get; set; }
        public Hashtable Headers { get; set; }
        public string MimeType { get; set; }
        public int StatusCode { get; set; }
    }
}
