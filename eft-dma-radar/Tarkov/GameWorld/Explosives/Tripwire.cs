using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using SDK;
using System.Xml.Linq;
using static SDK.Offsets;

namespace eft_dma_radar.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// Represents a Tripwire (with attached Grenade) in Local Game World.
    /// </summary>
    public sealed class Tripwire : IExplosiveItem, IWorldEntity, IMapEntity, IESPEntity
    {
        public static implicit operator ulong(Tripwire x) => x.Addr;

        /// <summary>
        /// Base Address of Grenade Object.
        /// </summary>
        public ulong Addr { get; }

        /// <summary>
        /// True if the Tripwire is in an active state.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Name of grenade tied to tripwire
        /// </summary>
        public string Name { get; private set; }

        public static EntityTypeSettings Settings => Program.Config.EntityTypeSettings.GetSettings("Tripwire");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("Tripwire");

        public Tripwire(ulong baseAddr)
        {
            Addr = baseAddr;
            this.IsActive = GetIsTripwireActive(false);
            if (this.IsActive)
            {
                _position = GetPosition(false);
                Name = GetName();
            }
        }

        public void Refresh()
        {
            this.IsActive = GetIsTripwireActive();
            if (this.IsActive)
            {
                this.Position = GetPosition();
                this.FromPosition = GetFromPosition();
            }
        }

        private bool GetIsTripwireActive(bool useCache = true)
        {
            var status = (Enums.ETripwireState)Memory.ReadValue<int>(this + Offsets.TripwireSynchronizableObject._tripwireState, useCache);
            return status is Enums.ETripwireState.Wait || status is Enums.ETripwireState.Active;
        }

        private Vector3 GetPosition(bool useCache = true)
        {
            var pos = Memory.ReadValue<Vector3>(this + Offsets.TripwireSynchronizableObject.ToPosition, useCache);
            pos.Y += 0.175f;

            return pos;
        }

        private Vector3 GetFromPosition(bool useCache = true)
        {
            var pos = Memory.ReadValue<Vector3>(this + Offsets.TripwireSynchronizableObject.FromPosition, useCache);
            pos.Y += 0.175f;

            return pos;
        }

        private string GetName()
        {
            if (!IsActive)
                return "";

            var id = Memory.ReadValue<Types.MongoID>(this + Offsets.TripwireSynchronizableObject.GrenadeTemplateId);
            var name = Memory.ReadUnityString(id.StringID, useCache: false);

            if (EftDataManager.AllItems.TryGetValue(name, out var grenade))
            {
                if (grenade.BsgId == "67b49e7335dec48e3e05e057")
                    return $"{grenade.ShortName} (SHORT)";
                else
                    return grenade.ShortName;
            }
            else
                return "Tripwire";
        }

        private List<SKPoint> GetTripwireLine()
        {
            Vector3 ToPosition = GetPosition(), FromPosition = GetFromPosition();

            if (!CameraManager.WorldToScreen(ref ToPosition, out var toScreenPos) || !CameraManager.WorldToScreen(ref FromPosition, out var fromScreenPos))
                return null;

            return new List<SKPoint> { toScreenPos, fromScreenPos };
        }

        #region Interfaces

        private Vector3 _position;
        private Vector3 _fromPosition;
        public ref Vector3 Position => ref _position;
        public ref Vector3 FromPosition => ref _fromPosition;

        public void Draw(SKCanvas canvas, LoneMapParams mapParams, ILocalPlayer localPlayer)
        {
            if (!IsActive)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > Settings.RenderDistance)
                return;

            var toPosition = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var fromPosition = FromPosition.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);

            var size = 5 * MainWindow.UIScale;
            var lineWidth = 2f * MainWindow.UIScale;

            SKPaints.PaintExplosives.StrokeWidth = lineWidth;

            // Get alpha-adjusted paints for height-aware transparency
            var paints = HeightAwareAlphaManager.GetEntityPaintsFromSingle(
                SKPaints.PaintExplosives, SKPaints.TextExplosives, SKPaints.ShapeOutline, SKPaints.TextOutline,
                Position, localPlayer.Position, Program.Config.HeightAwareAlpha);

            canvas.Save();

            // Create a matrix to counter-rotate around the loot item's icon position.
            // 'point' before it's offset is the center of the icon.
            var iconCenterPoint = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var counterRotation = SKMatrix.CreateRotationDegrees(-MainWindow.RotationDegrees, iconCenterPoint.X, iconCenterPoint.Y);
            canvas.Concat(ref counterRotation);


            if (Settings.ShowTripwireLine)
            {
                paints.ShapeOutline.StrokeWidth = lineWidth + 1f * MainWindow.UIScale;
                canvas.DrawLine(fromPosition, toPosition, paints.ShapeOutline);
                canvas.DrawLine(fromPosition, toPosition, paints.ShapeFill);
            }

            paints.ShapeOutline.StrokeWidth = 2f * MainWindow.UIScale;

            canvas.DrawCircle(toPosition, size, paints.ShapeOutline);
            canvas.DrawCircle(toPosition, size, paints.ShapeFill);

            if (Settings.ShowTripwireLine)
            {
                canvas.DrawCircle(fromPosition, size, paints.ShapeOutline);
                canvas.DrawCircle(fromPosition, size, paints.ShapeFill);
            }

            if (Settings.ShowName && !string.IsNullOrEmpty(Name))
            {
                var nameWidth = paints.TextFill.MeasureText(Name);
                var namePoint = new SKPoint(
                    toPosition.X - (nameWidth / 2),
                    toPosition.Y - 10f * MainWindow.UIScale
                );

                canvas.DrawText(Name, namePoint, paints.TextOutline);
                canvas.DrawText(Name, namePoint, paints.TextFill);
            }

            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = paints.TextFill.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    toPosition.X - (distWidth / 2),
                    toPosition.Y + 18f * MainWindow.UIScale
                );

                canvas.DrawText(distText, distPoint, paints.TextOutline);
                canvas.DrawText(distText, distPoint, paints.TextFill);
            }
            canvas.Restore();
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (!IsActive)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);

            if (dist > ESPSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref Position, out var scrPos))
                return;

            var scale = ESP.Config.FontScale;

            if (ESPSettings.ShowTripwireLine)
            {
                var tripLine = GetTripwireLine();

                if (tripLine != null)
                {
                    SKPaints.PaintExplosiveESP.StrokeWidth = 2f * ESP.Config.LineScale;
                    canvas.DrawLine(tripLine[0], tripLine[1], SKPaints.PaintExplosiveESP);
                }
            }

            switch (ESPSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintExplosiveESP);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintExplosiveESP.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y - crossSize,
                            scrPos.X + crossSize, scrPos.Y + crossSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y + crossSize,
                            scrPos.X + crossSize, scrPos.Y - crossSize,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Plus:
                    var plusSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintExplosiveESP.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X, scrPos.Y - plusSize,
                            scrPos.X, scrPos.Y + plusSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - plusSize, scrPos.Y,
                            scrPos.X + plusSize, scrPos.Y,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Square:
                    var boxHalf = 3f * scale;
                    var boxPt = new SKRect(
                        scrPos.X - boxHalf, scrPos.Y - boxHalf,
                        scrPos.X + boxHalf, scrPos.Y + boxHalf);
                    canvas.DrawRect(boxPt, SKPaints.PaintExplosiveESP);
                    break;

                case EntityRenderMode.Diamond:
                default:
                    var diamondSize = 3.5f * scale;
                    using (var diamondPath = new SKPath())
                    {
                        diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                        diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                        diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                        diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                        diamondPath.Close();
                        canvas.DrawPath(diamondPath, SKPaints.PaintExplosiveESP);
                    }
                    break;
            }

            if (ESPSettings.ShowName || ESPSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    ESPSettings.ShowDistance,
                    SKPaints.TextExplosiveESP,
                    ESPSettings.ShowName ? Name : null
                );
            }
        }

        #endregion
    }
}
