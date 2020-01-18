﻿// Copyright (c) Wiesław Šoltés. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace Svg.Skia
{
    public static class SKPaintUtil
    {
        public static char[] s_fontFamilyTrim = new char[] { '\'' };

        public static float AdjustSvgOpacity(float opacity)
        {
            return Math.Min(Math.Max(opacity, 0), 1);
        }

        public static SvgUnit NormalizeSvgUnit(SvgUnit svgUnit, SvgCoordinateUnits svgCoordinateUnits)
        {
            return svgUnit.Type == SvgUnitType.Percentage && svgCoordinateUnits == SvgCoordinateUnits.ObjectBoundingBox ?
                    new SvgUnit(SvgUnitType.User, svgUnit.Value / 100) : svgUnit;
        }

        public static SKColor GetColor(SvgColourServer svgColourServer, float opacity, IgnoreAttributes ignoreAttributes, bool forStroke = false)
        {
            if (svgColourServer == SvgPaintServer.None)
            {
                return SKColors.Transparent;
            }

            if (svgColourServer == SvgPaintServer.NotSet && forStroke)
            {
                return SKColors.Transparent;
            }

            var colour = svgColourServer.Colour;
            byte alpha = ignoreAttributes.HasFlag(IgnoreAttributes.Opacity) ? 
                svgColourServer.Colour.A : 
                (byte)Math.Round((opacity * (svgColourServer.Colour.A / 255.0)) * 255);

            return new SKColor(colour.R, colour.G, colour.B, alpha);
        }

        public static SKPathEffect? CreateDash(SvgElement svgElement, SKRect skBounds)
        {
            var strokeDashArray = svgElement.StrokeDashArray;
            var strokeDashOffset = svgElement.StrokeDashOffset;
            var count = strokeDashArray.Count;

            if (strokeDashArray != null && count > 0)
            {
                bool isOdd = count % 2 != 0;
                float sum = 0f;
                float[] intervals = new float[isOdd ? count * 2 : count];
                for (int i = 0; i < count; i++)
                {
                    var dash = strokeDashArray[i].ToDeviceValue(UnitRenderingType.Other, svgElement, skBounds);
                    if (dash < 0f)
                    {
                        return null;
                    }

                    intervals[i] = dash;

                    if (isOdd)
                    {
                        intervals[i + count] = intervals[i];
                    }

                    sum += dash;
                }

                if (sum <= 0f)
                {
                    return null;
                }

                float phase = strokeDashOffset != null ? strokeDashOffset.ToDeviceValue(UnitRenderingType.Other, svgElement, skBounds) : 0f;

                return SKPathEffect.CreateDash(intervals, phase);
            }

            return null;
        }

        public static void GetStops(SvgGradientServer svgGradientServer, SKRect skBounds, List<SKColor> colors, List<float> colorPos, SvgVisualElement svgVisualElement, float opacity, IgnoreAttributes ignoreAttributes)
        {
            foreach (var child in svgGradientServer.Children)
            {
                if (child is SvgGradientStop svgGradientStop)
                {
                    var svgStopColor = svgGradientStop.StopColor;
                    if (svgStopColor is SvgColourServer stopColorSvgColourServer)
                    {
                        var stopOpacity = AdjustSvgOpacity(svgGradientStop.StopOpacity);
                        var stopColor = GetColor(stopColorSvgColourServer, opacity * stopOpacity, ignoreAttributes, false);
                        float offset = svgGradientStop.Offset.ToDeviceValue(UnitRenderingType.Horizontal, svgGradientServer, skBounds);
                        offset /= skBounds.Width;
                        offset = (float)Math.Round(offset, 1, MidpointRounding.AwayFromZero);
                        colors.Add(stopColor);
                        colorPos.Add(offset);
                    }
                }
            }

            var inheritGradient = SvgDeferredPaintServer.TryGet<SvgGradientServer>(svgGradientServer.InheritGradient, svgVisualElement);
            if (colors.Count == 0 && inheritGradient != null)
            {
                GetStops(inheritGradient, skBounds, colors, colorPos, svgVisualElement, opacity, ignoreAttributes);
            }
        }

        public static SKShader CreateLinearGradient(SvgLinearGradientServer svgLinearGradientServer, SKRect skBounds, SvgVisualElement svgVisualElement, float opacity, IgnoreAttributes ignoreAttributes)
        {
            var normilizedX1 = NormalizeSvgUnit(svgLinearGradientServer.X1, svgLinearGradientServer.GradientUnits);
            var normilizedY1 = NormalizeSvgUnit(svgLinearGradientServer.Y1, svgLinearGradientServer.GradientUnits);
            var normilizedX2 = NormalizeSvgUnit(svgLinearGradientServer.X2, svgLinearGradientServer.GradientUnits);
            var normilizedY2 = NormalizeSvgUnit(svgLinearGradientServer.Y2, svgLinearGradientServer.GradientUnits);

            float x1 = normilizedX1.ToDeviceValue(UnitRenderingType.Horizontal, svgLinearGradientServer, skBounds);
            float y1 = normilizedY1.ToDeviceValue(UnitRenderingType.Vertical, svgLinearGradientServer, skBounds);
            float x2 = normilizedX2.ToDeviceValue(UnitRenderingType.Horizontal, svgLinearGradientServer, skBounds);
            float y2 = normilizedY2.ToDeviceValue(UnitRenderingType.Vertical, svgLinearGradientServer, skBounds);

            var skStart = new SKPoint(x1, y1);
            var skEnd = new SKPoint(x2, y2);

            var colors = new List<SKColor>();
            var colorPos = new List<float>();

            GetStops(svgLinearGradientServer, skBounds, colors, colorPos, svgVisualElement, opacity, ignoreAttributes);

            var shaderTileMode = svgLinearGradientServer.SpreadMethod switch
            {
                SvgGradientSpreadMethod.Reflect => SKShaderTileMode.Mirror,
                SvgGradientSpreadMethod.Repeat => SKShaderTileMode.Repeat,
                _ => SKShaderTileMode.Clamp,
            };
            var skColors = colors.ToArray();
            float[] skColorPos = colorPos.ToArray();

            if (skColors.Length == 0)
            {
                return SKShader.CreateColor(SKColors.Transparent);
            }
            else if (skColors.Length == 1)
            {
                return SKShader.CreateColor(skColors[0]);
            }

            var svgGradientTransform = svgLinearGradientServer.GradientTransform;

            if (svgLinearGradientServer.GradientUnits == SvgCoordinateUnits.ObjectBoundingBox)
            {
                var skBoundingBoxTransform = new SKMatrix()
                {
                    ScaleX = skBounds.Width,
                    SkewY = 0f,
                    SkewX = 0f,
                    ScaleY = skBounds.Height,
                    TransX = skBounds.Left,
                    TransY = skBounds.Top,
                    Persp0 = 0,
                    Persp1 = 0,
                    Persp2 = 1
                };

                if (svgGradientTransform != null && svgGradientTransform.Count > 0)
                {
                    var gradientTransform = SKMatrixUtil.GetSKMatrix(svgGradientTransform);
                    SKMatrix.PreConcat(ref skBoundingBoxTransform, ref gradientTransform);
                }

                return SKShader.CreateLinearGradient(skStart, skEnd, skColors, skColorPos, shaderTileMode, skBoundingBoxTransform);
            }
            else
            {
                if (svgGradientTransform != null && svgGradientTransform.Count > 0)
                {
                    var gradientTransform = SKMatrixUtil.GetSKMatrix(svgGradientTransform);
                    return SKShader.CreateLinearGradient(skStart, skEnd, skColors, skColorPos, shaderTileMode, gradientTransform);
                }
                else
                {
                    return SKShader.CreateLinearGradient(skStart, skEnd, skColors, skColorPos, shaderTileMode);
                }
            }
        }

        public static SKShader CreateTwoPointConicalGradient(SvgRadialGradientServer svgRadialGradientServer, SKRect skBounds, SvgVisualElement svgVisualElement, float opacity, IgnoreAttributes ignoreAttributes)
        {
            var normilizedCenterX = NormalizeSvgUnit(svgRadialGradientServer.CenterX, svgRadialGradientServer.GradientUnits);
            var normilizedCenterY = NormalizeSvgUnit(svgRadialGradientServer.CenterY, svgRadialGradientServer.GradientUnits);
            var normilizedFocalX = NormalizeSvgUnit(svgRadialGradientServer.FocalX, svgRadialGradientServer.GradientUnits);
            var normilizedFocalY = NormalizeSvgUnit(svgRadialGradientServer.FocalY, svgRadialGradientServer.GradientUnits);
            var normilizedRadius = NormalizeSvgUnit(svgRadialGradientServer.Radius, svgRadialGradientServer.GradientUnits);

            float centerX = normilizedCenterX.ToDeviceValue(UnitRenderingType.Horizontal, svgRadialGradientServer, skBounds);
            float centerY = normilizedCenterY.ToDeviceValue(UnitRenderingType.Vertical, svgRadialGradientServer, skBounds);
            float focalX = normilizedFocalX.ToDeviceValue(UnitRenderingType.Horizontal, svgRadialGradientServer, skBounds);
            float focalY = normilizedFocalY.ToDeviceValue(UnitRenderingType.Vertical, svgRadialGradientServer, skBounds);

            var skStart = new SKPoint(centerX, centerY);
            var skEnd = new SKPoint(focalX, focalY);

            float startRadius = 0f;
            float endRadius = normilizedRadius.ToDeviceValue(UnitRenderingType.Other, svgRadialGradientServer, skBounds);

            var colors = new List<SKColor>();
            var colorPos = new List<float>();

            GetStops(svgRadialGradientServer, skBounds, colors, colorPos, svgVisualElement, opacity, ignoreAttributes);

            var shaderTileMode = svgRadialGradientServer.SpreadMethod switch
            {
                SvgGradientSpreadMethod.Reflect => SKShaderTileMode.Mirror,
                SvgGradientSpreadMethod.Repeat => SKShaderTileMode.Repeat,
                _ => SKShaderTileMode.Clamp,
            };
            var skColors = colors.ToArray();
            float[] skColorPos = colorPos.ToArray();

            if (skColors.Length == 0)
            {
                return SKShader.CreateColor(SKColors.Transparent);
            }
            else if (skColors.Length == 1)
            {
                return SKShader.CreateColor(skColors[0]);
            }

            var svgGradientTransform = svgRadialGradientServer.GradientTransform;

            if (svgRadialGradientServer.GradientUnits == SvgCoordinateUnits.ObjectBoundingBox)
            {
                var skBoundingBoxTransform = new SKMatrix()
                {
                    ScaleX = skBounds.Width,
                    SkewY = 0f,
                    SkewX = 0f,
                    ScaleY = skBounds.Height,
                    TransX = skBounds.Left,
                    TransY = skBounds.Top,
                    Persp0 = 0,
                    Persp1 = 0,
                    Persp2 = 1
                };

                if (svgGradientTransform != null && svgGradientTransform.Count > 0)
                {
                    var gradientTransform = SKMatrixUtil.GetSKMatrix(svgGradientTransform);
                    SKMatrix.PreConcat(ref skBoundingBoxTransform, ref gradientTransform);
                }

                return SKShader.CreateTwoPointConicalGradient(
                    skStart, startRadius,
                    skEnd, endRadius,
                    skColors, skColorPos,
                    shaderTileMode,
                    skBoundingBoxTransform);
            }
            else
            {
                if (svgGradientTransform != null && svgGradientTransform.Count > 0)
                {
                    var gradientTransform = SKMatrixUtil.GetSKMatrix(svgGradientTransform);
                    return SKShader.CreateTwoPointConicalGradient(
                        skStart, startRadius,
                        skEnd, endRadius,
                        skColors, skColorPos,
                        shaderTileMode, gradientTransform);
                }
                else
                {
                    return SKShader.CreateTwoPointConicalGradient(
                        skStart, startRadius,
                        skEnd, endRadius,
                        skColors, skColorPos,
                        shaderTileMode);
                }
            }
        }

        public static SKPicture RecordPicture(SvgElementCollection svgElementCollection, float width, float height, SKMatrix skMatrix, float opacity, IgnoreAttributes ignoreAttributes)
        {
            var skSize = new SKSize(width, height);
            var skBounds = SKRect.Create(skSize);
            using var skPictureRecorder = new SKPictureRecorder();
            using var skCanvas = skPictureRecorder.BeginRecording(skBounds);

            skCanvas.SetMatrix(skMatrix);

            using var skPaintOpacity = ignoreAttributes.HasFlag(IgnoreAttributes.Opacity) ? null : GetOpacitySKPaint(opacity);
            if (skPaintOpacity != null)
            {
                skCanvas.SaveLayer(skPaintOpacity);
            }

            foreach (var svgElement in svgElementCollection)
            {
                using var drawable = DrawableFactory.Create(svgElement, skBounds, ignoreAttributes);
                drawable?.Draw(skCanvas, 0f, 0f);
            }

            if (skPaintOpacity != null)
            {
                skCanvas.Restore();
            }

            skCanvas.Restore();

            return skPictureRecorder.EndRecording();
        }

        public static SKShader? CreatePicture(SvgPatternServer svgPatternServer, SKRect skBounds, SvgVisualElement svgVisualElement, float opacity, IgnoreAttributes ignoreAttributes, CompositeDisposable disposable)
        {
            var svgPatternServers = new List<SvgPatternServer>();
            var currentPatternServer = svgPatternServer;
            do
            {
                svgPatternServers.Add(currentPatternServer);
                currentPatternServer = SvgDeferredPaintServer.TryGet<SvgPatternServer>(currentPatternServer.InheritGradient, svgVisualElement);
            } while (currentPatternServer != null);

            SvgPatternServer? firstChildren = null;
            SvgPatternServer? firstX = null;
            SvgPatternServer? firstY = null;
            SvgPatternServer? firstWidth = null;
            SvgPatternServer? firstHeight = null;
            SvgPatternServer? firstPatternUnit = null;
            SvgPatternServer? firstPatternContentUnit = null;
            SvgPatternServer? firstViewBox = null;
            SvgPatternServer? firstAspectRatio = null;

            foreach (var p in svgPatternServers)
            {
                if (firstChildren == null)
                {
                    if (p.Children.Count > 0)
                    {
                        firstChildren = p;
                    }
                }
                if (firstX == null)
                {
                    var pX = p.X;
                    if (pX != null && pX != SvgUnit.None)
                    {
                        firstX = p;
                    }
                }
                if (firstY == null)
                {
                    var pY = p.Y;
                    if (pY != null && pY != SvgUnit.None)
                    {
                        firstY = p;
                    }
                }
                if (firstWidth == null)
                {
                    var pWidth = p.Width;
                    if (pWidth != null && pWidth != SvgUnit.None)
                    {
                        firstWidth = p;
                    }
                }
                if (firstHeight == null)
                {
                    var pHeight = p.Height;
                    if (pHeight != null && pHeight != SvgUnit.None)
                    {
                        firstHeight = p;
                    }
                }
                if (firstPatternUnit == null)
                {
                    var pPatternUnits = p.PatternUnits;
                    if (pPatternUnits != SvgCoordinateUnits.Inherit)
                    {
                        firstPatternUnit = p;
                    }
                }
                if (firstPatternContentUnit == null)
                {
                    var pPatternContentUnits = p.PatternContentUnits;
                    if (pPatternContentUnits != SvgCoordinateUnits.Inherit)
                    {
                        firstPatternContentUnit = p;
                    }
                }
                if (firstViewBox == null)
                {
                    var pViewBox = p.ViewBox;
                    if (pViewBox != null && pViewBox != SvgViewBox.Empty)
                    {
                        firstViewBox = p;
                    }
                }
                if (firstAspectRatio == null)
                {
                    var pAspectRatio = p.AspectRatio;
                    if (pAspectRatio != null && pAspectRatio.Align != SvgPreserveAspectRatio.xMidYMid)
                    {
                        firstAspectRatio = p;
                    }
                }
            }

            if (firstChildren == null || firstWidth == null || firstHeight == null)
            {
                return null;
            }
            var xUnit = firstX == null ? new SvgUnit(0f) : firstX.X;
            var yUnit = firstY == null ? new SvgUnit(0f) : firstY.Y;
            var widthUnit = firstWidth.Width;
            var heightUnit = firstHeight.Height;
            var patternUnits = firstPatternUnit == null ? SvgCoordinateUnits.ObjectBoundingBox : firstPatternUnit.PatternUnits;
            var patternContentUnits = firstPatternContentUnit == null ? SvgCoordinateUnits.UserSpaceOnUse : firstPatternContentUnit.PatternContentUnits;
            var viewBox = firstViewBox == null ? SvgViewBox.Empty : firstViewBox.ViewBox;
            var aspectRatio = firstAspectRatio == null ? new SvgAspectRatio(SvgPreserveAspectRatio.xMidYMid, false) : firstAspectRatio.AspectRatio;

            float x = xUnit.ToDeviceValue(UnitRenderingType.Horizontal, svgPatternServer, skBounds);
            float y = yUnit.ToDeviceValue(UnitRenderingType.Vertical, svgPatternServer, skBounds);
            float width = widthUnit.ToDeviceValue(UnitRenderingType.Horizontal, svgPatternServer, skBounds);
            float height = heightUnit.ToDeviceValue(UnitRenderingType.Vertical, svgPatternServer, skBounds);

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            if (patternUnits == SvgCoordinateUnits.ObjectBoundingBox)
            {
                if (xUnit.Type != SvgUnitType.Percentage)
                {
                    x *= skBounds.Width;
                }

                if (yUnit.Type != SvgUnitType.Percentage)
                {
                    y *= skBounds.Height;
                }

                if (widthUnit.Type != SvgUnitType.Percentage)
                {
                    width *= skBounds.Width;
                }

                if (heightUnit.Type != SvgUnitType.Percentage)
                {
                    height *= skBounds.Height;
                }

                x += skBounds.Left;
                y += skBounds.Top;
            }

            SKRect skRectTransformed = SKRect.Create(x, y, width, height);

            var skMatrix = SKMatrix.MakeIdentity();

            var skPatternTransformMatrix = SKMatrixUtil.GetSKMatrix(svgPatternServer.PatternTransform);
            SKMatrix.PreConcat(ref skMatrix, ref skPatternTransformMatrix);

            var translateTransform = SKMatrix.MakeTranslation(skRectTransformed.Left, skRectTransformed.Top);
            SKMatrix.PreConcat(ref skMatrix, ref translateTransform);

            SKMatrix skPictureTransform = SKMatrix.MakeIdentity();
            if (!viewBox.Equals(SvgViewBox.Empty))
            {
                var viewBoxTransform = SKMatrixUtil.GetSvgViewBoxTransform(
                    viewBox,
                    aspectRatio,
                    0f,
                    0f,
                    skRectTransformed.Width,
                    skRectTransformed.Height);
                SKMatrix.PreConcat(ref skPictureTransform, ref viewBoxTransform);
            }
            else
            {
                if (patternContentUnits == SvgCoordinateUnits.ObjectBoundingBox)
                {
                    var skBoundsScaleTransform = SKMatrix.MakeScale(skBounds.Width, skBounds.Height);
                    SKMatrix.PreConcat(ref skPictureTransform, ref skBoundsScaleTransform);
                }
            }

            var skPicture = RecordPicture(firstChildren.Children, skRectTransformed.Width, skRectTransformed.Height, skPictureTransform, opacity, ignoreAttributes);
            disposable.Add(skPicture);

            return SKShader.CreatePicture(skPicture, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, skMatrix, skPicture.CullRect);
        }

        public static bool SetColorOrShader(SvgVisualElement svgVisualElement, SvgPaintServer server, float opacity, SKRect skBounds, SKPaint skPaint, bool forStroke, IgnoreAttributes ignoreAttributes, CompositeDisposable disposable)
        {
            var fallbackServer = SvgPaintServer.None;
            if (server is SvgDeferredPaintServer svgDeferredPaintServer)
            {
                server = SvgDeferredPaintServer.TryGet<SvgPaintServer>(svgDeferredPaintServer, svgVisualElement);
                fallbackServer = svgDeferredPaintServer.FallbackServer;
            }

            switch (server)
            {
                case SvgColourServer svgColourServer:
                    {
                        skPaint.Color = GetColor(svgColourServer, opacity, ignoreAttributes, forStroke);
                    }
                    break;
                case SvgPatternServer svgPatternServer:
                    {
                        var skShader = CreatePicture(svgPatternServer, skBounds, svgVisualElement, opacity, ignoreAttributes, disposable);
                        if (skShader != null)
                        {
                            disposable.Add(skShader);
                            skPaint.Shader = skShader;
                        }
                        else
                        {
                            if (fallbackServer is SvgColourServer svgColourServerFallback)
                            {
                                skPaint.Color = GetColor(svgColourServerFallback, opacity, ignoreAttributes, forStroke);
                            }
                            else
                            {
                                // Do not draw element.
                                return false;
                            }
                        }
                    }
                    break;
                case SvgLinearGradientServer svgLinearGradientServer:
                    {
                        if (svgLinearGradientServer.GradientUnits == SvgCoordinateUnits.ObjectBoundingBox && (skBounds.Width == 0f || skBounds.Height == 0f))
                        {
                            if (fallbackServer is SvgColourServer svgColourServerFallback)
                            {
                                skPaint.Color = GetColor(svgColourServerFallback, opacity, ignoreAttributes, forStroke);
                            }
                            else
                            {
                                // Do not draw element.
                                return false;
                            }
                        }
                        else
                        {
                            var skShader = CreateLinearGradient(svgLinearGradientServer, skBounds, svgVisualElement, opacity, ignoreAttributes);
                            if (skShader != null)
                            {
                                disposable.Add(skShader);
                                skPaint.Shader = skShader;
                            }
                            else
                            {
                                // Do not draw element.
                                return false;
                            }
                        }
                    }
                    break;
                case SvgRadialGradientServer svgRadialGradientServer:
                    {
                        if (svgRadialGradientServer.GradientUnits == SvgCoordinateUnits.ObjectBoundingBox && (skBounds.Width == 0f || skBounds.Height == 0f))
                        {
                            if (fallbackServer is SvgColourServer svgColourServerFallback)
                            {
                                skPaint.Color = GetColor(svgColourServerFallback, opacity, ignoreAttributes, forStroke);
                            }
                            else
                            {
                                // Do not draw element.
                                return false;
                            }
                        }
                        else
                        {
                            var skShader = CreateTwoPointConicalGradient(svgRadialGradientServer, skBounds, svgVisualElement, opacity, ignoreAttributes);
                            if (skShader != null)
                            {
                                disposable.Add(skShader);
                                skPaint.Shader = skShader;
                            }
                            else
                            {
                                // Do not draw element.
                                return false;
                            }
                        }
                    }
                    break;
                default:
                    // Do not draw element.
                    return false;
            }
            return true;
        }

        public static void SetDash(SvgVisualElement svgVisualElement, SKPaint skPaint, SKRect skBounds, CompositeDisposable disposable)
        {
            var skPathEffect = CreateDash(svgVisualElement, skBounds);
            if (skPathEffect != null)
            {
                disposable.Add(skPathEffect);
                skPaint.PathEffect = skPathEffect;
            }
        }

        public static bool IsAntialias(SvgElement svgElement)
        {
            switch (svgElement.ShapeRendering)
            {
                case SvgShapeRendering.Inherit:
                case SvgShapeRendering.Auto:
                default:
                    return true;
                case SvgShapeRendering.OptimizeSpeed:
                case SvgShapeRendering.CrispEdges:
                case SvgShapeRendering.GeometricPrecision:
                    return false;
            }
        }

        public static bool IsValidFill(SvgElement svgElement)
        {
            var fill = svgElement.Fill;
            return fill != null;
        }

        public static bool IsValidStroke(SvgElement svgElement, SKRect skBounds)
        {
            var stroke = svgElement.Stroke;
            var strokeWidth = svgElement.StrokeWidth;
            return stroke != null
                && stroke != SvgPaintServer.None
                && strokeWidth.ToDeviceValue(UnitRenderingType.Other, svgElement, skBounds) > 0f;
        }

        public static SKPaint? GetFillSKPaint(SvgVisualElement svgVisualElement, SKRect skBounds, IgnoreAttributes ignoreAttributes, CompositeDisposable disposable)
        {
            var skPaint = new SKPaint()
            {
                IsAntialias = IsAntialias(svgVisualElement),
                Style = SKPaintStyle.Fill
            };

            var server = svgVisualElement.Fill;
            var opacity = AdjustSvgOpacity(svgVisualElement.FillOpacity);
            if (SetColorOrShader(svgVisualElement, server, opacity, skBounds, skPaint, forStroke: false, ignoreAttributes, disposable) == false)
            {
                return null;
            }

            disposable.Add(skPaint);
            return skPaint;
        }

        public static SKPaint? GetStrokeSKPaint(SvgVisualElement svgVisualElement, SKRect skBounds, IgnoreAttributes ignoreAttributes, CompositeDisposable disposable)
        {
            var skPaint = new SKPaint()
            {
                IsAntialias = IsAntialias(svgVisualElement),
                Style = SKPaintStyle.Stroke
            };

            var server = svgVisualElement.Stroke;
            var opacity = AdjustSvgOpacity(svgVisualElement.StrokeOpacity);
            if (SetColorOrShader(svgVisualElement, server, opacity, skBounds, skPaint, forStroke: true, ignoreAttributes, disposable) == false)
            {
                return null;
            }

            switch (svgVisualElement.StrokeLineCap)
            {
                case SvgStrokeLineCap.Butt:
                    skPaint.StrokeCap = SKStrokeCap.Butt;
                    break;
                case SvgStrokeLineCap.Round:
                    skPaint.StrokeCap = SKStrokeCap.Round;
                    break;
                case SvgStrokeLineCap.Square:
                    skPaint.StrokeCap = SKStrokeCap.Square;
                    break;
            }

            switch (svgVisualElement.StrokeLineJoin)
            {
                case SvgStrokeLineJoin.Miter:
                    skPaint.StrokeJoin = SKStrokeJoin.Miter;
                    break;
                case SvgStrokeLineJoin.Round:
                    skPaint.StrokeJoin = SKStrokeJoin.Round;
                    break;
                case SvgStrokeLineJoin.Bevel:
                    skPaint.StrokeJoin = SKStrokeJoin.Bevel;
                    break;
            }

            skPaint.StrokeMiter = svgVisualElement.StrokeMiterLimit;

            skPaint.StrokeWidth = svgVisualElement.StrokeWidth.ToDeviceValue(UnitRenderingType.Other, svgVisualElement, skBounds);

            var strokeDashArray = svgVisualElement.StrokeDashArray;
            if (strokeDashArray != null)
            {
                SetDash(svgVisualElement, skPaint, skBounds, disposable);
            }

            disposable.Add(skPaint);
            return skPaint;
        }

        public static SKPaint? GetOpacitySKPaint(float opacity)
        {
            if (opacity < 1f)
            {
                var skPaint = new SKPaint()
                {
                    IsAntialias = true,
                    Color = new SKColor(255, 255, 255, (byte)Math.Round(opacity * 255)),
                    Style = SKPaintStyle.StrokeAndFill
                };
                return skPaint;
            }
            return null;
        }

        public static SKPaint? GetOpacitySKPaint(SvgElement svgElement, CompositeDisposable disposable)
        {
            float opacity = AdjustSvgOpacity(svgElement.Opacity);
            var skPaint = GetOpacitySKPaint(opacity);
            if (skPaint != null)
            {
                disposable.Add(skPaint);
                return skPaint;
            }
            return null;
        }

        public static SKFontStyleWeight SKFontStyleWeight(SvgFontWeight svgFontWeight)
        {
            var fontWeight = SkiaSharp.SKFontStyleWeight.Normal;

            switch (svgFontWeight)
            {
                case SvgFontWeight.Inherit:
                    // TODO: Implement SvgFontWeight.Inherit
                    break;
                case SvgFontWeight.Bolder:
                    // TODO: Implement SvgFontWeight.Bolder
                    break;
                case SvgFontWeight.Lighter:
                    // TODO: Implement SvgFontWeight.Lighter
                    break;
                case SvgFontWeight.W100:
                    fontWeight = SkiaSharp.SKFontStyleWeight.Thin;
                    break;
                case SvgFontWeight.W200:
                    fontWeight = SkiaSharp.SKFontStyleWeight.ExtraLight;
                    break;
                case SvgFontWeight.W300:
                    fontWeight = SkiaSharp.SKFontStyleWeight.Light;
                    break;
                case SvgFontWeight.W400: // SvgFontWeight.Normal:
                    fontWeight = SkiaSharp.SKFontStyleWeight.Normal;
                    break;
                case SvgFontWeight.W500:
                    fontWeight = SkiaSharp.SKFontStyleWeight.Medium;
                    break;
                case SvgFontWeight.W600:
                    fontWeight = SkiaSharp.SKFontStyleWeight.SemiBold;
                    break;
                case SvgFontWeight.W700: // SvgFontWeight.Bold:
                    fontWeight = SkiaSharp.SKFontStyleWeight.Bold;
                    break;
                case SvgFontWeight.W800:
                    fontWeight = SkiaSharp.SKFontStyleWeight.ExtraBold;
                    break;
                case SvgFontWeight.W900:
                    fontWeight = SkiaSharp.SKFontStyleWeight.Black;
                    break;
            }

            return fontWeight;
        }

        public static SKFontStyleWidth ToSKFontStyleWidth(string attributeFontStretch)
        {
            var fontWidth = SKFontStyleWidth.Normal;

            switch (attributeFontStretch?.ToLower())
            {
                case "inherit":
                    // TODO: Implement inherit
                    break;
                case "ultra-condensed":
                    fontWidth = SKFontStyleWidth.UltraCondensed;
                    break;
                case "extra-condensed":
                    fontWidth = SKFontStyleWidth.ExtraCondensed;
                    break;
                case "condensed":
                    fontWidth = SKFontStyleWidth.Condensed;
                    break;
                case "semi-condensed":
                    fontWidth = SKFontStyleWidth.SemiCondensed;
                    break;
                case "normal":
                    fontWidth = SKFontStyleWidth.Normal;
                    break;
                case "semi-expanded":
                    fontWidth = SKFontStyleWidth.SemiExpanded;
                    break;
                case "expanded":
                    fontWidth = SKFontStyleWidth.Expanded;
                    break;
                case "extra-expanded":
                    fontWidth = SKFontStyleWidth.ExtraExpanded;
                    break;
                case "ultra-expanded":
                    fontWidth = SKFontStyleWidth.UltraExpanded;
                    break;
                default:
                    break;
            }

            return fontWidth;
        }

        public static SKTextAlign ToSKTextAlign(SvgTextAnchor textAnchor)
        {
            return textAnchor switch
            {
                SvgTextAnchor.Middle => SKTextAlign.Center,
                SvgTextAnchor.End => SKTextAlign.Right,
                _ => SKTextAlign.Left,
            };
        }

        public static SKFontStyleSlant ToSKFontStyleSlant(SvgFontStyle fontStyle)
        {
            return fontStyle switch
            {
                SvgFontStyle.Oblique => SKFontStyleSlant.Oblique,
                SvgFontStyle.Italic => SKFontStyleSlant.Italic,
                _ => SKFontStyleSlant.Upright,
            };
        }

        private static void SetTypeface(SvgTextBase svgText, SKPaint skPaint, CompositeDisposable disposable)
        {
            var fontWeight = SKFontStyleWeight(svgText.FontWeight);

            // TODO: Use FontStretch property.
            svgText.TryGetAttribute("font-stretch", out string attributeFontStretch);
            var fontWidth = ToSKFontStyleWidth(attributeFontStretch);

            var fontStyle = ToSKFontStyleSlant(svgText.FontStyle);

            var fontFamily = svgText.FontFamily;

            var skTypeface = default(SKTypeface);
            var fontFamilyNames = fontFamily?.Split(',')?.Select(x => x.Trim().Trim(s_fontFamilyTrim))?.ToArray();
            if (fontFamilyNames != null && fontFamilyNames.Length > 0)
            {
                var defaultName = SKTypeface.Default.FamilyName;

                foreach (var fontFamilyName in fontFamilyNames)
                {
                    skTypeface = SKTypeface.FromFamilyName(fontFamilyName, fontWeight, fontWidth, fontStyle);
                    if (skTypeface != null)
                    {
                        if (!skTypeface.FamilyName.Equals(fontFamilyName, StringComparison.Ordinal)
                            && defaultName.Equals(skTypeface.FamilyName, StringComparison.Ordinal))
                        {
                            skTypeface.Dispose();
                            continue;
                        }
                        break;
                    }
                }
            }

            if (skTypeface != null)
            {
                disposable.Add(skTypeface);
                skPaint.Typeface = skTypeface;
            }
        }

        public static void SetSKPaintText(SvgTextBase svgText, SKRect skBounds, SKPaint skPaint, CompositeDisposable disposable)
        {
            skPaint.LcdRenderText = true;
            skPaint.SubpixelText = true;
            skPaint.TextEncoding = SKTextEncoding.Utf16;

            skPaint.TextAlign = ToSKTextAlign(svgText.TextAnchor);

            if (svgText.TextDecoration.HasFlag(SvgTextDecoration.Underline))
            {
                // TODO: Implement SvgTextDecoration.Underline
            }

            if (svgText.TextDecoration.HasFlag(SvgTextDecoration.Overline))
            {
                // TODO: Implement SvgTextDecoration.Overline
            }

            if (svgText.TextDecoration.HasFlag(SvgTextDecoration.LineThrough))
            {
                // TODO: Implement SvgTextDecoration.LineThrough
            }

            float fontSize;
            var fontSizeUnit = svgText.FontSize;
            if (fontSizeUnit == SvgUnit.None || fontSizeUnit == SvgUnit.Empty)
            {
                // TODO: Do not use implicit float conversion from SvgUnit.ToDeviceValue
                //fontSize = new SvgUnit(SvgUnitType.Em, 1.0f);
                // NOTE: Use default SkPaint Font_Size
                fontSize = 12f;
            }
            else
            {
                fontSize = fontSizeUnit.ToDeviceValue(UnitRenderingType.Vertical, svgText, skBounds);
            }

            skPaint.TextSize = fontSize;

            SetTypeface(svgText, skPaint, disposable);
        }
    }
}
