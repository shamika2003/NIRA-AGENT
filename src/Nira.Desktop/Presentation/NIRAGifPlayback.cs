using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NIRAAgent.UI.Presentation;

/// <summary>Only locally verified, host-composed full-frame animations; no network or external player.</summary>
internal sealed class NIRAGifPlayback
{
    private readonly Image _image;
    private readonly DispatcherTimer _timer;
    private NIRAImageStrip.Photo? _photo;
    private BitmapSource[]? _frames;
    private int _index;
    private bool _requested;
    internal bool IsPlaying => _requested;
    internal NIRAGifPlayback(Image image)
    {
        _image = image;
        _timer = new DispatcherTimer(DispatcherPriority.Background, image.Dispatcher);
        _timer.Tick += (_, _) =>
        {
            if (_frames is not { Length: 4 } frames) { _timer.Stop(); return; }
            _index = (_index + 1) % frames.Length;
            _image.Source = frames[_index];
        };
        image.Loaded += (_, _) => UpdateTimer();
        image.Unloaded += (_, _) => { _timer.Stop(); _frames = null; _index = 0; _image.Source = _photo?.Bitmap; };
        image.IsVisibleChanged += (_, _) => UpdateTimer();
    }
    internal void SetPhoto(NIRAImageStrip.Photo photo)
    {
        _timer.Stop(); _frames = null; _index = 0; _photo = photo;
        _image.Source = photo.Bitmap;
        _requested = photo.IsAnimated && SystemParameters.ClientAreaAnimation &&
            Environment.GetEnvironmentVariable("NIRA_REDUCE_MOTION") != "1";
        UpdateTimer();
    }
    internal void Toggle()
    {
        if (_photo?.IsAnimated != true) return;
        _requested = !_requested;
        UpdateTimer();
    }
    private void UpdateTimer()
    {
        _timer.Stop();
        var photo = _photo;
        if (!_requested || !_image.IsLoaded || !_image.IsVisible || photo is null || photo.Animation is not byte[] bytes) return;
        try
        {
            if (_frames is null)
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count != 4 || decoder.Frames.Any(frame => frame.PixelWidth != photo.Bitmap.PixelWidth ||
                    frame.PixelHeight != photo.Bitmap.PixelHeight || frame.PixelWidth > 960 || frame.PixelHeight > 960))
                    throw new IOException("Unexpected GIF frames.");
                _frames = decoder.Frames.Select(frame => { frame.Freeze(); return (BitmapSource)frame; }).ToArray();
                _image.Source = _frames[0];
            }
            _timer.Interval = TimeSpan.FromMilliseconds(photo.FrameDurationMs);
            _timer.Start();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { _requested = false; _frames = null; _image.Source = photo.Bitmap; }
    }
}
