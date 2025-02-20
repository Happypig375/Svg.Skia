﻿// https://github.com/mono/SkiaSharp/blob/master/source/SkiaSharp.Views/SkiaSharp.Views.WPF/SKElement.cs
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Svg.Skia;

namespace SvgToPng;

[DefaultEvent("PaintSurface")]
[DefaultProperty("Name")]
public class SKElement : FrameworkElement
{
    private readonly bool designMode;

    private WriteableBitmap bitmap;
    private bool ignorePixelScaling;

    public SKElement()
    {
        designMode = DesignerProperties.GetIsInDesignMode(this);
    }

    [Bindable(false)]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SkiaSharp.SKSize CanvasSize => bitmap == null ? SkiaSharp.SKSize.Empty : new SkiaSharp.SKSize(bitmap.PixelWidth, bitmap.PixelHeight);

    public bool IgnorePixelScaling
    {
        get { return ignorePixelScaling; }
        set
        {
            ignorePixelScaling = value;
            InvalidateVisual();
        }
    }

    [Category("Appearance")]
    public event EventHandler<SKPaintSurfaceEventArgs> PaintSurface;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (designMode)
            return;

        if (Visibility != Visibility.Visible)
            return;

        var size = CreateSize(out var scaleX, out var scaleY);
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var info = new SkiaSharp.SKImageInfo(size.Width, size.Height, SkiaSharp.SKImageInfo.PlatformColorType, SkiaSharp.SKAlphaType.Premul, SKSvgSettings.s_srgb);
        // reset the bitmap if the size has changed
        if (bitmap == null || info.Width != bitmap.PixelWidth || info.Height != bitmap.PixelHeight)
        {
            bitmap = new WriteableBitmap(info.Width, size.Height, 96 * scaleX, 96 * scaleY, PixelFormats.Pbgra32, null);
        }

        // draw on the bitmap
        bitmap.Lock();
        using (var surface = SkiaSharp.SKSurface.Create(info, bitmap.BackBuffer, bitmap.BackBufferStride))
        {
            OnPaintSurface(new SKPaintSurfaceEventArgs(surface, info));
        }

        // draw the bitmap to the screen
        bitmap.AddDirtyRect(new Int32Rect(0, 0, info.Width, size.Height));
        bitmap.Unlock();
        drawingContext.DrawImage(bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    protected virtual void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        // invoke the event
        PaintSurface?.Invoke(this, e);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        InvalidateVisual();
    }

    private SkiaSharp.SKSizeI CreateSize(out double scaleX, out double scaleY)
    {
        scaleX = 1.0;
        scaleY = 1.0;

        var w = ActualWidth;
        var h = ActualHeight;

        if (!IsPositive(w) || !IsPositive(h))
            return SkiaSharp.SKSizeI.Empty;

        if (IgnorePixelScaling)
            return new SkiaSharp.SKSizeI((int)w, (int)h);

        var m = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
        scaleX = m.M11;
        scaleY = m.M22;
        return new SkiaSharp.SKSizeI((int)(w * scaleX), (int)(h * scaleY));

        bool IsPositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }
    }
}
