using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public sealed class EspWidget : SKWidget
    {
        private SKBitmap _espBitmap;
        private SKCanvas _espCanvas;
        private readonly float _textOffsetY;
        private readonly float _boxHalfSize;
        
        // FPS tracking
        private DateTime _lastFrameTime = DateTime.UtcNow;
        private float _currentFps = 0f;
        private readonly Queue<float> _fpsHistory = new Queue<float>();
        private const int FPS_HISTORY_SIZE = 10; // Average over last 10 frames
        
        private static Config Config => Program.Config;

        public EspWidget(SKGLElement parent, SKRect location, bool minimized, float scale)
            : base(parent, "ESP", new SKPoint(location.Left, location.Top), new SKSize(location.Width, location.Height), scale)
        {
            _espBitmap = new SKBitmap((int)location.Width, (int)location.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
            _espCanvas = new SKCanvas(_espBitmap);
            _textOffsetY = 12.5f * scale;
            _boxHalfSize = 4f * scale;
            Minimized = minimized;
            SetScaleFactor(scale);
        }

        private static LocalPlayer LocalPlayer => Memory.LocalPlayer;
        private static IReadOnlyCollection<Player> AllPlayers => Memory.Players;
        private static bool InRaid => Memory.InRaid;
        private static IEnumerable<LootItem> Loot => Memory.Loot?.FilteredLoot;
        private static IEnumerable<StaticLootContainer> Containers => Memory.Loot?.StaticLootContainers;

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);
            if (!Minimized)
                RenderESPWidget(canvas, ClientRectangle);
        }

        private void RenderESPWidget(SKCanvas parent, SKRect dest)
        {
            EnsureBitmapSize();

            _espCanvas.Clear(SKColors.Transparent);

            try
            {
                // Calculate FPS
                var now = DateTime.UtcNow;
                var deltaTime = (float)(now - _lastFrameTime).TotalMilliseconds;
                _lastFrameTime = now;
                
                if (deltaTime > 0)
                {
                    var instantFps = 1000f / deltaTime;
                    _fpsHistory.Enqueue(instantFps);
                    
                    if (_fpsHistory.Count > FPS_HISTORY_SIZE)
                        _fpsHistory.Dequeue();
                    
                    _currentFps = _fpsHistory.Average();
                }

                var inRaid = InRaid;
                var localPlayer = LocalPlayer;

                if (inRaid && localPlayer != null)
                {
                    // Debug: Track container processing
                    var containerCount = Containers?.Count() ?? 0;
                    var lootCount = Loot?.Count() ?? 0;
                    
                    var debugText = $"FPS: {_currentFps:F1} | Containers: {containerCount}, Loot: {lootCount}";
                    _espCanvas.DrawText(debugText, new SKPoint(10, 20), TextESPWidgetLoot);

                    if (Config.ProcessLoot)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        DrawLoot(localPlayer);
                        sw.Stop();
                        var lootTime = sw.ElapsedMilliseconds;
                        
                        sw.Restart();
                        DrawContainers(localPlayer);
                        sw.Stop();
                        var containerTime = sw.ElapsedMilliseconds;
                        
                        var timingText = $"Loot: {lootTime}ms, Containers: {containerTime}ms";
                        _espCanvas.DrawText(timingText, new SKPoint(10, 40), TextESPWidgetLoot);
                    }

                    DrawPlayers();
                    DrawCrosshair();
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ESP WIDGET RENDER ERROR: {ex}");
            }

            _espCanvas.Flush();
            parent.DrawBitmap(_espBitmap, dest, SharedPaints.PaintBitmap);
        }

        private void EnsureBitmapSize()
        {
            var size = Size;
            if (_espBitmap == null || _espCanvas == null ||
                _espBitmap.Width != size.Width || _espBitmap.Height != size.Height)
            {
                _espCanvas?.Dispose();
                _espCanvas = null;
                _espBitmap?.Dispose();
                _espBitmap = null;

                _espBitmap = new SKBitmap((int)size.Width, (int)size.Height,
                    SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                _espCanvas = new SKCanvas(_espBitmap);
            }
        }

        private void DrawLoot(LocalPlayer localPlayer)
        {
            var loot = Loot;
            if (loot == null) return;

            foreach (var item in loot)
            {
                var dist = Vector3.Distance(localPlayer.Position, item.Position);
                if (dist >= 10f) continue;

                if (!CameraManagerBase.WorldToScreen(ref item.Position, out var itemScrPos)) continue;

                var adjPos = ScaleESPPoint(itemScrPos);
                var boxPt = new SKRect(
                    adjPos.X - _boxHalfSize,
                    adjPos.Y + _boxHalfSize,
                    adjPos.X + _boxHalfSize,
                    adjPos.Y - _boxHalfSize);

                var textPt = new SKPoint(adjPos.X, adjPos.Y + _textOffsetY);

                _espCanvas.DrawRect(boxPt, PaintESPWidgetLoot);
                var label = item.GetUILabel() + $" ({dist:n1}m)";
                _espCanvas.DrawText(label, textPt, TextESPWidgetLoot);
            }
        }

        private void DrawContainers(LocalPlayer localPlayer)
        {
            if (!Config.Containers.Show) return;

            var containers = Containers;
            if (containers == null) return;

            foreach (var container in containers)
            {
                if (!LootSettingsControl.ContainerIsTracked(container.ID ?? "NULL")) continue;

                if (Config.Containers.HideSearched && container.Searched) continue;

                var dist = Vector3.Distance(localPlayer.Position, container.Position);
                if (dist >= 10f) continue;

                if (!CameraManagerBase.WorldToScreen(ref container.Position, out var containerScrPos)) continue;

                var adjPos = ScaleESPPoint(containerScrPos);
                var boxPt = new SKRect(
                    adjPos.X - _boxHalfSize,
                    adjPos.Y + _boxHalfSize,
                    adjPos.X + _boxHalfSize,
                    adjPos.Y - _boxHalfSize);

                var textPt = new SKPoint(adjPos.X, adjPos.Y + _textOffsetY);

                _espCanvas.DrawRect(boxPt, PaintESPWidgetLoot);
                var label = $"{container.Name} ({dist:n1}m)";
                _espCanvas.DrawText(label, textPt, TextESPWidgetLoot);
            }
        }

        private void DrawPlayers()
        {
            var allPlayers = AllPlayers?
                .Where(x => x.IsActive && x.IsAlive && x is not Tarkov.EFTPlayer.LocalPlayer);

            if (allPlayers == null) return;

            var scaleX = _espBitmap.Width / (float)CameraManagerBase.Viewport.Width;
            var scaleY = _espBitmap.Height / (float)CameraManagerBase.Viewport.Height;

            foreach (var player in allPlayers)
            {
                if (player.Skeleton.UpdateESPWidgetBuffer(scaleX, scaleY))
                {
                    _espCanvas.DrawPoints(SKPointMode.Lines, Skeleton.ESPWidgetBuffer, GetPlayerPaint(player));
                }
            }
        }

        private void DrawCrosshair()
        {
            var bounds = _espBitmap.Info.Rect;
            float centerX = bounds.Left + bounds.Width / 2;
            float centerY = bounds.Top + bounds.Height / 2;

            _espCanvas.DrawLine(bounds.Left, centerY, bounds.Right, centerY, PaintESPWidgetCrosshair);
            _espCanvas.DrawLine(centerX, bounds.Top, centerX, bounds.Bottom, PaintESPWidgetCrosshair);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SKPoint ScaleESPPoint(SKPoint original)
        {
            var scaleX = _espBitmap.Width / (float)CameraManagerBase.Viewport.Width;
            var scaleY = _espBitmap.Height / (float)CameraManagerBase.Viewport.Height;

            return new SKPoint(original.X * scaleX, original.Y * scaleY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKPaint GetPlayerPaint(Player player)
        {
            if (player.IsAimbotLocked)
                return PaintESPWidgetAimbotLocked;

            if (player.IsFocused)
                return PaintESPWidgetFocused;

            if (player is LocalPlayer)
                return PaintESPWidgetLocalPlayer;

            switch (player.Type)
            {
                case Player.PlayerType.Teammate:
                    return PaintESPWidgetTeammate;
                case Player.PlayerType.USEC:
                    return PaintESPWidgetUSEC;
                case Player.PlayerType.BEAR:
                    return PaintESPWidgetBEAR;
                case Player.PlayerType.AIScav:
                    return PaintESPWidgetScav;
                case Player.PlayerType.AIRaider:
                    return PaintESPWidgetRaider;
                case Player.PlayerType.AIBoss:
                    return PaintESPWidgetBoss;
                case Player.PlayerType.PScav:
                    return PaintESPWidgetPScav;
                case Player.PlayerType.SpecialPlayer:
                    return PaintESPWidgetSpecial;
                case Player.PlayerType.Streamer:
                    return PaintESPWidgetStreamer;
                default:
                    return PaintESPWidgetUSEC;
            }
        }

        public override void SetScaleFactor(float newScale)
        {
            base.SetScaleFactor(newScale);

            var newLocation = new SKPoint(Location.X, Location.Y);
            var newSize = new SKSize(Size.Width, Size.Height);

            _espCanvas?.Dispose();
            _espCanvas = null;
            _espBitmap?.Dispose();
            _espBitmap = null;

            _espBitmap = new SKBitmap((int)newSize.Width, (int)newSize.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
            _espCanvas = new SKCanvas(_espBitmap);
        }

        public override void Dispose()
        {
            _espCanvas?.Dispose();
            _espBitmap?.Dispose();
            base.Dispose();
        }

        #region Paint Objects
        private static SKPaint PaintESPWidgetCrosshair { get; } = new()
        {
            Color = SKColors.LimeGreen,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetLocalPlayer { get; } = new()
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetUSEC { get; } = new()
        {
            Color = SKColors.Blue,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetBEAR { get; } = new()
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetAimbotLocked { get; } = new()
        {
            Color = SKColors.Orange,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetSpecial { get; } = new()
        {
            Color = SKColors.Purple,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetStreamer { get; } = new()
        {
            Color = SKColors.Pink,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetTeammate { get; } = new()
        {
            Color = SKColors.LimeGreen,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetBoss { get; } = new()
        {
            Color = SKColors.Yellow,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetScav { get; } = new()
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetRaider { get; } = new()
        {
            Color = SKColors.Orange,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetPScav { get; } = new()
        {
            Color = SKColors.Gray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetFocused { get; } = new()
        {
            Color = SKColors.Cyan,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetLoot { get; } = new()
        {
            Color = SKColors.LimeGreen,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        internal static SKPaint TextESPWidgetLoot { get; } = new()
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 12,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };
        #endregion
    }
}