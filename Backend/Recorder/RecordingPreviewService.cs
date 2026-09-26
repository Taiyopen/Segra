using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ObsKit.NET;
using ObsKit.NET.Native.Types;
using ObsKit.NET.Video;
using Segra.Backend.App;
using Serilog;

namespace Segra.Backend.Recorder
{
    /// <summary>
    /// Streams low-resolution JPEG previews per recording slot to the frontend while recording.
    /// Slot 0 uses the main OBS canvas; additional slots use periodic game/display screenshots.
    /// </summary>
    public static class RecordingPreviewService
    {
        private const uint PreviewWidth = 480;
        private const uint PreviewHeight = 270;
        private const int TargetFps = 10;
        private const long JpegQuality = 65L;

        private static readonly object _lock = new();
        private static readonly HashSet<int> _activeSlots = new();
        private static readonly Dictionary<int, byte[]> _lastJpegBySlot = new();
        private static RawVideoSubscription? _mainCanvasSubscription;
        private static CancellationTokenSource? _screenshotLoopCts;
        private static uint _recordingFps;
        private static bool _enabled;
        private static int _isEncodingMainCanvas;
        // The JPEG preview uses System.Drawing/GDI+, which needs libgdiplus on Linux. Resolve it
        // lazily and defensively so a missing GDI+ never throws from this type's static constructor
        // (which would break OnRecordingStarted/OnRecordingStopped and thus every recording). When
        // null, the preview simply cannot be enabled; recording is unaffected.
        private static readonly ImageCodecInfo? _jpegCodec = TryGetJpegCodec();

        private static ImageCodecInfo? TryGetJpegCodec()
        {
            try
            {
                return ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");
            }
            catch (Exception ex)
            {
                Log.Warning($"Recording preview unavailable (no JPEG/GDI+ codec): {ex.Message}");
                return null;
            }
        }

        public static bool IsEnabled => _enabled;

        public static byte[]? TryGetLatestJpegFrame(int slot = 0)
        {
            lock (_lock)
            {
                if (!_lastJpegBySlot.TryGetValue(slot, out var frame) || frame.Length == 0)
                    return null;
                return (byte[])frame.Clone();
            }
        }

        public static void OnRecordingStarted(uint recordingFps, int slot)
        {
            lock (_lock)
            {
                _recordingFps = recordingFps;
                _activeSlots.Add(slot);
                if (_activeSlots.Count == 1)
                    _enabled = StartPreviewLocked();
                else if (_enabled)
                {
                    if (slot == 0)
                        EnsureMainCanvasSubscriptionLocked();
                    EnsureScreenshotLoopRunningLocked();
                }
            }

            BroadcastState();
        }

        public static void OnRecordingStopped(int slot)
        {
            lock (_lock)
            {
                _activeSlots.Remove(slot);
                _lastJpegBySlot.Remove(slot);

                if (!_activeSlots.Contains(0))
                    DisposeMainCanvasSubscriptionLocked();

                if (_activeSlots.Count == 0)
                {
                    _enabled = false;
                    StopPreviewLocked();
                }
                else if (_enabled)
                {
                    if (_activeSlots.Contains(0))
                        EnsureMainCanvasSubscriptionLocked();
                    EnsureScreenshotLoopRunningLocked();
                }
            }

            BroadcastState();
        }

        public static void Toggle()
        {
            lock (_lock)
            {
                if (_activeSlots.Count == 0)
                    return;

                if (_enabled)
                {
                    _enabled = false;
                    StopPreviewLocked();
                }
                else
                {
                    _enabled = StartPreviewLocked();
                }
            }

            BroadcastState();
        }

        private static bool StartPreviewLocked()
        {
            if (_jpegCodec == null)
            {
                Log.Warning("Cannot enable recording preview: no JPEG encoder available on this platform.");
                return false;
            }

            StopPreviewLocked();

            if (_activeSlots.Contains(0))
                EnsureMainCanvasSubscriptionLocked();

            EnsureScreenshotLoopRunningLocked();
            return _mainCanvasSubscription != null || _screenshotLoopCts != null;
        }

        private static void EnsureMainCanvasSubscriptionLocked()
        {
            if (_mainCanvasSubscription != null || !_activeSlots.Contains(0))
                return;

            uint divisor = _recordingFps == 0 ? 1u : Math.Max(1u, _recordingFps / (uint)TargetFps);
            try
            {
                _mainCanvasSubscription = Obs.SubscribeRawVideo(
                    VideoFormat.BGRA,
                    PreviewWidth,
                    PreviewHeight,
                    OnMainCanvasFrame,
                    frameRateDivisor: divisor);
                Log.Information(
                    "Recording preview enabled for slot 0 ({W}x{H}, divisor={Divisor} from {Fps}fps)",
                    PreviewWidth, PreviewHeight, divisor, _recordingFps);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to enable main canvas recording preview");
                _mainCanvasSubscription = null;
            }
        }

        private static void DisposeMainCanvasSubscriptionLocked()
        {
            var sub = _mainCanvasSubscription;
            _mainCanvasSubscription = null;
            if (sub == null)
                return;

            try
            {
                sub.Dispose();
                Log.Information("Recording preview disabled for slot 0");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error disposing main canvas preview subscription");
            }
        }

        private static void EnsureScreenshotLoopRunningLocked()
        {
            bool needsScreenshotLoop = _activeSlots.Any(s => s != 0);
            if (!needsScreenshotLoop)
            {
                _screenshotLoopCts?.Cancel();
                _screenshotLoopCts = null;
                return;
            }

            if (_screenshotLoopCts != null)
                return;

            _screenshotLoopCts = new CancellationTokenSource();
            var token = _screenshotLoopCts.Token;
            _ = Task.Run(() => ScreenshotLoopAsync(token), token);
        }

        private static async Task ScreenshotLoopAsync(CancellationToken token)
        {
            var interval = TimeSpan.FromMilliseconds(1000.0 / TargetFps);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!_enabled)
                    continue;

                int[] slots;
                lock (_lock)
                {
                    slots = _activeSlots.Where(s => s != 0).ToArray();
                }

                foreach (int slot in slots)
                {
                    if (token.IsCancellationRequested || !_enabled)
                        break;

                    try
                    {
                        byte[]? jpeg = OBSService.TryGetRecordingPreviewJpeg(slot, (int)PreviewWidth);
                        if (jpeg == null || jpeg.Length == 0)
                            continue;

                        lock (_lock)
                        {
                            _lastJpegBySlot[slot] = jpeg;
                        }

                        await SendJpegAsync(slot, jpeg);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Screenshot preview failed for slot {Slot}", slot);
                    }
                }
            }
        }

        private static void StopPreviewLocked()
        {
            DisposeMainCanvasSubscriptionLocked();

            _screenshotLoopCts?.Cancel();
            _screenshotLoopCts = null;
        }

        private static void BroadcastState()
        {
            _ = MessageService.SendFrontendMessage("RecordingPreviewState", new { enabled = _enabled });
        }

        private static void OnMainCanvasFrame(in RawVideoFrame frame)
        {
            if (!_enabled || !_activeSlots.Contains(0))
                return;

            if (Interlocked.CompareExchange(ref _isEncodingMainCanvas, 1, 0) != 0)
                return;

            int width = (int)frame.Width;
            int height = (int)frame.Height;
            int srcStride = (int)frame.GetLinesize(0);
            int rowBytes = width * 4;
            int packedSize = rowBytes * height;

            var buffer = ArrayPool<byte>.Shared.Rent(packedSize);
            try
            {
                var src = frame.GetPlane(0, frame.Height);
                if (src.IsEmpty)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    Interlocked.Exchange(ref _isEncodingMainCanvas, 0);
                    return;
                }

                for (int y = 0; y < height; y++)
                    src.Slice(y * srcStride, rowBytes).CopyTo(buffer.AsSpan(y * rowBytes, rowBytes));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Preview frame copy failed (slot 0)");
                ArrayPool<byte>.Shared.Return(buffer);
                Interlocked.Exchange(ref _isEncodingMainCanvas, 0);
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_enabled && _activeSlots.Contains(0))
                        await EncodeAndSendAsync(0, buffer, width, height, packedSize);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Preview frame encode/send failed (slot 0)");
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    Interlocked.Exchange(ref _isEncodingMainCanvas, 0);
                }
            });
        }

        private static async Task EncodeAndSendAsync(int slot, byte[] bgra, int width, int height, int packedSize)
        {
            if (_jpegCodec == null)
            {
                Log.Warning("Recording preview skipped: JPEG encoder not available on this machine.");
                return;
            }

            byte[] jpegBytes;
            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var rect = new Rectangle(0, 0, width, height);
                var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int srcRow = width * 4;
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(bgra, y * srcRow, data.Scan0 + y * data.Stride, srcRow);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }

                using var ms = new MemoryStream(packedSize / 4);
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                bmp.Save(ms, _jpegCodec, encoderParams);
                jpegBytes = ms.ToArray();
            }

            lock (_lock)
            {
                _lastJpegBySlot[slot] = jpegBytes;
            }

            await SendJpegAsync(slot, jpegBytes, width, height);
        }

        private static async Task SendJpegAsync(int slot, byte[] jpegBytes, int? width = null, int? height = null)
        {
            var b64 = Convert.ToBase64String(jpegBytes);
            await MessageService.SendFrontendMessage("RecordingPreviewFrame", new
            {
                slot,
                jpegBase64 = b64,
                width,
                height
            });
        }
    }
}
