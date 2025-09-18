using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace PANTOMRELOADEDbyPabloJimenez
{
    public class PANTOMRELOADEDAbsorption : Indicator, IVolumeAnalysisIndicator
    {
        [InputParameter("Sensitivity", 10, 0.01, 1, 0.01, 2)]
        public double Sensitivity = 0.3;

        [InputParameter("Absorption Volume Threshold", 20, 1, 10, 1, 0)]
        public double AbsorptionVolumeThreshold = 5;

        [InputParameter("Bid/Ask Imbalance Ratio", 30, 1, 5, 0.1, 1)]
        public double BidAskImbalanceRatio = 3.0;

        [InputParameter("ATR Period", 40, 5, 50, 1, 0)]
        public int AtrPeriod = 14;

        [InputParameter("Cluster Size (Ticks)", 50, 1, 10, 1, 0)]
        public int ClusterSize = 10;

        [InputParameter("Bullish Color", 60)]
        public Color BullishColor = Color.Green;

        [InputParameter("Bearish Color", 70)]
        public Color BearishColor = Color.Red;

        [InputParameter("Show Labels", 80)]
        public bool ShowLabels = false;

        private bool volumeAnalysisLoaded;
        public bool IsRequirePriceLevelsCalculation => true;

        private Indicator atrIndicator;
        private readonly List<AbsorptionData> absorptionZones = new List<AbsorptionData>();
        public void VolumeAnalysisData_Loaded()
        {
            volumeAnalysisLoaded = true;
            this.OnSettingsUpdated();
        }

        public PANTOMRELOADEDAbsorption()
            : base()
        {
            Name = "PANTOMRELOADEDAbsorption";
            Description = "Detects absorption zones in Footprint charts with bullish/bearish classification.";

            AddLineSeries("line1", Color.CadetBlue, 1, LineStyle.Solid);
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            // Initialize indicators
            this.atrIndicator = Core.Indicators.BuiltIn.ATR(AtrPeriod, MaMode.EMA, IndicatorCalculationType.ByPeriod);
            this.AddIndicator(atrIndicator);

            absorptionZones.Clear();
        }

        protected override void OnUpdate(UpdateArgs args)
        {
            if (!this.volumeAnalysisLoaded || this.Count < AtrPeriod || this.HistoricalData[this.Count - 1, SeekOriginHistory.Begin] is not HistoryItemBar bar || bar.VolumeAnalysisData == null)
                return;

            DetectAbsorption(bar.VolumeAnalysisData.PriceLevels, bar.High, bar.Low, this.Count - 1, out double? absorptionPrice, out double strength, out bool isBullish);

            if (absorptionPrice.HasValue)
            {
                absorptionZones.Add(new AbsorptionData
                {
                    BarIndex = this.Count - 1,
                    Price = absorptionPrice.Value,
                    Strength = strength,
                    IsBullish = isBullish,
                    Time = bar.TimeLeft
                });
            }
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);

            if (!this.volumeAnalysisLoaded)
                return;

            var mainWindow = this.CurrentChart.MainWindow;
            Graphics gr = args.Graphics;
            var prevClip = gr.ClipBounds;
            gr.SetClip(mainWindow.ClientRectangle);

            using Font debugFont = new Font("Arial", 8);
            using Brush textBrush = new SolidBrush(Color.White);
            using Pen bullishPen = new Pen(BullishColor, 2);
            using Pen bearishPen = new Pen(BearishColor, 2);

            try
            {
                DateTime leftTime = mainWindow.CoordinatesConverter.GetTime(mainWindow.ClientRectangle.Left);
                DateTime rightTime = mainWindow.CoordinatesConverter.GetTime(mainWindow.ClientRectangle.Right);

                foreach (var zone in absorptionZones)
                {
                    if (zone.Time < leftTime || zone.Time > rightTime)
                        continue;

                    int barLeftX = (int)Math.Round(mainWindow.CoordinatesConverter.GetChartX(zone.Time));
                    int barWidth = this.CurrentChart.BarsWidth;
                    int yCenter = (int)mainWindow.CoordinatesConverter.GetChartY(zone.Price);

                    // Dynamic height based on strength
                    int height = (int)(Symbol.TickSize * mainWindow.YScaleFactor * (20 + zone.Strength * 20));
                    int yTop = yCenter - (height / 2);
                    int yBottom = yCenter + (height / 2);

                    // Color and transparency based on direction and strength
                    int alpha = (int)(50 + (zone.Strength * 100));
                    Color fillColor = zone.IsBullish ? BullishColor : BearishColor;
                    using (Brush brush = new SolidBrush(Color.FromArgb(alpha, fillColor)))
                    {
                        gr.FillRectangle(brush, barLeftX, yTop, barWidth, height);
                    }

                    // Draw ellipse
                    int circleDiameter = (int)(height * 0.8);
                    int circleX = barLeftX + (barWidth / 2) - (circleDiameter / 2);
                    int circleY = yCenter - (circleDiameter / 2);
                    gr.DrawEllipse(zone.IsBullish ? bullishPen : bearishPen, circleX, circleY, circleDiameter, circleDiameter);

                    // Draw label if enabled
                    if (ShowLabels)
                    {
                        string labelText = $"{(zone.IsBullish ? "Bullish" : "Bearish")} Absorption: {zone.Price:F2}, Strength: {zone.Strength:F2}";
                        int textY = yBottom + 5;
                        gr.DrawString(labelText, debugFont, textBrush, barLeftX, textY);
                    }
                }
            }
            finally
            {
                gr.SetClip(prevClip);
            }
        }

        private void DetectAbsorption(Dictionary<double, VolumeAnalysisItem> priceLevels, double high, double low, int barIndex, out double? absorptionPrice, out double strength, out bool isBullish)
        {
            absorptionPrice = null;
            strength = 0;
            isBullish = false;

            if (priceLevels == null || priceLevels.Count == 0)
                return;

            double avgVolume = priceLevels.Values.Average(v => v.GetValue(VolumeAnalysisField.Volume));
            double maxVolume = priceLevels.Values.Max(v => v.GetValue(VolumeAnalysisField.Volume));
            double atrValue = this.atrIndicator.GetValue(barIndex, 0);
            double maxAllowedRange = atrValue * Sensitivity;

            // Get high-volume levels
            var highVolumeLevels = priceLevels
                .Where(p => p.Value.GetValue(VolumeAnalysisField.Volume) >= avgVolume * AbsorptionVolumeThreshold)
                .OrderByDescending(p => p.Value.GetValue(VolumeAnalysisField.Volume))
                .Take(ClusterSize)
                .ToList();

            if (highVolumeLevels.Count == 0)
                return;

            // Check cluster range
            double clusterRange = highVolumeLevels.Max(p => p.Key) - highVolumeLevels.Min(p => p.Key);
            if (clusterRange > maxAllowedRange)
                return;

            // Calculate volume, delta, and imbalance
            double totalVolume = 0;
            double totalDelta = 0;
            double maxImbalance = 0;

            foreach (var level in highVolumeLevels)
            {
                double bidVolume = level.Value.GetValue(VolumeAnalysisField.BuyVolume);
                double askVolume = level.Value.GetValue(VolumeAnalysisField.SellVolume);
                double volume = level.Value.GetValue(VolumeAnalysisField.Volume);
                double delta = level.Value.GetValue(VolumeAnalysisField.Delta);

                double imbalanceRatio = Math.Max(bidVolume, askVolume) / Math.Min(bidVolume, askVolume);
                if (imbalanceRatio > maxImbalance)
                    maxImbalance = imbalanceRatio;

                totalVolume += volume;
                totalDelta += delta;
            }

            // Confirm absorption
            if (maxImbalance >= BidAskImbalanceRatio && totalVolume >= avgVolume * AbsorptionVolumeThreshold)
            {
                absorptionPrice = highVolumeLevels.Average(p => p.Key);
                strength = Math.Min(1.0, (totalVolume / (avgVolume * AbsorptionVolumeThreshold)) * (maxImbalance / BidAskImbalanceRatio));

                // Determine bullish/bearish based on delta and price action
                double closePrice = this.Close(barIndex);
                isBullish = totalDelta > 0;
            }
        }

        private class AbsorptionData
        {
            public int BarIndex { get; set; }
            public double Price { get; set; }
            public double Strength { get; set; }
            public bool IsBullish { get; set; }
            public DateTime Time { get; set; }
        }

        protected override void OnClear()
        {
            base.OnClear();
            absorptionZones.Clear();
            atrIndicator?.Dispose();
        }
    }
}
