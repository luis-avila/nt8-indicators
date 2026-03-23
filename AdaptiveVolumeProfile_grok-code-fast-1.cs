#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AdaptiveVolumeProfile : Indicator
    {
        // Private variables
        private Dictionary<double, long> volumeProfile = new Dictionary<double, long>();
        private long totalVolume = 0;
        private double pocPrice = double.NaN;
        private long pocVolume = 0;
        private double vah = double.NaN;
        private double val = double.NaN;
        private SharpDX.Direct2D1.SolidColorBrush pocBrushDx;
        private SharpDX.Direct2D1.SolidColorBrush vaBrushDx;
        private SharpDX.Direct2D1.SolidColorBrush otherBrushDx;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Displays adaptive real-time volume profile for current session with POC and value area.";
                Name                                        = "AdaptiveVolumeProfile";
                Calculate                                   = Calculate.OnEachTick;
                IsOverlay                                   = true;
                DisplayInDataBox                            = false;
                DrawOnPricePanel                            = true;
                DrawHorizontalGridLines                     = true;
                DrawVerticalGridLines                       = true;
                PaintPriceMarkers                           = false;
                ScaleJustification                          = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive                    = true;
                ValueAreaPercentage                         = 70;
                NumPriceLevels                              = 100;
                ShowProfile                                 = true;
                MaxBarWidth                                 = 100;
                POCBrush                                    = Brushes.Red;
                ValueAreaBrush                              = Brushes.Yellow;
                OtherBrush                                  = Brushes.Gray;
            }
            else if (State == State.DataLoaded)
            {
                volumeProfile = new Dictionary<double, long>();
                totalVolume = 0;
                pocPrice = double.NaN;
                pocVolume = 0;
                vah = double.NaN;
                val = double.NaN;
            }
            else if (State == State.Terminated)
            {
                if (pocBrushDx != null && !pocBrushDx.IsDisposed)
                    pocBrushDx.Dispose();
                if (vaBrushDx != null && !vaBrushDx.IsDisposed)
                    vaBrushDx.Dispose();
                if (otherBrushDx != null && !otherBrushDx.IsDisposed)
                    otherBrushDx.Dispose();
            }
        }

        protected override void OnRenderTargetChanged()
        {
            if (pocBrushDx != null && !pocBrushDx.IsDisposed)
                pocBrushDx.Dispose();
            if (vaBrushDx != null && !vaBrushDx.IsDisposed)
                vaBrushDx.Dispose();
            if (otherBrushDx != null && !otherBrushDx.IsDisposed)
                otherBrushDx.Dispose();
            if (RenderTarget == null)
                return;
            pocBrushDx = POCBrush.ToDxBrush(RenderTarget);
            vaBrushDx = ValueAreaBrush.ToDxBrush(RenderTarget);
            otherBrushDx = OtherBrush.ToDxBrush(RenderTarget);
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType == MarketDataType.Last && !e.MarketData.IsReset && Bars.Count > 0)
            {
                AccumulateVolume(e.Price, (long)e.Volume);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;
            if (Bars.IsFirstBarOfSession)
            {
                volumeProfile.Clear();
                totalVolume = 0;
                pocPrice = double.NaN;
                pocVolume = 0;
                vah = double.NaN;
                val = double.NaN;
            }
            if (volumeProfile.Count > 0)
            {
                CalculatePOCAndVA();
            }
        }

        private void AccumulateVolume(double price, long volume)
        {
            double normalizedPrice = Math.Round(price / Instrument.MasterInstrument.TickSize) * Instrument.MasterInstrument.TickSize;
            if (volumeProfile.ContainsKey(normalizedPrice))
                volumeProfile[normalizedPrice] += volume;
            else
                volumeProfile[normalizedPrice] = volume;
            totalVolume += volume;
        }

        private void CalculatePOCAndVA()
        {
            if (volumeProfile.Count == 0)
                return;
            pocVolume = volumeProfile.Values.Max();
            pocPrice = volumeProfile.First(kvp => kvp.Value == pocVolume).Key;
            CalculateValueArea();
        }

        private void CalculateValueArea()
        {
            double targetVolume = totalVolume * (ValueAreaPercentage / 100.0);
            double accumulated = pocVolume;
            vah = pocPrice;
            val = pocPrice;
            var sortedPrices = volumeProfile.Keys.OrderBy(p => p).ToList();
            int pocIndex = sortedPrices.IndexOf(pocPrice);
            int upperIndex = pocIndex + 1;
            int lowerIndex = pocIndex - 1;
            try
            {
                while (accumulated < targetVolume)
                {
                    long volumeAbove = upperIndex < sortedPrices.Count ? volumeProfile[sortedPrices[upperIndex]] : 0;
                    long volumeBelow = lowerIndex >= 0 ? volumeProfile[sortedPrices[lowerIndex]] : 0;
                    if (volumeAbove <= 0 && volumeBelow <= 0)
                        break;
                    if (volumeAbove >= volumeBelow)
                    {
                        accumulated += volumeAbove;
                        vah = sortedPrices[upperIndex];
                        upperIndex++;
                    }
                    else
                    {
                        accumulated += volumeBelow;
                        val = sortedPrices[lowerIndex];
                        lowerIndex--;
                    }
                }
            }
            catch (Exception e)
            {
                Log("QA: AdaptiveVolumeProfile CalculateValueArea exception: " + e.Message, LogLevel.Error);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (!ShowProfile || volumeProfile.Count == 0 || RenderTarget == null || chartControl == null || chartScale == null || ChartBars == null)
                return;
            try
            {
                float xRight = ChartPanel.X + ChartPanel.Width;
                float barMaxWidthPx = MaxBarWidth;
                var priceLevels = volumeProfile.OrderByDescending(kvp => kvp.Value).Take(NumPriceLevels).OrderBy(kvp => kvp.Key).ToList();
                long maxVol = priceLevels.Count > 0 ? priceLevels.Max(kvp => kvp.Value) : 0;
                if (maxVol == 0)
                    return;
                double tickSize = Instrument.MasterInstrument.TickSize;
                foreach (var kvp in priceLevels)
                {
                    float barWidthPx = (float)(kvp.Value / (double)maxVol * barMaxWidthPx);
                    float yTop = chartScale.GetYByValue(kvp.Key + tickSize / 2.0);
                    float yBottom = chartScale.GetYByValue(kvp.Key - tickSize / 2.0);
                    SharpDX.Direct2D1.Brush brush;
                    if (Math.Abs(kvp.Key - pocPrice) < tickSize * 0.5)
                        brush = pocBrushDx;
                    else if (kvp.Key <= vah && kvp.Key >= val)
                        brush = vaBrushDx;
                    else
                        brush = otherBrushDx;
                    if (brush != null && barWidthPx > 0.1f)
                    {
                        SharpDX.RectangleF rect = new SharpDX.RectangleF(xRight - barWidthPx, yTop, barWidthPx, Math.Abs(yBottom - yTop));
                        RenderTarget.FillRectangle(rect, brush);
                    }
                }
            }
            catch (Exception e)
            {
                Log("QA: AdaptiveVolumeProfile OnRender exception: " + e.Message, LogLevel.Error);
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Value Area Percentage", Description = "Percentage of total volume to include in Value Area", Order = 1, GroupName = "Parameters")]
        public int ValueAreaPercentage { get; set; }

        [NinjaScriptProperty]
        [Range(10, 1000)]
        [Display(Name = "Number of Price Levels", Description = "Maximum number of price levels to display", Order = 2, GroupName = "Parameters")]
        public int NumPriceLevels { get; set; }

        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "Max Bar Width", Description = "Maximum width in pixels for the bars", Order = 3, GroupName = "Parameters")]
        public int MaxBarWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Profile", Description = "Show or hide the volume profile", Order = 4, GroupName = "Parameters")]
        public bool ShowProfile { get; set; }

        [XmlIgnore]
        [Display(Name = "POC Color", Order = 1, GroupName = "Colors")]
        public Brush POCBrush { get; set; }

        [Browsable(false)]
        public string POCBrushSerialize
        {
            get { return Serialize.BrushToString(POCBrush); }
            set { POCBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Value Area Color", Order = 2, GroupName = "Colors")]
        public Brush ValueAreaBrush { get; set; }

        [Browsable(false)]
        public string ValueAreaBrushSerialize
        {
            get { return Serialize.BrushToString(ValueAreaBrush); }
            set { ValueAreaBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Other Levels Color", Order = 3, GroupName = "Colors")]
        public Brush OtherBrush { get; set; }

        [Browsable(false)]
        public string OtherBrushSerialize
        {
            get { return Serialize.BrushToString(OtherBrush); }
            set { OtherBrush = Serialize.StringToBrush(value); }
        }

        #endregion
    }
}