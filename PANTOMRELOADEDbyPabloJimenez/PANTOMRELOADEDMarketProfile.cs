using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;

namespace PANTOMRELOADEDbyPabloJimenez
{
    public class TPOData
    {
        public double PriceLevel { get; set; }
        public int Score { get; set; }
        public string Letters { get; set; }
        public bool IsSinglePrint { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
    public class SinglePrintData
    {
        public double PriceLevel { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }

    public class SessionTPO
    {
        public DateTime SessionStart { get; set; }
        public DateTime SessionEnd { get; set; }
        public List<TPOData> TPOs { get; set; } = new List<TPOData>();
        public int CandlesCount { get; set; }
    }

    public class PANTOMRELOADEDMarketProfile : Indicator
    {
        public static PANTOMRELOADEDMarketProfile ActiveInstance { get; private set; }

        private const string CHART_SESSION_CONTAINER = "Chart session";
        private const string SESSION_TEMPLATE_NAME = "SessionsTemplate";
        private string specifiedSessionContainerId;
        private ISessionsContainer selectedSessionContainer;
        private Period currentPeriod;

        private readonly object lockObj = new object(); 
        #region Input Parameters
        [InputParameter("Ticks Number per TPO", 10, 1, 9999, 1, 0)]
        public int TicksNumberPerTPO = 10;

        [InputParameter("Round TPO Levels", 20)]
        public bool RoundValues = true;

        [InputParameter("Extend Yesterday's Levels", 50)]
        public bool ExtendLevels = false;

        [InputParameter("Highlight Single Prints", 60)]
        public bool ShowSinglePrints = true;

        [InputParameter("Single Prints Color", 70)]
        public Color SinglePrintsColor = Color.FromArgb(236, 64, 64, 122); 
        [InputParameter("TPO Block Color", 75)]
        public Color TPOBlockColor = Color.FromArgb(50, 100, 150, 200); 
        [InputParameter("Extend Single Prints", 80)]
        public bool ExtendSinglePrints = false;

        [InputParameter("Single Prints Transparency", 90, 0, 100, 1, 0)]
        public int SinglePrintsTransparency = 80;

        [InputParameter("TPO Spacing", 100, 0, 100, 1, 0)]
        public int TPOSpacing = 0;
        #endregion

        public override string ShortName => "TPO Profile";

        private double tickSize;
        private readonly string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789@$€£{}[]()*+-/=%&?!";
        private DateTime lastProcessedTime;
        private readonly TimeSpan processThrottle = TimeSpan.FromSeconds(0.5); // Throttle for updates
        public HistoricalData _additionalData;
        public IReadOnlyDictionary<DateTime, SessionTPO> SessionTPOs => this.sessionCache;
        private readonly Dictionary<DateTime, SessionTPO> sessionCache = new Dictionary<DateTime, SessionTPO>();

        public PANTOMRELOADEDMarketProfile() : base()
        {
            Name = "PANTOMRELOADEDMarketProfile";
            Description = "Draws TPO profiles for active session periods with blocks and letters.";
            SeparateWindow = false;
            OnBackGround = true;
            specifiedSessionContainerId = "zw34eLhbUacKBSUMsV4kQ";
            UpdateType = IndicatorUpdateType.OnTick;
            ActiveInstance = this;
        }

        public IReadOnlyDictionary<DateTime, List<SinglePrintData>> SinglePrintsBySession
        {
            get
            {
                return sessionCache.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.TPOs
                        .Where(tpo => tpo.IsSinglePrint)
                        .Select(tpo => new SinglePrintData
                        {
                            PriceLevel = tpo.PriceLevel,
                            StartTime = tpo.StartTime,
                            EndTime = tpo.EndTime
                        })
                        .ToList()
                ).AsReadOnly();
            }
        }

        protected override void OnInit()
        {
            HistoryAggregation currentAggregation = this.HistoricalData.Aggregation;

            bool is30MinAggregation = currentAggregation is HistoryAggregationTime timeAggregation &&
                                      timeAggregation.Period == Period.MIN30;

            HistoryAggregation targetAggregation;
            if (is30MinAggregation)
            {
                targetAggregation = (HistoryAggregation)currentAggregation.Clone();
            }
            else
            {
                targetAggregation = new HistoryAggregationTime(Period.MIN30, this.Symbol.HistoryType);
            }

            DateTime startTime = this.HistoricalData.FromTime;

            _additionalData = this.Symbol.GetHistory(new HistoryRequestParameters()
            {
                Symbol = this.Symbol,
                Aggregation = targetAggregation,
                FromTime = startTime,
                ToTime = this.HistoricalData.ToTime,
                SessionsContainer = selectedSessionContainer
            });

            Core.Loggers.Log($"Additional data loaded: {_additionalData.Count} bars from {startTime} with {targetAggregation.GetType().Name}");

            tickSize = Symbol != null ? TicksNumberPerTPO * Symbol.TickSize : 0;
            lock (lockObj)
            {
                sessionCache.Clear();
            }

            lastProcessedTime = DateTime.MinValue;
            UpdateSessionContainer();
            UpdateSessionContainer();
            if (_additionalData != null && selectedSessionContainer != null)
                ProcessHistoricalData();

        }

        private void UpdateSessionContainer()
        {
            if (CurrentChart == null || Core.Instance == null)
            {
                selectedSessionContainer = null;
                Core.Instance.Loggers.Log("UpdateSessionContainer: CurrentChart or Core.Instance is null", LoggingLevel.Error);
                return;
            }

            selectedSessionContainer = specifiedSessionContainerId == CHART_SESSION_CONTAINER
                ? CurrentChart.CurrentSessionContainer
                : Core.Instance.CustomSessions.FirstOrDefault(s => s.Id == specifiedSessionContainerId);

            if (selectedSessionContainer == null)
                Core.Instance.Loggers.Log($"UpdateSessionContainer: No session container found for ID {specifiedSessionContainerId}", LoggingLevel.Error);
        }

        private void ProcessHistoricalData()
        {
            lock (lockObj)
            {
                if (DateTime.UtcNow - lastProcessedTime < processThrottle)
                    return;

                lastProcessedTime = DateTime.UtcNow;

                if (_additionalData == null || selectedSessionContainer == null || CurrentChart == null)
                {
                    Core.Instance.Loggers.Log("ProcessHistoricalData: Missing data, session container, or chart", LoggingLevel.Error);
                    return;
                }

                var mainWindow = CurrentChart.MainWindow;
                if (mainWindow == null)
                {
                    Core.Instance.Loggers.Log("Process_additionalData: MainWindow is null", LoggingLevel.Error);
                    return;
                }

                var leftBorderTime = _additionalData[0, SeekOriginHistory.Begin].TimeLeft;
                var rightBorderTime = _additionalData[0, SeekOriginHistory.End].TimeLeft;

                if (leftBorderTime == DateTime.MinValue || rightBorderTime == DateTime.MinValue || leftBorderTime > rightBorderTime)
                {
                    Core.Instance.Loggers.Log($"ProcessHistoricalData: Invalid chart time range (Left: {leftBorderTime}, Right: {rightBorderTime})", LoggingLevel.Error);
                    return;
                }

                var startDate = leftBorderTime.Date;
                var endDate = rightBorderTime.Date.AddDays(1);

                if (_additionalData.Count > 0)
                {
                    var earliestData = _additionalData[_additionalData.Count - 1].TimeLeft.Date;
                    var latestData = _additionalData[0].TimeLeft.Date;
                    startDate = startDate < earliestData ? earliestData : startDate;
                    endDate = endDate > latestData ? latestData.AddDays(1) : endDate;
                }

                Core.Instance.Loggers.Log($"ProcessHistoricalData: Processing sessions from {startDate} to {endDate}, HistoricalData count: {_additionalData?.Count ?? 0}", LoggingLevel.System);

                var keysToRemove = sessionCache.Keys.Where(k => k.Date < startDate || k.Date > endDate).ToList();
                foreach (var key in keysToRemove)
                    sessionCache.Remove(key);

                foreach (var session in selectedSessionContainer.ActiveSessions)
                {
                    var currentDate = startDate;
                    while (currentDate <= endDate)
                    {
                        try
                        {
                            var sessionStart = new DateTime(currentDate.Year, currentDate.Month, currentDate.Day,
                                session.OpenTime.Hours, session.OpenTime.Minutes, session.OpenTime.Seconds);
                            var sessionEnd = new DateTime(currentDate.Year, currentDate.Month, currentDate.Day,
                                session.CloseTime.Hours, session.CloseTime.Minutes, session.CloseTime.Seconds);

                            if (session.OpenTime > session.CloseTime)
                                sessionEnd = sessionEnd.AddDays(1);

                            if (sessionStart > rightBorderTime || sessionEnd < leftBorderTime)
                            {
                                currentDate = currentDate.AddDays(1);
                                continue;
                            }

                            sessionEnd = sessionEnd > rightBorderTime ? rightBorderTime : sessionEnd;

                            if (!sessionCache.ContainsKey(sessionStart))
                            {
                                var sessionTPO = new SessionTPO { SessionStart = sessionStart, SessionEnd = sessionEnd };
                                CalculateSessionTPOs(sessionTPO);
                                if (sessionTPO.CandlesCount > 0)
                                {
                                    sessionCache[sessionStart] = sessionTPO;
                                    Core.Instance.Loggers.Log($"Added session: {sessionStart} to {sessionEnd}, Candles: {sessionTPO.CandlesCount}", LoggingLevel.System);
                                }
                            }

                            currentDate = currentDate.AddDays(1);
                        }
                        catch (ArgumentOutOfRangeException ex)
                        {
                            Core.Instance.Loggers.Log($"DateTime error on {currentDate}: {ex.Message}", LoggingLevel.Error);
                            break;
                        }
                    }
                }
            }
        }
        private DateTime LastTimeUpdateRealRime; 
        protected override void OnUpdate(UpdateArgs args)
        {
            lock (lockObj)
            {
                if (Symbol == null || selectedSessionContainer == null)
                {
                    Core.Instance.Loggers.Log("OnUpdate: Missing Symbol or session container", LoggingLevel.Error);
                    return;
                }

                tickSize = TicksNumberPerTPO * Symbol.TickSize;

                SessionTPO currentSession = null;
                if (args.Reason == UpdateReason.NewTick && Symbol.LastDateTime < LastTimeUpdateRealRime)
                {
                    this.LastTimeUpdateRealRime = Symbol.LastDateTime.AddMinutes(1);
                    return;
                }
                var currentTime = Time(0); 
                foreach (var session in selectedSessionContainer.ActiveSessions)
                {
                    var sessionStart = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day,
                        session.OpenTime.Hours, session.OpenTime.Minutes, session.OpenTime.Seconds);
                    var sessionEnd = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day,
                        session.CloseTime.Hours, session.CloseTime.Minutes, session.CloseTime.Seconds);

                    if (session.OpenTime > session.CloseTime)
                        sessionEnd = sessionEnd.AddDays(1);

                    if (currentTime >= sessionStart && currentTime <= sessionEnd)
                    {
                        if (sessionCache.ContainsKey(sessionStart))
                        {
                            currentSession = sessionCache[sessionStart];
                        }
                        else
                        {
                            currentSession = new SessionTPO { SessionStart = sessionStart, SessionEnd = sessionEnd };
                            sessionCache[sessionStart] = currentSession;
                        }
                        break;
                    }
                }

                if (currentSession != null)
                {
                    CalculateSessionTPOs(currentSession);
                }
            }
        }

        private void CalculateSessionTPOs(SessionTPO session)
        {
            session.TPOs.Clear();
            session.CandlesCount = 0;

            if (_additionalData == null || _additionalData.Count == 0)
            {
                Core.Instance.Loggers.Log($"CalculateSessionTPOs: No historical data available for session {session.SessionStart}", LoggingLevel.Error);
                return;
            }

            int minIndex = -1; 
            int maxIndex = -1;
            for (int i = 0; i < _additionalData.Count; i++)
            {
                var barTime = _additionalData[i].TimeLeft;
                if (barTime >= session.SessionStart && barTime <= session.SessionEnd)
                {
                    if (minIndex == -1 || i < minIndex)
                        minIndex = i;
                    if (maxIndex == -1 || i > maxIndex)
                        maxIndex = i;
                }
            }

            if (minIndex == -1 || maxIndex == -1 || minIndex > maxIndex)
            {
                Core.Instance.Loggers.Log($"CalculateSessionTPOs: No bars found for session {session.SessionStart} to {session.SessionEnd}", LoggingLevel.System);
                return;
            }

            minIndex = Math.Max(0, minIndex);
            maxIndex = Math.Min(_additionalData.Count - 1, maxIndex);

            double high = double.MinValue;
            double low = double.MaxValue;

            for (int i = maxIndex; i >= minIndex; i--) 
            {
                var bar = _additionalData[i];
                if (bar[PriceType.High] == 0 || bar[PriceType.Low] == 0)
                    continue;

                high = Math.Max(high, bar[PriceType.High]);
                low = Math.Min(low, bar[PriceType.Low]);
                session.CandlesCount++;
            }

            if (high == double.MinValue || low == double.MaxValue || session.CandlesCount == 0)
            {
                Core.Instance.Loggers.Log($"CalculateSessionTPOs: No valid price data for session {session.SessionStart}", LoggingLevel.System);
                return;
            }

            if (RoundValues && tickSize > 0)
            {
                high = Math.Ceiling(high / tickSize) * tickSize;
                low = Math.Floor(low / tickSize) * tickSize;
            }
            else if (tickSize > 0)
            {
                high = Math.Round(high / tickSize) * tickSize;
                low = Math.Round(low / tickSize) * tickSize;
            }

            var tpoLevels = new Dictionary<double, TPOData>();
            for (double price = low; price <= high + tickSize / 2; price += tickSize)
            {
                tpoLevels[price] = new TPOData
                {
                    PriceLevel = price,
                    Score = 0,
                    Letters = "",
                    StartTime = session.SessionStart,
                    EndTime = session.SessionEnd
                };
            }

            int letterIndex = 0;
            for (int i = maxIndex; i >= minIndex; i--) 
            {
                var bar = _additionalData[i];
                double barHigh = bar[PriceType.High];
                double barLow = bar[PriceType.Low];
                double startPrice = Math.Floor(barLow / tickSize) * tickSize;
                double endPrice = Math.Ceiling(barHigh / tickSize) * tickSize;
                for (double price = startPrice; price <= endPrice; price += tickSize)
                {
                    if (tpoLevels.TryGetValue(price, out var tpo))
                    {
                        tpo.Score++;
                        if (letterIndex < letters.Length)
                            tpo.Letters += letters[letterIndex] + new string(' ', TPOSpacing);
                    }
                }
                letterIndex = (letterIndex + 1) % letters.Length;
            }

            foreach (var tpo in tpoLevels.Values)
            {
                tpo.IsSinglePrint = ShowSinglePrints && tpo.Score == 1;
                session.TPOs.Add(tpo);
            }
            session.TPOs.Sort((a, b) => b.PriceLevel.CompareTo(a.PriceLevel));
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);
            if (CurrentChart == null || sessionCache.Count == 0)
                return;

            var graphics = args.Graphics;
            var mainWindow = CurrentChart.MainWindow;
            var prevClipRectangle = graphics.ClipBounds;
            graphics.SetClip(args.Rectangle);

            List<SessionTPO> sessionsToPaint;
            lock (lockObj)
            {
                sessionsToPaint = sessionCache.Values.ToList(); 
            }

            try
            {
                var leftBorderTime = _additionalData[0, SeekOriginHistory.Begin].TimeLeft;
                var rightBorderTime = _additionalData[0, SeekOriginHistory.End].TimeLeft;
                var leftIndex = (int)mainWindow.CoordinatesConverter.GetBarIndex(leftBorderTime);
                var rightIndex = (int)mainWindow.CoordinatesConverter.GetBarIndex(rightBorderTime);
                
                int visibleBars = Math.Abs(rightIndex - leftIndex) + 1;
              
                int barWidth = CurrentChart.BarsWidth;
                double priceRange = Math.Abs(mainWindow.CoordinatesConverter.GetPrice(mainWindow.ClientRectangle.Top) - mainWindow.CoordinatesConverter.GetPrice(mainWindow.ClientRectangle.Bottom));
                float pixelsPerPriceUnit = priceRange > 0 ? mainWindow.ClientRectangle.Height / (float)priceRange : 0;
                float footprintFontSize = Math.Max(2f, Math.Min(10f, (barWidth / 10f) * (pixelsPerPriceUnit / 15.50f)));
                if (pixelsPerPriceUnit < 18.46f)
                    footprintFontSize = Math.Max(1.5f, footprintFontSize * 0.8f);

                float minFontSize = 8f;  
                float zoomFactor = visibleBars / 50f; 
                using var footprintFont = new Font("Arial", footprintFontSize);
                float textHeight = graphics.MeasureString("0", footprintFont).Height;

                float footprintFontSizeAdjusted = Math.Max(minFontSize, Math.Min(10f, (barWidth / 10f) * (pixelsPerPriceUnit / 15.50f)));
                if (pixelsPerPriceUnit > 46 && pixelsPerPriceUnit <= 75.63f)
                {
                    footprintFontSizeAdjusted = Math.Max(minFontSize * 0.4f, footprintFontSizeAdjusted * 0.5f);
                }
                else if (pixelsPerPriceUnit > 75.63f && pixelsPerPriceUnit <= 100)
                {
                    float scaleFactor = 0.5f + 0.5f * (pixelsPerPriceUnit - 75.63f) / (100f - 75.63f);
                    footprintFontSizeAdjusted = Math.Max(minFontSize * 0.4f, footprintFontSizeAdjusted * scaleFactor);
                }
                else if (pixelsPerPriceUnit >= 4.62f && pixelsPerPriceUnit < 8.10f)
                {
                    float scaleFactor = 0.2f + 0.6f * (pixelsPerPriceUnit - 4.62f) / (8.10f - 4.62f);
                    footprintFontSizeAdjusted = Math.Max(minFontSize * 0.2f, footprintFontSizeAdjusted * scaleFactor);
                }

                using var adjustedFootprintFont = new Font("Arial", footprintFontSizeAdjusted);
                textHeight = graphics.MeasureString("0", adjustedFootprintFont).Height;

                foreach (var session in sessionsToPaint)
                {
                    if (session.SessionEnd < leftBorderTime || session.SessionStart > rightBorderTime)
                        continue;

                    int sessionStartX = (int)mainWindow.CoordinatesConverter.GetChartX(session.SessionStart);
                    int sessionEndX = ExtendLevels
                        ? mainWindow.ClientRectangle.Right
                        : (int)mainWindow.CoordinatesConverter.GetChartX(session.SessionEnd);

                    List<TPOData> tposCopy;
                    lock (lockObj)
                    {
                        tposCopy = session.TPOs.ToList();
                    }

                    List<(float TopY, float BottomY, string Letters, bool IsSinglePrint, int TPOCount)> tpoData = new();
                    int maxTPOCount = 0; 
                    foreach (var tpo in tposCopy)
                    {
                        float topY = (float)mainWindow.CoordinatesConverter.GetChartY(tpo.PriceLevel + (tickSize / 2));
                        float bottomY = (float)mainWindow.CoordinatesConverter.GetChartY(tpo.PriceLevel - (tickSize / 2));
                        
                        int tpoCount = tpo.Letters?.Length ?? 1;
                        tpoData.Add((topY, bottomY, tpo.Letters, tpo.IsSinglePrint, tpoCount));
                        maxTPOCount = Math.Max(maxTPOCount, tpoCount);
                    }

                    foreach (var tpo in tposCopy)
                    {
                        float topY = (float)mainWindow.CoordinatesConverter.GetChartY(tpo.PriceLevel + (tickSize / 2));
                        float bottomY = (float)mainWindow.CoordinatesConverter.GetChartY(tpo.PriceLevel - (tickSize / 2));
                        var blockColor = tpo.IsSinglePrint
                            ? Color.FromArgb(255 - (255 * SinglePrintsTransparency / 100), SinglePrintsColor)
                            : TPOBlockColor;
                        using (var brush = new SolidBrush(blockColor))
                        {
                            graphics.FillRectangle(brush, sessionStartX, topY, sessionEndX - sessionStartX, bottomY - topY);
                        }
                    }

                    float sessionWidth = sessionEndX - sessionStartX;
                    float minHistogramWidth = sessionWidth * 0.5f;
                    float baseTPOBlockWidth = 5f; 
                    float tpoBlockWidth = baseTPOBlockWidth;

                    if (maxTPOCount > 0)
                    {
                        float calculatedWidth = maxTPOCount * baseTPOBlockWidth;
                        if (calculatedWidth < minHistogramWidth)
                        {
                            tpoBlockWidth = minHistogramWidth / maxTPOCount; 
                        }
                    }

                    using (var borderPen = new Pen(Color.Black, 1f))
                    {
                        foreach (var data in tpoData)
                        {
                            var blockColor = data.IsSinglePrint
                                ? Color.FromArgb(255 - (255 * SinglePrintsTransparency / 100), SinglePrintsColor)
                                : Color.FromArgb(150, 100, 200, 100); 
                            using (var histogramBrush = new SolidBrush(blockColor))
                            using (var textBrush = new SolidBrush(data.IsSinglePrint ? SinglePrintsColor : Color.White))
                            {
                                for (int i = 0; i < data.TPOCount; i++)
                                {
                                    float blockX = sessionStartX + (i * tpoBlockWidth);
                                    graphics.FillRectangle(histogramBrush, blockX, data.TopY, tpoBlockWidth, data.BottomY - data.TopY);
                                    graphics.DrawRectangle(borderPen, blockX, data.TopY, tpoBlockWidth, data.BottomY - data.TopY);
                                    if (pixelsPerPriceUnit > 6.62f)
                                    {
                                        if (!string.IsNullOrEmpty(data.Letters) && i < data.Letters.Length)
                                        {
                                            string letter = data.Letters[i].ToString();
                                            SizeF textSize = graphics.MeasureString(letter, adjustedFootprintFont);
                                            float letterX = blockX + (tpoBlockWidth - textSize.Width) / 2;
                                            float letterY = data.TopY + ((data.BottomY - data.TopY) - textSize.Height) / 2;
                                            graphics.DrawString(letter, adjustedFootprintFont, textBrush, letterX, letterY);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                graphics.SetClip(prevClipRectangle);
            }
        }
        public override IList<SettingItem> Settings
        {
            get
            {
                var settings = base.Settings;
                var separ = settings.FirstOrDefault()?.SeparatorGroup ?? new SettingItemSeparatorGroup("");

                var items = new List<SelectItem> { new SelectItem("Chart Session", CHART_SESSION_CONTAINER) };
                if (Core.Instance?.CustomSessions != null)
                {
                    items.AddRange(Core.Instance.CustomSessions.Select(s =>
                        new SelectItem(s.ActiveSessions.FirstOrDefault()?.Name ?? $"Session (ID: {s.Id})", s.Id)));
                }

                var selectedItem = items.FirstOrDefault(i => i.Value.Equals(specifiedSessionContainerId))
                    ?? items.FirstOrDefault() ?? new SelectItem("Chart Session", CHART_SESSION_CONTAINER);

                settings.Add(new SettingItemSelectorLocalized(SESSION_TEMPLATE_NAME, selectedItem, items, 10)
                {
                    Text = "Sessions template",
                    SeparatorGroup = separ
                });
                return settings;
            }
            set
            {
                lock (lockObj)
                {
                    var holder = new SettingsHolder(value);
                    base.Settings = value;

                    bool needRefresh = false;

                    if (holder.TryGetValue(SESSION_TEMPLATE_NAME, out var item) &&
                        specifiedSessionContainerId != item.GetValue<string>())
                    {
                        specifiedSessionContainerId = item.GetValue<string>();
                        UpdateSessionContainer();
                        needRefresh |= item.ValueChangingReason == SettingItemValueChangingReason.Manually;
                    }

                    if (Symbol != null)
                    {
                        tickSize = TicksNumberPerTPO * Symbol.TickSize;
                    }

                    if (needRefresh)
                    {
                        sessionCache.Clear();
                        ProcessHistoricalData();
                    }
                }
            }
        }
        protected override void OnClear()
        {
            // Limpiar la instancia activa al destruir el indicador
            if (ActiveInstance == this)
                ActiveInstance = null;
            lock (lockObj)
            {
                sessionCache.Clear();
            }
        }
    }
}