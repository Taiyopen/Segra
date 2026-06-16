using Segra.Backend.App;
using Segra.Backend.Core.Models;
using Segra.Backend.Shared;
using Segra.Backend.Windows.Display;
using Serilog;

namespace Segra.Backend.Services
{
    public static class PresetsService
    {
        private static bool IsAmdEncoder()
        {
            var codec = Settings.Instance.Codec;
            return codec != null && codec.InternalEncoderId.Contains("amf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Applies a video quality preset to the settings
        /// </summary>
        public static async Task ApplyVideoPreset(string presetName)
        {
            var settings = Settings.Instance;
            settings.BeginBulkUpdate();
            bool isAmd = IsAmdEncoder();

            try
            {
                switch (presetName.ToLower())
                {
                    case "low":
                        settings.VideoQualityPreset = "low";
                        settings.Resolution = "720p";
                        settings.FrameRate = 30;
                        settings.RateControl = "VBR";
                        settings.CqLevel = isAmd ? 22 : 24;
                        settings.Bitrate = isAmd ? 20 : 15;
                        settings.MinBitrate = 10;
                        settings.MaxBitrate = isAmd ? 20 : 15;
                        settings.Encoder = "gpu";
                        break;

                    case "standard":
                        settings.VideoQualityPreset = "standard";
                        settings.Resolution = "1080p";
                        settings.FrameRate = 60;
                        settings.RateControl = "VBR";
                        settings.CqLevel = isAmd ? 20 : 22;
                        settings.Bitrate = isAmd ? 40 : 30;
                        settings.MinBitrate = isAmd ? 25 : 20;
                        settings.MaxBitrate = isAmd ? 50 : 40;
                        settings.Encoder = "gpu";
                        break;

                    case "high":
                        settings.VideoQualityPreset = "high";
                        settings.Resolution = DisplayService.HasDisplayWithMinHeight(1440) ? "1440p" : "1080p";
                        settings.FrameRate = 60;
                        settings.RateControl = "VBR";
                        settings.CqLevel = isAmd ? 18 : 20;
                        settings.Bitrate = isAmd ? 60 : 50;
                        settings.MinBitrate = isAmd ? 45 : 40;
                        settings.MaxBitrate = isAmd ? 90 : 70;
                        settings.Encoder = "gpu";
                        break;

                    case "custom":
                        settings.VideoQualityPreset = "custom";
                        break;

                    default:
                        Log.Warning($"Unknown video preset: {presetName}");
                        return;
                }

                Log.Information("Applied video preset '{Preset}': {Resolution}, {FrameRate}fps, {RateControl}, {Encoder}",
                    settings.VideoQualityPreset, settings.Resolution, settings.FrameRate, settings.RateControl, settings.Encoder);

                settings.EndBulkUpdateAndSaveSettings();
                await MessageService.SendSettingsToFrontend("Video preset applied");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply video preset");
                settings.EndBulkUpdateAndSaveSettings();
            }
        }

        private static bool IsAmdClipGpu()
        {
            return GeneralUtils.DetectGpuVendor() == GeneralUtils.GpuVendor.AMD;
        }

        private static string DefaultClipEncoderPreset()
        {
            return GeneralUtils.DetectGpuVendor() switch
            {
                GeneralUtils.GpuVendor.AMD => "transcoding",
                GeneralUtils.GpuVendor.Intel => "medium",
                _ => "medium",
            };
        }

        /// <summary>
        /// Applies a clip quality preset to the settings (aligned with video/recording presets)
        /// </summary>
        public static async Task ApplyClipPreset(string presetName)
        {
            var settings = Settings.Instance;
            settings.BeginBulkUpdate();
            bool isAmd = IsAmdClipGpu();

            try
            {
                switch (presetName.ToLower())
                {
                    case "low":
                        settings.ClipQualityPreset = "low";
                        settings.ClipFps = 30;
                        settings.ClipRateControl = "VBR";
                        settings.ClipQualityGpu = isAmd ? 22 : 24;
                        settings.ClipBitrate = isAmd ? 20 : 15;
                        settings.ClipMinBitrate = 10;
                        settings.ClipMaxBitrate = isAmd ? 20 : 15;
                        settings.ClipEncoder = "gpu";
                        settings.ClipCodec = "h264";
                        settings.ClipAudioQuality = "96k";
                        settings.ClipPreset = DefaultClipEncoderPreset();
                        break;

                    case "standard":
                        settings.ClipQualityPreset = "standard";
                        settings.ClipFps = 60;
                        settings.ClipRateControl = "VBR";
                        settings.ClipQualityGpu = isAmd ? 20 : 22;
                        settings.ClipBitrate = isAmd ? 40 : 30;
                        settings.ClipMinBitrate = isAmd ? 25 : 20;
                        settings.ClipMaxBitrate = isAmd ? 50 : 40;
                        settings.ClipEncoder = "gpu";
                        settings.ClipCodec = "h264";
                        settings.ClipAudioQuality = "128k";
                        settings.ClipPreset = DefaultClipEncoderPreset();
                        break;

                    case "high":
                        settings.ClipQualityPreset = "high";
                        settings.ClipFps = 60;
                        settings.ClipRateControl = "VBR";
                        settings.ClipQualityGpu = isAmd ? 18 : 20;
                        settings.ClipBitrate = isAmd ? 60 : 50;
                        settings.ClipMinBitrate = isAmd ? 45 : 40;
                        settings.ClipMaxBitrate = isAmd ? 90 : 70;
                        settings.ClipEncoder = "gpu";
                        settings.ClipCodec = "h264";
                        settings.ClipAudioQuality = "192k";
                        settings.ClipPreset = DefaultClipEncoderPreset();
                        break;

                    case "custom":
                        settings.ClipQualityPreset = "custom";
                        break;

                    default:
                        Log.Warning($"Unknown clip preset: {presetName}");
                        return;
                }

                Log.Information("Applied clip preset '{Preset}': {Encoder}, {RateControl}, {Fps}fps, {Codec}, {Audio} audio, {EncoderPreset}",
                    settings.ClipQualityPreset, settings.ClipEncoder, settings.ClipRateControl, settings.ClipFps, settings.ClipCodec, settings.ClipAudioQuality, settings.ClipPreset);

                settings.EndBulkUpdateAndSaveSettings();
                await MessageService.SendSettingsToFrontend("Clip preset applied");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply clip preset");
                settings.EndBulkUpdateAndSaveSettings();
            }
        }
    }
}
