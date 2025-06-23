using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;

namespace eft_dma_radar.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// Represents a 'Hot' grenade in Local Game World.
    /// </summary>
    public sealed class Grenade : IExplosiveItem, IWorldEntity, IMapEntity, IESPEntity
    {
        public static EntityTypeSettings Settings => Program.Config.EntityTypeSettings.GetSettings("Grenade");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("Grenade");

        public static implicit operator ulong(Grenade x) => x.Addr;
        private static readonly uint[] _toPosChain = ObjectClass.To_GameObject.Concat(new uint[] { GameObject.ComponentsOffset, 0x8, 0x38 }).ToArray();
        private static readonly uint[] _toWeaponTemplate = new uint[] { Offsets.Grenade.WeaponSource, Offsets.LootItem.Template };
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly ConcurrentDictionary<ulong, IExplosiveItem> _parent;

        private const int GRENADE_RADIUS_POINTS = 16;

        private static readonly Dictionary<string, float> EffectiveDistances = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            { "F-1", 7f },
            { "M67", 8f },
            { "RGD-5", 7f },
            { "RGN", 5f },
            { "RGO", 7f },
            { "V40", 5f },
            { "VOG-17", 6f },
            { "VOG-25", 7f }
        };

        /// <summary>
        /// Base Address of Grenade Object.
        /// </summary>
        public ulong Addr { get; }

        /// <summary>
        /// Position Pointer for the Vector3 location of this object.
        /// </summary>
        private ulong PosAddr { get; }

        /// <summary>
        /// True if grenade is currently active.
        /// </summary>
        public bool IsActive => _sw.Elapsed.TotalSeconds < 12f;

        public string Name { get; }

        /// <summary>
        /// Effective distance of the grenade in meters. 0 if unknown.
        /// </summary>
        public float EffectiveDistance { get; private set; } = 0f;

        /// <summary>
        /// True if the grenade has detonated.
        /// Doesn't work on smoke grenades.
        /// </summary>
        private bool IsDetonated
        {
            get
            {
                return Memory.ReadValue<bool>(this + Offsets.Grenade.IsDestroyed, false);
            }
        }

        public Grenade(ulong baseAddr, ConcurrentDictionary<ulong, IExplosiveItem> parent)
        {
            Addr = baseAddr;
            _parent = parent;

            if (IsDetonated)
                throw new Exception("Grenade is already detonated.");

            PosAddr = Memory.ReadPtrChain(baseAddr, _toPosChain, false);

            var toWeaponTemplate = Memory.ReadPtrChain(baseAddr, _toWeaponTemplate, false);
            var id = Memory.ReadValue<Types.MongoID>(toWeaponTemplate + Offsets.ItemTemplate._id);
            var name = Memory.ReadUnityString(id.StringID, useCache: false);

            if (EftDataManager.AllItems.TryGetValue(name, out var grenade))
                Name = grenade.ShortName;

            if (!string.IsNullOrEmpty(Name) && EffectiveDistances.TryGetValue(Name, out float distance))
                EffectiveDistance = distance;

            if (grenade is not null && grenade.BsgId == "67b49e7335dec48e3e05e057")
                Name = $"{Name} (SHORT)";

            Refresh();
        }

        /// <summary>
        /// Get the updated Position of this Grenade.
        /// </summary>
        public void Refresh()
        {
            if (!this.IsActive)
            {
                return;
            }
            else if (IsDetonated)
            {
                _parent.TryRemove(this, out _);
                return;
            }
            Position = Memory.ReadValue<Vector3>(PosAddr + 0x90, false);
        }

        #region Interfaces

        private Vector3 _position;
        public ref Vector3 Position => ref _position;

        public void Draw(SKCanvas canvas, LoneMapParams mapParams, ILocalPlayer localPlayer)
        {
            if (!IsActive)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > Settings.RenderDistance)
                return;

            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var isPlayerInDanger = EffectiveDistance > 0 && dist <= EffectiveDistance;

            SKPaints.ShapeOutline.StrokeWidth = SKPaints.PaintExplosives.StrokeWidth + 2f * MainWindow.UIScale;
            var size = 5 * MainWindow.UIScale;

            var paintToUse = isPlayerInDanger ? SKPaints.PaintExplosivesDanger : SKPaints.PaintExplosives;
            var textPaintToUse = isPlayerInDanger ? SKPaints.TextExplosivesDanger : SKPaints.TextExplosives;

            // Get alpha-adjusted paints for height-aware transparency
            var paints = HeightAwareAlphaManager.GetEntityPaintsFromSingle(
                paintToUse, textPaintToUse, SKPaints.ShapeOutline, SKPaints.TextOutline,
                Position, localPlayer.Position, Program.Config.HeightAwareAlpha);

            canvas.DrawCircle(point, size, paints.ShapeOutline);
            canvas.DrawCircle(point, size, paints.ShapeFill);

            if (Settings.ShowRadius && EffectiveDistance > 0)
            {
                var radiusUnscaled = EffectiveDistance * mapParams.Map.Scale * mapParams.Map.SvgScale;
                var radius = radiusUnscaled * mapParams.XScale;

                using (var radiusPaint = new SKPaint
                {
                    Color = isPlayerInDanger ? paintToUse.Color.WithAlpha(80) : paintToUse.Color.WithAlpha(80),
                    StrokeWidth = 1.5f * MainWindow.UIScale,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke
                })
                {
                    canvas.DrawCircle(point, radius, radiusPaint);
                }

                using (var fillPaint = new SKPaint
                {
                    Color = isPlayerInDanger ? paintToUse.Color.WithAlpha(30) : paintToUse.Color.WithAlpha(30),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                })
                {
                    canvas.DrawCircle(point, radius, fillPaint);
                }
            }

            var distanceYOffset = 20f * MainWindow.UIScale;
            var nameXOffset = 10f * MainWindow.UIScale;
            var nameYOffset = 4f * MainWindow.UIScale;
            canvas.Save();

            // Create a matrix to counter-rotate around the loot item's icon position.
            // 'point' before it's offset is the center of the icon.
            var iconCenterPoint = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var counterRotation = SKMatrix.CreateRotationDegrees(-MainWindow.RotationDegrees, iconCenterPoint.X, iconCenterPoint.Y);
            canvas.Concat(ref counterRotation);

            if (Settings.ShowName && !string.IsNullOrEmpty(Name))
            {
                var namePoint = new SKPoint(point.X + nameXOffset, point.Y + nameYOffset);
                canvas.DrawText(Name, namePoint, paints.TextOutline);
                canvas.DrawText(Name, namePoint, paints.TextFill);
            }

            if (Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = paints.TextFill.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2),
                    point.Y + distanceYOffset
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
            var isPlayerInDanger = EffectiveDistance > 0 && dist <= EffectiveDistance;

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

            if (ESPSettings.ShowRadius && EffectiveDistance > 0)
            {
                var circlePoints = new List<SKPoint>();

                for (int i = 0; i < GRENADE_RADIUS_POINTS; i++)
                {
                    var angle = (float)(2 * Math.PI * i / GRENADE_RADIUS_POINTS);
                    var x = Position.X + EffectiveDistance * (float)Math.Cos(angle);
                    var z = Position.Z + EffectiveDistance * (float)Math.Sin(angle);
                    var circleWorldPos = new Vector3(x, Position.Y, z);

                    if (CameraManagerBase.WorldToScreen(ref circleWorldPos, out var circleScreenPos))
                        circlePoints.Add(circleScreenPos);
                }

                if (circlePoints.Count > 2)
                {
                    using (var path = new SKPath())
                    {
                        if (circlePoints.Count > 0)
                        {
                            path.MoveTo(circlePoints[0]);
                            for (int i = 1; i < circlePoints.Count; i++)
                            {
                                path.LineTo(circlePoints[i]);
                            }
                            path.Close();
                            canvas.DrawPath(path, SKPaints.PaintExplosiveRadiusESP);
                        }
                    }

                }
            }

            if (isPlayerInDanger || ESPSettings.ShowName || ESPSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                string nameText = null;

                if (isPlayerInDanger)
                    nameText = "*DANGER*";

                if (ESPSettings.ShowName && !string.IsNullOrEmpty(Name))
                    if (isPlayerInDanger)
                        nameText += " " + Name;
                    else
                        nameText = Name;

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    ESPSettings.ShowDistance,
                    SKPaints.TextExplosiveESP,
                    nameText
                );
            }
        }

        #endregion
    }
}
