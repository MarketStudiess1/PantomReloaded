// Copyright QUANTOWER LLC. © 2017-2024. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace PANTOMRELOADEDbyPabloJimenez
{
    public class PANTOMRELOADEDImbalances : Indicator, IVolumeAnalysisIndicator
    {
        [InputParameter("Multiplier for imbalance", 1, 0.1, 100, 0.1, 1)]
        private double InputImbalanceMultiplier = 4; // 50%

        [InputParameter("Min consecutive levels", 2, 1, 10, 1, 0)]
        private int InputminConsecutiveLevels = 2;

        private List<List<BuyImbalance>> buyImbalancesPerOffset;
        private List<List<SellImbalance>> sellImbalancesPerOffset;
        private readonly object lockObj = new object();
        private bool volumeAnalysisLoaded;

        public static PANTOMRELOADEDImbalances ActiveInstance { get; private set; }

        public PANTOMRELOADEDImbalances()
            : base()
        {   
            Name = "PANTOMRELOADEDImbalances";
            AddLineSeries("line1", Color.CadetBlue, 1, LineStyle.Solid);
            SeparateWindow = false;

            buyImbalancesPerOffset = new List<List<BuyImbalance>>();
            sellImbalancesPerOffset = new List<List<SellImbalance>>();
            volumeAnalysisLoaded = false;
            ActiveInstance = this;
        }

        public bool IsRequirePriceLevelsCalculation => true;

        public IReadOnlyDictionary<int, (List<BuyImbalance> Buys, List<SellImbalance> Sells)> ImbalancesByBar
        {
            get
            {
                lock (lockObj)
                {
                    return Enumerable.Range(0, Math.Min(buyImbalancesPerOffset.Count, sellImbalancesPerOffset.Count))
                        .ToDictionary(
                            offset => offset,
                            offset => (buyImbalancesPerOffset[offset] ?? new List<BuyImbalance>(),
                                      sellImbalancesPerOffset[offset] ?? new List<SellImbalance>())
                        ).AsReadOnly();
                }
            }
        }

        public void VolumeAnalysisData_Loaded()
        {
            Task.Factory.StartNew(() =>
            {
                while (this.Count != this.HistoricalData.Count)
                    Thread.Sleep(20);

                // Calcular imbalances para todas las barras históricas en orden incremental
                for (int offset = 0; offset < this.HistoricalData.Count; offset++)
                    this.ComputeImbalances(offset);

                this.volumeAnalysisLoaded = true;
            });
        }

        protected override void OnUpdate(UpdateArgs args)
        {
            if (this.HistoricalData.VolumeAnalysisCalculationProgress == null)
                return;

            if (this.HistoricalData.VolumeAnalysisCalculationProgress.State != VolumeAnalysisCalculationState.Finished)
                return;

            // Calcular imbalances para la barra más reciente (Count-1)
            this.ComputeImbalances(this.HistoricalData.Count - 1);
        }

        private void ComputeImbalances(int offset)
        {
            // Usar SeekOriginHistory.Begin para obtener los datos de la barra
            var bar = this.HistoricalData[offset, SeekOriginHistory.Begin] as HistoryItemBar;
            if (bar == null)
                return;

            var analysisData = bar.VolumeAnalysisData;
            if (analysisData == null)
                return;

            this.CalculateVolumeNodes(analysisData.PriceLevels, out List<BuyImbalance> buys, out List<SellImbalance> sells, InputImbalanceMultiplier, InputminConsecutiveLevels);

            lock (lockObj)
            {
                // Asegurar que las listas tengan suficiente capacidad
                while (buyImbalancesPerOffset.Count <= offset)
                    buyImbalancesPerOffset.Add(null);
                while (sellImbalancesPerOffset.Count <= offset)
                    sellImbalancesPerOffset.Add(null);

                // Almacenar los imbalances en la posición correspondiente
                buyImbalancesPerOffset[offset] = buys;
                sellImbalancesPerOffset[offset] = sells;
            }
        }

        private DateTime LastPaintedTime = DateTime.MinValue;
        public override void OnPaintChart(PaintChartEventArgs args)
        {
            if (this.Symbol.LastDateTime < LastPaintedTime)
            {
                this.LastPaintedTime = Symbol.LastDateTime.AddSeconds(10);
                return;
            }

            base.OnPaintChart(args);

            if (!this.volumeAnalysisLoaded)
                return;

            var mainWindow = this.CurrentChart.MainWindow;
            Graphics gr = args.Graphics;

            var prevClip = gr.ClipBounds;
            gr.SetClip(mainWindow.ClientRectangle);

            Pen borderPenBuy = new Pen(Color.Cyan, 2);
            SolidBrush fillBrushBuy = new SolidBrush(Color.FromArgb(50, Color.Cyan));
            Pen borderPenSell = new Pen(Color.DeepPink, 2);
            SolidBrush fillBrushSell = new SolidBrush(Color.FromArgb(50, Color.DeepPink));

            try
            {
                DateTime leftTime = mainWindow.CoordinatesConverter.GetTime(mainWindow.ClientRectangle.Left);
                DateTime rightTime = mainWindow.CoordinatesConverter.GetTime(mainWindow.ClientRectangle.Right);

                int leftIndex = (int)mainWindow.CoordinatesConverter.GetBarIndex(leftTime); // Barra más antigua
                int rightIndex = (int)mainWindow.CoordinatesConverter.GetBarIndex(rightTime); // Barra más reciente

                if (leftIndex < 0)
                    leftIndex = 0;
                if (rightIndex >= this.HistoricalData.Count)
                    rightIndex = this.HistoricalData.Count - 1;

                // Iterar de la barra más antigua a la más reciente
                for (int offset = leftIndex; offset <= rightIndex && offset < this.HistoricalData.Count; offset++)
                {
                    if (offset >= buyImbalancesPerOffset.Count)
                        continue;

                    var bar = this.HistoricalData[offset, SeekOriginHistory.Begin] as HistoryItemBar;
                    if (bar == null)
                        continue;

                    var buys = buyImbalancesPerOffset[offset];
                    var sells = sellImbalancesPerOffset[offset];

                    if (buys == null || sells == null)
                        continue;

                    int barLeftX = (int)Math.Round(mainWindow.CoordinatesConverter.GetChartX(bar.TimeLeft));
                    int barRightX = barLeftX + this.CurrentChart.BarsWidth;

                    foreach (var hvc in buys)
                    {
                        int hvcStartY = (int)mainWindow.CoordinatesConverter.GetChartY(hvc.EndPrice);
                        int hvcEndY = (int)mainWindow.CoordinatesConverter.GetChartY(hvc.StartPrice);

                        gr.FillRectangle(fillBrushBuy, barLeftX, Math.Min(hvcStartY, hvcEndY), barRightX - barLeftX, Math.Abs(hvcStartY - hvcEndY));
                        gr.DrawRectangle(borderPenBuy, barLeftX, Math.Min(hvcStartY, hvcEndY), barRightX - barLeftX, Math.Abs(hvcStartY - hvcEndY));
                    }

                    foreach (var lvc in sells)
                    {
                        int lvcStartY = (int)mainWindow.CoordinatesConverter.GetChartY(lvc.EndPrice);
                        int lvcEndY = (int)mainWindow.CoordinatesConverter.GetChartY(lvc.StartPrice);

                        gr.FillRectangle(fillBrushSell, barLeftX, Math.Min(lvcStartY, lvcEndY), barRightX - barLeftX, Math.Abs(lvcStartY - lvcEndY));
                        gr.DrawRectangle(borderPenSell, barLeftX, Math.Min(lvcStartY, lvcEndY), barRightX - barLeftX, Math.Abs(lvcStartY - lvcEndY));
                    }
                }
            }
            finally
            {
                gr.SetClip(prevClip);
                borderPenBuy.Dispose();
                fillBrushBuy.Dispose();
                borderPenSell.Dispose();
                fillBrushSell.Dispose();
            }
        }

        protected override void OnClear()
        {
            lock (lockObj)
            {
                buyImbalancesPerOffset.Clear();
                sellImbalancesPerOffset.Clear();
                if (ActiveInstance == this)
                    ActiveInstance = null;
            }
        }

        private void CalculateVolumeNodes(
      Dictionary<double, VolumeAnalysisItem> priceLevels,
      out List<BuyImbalance> buyImbalances,
      out List<SellImbalance> sellImbalances,
      double imbalanceRatio,         // 👈 ratio mínimo (ej. 2.0 = 200%)
      int minConsecutiveLevels       // 👈 mínimo de niveles consecutivos
  )
        {
            const double epsilon = 0.000001;

            List<VolumeInfo> sortedPriceLevels = priceLevels
                .Select(item => new VolumeInfo { Item = item.Value, Price = item.Key })
                .OrderBy(it => it.Price)
                .ToList();

            buyImbalances = new List<BuyImbalance>();
            sellImbalances = new List<SellImbalance>();

            int n = sortedPriceLevels.Count;

            // ========================
            // Detectar Buy Imbalances (Bullish)
            // ========================
            int startBuy = -1;
            var buyValues = new List<double>();

            for (int i = 1; i < n; i++) // empezamos desde 1 porque miramos el nivel anterior
            {
                double askVolume = sortedPriceLevels[i].Item.GetValue(VolumeAnalysisField.BuyVolume);
                double bidVolumePrev = sortedPriceLevels[i - 1].Item.GetValue(VolumeAnalysisField.SellVolume);

                double adjustedBid = bidVolumePrev > 0 ? bidVolumePrev : epsilon;
                double ratio = askVolume / adjustedBid;

                if (ratio >= imbalanceRatio)
                {
                    if (startBuy == -1)
                        startBuy = i;

                    buyValues.Add(ratio);
                }
                else
                {
                    if (startBuy != -1 && (i - startBuy + 1) >= minConsecutiveLevels)
                    {
                        buyImbalances.Add(new BuyImbalance
                        {
                            StartPrice = sortedPriceLevels[startBuy].Price,
                            EndPrice = sortedPriceLevels[i - 1].Price,
                            ImbalanceValues = new List<double>(buyValues)
                        });
                    }
                    startBuy = -1;
                    buyValues.Clear();
                }
            }

            // Capturar secuencia abierta al final
            if (startBuy != -1 && (n - startBuy) >= minConsecutiveLevels)
            {
                buyImbalances.Add(new BuyImbalance
                {
                    StartPrice = sortedPriceLevels[startBuy].Price,
                    EndPrice = sortedPriceLevels[n - 1].Price,
                    ImbalanceValues = new List<double>(buyValues)
                });
            }

            // ========================
            // Detectar Sell Imbalances (Bearish)
            // ========================
            int startSell = -1;
            var sellValues = new List<double>();

            for (int i = 0; i < n - 1; i++) // hasta n-2 porque miramos el nivel siguiente
            {
                double bidVolume = sortedPriceLevels[i].Item.GetValue(VolumeAnalysisField.SellVolume);
                double askVolumeNext = sortedPriceLevels[i + 1].Item.GetValue(VolumeAnalysisField.BuyVolume);

                double adjustedAsk = askVolumeNext > 0 ? askVolumeNext : epsilon;
                double ratio = bidVolume / adjustedAsk;

                if (ratio >= imbalanceRatio)
                {
                    if (startSell == -1)
                        startSell = i;

                    sellValues.Add(ratio);
                }
                else
                {
                    if (startSell != -1 && (i - startSell + 1) >= minConsecutiveLevels)
                    {
                        sellImbalances.Add(new SellImbalance
                        {
                            StartPrice = sortedPriceLevels[startSell].Price,
                            EndPrice = sortedPriceLevels[i].Price,
                            ImbalanceValues = new List<double>(sellValues)
                        });
                    }
                    startSell = -1;
                    sellValues.Clear();
                }
            }

            // Capturar secuencia abierta al final
            if (startSell != -1 && (n - startSell) >= minConsecutiveLevels)
            {
                sellImbalances.Add(new SellImbalance
                {
                    StartPrice = sortedPriceLevels[startSell].Price,
                    EndPrice = sortedPriceLevels[n - 1].Price,
                    ImbalanceValues = new List<double>(sellValues)
                });
            }
        }


        protected override void OnInit()
        {}

        public class VolumeInfo
        {
            public VolumeAnalysisItem Item;
            public double Price;
        }

        public class BuyImbalance
        {
            public double StartPrice { get; set; }
            public double EndPrice { get; set; }
            public List<double> ImbalanceValues { get; set; } = new List<double>();
        }

        public class SellImbalance
        {
            public double StartPrice { get; set; }
            public double EndPrice { get; set; }
            public List<double> ImbalanceValues { get; set; } = new List<double>();
        }
    }
}