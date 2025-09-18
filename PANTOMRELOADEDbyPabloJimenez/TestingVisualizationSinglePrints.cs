using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using TradingPlatform.BusinessLayer;
using static PANTOMRELOADEDbyPabloJimenez.PANTOMRELOADEDImbalances;

namespace PANTOMRELOADEDbyPabloJimenez
{
    public class TestingVisualizationSinglePrints : Indicator
    {
        [InputParameter("Support color", 0)]
        public Color SupportColor = Color.DarkGreen;

        [InputParameter("Resistance color", 1)]
        public Color ResistanceColor = Color.OrangeRed;

        [InputParameter("Single Print color", 2)]
        public Color SinglePrintColor = Color.Purple; // Color para los Single Prints

        private readonly object lockObject = new object();
        private readonly object lockObject2 = new object();
        [InputParameter("Buy Imbalance color", 0)]
        public Color BuyImbalanceColor = Color.Cyan;

        [InputParameter("Sell Imbalance color", 1)]
        public Color SellImbalanceColor = Color.DeepPink;


        public TestingVisualizationSinglePrints() : base()
        {
            Name = "TestingVisualizationSinglePrints";
            Description = "Visualization of the levels and Single Prints";
            SeparateWindow = false;
            UpdateType = IndicatorUpdateType.OnBarClose;
        }

        protected override void OnInit()
        {
        }

        protected override void OnUpdate(UpdateArgs args)
        {
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);
            var gr = args.Graphics;
            var window = this.CurrentChart.Windows[args.WindowIndex];

            // Obtener instancia de PANTOMRELOADEDMarketProfile
            IReadOnlyDictionary<DateTime, List<SinglePrintData>> singlePrints;
            lock (lockObject)
            {
                var marketProfile = PANTOMRELOADEDMarketProfile.ActiveInstance;
                if (marketProfile == null)
                {
                    var font = new Font("Arial", 12, FontStyle.Bold);
                    var brush = new SolidBrush(Color.Red);
                    gr.DrawString("PANTOMRELOADEDMarketProfile is not connected.", font, brush, 10, 10);
                    return;
                }
                singlePrints = marketProfile.SinglePrintsBySession;
                if (!singlePrints.Any())
                {
                    var font = new Font("Arial", 10);
                    var brush = new SolidBrush(Color.Gray);
                    gr.DrawString("PANTOMRELOADEDMarketProfile connected, but no Single Prints available.", font, brush, 10, 30);
                    return;
                }
            }

            // Graficar Single Prints
            int rightX = window.ClientRectangle.Right;
            var leftBorderTime = HistoricalData[0, SeekOriginHistory.Begin].TimeLeft;
            var rightBorderTime = HistoricalData[0, SeekOriginHistory.End].TimeLeft;

            foreach (var session in singlePrints)
            {
                if (session.Key < leftBorderTime || session.Key > rightBorderTime)
                    continue; // Saltar sesiones fuera del rango visible

                foreach (var sp in session.Value)
                {
                    int x1 = (int)window.CoordinatesConverter.GetChartX(sp.StartTime);
                    int y = (int)window.CoordinatesConverter.GetChartY(sp.PriceLevel);

                    Pen pen = new Pen(SinglePrintColor)
                    {
                        DashStyle = System.Drawing.Drawing2D.DashStyle.Dot, // Línea punteada
                        Width = 2
                    };
                    gr.DrawLine(pen, x1, y, rightX, y);

                    string label = $"SP: {sp.PriceLevel:F2} | {sp.StartTime:HH:mm}";
                    var font = new Font("Arial", 8, FontStyle.Regular);
                    var brush = new SolidBrush(SinglePrintColor);
                    SizeF labelSize = gr.MeasureString(label, font);
                    gr.DrawString(label, font, brush, x1, y + 5); // Etiqueta debajo de la línea
                }
            }

            // Mostrar estado
            var statusFont = new Font("Arial", 8);
            var statusBrush = new SolidBrush(Color.Blue);
            gr.DrawString($"Single Prints: {singlePrints.Sum(kvp => kvp.Value.Count)}", statusFont, statusBrush, 10, window.ClientRectangle.Height - 20);


            IReadOnlyDictionary<int, (List<BuyImbalance> Buys, List<SellImbalance> Sells)> imbalances;
            lock (lockObject2)
            {
                var imbalancesIndicator = PANTOMRELOADEDImbalances.ActiveInstance;
                if (imbalancesIndicator == null)
                {
                    var font = new Font("Arial", 12, FontStyle.Bold);
                    var brush = new SolidBrush(Color.Red);
                    gr.DrawString("PANTOMRELOADEDImbalances is not connected.", font, brush, 10, 10);
                    return;
                }
                imbalances = imbalancesIndicator.ImbalancesByBar;
                if (!imbalances.Any())
                {
                    var font = new Font("Arial", 10);
                    var brush = new SolidBrush(Color.Gray);
                    gr.DrawString("PANTOMRELOADEDImbalances connected, but no imbalances available.", font, brush, 10, 30);
                    return;
                }
            }

            foreach (var kvp in imbalances)
            {
                int offset = kvp.Key;
                var bar = this.HistoricalData[offset, SeekOriginHistory.Begin] as HistoryItemBar;
                if (bar == null || bar.TimeLeft < leftBorderTime || bar.TimeLeft > rightBorderTime)
                    continue; // Saltar barras fuera del rango visible

                int x1 = (int)window.CoordinatesConverter.GetChartX(bar.TimeLeft);

                // Dibujar Buy Imbalances
                foreach (var buy in kvp.Value.Buys)
                {
                    int y = (int)window.CoordinatesConverter.GetChartY(buy.StartPrice);
                    Pen pen = new Pen(BuyImbalanceColor)
                    {
                        DashStyle = System.Drawing.Drawing2D.DashStyle.Dot,
                        Width = 2
                    };
                    gr.DrawLine(pen, x1, y, rightX, y);

                    string label = $"Buy: {buy.StartPrice:F2}";
                    var font = new Font("Arial", 8, FontStyle.Regular);
                    var brush = new SolidBrush(BuyImbalanceColor);
                    gr.DrawString(label, font, brush, x1, y + 5);
                }

                // Dibujar Sell Imbalances
                foreach (var sell in kvp.Value.Sells)
                {
                    int y = (int)window.CoordinatesConverter.GetChartY(sell.StartPrice);
                    Pen pen = new Pen(SellImbalanceColor)
                    {
                        DashStyle = System.Drawing.Drawing2D.DashStyle.Dot,
                        Width = 2
                    };
                    gr.DrawLine(pen, x1, y, rightX, y);

                    string label = $"Sell: {sell.StartPrice:F2}";
                    var font = new Font("Arial", 8, FontStyle.Regular);
                    var brush = new SolidBrush(SellImbalanceColor);
                    gr.DrawString(label, font, brush, x1, y + 5);
                }
            }

            // Mostrar estado
            int totalImbalances = imbalances.Sum(kvp => kvp.Value.Buys.Count + kvp.Value.Sells.Count);
            gr.DrawString($"Imbalances: {totalImbalances}", statusFont, statusBrush, 10, window.ClientRectangle.Height - 20);

        }

        protected override void OnClear()
        {
        }
    }
}