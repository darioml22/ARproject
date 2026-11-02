using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Xrqr.Unity
{
    /// <summary>The outcome of one GET: a status and body, or the reason there is none.</summary>
    public sealed class HttpResult
    {
        /// <summary>The HTTP status, or 0 when no response arrived.</summary>
        public long Status;
        public byte[] Body;
        /// <summary>Why the request gave no usable answer, or <c>null</c> for a 200 within the limit.</summary>
        public string Error;

        public bool Ok => Error == null;
    }

    /// <summary>
    /// GET requests as the client protocol makes them (section 5): no redirect is followed, any
    /// status other than 200 is no answer, and a body is read only up to a limit.
    /// </summary>
    public static class XrqrHttp
    {
        /// <summary>The time a request may take, in seconds.</summary>
        public const int TimeoutSeconds = 30;

        /// <summary>Must be called from the main thread; the continuation returns to it.</summary>
        public static async Task<HttpResult> GetAsync(string url, long limit, CancellationToken cancellation)
        {
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                var handler = new CappedDownloadHandler(limit);
                request.downloadHandler = handler;
                request.redirectLimit = 0;
                request.timeout = TimeoutSeconds;
                using (cancellation.Register(request.Abort))
                    await request.SendWebRequest();

                var result = new HttpResult { Status = request.responseCode };
                if (cancellation.IsCancellationRequested)
                    result.Error = "cancelled";
                else if (handler.Exceeded)
                    result.Error = $"body over {limit} octets";
                else if (result.Status >= 300 && result.Status < 400)
                    result.Error = $"redirect refused ({result.Status})";
                else if (result.Status != 200)
                    result.Error = result.Status == 0 ? "no response: " + request.error : $"status {result.Status}";
                else if (request.result != UnityWebRequest.Result.Success)
                    result.Error = request.error;
                else
                    result.Body = handler.Body;
                return result;
            }
        }

        /// <summary>Collects a body and aborts the transfer once it would pass a limit.</summary>
        sealed class CappedDownloadHandler : DownloadHandlerScript
        {
            readonly long m_Limit;
            readonly MemoryStream m_Stream = new MemoryStream();

            public bool Exceeded { get; private set; }
            public byte[] Body => m_Stream.ToArray();

            public CappedDownloadHandler(long limit)
                : base(new byte[64 * 1024])
            {
                m_Limit = limit;
            }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                if (contentLength > (ulong)m_Limit)
                    Exceeded = true;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (Exceeded || m_Stream.Length + dataLength > m_Limit)
                {
                    Exceeded = true;
                    return false;
                }
                m_Stream.Write(data, 0, dataLength);
                return true;
            }

            protected override byte[] GetData() => m_Stream.ToArray();
        }
    }
}
