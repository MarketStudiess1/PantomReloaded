using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace PANTOMRELOADEDbyPabloJimenez
{
    public class PANTOMRELOADEDTrappedTraders : Indicator, IVolumeAnalysisIndicator
    {
        [InputParameter("Min Levels Trapped", 2, 1, 10, 1, 0)]
        public int LevelsTrapped = 5;

        [InputParameter("Imbalance Ratio", 4, 1, 10, 0.1, 1)]
        public double ImbalanceRatio = 4.0;

        [InputParameter("True Range Multiplier", 3, 0.1, 1, 0.1, 2)]
        public double TrueRangeMultiplier = 0.1;

        private bool volumeAnalysisLoaded;

        public PANTOMRELOADEDTrappedTraders()
            : base()
        {
            Name = "PANTOMRELOADEDTrappedTraders";
            AddLineSeries("line1", Color.CadetBlue, 1, LineStyle.Solid);
            SeparateWindow = false;
        }

        public bool IsRequirePriceLevelsCalculation => true;

        public void VolumeAnalysisData_Loaded()
            => this.volumeAnalysisLoaded = true;

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);

            if (!this.volumeAnalysisLoaded)
                return;

            var mainWindow = this.CurrentChart.MainWindow;
            Graphics gr = args.Graphics;

            var prevClip = gr.ClipBounds;
            gr.SetClip(mainWindow.ClientRectangle);

            int halfTickSizeInPx = (int)(Symbol.TickSize * mainWindow.YScaleFactor / 2.0);

            try
            {
                DateTime leftTime = mainWindow.CoordinatesConverter.GetTime(mainWindow.ClientRectangle.Left);
                DateTime rightTime = mainWindow.CoordinatesConverter.GetTime(mainWindow.ClientRectangle.Right);

                int leftIndex = (int)mainWindow.CoordinatesConverter.GetBarIndex(leftTime);
                int rightIndex = (int)Math.Ceiling(mainWindow.CoordinatesConverter.GetBarIndex(rightTime));

                for (int i = leftIndex; i <= rightIndex; i++)
                {
                    if (i > 0 && i < this.HistoricalData.Count && this.HistoricalData[i, SeekOriginHistory.Begin] is HistoryItemBar bar
                        && bar.VolumeAnalysisData != null)
                    {
                        this.FindConsecutiveTrappedTraders(bar.VolumeAnalysisData.PriceLevels, out bool trappedSellers, out double sellerClusterHigh, out bool trappedBuyers, out double buyerClusterLow);

                        int barLeftX = (int)Math.Round(mainWindow.CoordinatesConverter.GetChartX(bar.TimeLeft));
                        int barWidth = this.CurrentChart.BarsWidth;

                        int yCenter = (int)mainWindow.CoordinatesConverter.GetChartY(bar.Median);
                        int height = (int)(Symbol.TickSize * mainWindow.YScaleFactor * 10);
                        int yTop = yCenter - (height / 2);
                        int yBottom = yCenter + (height / 2);

                        int circleDiameter = (int)(height * 0.8);
                        int circleX = barLeftX + (barWidth / 2) - (circleDiameter / 2);
                        int circleY = yCenter - (circleDiameter / 2);

                        double Tr = (bar.High - bar.Low) / Symbol.TickSize;
                        using (Font font = new Font("Arial", 8))
                        {
                            int textOffsetX = circleX + circleDiameter + 5;
                            int textY = circleY + (circleDiameter / 2);

                            if (trappedBuyers && bar.Median > bar.Close)
                            {
                                double rejectionPriceforhigh = (bar.High - bar.Close) / this.Symbol.TickSize;
                                if (rejectionPriceforhigh > (Tr * this.TrueRangeMultiplier) && bar.Close < buyerClusterLow)
                                {
                                    gr.DrawEllipse(Pens.Green, circleX, circleY, circleDiameter, circleDiameter);
                                    gr.DrawString("Buyers", font, Brushes.Green, textOffsetX, textY);
                                }
                            }

                            if (trappedSellers && bar.Median < bar.Close)
                            {
                                double rejectionPriceforlow = (bar.Close - bar.Low) / this.Symbol.TickSize;
                                if (rejectionPriceforlow > (Tr * this.TrueRangeMultiplier) && bar.Close > sellerClusterHigh)
                                {
                                    gr.DrawEllipse(Pens.Red, circleX, circleY, circleDiameter, circleDiameter);
                                    gr.DrawString("Sellers", font, Brushes.Red, textOffsetX, textY - 15); // Offset vertically to avoid overlap
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                gr.SetClip(prevClip);
            }
        }

        protected override void OnInit() { }

        protected override void OnUpdate(UpdateArgs args) { }

        class VolumeInfo
        {
            public VolumeAnalysisItem Item;
            public double Price;
        }

        #region TrappedTraders

        private void FindConsecutiveTrappedTraders(Dictionary<double, VolumeAnalysisItem> priceLevels, out bool trappedSellers, out double sellerClusterHigh, out bool trappedBuyers, out double buyerClusterLow)
        {
            trappedSellers = false;
            sellerClusterHigh = double.NaN;
            trappedBuyers = false;
            buyerClusterLow = double.NaN;

            List<VolumeInfo> sortedPriceLevels = priceLevels
                .Select(item => new VolumeInfo() { Item = item.Value, Price = item.Key })
                .OrderBy(it => it.Price)
                .ToList();

            int levelsCount = sortedPriceLevels.Count;

            double AvgVolume = sortedPriceLevels.Average(it => it.Item.GetValue(VolumeAnalysisField.Volume));
            double TotalVolume = sortedPriceLevels.Sum(it => it.Item.GetValue(VolumeAnalysisField.Volume));

            // Volume thresholds
            double minVolumeThreshold = Math.Max(1.5 * AvgVolume, 0.005 * TotalVolume); // Use OR condition by taking the maximum

            if (levelsCount < this.LevelsTrapped)
                return;

            double totalBid = 0;
            double totalAsk = 0;
            int sellerClusterSize = 0;

            for (int i = 0; i < levelsCount; i++)
            {
                double bid = sortedPriceLevels[i].Item.GetValue(VolumeAnalysisField.SellVolume);
                double ask = sortedPriceLevels[i].Item.GetValue(VolumeAnalysisField.BuyVolume);
                totalBid += bid;
                totalAsk += ask;

                if (totalBid >= this.ImbalanceRatio * totalAsk && (bid >= 1.5 * AvgVolume || bid >= 0.005 * TotalVolume))
                {
                    sellerClusterSize = i + 1;
                }
                else
                {
                    break;
                }
            }

            if (sellerClusterSize >= this.LevelsTrapped)
            {
                trappedSellers = true;
                sellerClusterHigh = sortedPriceLevels[sellerClusterSize - 1].Price;
            }

            totalBid = 0;
            totalAsk = 0;
            int buyerClusterSize = 0;

            for (int i = levelsCount - 1; i >= 0; i--)
            {
                double bid = sortedPriceLevels[i].Item.GetValue(VolumeAnalysisField.SellVolume);
                double ask = sortedPriceLevels[i].Item.GetValue(VolumeAnalysisField.BuyVolume);
                totalBid += bid;
                totalAsk += ask;

                if (totalAsk >= this.ImbalanceRatio * totalBid && (ask >= 1.5 * AvgVolume || ask >= 0.005 * TotalVolume))
                {
                    buyerClusterSize = levelsCount - i;
                }
                else
                {
                    break;
                }
            }

            if (buyerClusterSize >= this.LevelsTrapped)
            {
                trappedBuyers = true;
                buyerClusterLow = sortedPriceLevels[levelsCount - buyerClusterSize].Price;
            }
        }
        #endregion TrappedTraders
    }
}