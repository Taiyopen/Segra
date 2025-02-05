using System.Net;

namespace Segra.Backend
{
    public class ProgressableStreamContent : HttpContent
    {
        private readonly byte[] _content;
        private readonly Action<long, long> _progressCallback;

        public ProgressableStreamContent(byte[] content, string mediaType, Action<long, long> progressCallback)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _progressCallback = progressCallback;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            long totalBytes = _content.Length;
            long totalWritten = 0;
            int bufferSize = 4096;

            for (int i = 0; i < _content.Length; i += bufferSize)
            {
                int toWrite = Math.Min(bufferSize, _content.Length - i);
                await stream.WriteAsync(_content.AsMemory(i, toWrite));
                totalWritten += toWrite;
                _progressCallback?.Invoke(totalWritten, totalBytes);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _content.Length;
            return true;
        }
    }
}
