using eft_dma_radar.Tarkov.EFTPlayer;
using System.Collections.Frozen;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Misc;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Pages;
using eft_dma_radar;

namespace eft_dma_radar.Tarkov.GameWorld
{
    public sealed class QuestManager
    {
        private static Config Config => Program.Config;

        private static readonly FrozenDictionary<string, string> _mapToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "factory4_day", "55f2d3fd4bdc2d5f408b4567" },
            { "factory4_night", "59fc81d786f774390775787e" },
            { "bigmap", "56f40101d2720b2a4d8b45d6" },
            { "woods", "5704e3c2d2720bac5b8b4567" },
            { "lighthouse", "5704e4dad2720bb55b8b4567" },
            { "shoreline", "5704e554d2720bac5b8b456e" },
            { "labyrinth", "6733700029c367a3d40b02af" },
            { "rezervbase", "5704e5fad2720bc05b8b4567" },
            { "interchange", "5714dbc024597771384a510d" },
            { "tarkovstreets", "5714dc692459777137212e12" },
            { "laboratory", "5b0fc42d86f7744a585f9105" },
            { "Sandbox", "653e6760052c01c1c805532f" },
            { "Sandbox_high", "65b8d6f5cdde2479cb2a3125" }
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static FrozenDictionary<string, FrozenDictionary<string, Vector3>> GetQuestZones()
        {
            var tasks = Config.QuestHelper.KappaFilter
                ? EftDataManager.TaskData.Values.Where(task => task.KappaRequired)
                : EftDataManager.TaskData.Values;

            return tasks
                .Where(task => task.Objectives is not null) // Ensure the Objectives are not null
                .SelectMany(task => task.Objectives)   // Flatten the Objectives from each TaskElement
                .Where(objective => objective.Zones is not null) // Ensure the Zones are not null
                .SelectMany(objective => objective.Zones)    // Flatten the Zones from each Objective
                .Where(zone => zone.Position is not null && zone.Map?.Id is not null) // Ensure Position and Map are not null
                .GroupBy(zone => zone.Map.Id, zone => new
                {
                    id = zone.Id,
                    pos = new Vector3(zone.Position.X, zone.Position.Y, zone.Position.Z)
                }, StringComparer.OrdinalIgnoreCase)
                .DistinctBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key, // Map Id
                    group => group
                    .DistinctBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        zone => zone.id,
                        zone => zone.pos,
                        StringComparer.OrdinalIgnoreCase
                    ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        private static FrozenDictionary<string, FrozenDictionary<string, List<Vector3>>> GetQuestOutlines()
        {
            var tasks = Config.QuestHelper.KappaFilter
                ? EftDataManager.TaskData.Values.Where(task => task.KappaRequired)
                : EftDataManager.TaskData.Values;

            return tasks
                .Where(task => task.Objectives is not null) // Ensure the Objectives are not null
                .SelectMany(task => task.Objectives) // Flatten the Objectives from each TaskElement
                .Where(objective => objective.Zones is not null) // Ensure the Zones are not null
                .SelectMany(objective => objective.Zones) // Flatten the Zones from each Objective
                .Where(zone => zone.Outline is not null && zone.Map?.Id is not null) // Ensure Outlines and Map are not null
                .GroupBy(zone => zone.Map.Id, zone => new
                {
                    id = zone.Id,
                    outline = zone.Outline.Select(outline => new Vector3(outline.X, outline.Y, outline.Z)).ToList()
                }, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key, // Map Id
                    group => group
                        .DistinctBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            zone => zone.id,
                            zone => zone.outline,
                            StringComparer.OrdinalIgnoreCase
                        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        // Cache variables to avoid recalculating on every frame
        private static FrozenDictionary<string, FrozenDictionary<string, Vector3>> _questZones;
        private static FrozenDictionary<string, FrozenDictionary<string, List<Vector3>>> _questOutlines;
        private static bool _lastKappaFilterState;

        // Static initializer to set up the initial caches
        static QuestManager()
        {
            UpdateCaches();
        }

        public static void UpdateCaches()
        {
            if (_lastKappaFilterState != Config.QuestHelper.KappaFilter || _questZones == null || _questOutlines == null)
            {
                _questZones = GetQuestZones();
                _questOutlines = GetQuestOutlines();
                _lastKappaFilterState = Config.QuestHelper.KappaFilter;
            }
        }

        public static EntityTypeSettings Settings => Config.EntityTypeSettings.GetSettings("QuestZone");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("QuestZone");

        private readonly Stopwatch _rateLimit = new();
        private readonly ulong _profile;

        public QuestManager(ulong profile)
        {
            _profile = profile;
            Refresh();
        }

        /// <summary>
        /// Currently logged quests.
        /// </summary>
        public IReadOnlySet<string> CurrentQuests { get; private set; } = new HashSet<string>();
        /// <summary>
        /// Contains a List of BSG ID's that we need to pickup.
        /// </summary>
        public IReadOnlySet<string> ItemConditions { get; private set; } = new HashSet<string>();

        /// <summary>
        /// Contains a List of locations that we need to visit.
        /// </summary>
        public IReadOnlyList<QuestLocation> LocationConditions { get; private set; } = new List<QuestLocation>();

        /// <summary>
        /// Map Identifier of Current Map.
        /// </summary>
        private static string MapID
        {
            get
            {
                var id = Memory.MapID;
                id ??= "MAPDEFAULT";
                return id;
            }
        }

        public void Refresh()
        {
            UpdateCaches();

            if (_rateLimit.IsRunning && _rateLimit.Elapsed.TotalSeconds < 2d)
                return;

            var currentQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var masterItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var masterLocations = new List<QuestLocation>();
            var questsData = Memory.ReadPtr(_profile + Offsets.Profile.QuestsData);
            using var questsDataList = MemList<ulong>.Get(questsData);
            foreach (var qDataEntry in questsDataList)
            {
                try
                {
                    var qStatus = Memory.ReadValue<int>(qDataEntry + Offsets.QuestData.Status);

                    if (qStatus != 2) // 2 == Started
                        continue;

                    var completedPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.CompletedConditions);
                    using var completedHS = MemHashSet<Types.MongoID>.Get(completedPtr);
                    var completedConditions = new HashSet<string>();

                    foreach (var c in completedHS)
                    {
                        var completedCond = Memory.ReadUnityString(c.Value.StringID);
                        completedConditions.Add(completedCond);
                    }

                    var qIDPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.Id);
                    var qID = Memory.ReadUnityString(qIDPtr);
                    currentQuests.Add(qID);

                    if (Config.QuestHelper.BlacklistedQuests.Contains(qID, StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (Config.QuestHelper.KappaFilter &&
                        EftDataManager.TaskData.TryGetValue(qID, out var taskElement) &&
                        !taskElement.KappaRequired)
                    {
                        continue;
                    }

                    var qTemplate = Memory.ReadPtr(qDataEntry + Offsets.QuestData.Template);
                    var qConditions = Memory.ReadPtr(qTemplate + Offsets.QuestTemplate.Conditions);

                    using var qCondDict = MemDictionary<int, ulong>.Get(qConditions);

                    foreach (var qDicCondEntry in qCondDict)
                    {
                        var condListPtr = Memory.ReadPtr(qDicCondEntry.Value + Offsets.QuestConditionsContainer.ConditionsList);
                        using var condList = MemList<ulong>.Get(condListPtr);
                        foreach (var condition in condList)
                            GetQuestConditions(qID, condition, completedConditions, masterItems, masterLocations);
                    }
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[QuestManager] ERROR parsing Quest at 0x{qDataEntry.ToString("X")}: {ex}");
                }
            }

            CurrentQuests = currentQuests;
            ItemConditions = masterItems;
            LocationConditions = masterLocations;

            if (MainWindow.Window?.GeneralSettingsControl?.QuestItems?.Count != CurrentQuests.Count)
                MainWindow.Window?.GeneralSettingsControl?.RefreshQuestHelper();

            _rateLimit.Restart();
        }

        private static void GetQuestConditions(string questID, ulong condition, HashSet<string> completedConditions, HashSet<string> items, List<QuestLocation> locations)
        {
            try
            {
                if (Config.QuestHelper.KappaFilter &&
                    (!EftDataManager.TaskData.TryGetValue(questID, out var taskElement) || !taskElement.KappaRequired))
                {
                    return;
                }

                var condIDPtr = Memory.ReadValue<Types.MongoID>(condition + Offsets.QuestCondition.id);
                var condID = Memory.ReadUnityString(condIDPtr.StringID);

                if (completedConditions.Contains(condID))
                    return;

                var condName = ObjectClass.ReadName(condition);

                if (condName == "ConditionFindItem" || condName == "ConditionHandoverItem")
                {
                    var targetArray = Memory.ReadPtr(condition + Offsets.QuestConditionFindItem.target); // this is a typical unity array[] at 0x48
                    using var targets = MemArray<ulong>.Get(targetArray);

                    foreach (var targetPtr in targets)
                    {
                        var target = Memory.ReadUnityString(targetPtr);
                        items.Add(target);
                    }
                }
                else if (condName == "ConditionPlaceBeacon" || condName == "ConditionLeaveItemAtLocation")
                {
                    var zoneIDPtr = Memory.ReadPtr(condition + Offsets.QuestConditionPlaceBeacon.zoneId);
                    var target = Memory.ReadUnityString(zoneIDPtr);
                    if (_mapToId.TryGetValue(MapID, out var id) &&
                        _questZones.TryGetValue(id, out var zones) &&
                        zones.TryGetValue(target, out var loc))
                    {
                        locations.Add(new QuestLocation(questID, target, loc));
                    }
                }
                else if (condName == "ConditionVisitPlace")
                {
                    var targetPtr = Memory.ReadPtr(condition + Offsets.QuestConditionVisitPlace.target);
                    var target = Memory.ReadUnityString(targetPtr);
                    if (_mapToId.TryGetValue(MapID, out var id) &&
                        _questZones.TryGetValue(id, out var zones) &&
                        zones.TryGetValue(target, out var loc))
                    {
                        locations.Add(new QuestLocation(questID, target, loc));
                    }
                }
                else if (condName == "ConditionCounterCreator")
                {
                    var conditionsPtr = Memory.ReadPtr(condition + Offsets.QuestConditionCounterCreator.Conditions);
                    var conditionsListPtr = Memory.ReadPtr(conditionsPtr + Offsets.QuestConditionsContainer.ConditionsList);
                    using var counterList = MemList<ulong>.Get(conditionsListPtr);
                    foreach (var childCond in counterList)
                        GetQuestConditions(questID, childCond, completedConditions, items, locations);
                }
                else if (condName == "ConditionLaunchFlare")
                {
                    var zonePtr = Memory.ReadPtr(condition + Offsets.QuestConditionLaunchFlare.zoneId);
                    var target = Memory.ReadUnityString(zonePtr);
                    if (_mapToId.TryGetValue(MapID, out var id) &&
                        _questZones.TryGetValue(id, out var zones) &&
                        zones.TryGetValue(target, out var loc))
                    {
                        locations.Add(new QuestLocation(questID, target, loc));
                    }
                }
                else if (condName == "ConditionZone")
                {
                    var zonePtr = Memory.ReadPtr(condition + Offsets.QuestConditionZone.zoneId);
                    var targetPtr = Memory.ReadPtr(condition + Offsets.QuestConditionZone.target);
                    var zone = Memory.ReadUnityString(zonePtr);
                    using var targets = MemArray<ulong>.Get(targetPtr);
                    foreach (var targetPtr2 in targets)
                        items.Add(Memory.ReadUnityString(targetPtr2));
                    if (_mapToId.TryGetValue(MapID, out var id) &&
                        _questZones.TryGetValue(id, out var zones) &&
                        zones.TryGetValue(zone, out var loc))
                    {
                        locations.Add(new QuestLocation(questID, zone, loc));
                    }
                }
                else if (condName == "ConditionInZone")
                {
                    var zonePtr = Memory.ReadPtr(condition + 0x70);
                    using var zones = MemArray<ulong>.Get(zonePtr);
                    foreach (var zone in zones)
                    {
                        var id = Memory.ReadUnityString(zone);
                        if (_mapToId.TryGetValue(MapID, out var mapId) &&
                            _questOutlines.TryGetValue(mapId, out var outzone) &&
                            outzone.TryGetValue(id, out var outlines) &&
                            _questZones.TryGetValue(mapId, out var outpos) &&
                            outpos.TryGetValue(id, out var locc))
                        {
                            locations.Add(new QuestLocation(questID, id, locc, outlines));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[QuestManager] ERROR parsing Condition(s): {ex}");
            }
        }
    }

    /// <summary>
    /// Wraps a Mouseoverable Quest Location marker onto the Map GUI.
    /// </summary>
    public sealed class QuestLocation : IWorldEntity, IMapEntity, IMouseoverEntity, IESPEntity
    {
        private Vector3 _position;
        private List<Vector3> _outline;

        /// <summary>
        /// Name of this quest.
        /// </summary>
        public string Name { get; }
        public Vector2 MouseoverPosition { get; set; }
        public ref Vector3 Position => ref _position;
        public ref List<Vector3> Outline => ref _outline;

        public QuestLocation(string questID, string target, Vector3 position, List<Vector3> outline = null)
        {
            if (EftDataManager.TaskData.TryGetValue(questID, out var q))
                Name = q.Name;
            else
                Name = target;
            Position = position;
            Outline = outline;
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (!Memory.LocalPlayer.IsPmc)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > QuestManager.ESPSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var scale = ESP.Config.FontScale;

            switch (QuestManager.ESPSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintQuestHelperESP);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;

                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintQuestHelperESP.Color,
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
                        Color = SKPaints.PaintQuestHelperESP.Color,
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
                default:
                    var boxHalf = 3f * scale;
                    var boxPt = new SKRect(
                        scrPos.X - boxHalf, scrPos.Y - boxHalf,
                        scrPos.X + boxHalf, scrPos.Y + boxHalf);
                    canvas.DrawRect(boxPt, SKPaints.PaintQuestHelperESP);
                    break;

                case EntityRenderMode.Diamond:
                    var diamondSize = 3.5f * scale;
                    using (var diamondPath = new SKPath())
                    {
                        diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                        diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                        diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                        diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                        diamondPath.Close();
                        canvas.DrawPath(diamondPath, SKPaints.PaintQuestHelperESP);
                    }
                    break;
            }

            if (QuestManager.ESPSettings.ShowName || QuestManager.ESPSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    QuestManager.ESPSettings.ShowDistance,
                    SKPaints.TextQuestHelperESP,
                    QuestManager.ESPSettings.ShowName ? Name : null
                );
            }
        }

        public void Draw(SKCanvas canvas, LoneMapParams mapParams, ILocalPlayer localPlayer)
        {
            if (!Memory.LocalPlayer.IsPmc)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > QuestManager.Settings.RenderDistance)
                return;

            if (Outline is not null && Program.Config.QuestHelper.KillZones)
            {
                var mapPoints = Outline.Select(p => p.ToMapPos(mapParams.Map).ToZoomedPos(mapParams)).ToList();

                if (mapPoints.Count > 0)
                {
                    using var path = new SKPath();

                    path.AddPoly(
                        mapPoints.Select(p => new SKPoint(p.X, p.Y)).ToArray(),
                        close: true
                    );

                    canvas.DrawPath(path, SKPaints.QuestHelperOutline);
                }
            }

            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
            var heightDiff = Position.Y - localPlayer.Position.Y;
            SKPaints.ShapeOutline.StrokeWidth = 2f;

            float distanceYOffset;
            float nameXOffset = 7f * MainWindow.UIScale;
            float nameYOffset;
            canvas.Save();

            // Create a matrix to counter-rotate around the loot item's icon position.
            // 'point' before it's offset is the center of the icon.
            var iconCenterPoint = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var counterRotation = SKMatrix.CreateRotationDegrees(-MainWindow.RotationDegrees, iconCenterPoint.X, iconCenterPoint.Y);
            canvas.Concat(ref counterRotation);
            if (heightDiff > 1.45)
            {
                using var path = point.GetUpArrow();
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, SKPaints.QuestHelperPaint);
                distanceYOffset = 20f * MainWindow.UIScale;
                nameYOffset = 6f * MainWindow.UIScale;
            }
            else if (heightDiff < -1.45)
            {
                using var path = point.GetDownArrow();
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, SKPaints.QuestHelperPaint);
                distanceYOffset = 14f * MainWindow.UIScale;
                nameYOffset = 1f * MainWindow.UIScale;
            }
            else
            {
                var squareSize = 8 * MainWindow.UIScale;
                canvas.DrawRect(point.X, point.Y, squareSize, squareSize, SKPaints.ShapeOutline);
                canvas.DrawRect(point.X, point.Y, squareSize, squareSize, SKPaints.QuestHelperPaint);
                distanceYOffset = 22f * MainWindow.UIScale;
                nameYOffset = 8f * MainWindow.UIScale;
                nameXOffset = 12f * MainWindow.UIScale;
            }

            if (QuestManager.Settings.ShowName)
            {
                var namePoint = point;
                namePoint.Offset(nameXOffset, nameYOffset);
                canvas.DrawText(Name, namePoint, SKPaints.TextOutline);
                canvas.DrawText(Name, namePoint, SKPaints.QuestHelperText);
            }

            if (QuestManager.Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = SKPaints.QuestHelperText.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2),
                    point.Y + distanceYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, SKPaints.QuestHelperText);
            }
            canvas.Restore();
        }

        public void DrawMouseover(SKCanvas canvas, LoneMapParams mapParams, LocalPlayer localPlayer)
        {
            string[] lines = new string[] { Name };
            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }
    }
}