#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
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
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AdaptiveVolumeProfile : Indicator
    {
        private Dictionary<double, long> volumeProfile;
        private long totalVolume;
        private double pocPrice;
        private long pocVolume;
        private double vah;
        private double val;
        private SolidColorBrush pocBrush;
        private SolidColorBrush vahValBrush;
        private SolidColorBrush otherBrush;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Real-time volume profile for current session with POC and Value Area highlighting.";
                Name = "AdaptiveVolumeProfile";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                NumPriceLevels = 100;
                ValueAreaPercentage = 70.0;
                MaxBarWidth = 100;
                ShowProfile = true;
            }
            else if (State == State.DataLoaded)
            {
                volumeProfile = new Dictionary<double, long>();
                totalVolume = 0;
                pocPrice = 0;
                pocVolume = 0;
                vah = 0;
                val = 0;
            }
            else if (State == State.Terminated)
            {
                if (pocBrush != null && !pocBrush.IsDisposed)
                    pocBrush.Dispose();
                if (vahValBrush != null && !vahValBrush.IsDisposed)
                    vahValBrush.Dispose();
                if (otherBrush != null && !otherBrush.IsDisposed)
                    otherBrush.Dispose();
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            try
            {
                if (BarsInProgress != 0 || marketDataUpdate.MarketDataType != MarketDataType.Last || marketDataUpdate.Volume == 0)
                    return;

                if (Bars.IsFirstBarOfSession)
                {
                    volumeProfile.Clear();
                    totalVolume = 0;
                    pocPrice = 0;
                    pocVolume = 0;
                    vah = 0;
                    val = 0;
                }

                double price = Math.Round(marketDataUpdate.Price / TickSize, 0) * TickSize;
                if (!volumeProfile.ContainsKey(price))
                    volumeProfile[price] = 0;
                volumeProfile[price] += marketDataUpdate.Volume;
                totalVolume += marketDataUpdate.Volume;

                UpdatePOC();
                UpdateValueArea();
            }
            catch (Exception ex)
            {
                Log("AdaptiveVolumeProfile OnMarketData error: " + ex.Message, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        private void UpdatePOC()
        {
            pocPrice = 0;
            pocVolume = 0;
            foreach (var kvp in volumeProfile)
            {
                if (kvp.Value > pocVolume)
                {
                    pocVolume = kvp.Value;
                    pocPrice = kvp.Key;
                }
            }
        }

        private void UpdateValueArea()
        {
            if (totalVolume == 0 || pocPrice == 0)
                return;

            long targetVolume = (long)(totalVolume * (ValueAreaPercentage / 100.0));
            long cumVolume = 0;
            double[] sortedPrices = volumeProfile.Keys.OrderBy(k => k).ToArray();

            // Find val (lowest price where cumulative volume >= poc - ha
            foreach (double price in sortedPrices)
            {
                cumVolume += volumeProfile[price];
                if (cumVolume >= (targetVolume / 2))
                {
                    val = price;
                    break;
                }
            }

            // Find vah (highest price where cumulative volume >= poc + ha
            cumVolume = 0;
            Array.Reverse(sortedPrices);
            foreach (double price in sortedPrices)
            {
                cumVolume += volumeProfile[price];
                if (cumVolume >= (targetVolume / 2))
                {
                    vah = price;
                    break;
                }
            }
        }

        protected override void OnRenderTargetChanged()
        {
            try
            {
                if (pocBrush != null && !pocBrush.IsDisposed)
                    pocBrush.Dispose();
                if (vahValBrush != null && !vahValBrush.IsDisposed)
                    vahValBrush.Dispose();
                if (otherBrush != null && !otherBrush.IsDisposed)
                    otherBrush.Dispose();

                pocBrush = new SolidColorBrush(RenderTarget, SharpDX.Color.Red.ToDxColor4());
                vahValBrush = new SolidColorBrush(RenderTarget, SharpDX.Color.Yellow.ToDxColor4());
                otherBrush = new SolidColorBrush(RenderTarget, SharpDX.Color.DarkGray.ToDxColor4());
            }
            catch (Exception ex)
            {
                Log("AdaptiveVolumeProfile OnRenderTargetChanged error: " + ex.Message, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            try
            {
                if (!ShowProfile || volumeProfile.Count == 0 || pocVolume == 0)
                    return;

                double maxWidth = Math.Min(MaxBarWidth, ChartPanel.Width * 0.5);
                double scaleFactor = pocVolume > 0 ? maxWidth / pocVolume : 0;

                var topLevels = volumeProfile.OrderByDescending(kvp => kvp.Value).Take(NumPriceLevels);

                foreach (var kvp in topLevels)
                {
                    double price = kvp.Key;
                    long volume = kvp.Value;
                    double width = volume * scaleFactor;

                    SharpDX.Color color = otherBrush.Color4;
                    if (price == pocPrice)
                        color = pocBrush.Color4;
                    else if (price >= val && price <= vah)
                        color = vahValBrush.Color4;

                    using (var brush = new SolidColorBrush(RenderTarget, color))
                    {
                        float y = (float)chartScale.GetYByValue(price);
                        float height = (float)(chartScale.GetYByValue(price - TickSize) - y);
                        RenderTarget.FillRectangle(new RectangleF(ChartPanel.X + ChartPanel.Width - (float)width, y - height / 2, (float)width, height), brush);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("AdaptiveVolumeProfile OnRender error: " + ex.Message, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Num Price Levels", Order = 1, GroupName = "Parameters")]
        public int NumPriceLevels { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Value Area Percentage", Order = 2, GroupName = "Parameters")]
        public double ValueAreaPercentage { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Max Bar Width", Order = 3, GroupName = "Parameters")]
        public int MaxBarWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Profile", Order = 4, GroupName = "Parameters")]
        public bool ShowProfile { get; set; }
        #endregion
    }
}